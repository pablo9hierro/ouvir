using Jubilados.Application.Interfaces;
using Jubilados.Domain.Entities;
using Jubilados.Domain.Enums;
using Jubilados.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jubilados.Infrastructure.Services;

public class DanfeService : IDanfeService
{
    private readonly JubiladosDbContext _db;
    private readonly ILogger<DanfeService> _logger;

    public DanfeService(JubiladosDbContext db, ILogger<DanfeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<byte[]> GerarDanfeAsync(Guid notaFiscalId, CancellationToken cancellationToken = default)
    {
        var nota = await _db.NotasFiscais
            .Include(n => n.Itens)
            .FirstOrDefaultAsync(n => n.Id == notaFiscalId, cancellationToken)
            ?? throw new InvalidOperationException($"Nota {notaFiscalId} não encontrada.");

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == nota.EmpresaId, cancellationToken);

        Entidades.ClienteNome? cliente = null;
        if (nota.ClienteId.HasValue)
        {
            cliente = await _db.Clientes.AsNoTracking()
                .Where(c => c.Id == nota.ClienteId.Value)
                .Select(c => new Entidades.ClienteNome(c.Nome, c.CPF_CNPJ, c.Logradouro, c.Numero, c.Bairro, c.Municipio, c.UF, c.CEP, c.InscricaoEstadual))
                .FirstOrDefaultAsync(cancellationToken);
        }

        var produtoIds = nota.Itens.Select(i => i.ProdutoId).ToList();
        var produtos = await _db.Produtos.AsNoTracking()
            .Where(p => produtoIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        _logger.LogInformation("[DANFE] Gerando PDF para nota #{Numero}", nota.Numero);

        QuestPDF.Settings.License = LicenseType.Community;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(7).FontFamily("Arial"));

                page.Content().Column(col =>
                {
                    // ── Cabeçalho DANFE ──────────────────────────────────────
                    col.Item().Border(1).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(4); // emitente
                            c.ConstantColumn(80); // DANFE box
                            c.RelativeColumn(3); // n. NF
                        });

                        // Emitente
                        table.Cell().RowSpan(3).Padding(4).Column(ec =>
                        {
                            ec.Item().Text("DANFE").FontSize(9).Bold().AlignCenter();
                            ec.Item().Text("Documento Auxiliar da Nota Fiscal Eletrônica").FontSize(6).AlignCenter();
                            ec.Item().PaddingTop(4).Text(empresa?.RazaoSocial ?? "").Bold().FontSize(9);
                            if (!string.IsNullOrWhiteSpace(empresa?.NomeFantasia))
                                ec.Item().Text(empresa.NomeFantasia).FontSize(7);
                            ec.Item().Text($"{empresa?.Logradouro}, {empresa?.Numero} - {empresa?.Bairro}").FontSize(7);
                            ec.Item().Text($"{empresa?.Municipio} - {empresa?.UF} | CEP: {empresa?.CEP}").FontSize(7);
                            ec.Item().Text($"CNPJ: {FormatCnpj(empresa?.CNPJ ?? "")}").FontSize(7);
                            ec.Item().Text($"IE: {empresa?.InscricaoEstadual}").FontSize(7);
                        });

                        // DANFE centro
                        table.Cell().RowSpan(3).BorderLeft(1).Padding(4).Column(dc =>
                        {
                            dc.Item().Text("0 - ENTRADA\n1 - SAÍDA").FontSize(6);
                            dc.Item().PaddingTop(2).Border(1).Padding(4)
                                .Text(nota.TipoOperacao).Bold().FontSize(14).AlignCenter();
                            dc.Item().PaddingTop(4).Text("Folha 1/1").FontSize(6);
                        });

                        // Número NF-e
                        table.Cell().Padding(4).Column(nc =>
                        {
                            nc.Item().Text($"N°: {nota.Numero:D9}").Bold().FontSize(8);
                            nc.Item().Text($"Série: {nota.Serie}").FontSize(7);
                            nc.Item().Text($"Data de Emissão: {nota.EmitidaEm.ToLocalTime():dd/MM/yyyy}").FontSize(7);
                        });

                        // Natureza da Operação
                        table.Cell().ColumnSpan(3).BorderTop(1).Padding(3).Column(no =>
                        {
                            no.Item().Text("NATUREZA DA OPERAÇÃO").FontSize(5).FontColor(Colors.Grey.Darken2);
                            no.Item().Text(nota.NaturezaOperacao).Bold().FontSize(7);
                        });
                    });

                    // ── Chave de acesso ───────────────────────────────────────
                    col.Item().PaddingTop(2).Border(1).Padding(3).Column(ch =>
                    {
                        ch.Item().Text("CHAVE DE ACESSO").FontSize(5).FontColor(Colors.Grey.Darken2);
                        ch.Item().Text(FormatChave(nota.ChaveAcesso)).FontFamily("Courier New").FontSize(7).Bold();
                        if (!string.IsNullOrWhiteSpace(nota.Protocolo))
                            ch.Item().PaddingTop(1).Text($"Protocolo: {nota.Protocolo} — {nota.AutorizadaEm?.ToLocalTime():dd/MM/yyyy HH:mm:ss}").FontSize(6);
                        if (nota.Status != StatusNota.Autorizada)
                            ch.Item().PaddingTop(1).Text($"⚠ {nota.XMotivo}").FontColor(Colors.Red.Medium).Bold();
                    });

                    // ── Destinatário ─────────────────────────────────────────
                    col.Item().PaddingTop(2).Border(1).Padding(3).Column(dest =>
                    {
                        dest.Item().Text("DESTINATÁRIO/REMETENTE").FontSize(5).FontColor(Colors.Grey.Darken2).Bold();
                        if (cliente is not null)
                        {
                            dest.Item().Row(r =>
                            {
                                r.RelativeItem(3).Column(c =>
                                {
                                    c.Item().Text("NOME / RAZÃO SOCIAL").FontSize(5).FontColor(Colors.Grey.Darken2);
                                    c.Item().Text(cliente.Nome).Bold().FontSize(8);
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("CPF / CNPJ").FontSize(5).FontColor(Colors.Grey.Darken2);
                                    c.Item().Text(FormatDoc(cliente.CpfCnpj)).FontSize(7);
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("DATA EMISSÃO").FontSize(5).FontColor(Colors.Grey.Darken2);
                                    c.Item().Text(nota.EmitidaEm.ToLocalTime().ToString("dd/MM/yyyy")).FontSize(7);
                                });
                            });
                            dest.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem(3).Column(c =>
                                {
                                    c.Item().Text("ENDEREÇO").FontSize(5).FontColor(Colors.Grey.Darken2);
                                    c.Item().Text($"{cliente.Logradouro}, {cliente.Numero} - {cliente.Bairro}").FontSize(7);
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("MUNICÍPIO").FontSize(5).FontColor(Colors.Grey.Darken2);
                                    c.Item().Text(cliente.Municipio).FontSize(7);
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("UF").FontSize(5).FontColor(Colors.Grey.Darken2);
                                    c.Item().Text(cliente.UF).FontSize(7);
                                });
                                r.ConstantItem(50).Column(c =>
                                {
                                    c.Item().Text("IE").FontSize(5).FontColor(Colors.Grey.Darken2);
                                    c.Item().Text(cliente.IE ?? "ISENTO").FontSize(7);
                                });
                            });
                        }
                        else
                        {
                            dest.Item().Text("CONSUMIDOR NÃO IDENTIFICADO").Bold().FontSize(8);
                        }
                    });

                    // ── Itens ─────────────────────────────────────────────────
                    col.Item().PaddingTop(2).Border(1).Column(itens =>
                    {
                        itens.Item().Background(Colors.Grey.Lighten3).Padding(3)
                            .Text("DADOS DOS PRODUTOS / SERVIÇOS").FontSize(6).Bold();

                        itens.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(20);  // #
                                c.RelativeColumn(4);   // produto
                                c.ConstantColumn(30);  // NCM
                                c.ConstantColumn(30);  // CFOP
                                c.ConstantColumn(25);  // UN
                                c.ConstantColumn(35);  // QTD
                                c.ConstantColumn(50);  // VL UNIT
                                c.ConstantColumn(20);  // DESC
                                c.ConstantColumn(50);  // VL TOTAL
                            });

                            // Header
                            static IContainer HeaderCell(IContainer c) =>
                                c.Background(Colors.Grey.Lighten2).Padding(2);

                            t.Header(h =>
                            {
                                h.Cell().Element(HeaderCell).Text("#").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("DESCRIÇÃO DO PRODUTO").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("NCM").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("CFOP").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("UN").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("QTD").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("VL.UNIT").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("%DESC").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("VL.TOTAL").FontSize(5).Bold();
                            });

                            static IContainer DataCell(IContainer c) =>
                                c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2);

                            foreach (var item in nota.Itens.OrderBy(i => i.NumeroItem))
                            {
                                var prod = produtos.TryGetValue(item.ProdutoId, out var p) ? p : null;
                                t.Cell().Element(DataCell).Text(item.NumeroItem.ToString()).FontSize(6);
                                t.Cell().Element(DataCell).Text(prod?.Nome ?? "—").FontSize(6);
                                t.Cell().Element(DataCell).Text(prod?.NCM ?? "").FontSize(6);
                                t.Cell().Element(DataCell).Text(prod?.CFOP ?? "").FontSize(6);
                                t.Cell().Element(DataCell).Text(item.Unidade).FontSize(6);
                                t.Cell().Element(DataCell).Text(item.Quantidade.ToString("F2")).FontSize(6);
                                t.Cell().Element(DataCell).Text(item.ValorUnitario.ToString("N2")).FontSize(6);
                                t.Cell().Element(DataCell).Text(item.ValorDesconto > 0 ? item.ValorDesconto.ToString("N2") : "").FontSize(6);
                                t.Cell().Element(DataCell).Text(item.ValorTotal.ToString("N2")).FontSize(6);
                            }
                        });
                    });

                    // ── Totais ────────────────────────────────────────────────
                    col.Item().PaddingTop(2).Border(1).Padding(3).Row(r =>
                    {
                        static void TotalItem(RowDescriptor row, string label, string value)
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(label).FontSize(5).FontColor(Colors.Grey.Darken2);
                                c.Item().Text(value).Bold().FontSize(7);
                            });
                        }

                        TotalItem(r, "V. PRODUTOS", $"R$ {nota.ValorProdutos:N2}");
                        TotalItem(r, "V. FRETE",    $"R$ {nota.ValorFrete:N2}");
                        TotalItem(r, "V. SEGURO",   $"R$ {nota.ValorSeguro:N2}");
                        TotalItem(r, "DESCONTO",    $"R$ {nota.ValorDesconto:N2}");
                        TotalItem(r, "V. IPI",      $"R$ {nota.ValorIPI:N2}");
                        TotalItem(r, "V. ICMS",     $"R$ {nota.ValorICMS:N2}");
                        TotalItem(r, "V. PIS",      $"R$ {nota.ValorPIS:N2}");
                        TotalItem(r, "V. COFINS",   $"R$ {nota.ValorCOFINS:N2}");
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("VALOR TOTAL DA NF-e").FontSize(5).FontColor(Colors.Grey.Darken2).Bold();
                            c.Item().Text($"R$ {nota.ValorTotal:N2}").Bold().FontSize(10).FontColor(Colors.Green.Darken2);
                        });
                    });

                    // ── Homologação aviso ─────────────────────────────────────
                    if (nota.CStat == "100" && nota.XMotivo?.Contains("HOMOLOGACAO", StringComparison.OrdinalIgnoreCase) == true
                        || nota.XMotivo?.Contains("HOMOLOG", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        col.Item().PaddingTop(4).Background(Colors.Yellow.Lighten3).Padding(4)
                            .Text("*** EMITIDA EM AMBIENTE DE HOMOLOGAÇÃO — SEM VALOR FISCAL ***")
                            .Bold().FontSize(8).FontColor(Colors.Red.Medium).AlignCenter();
                    }

                    // ── Informações Adicionais ────────────────────────────────
                    col.Item().PaddingTop(2).Border(1).Padding(3).Column(info =>
                    {
                        info.Item().Text("INFORMAÇÕES COMPLEMENTARES").FontSize(5).FontColor(Colors.Grey.Darken2).Bold();
                        info.Item().Text("Documento emitido por sistema autorizado. Consulte a validade em www.nfe.fazenda.gov.br").FontSize(6);
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string FormatChave(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44) return chave;
        return string.Join(" ", Enumerable.Range(0, 11).Select(i => chave.Substring(i * 4, 4)));
    }

    private static string FormatCnpj(string v)
    {
        var d = new string(v.Where(char.IsDigit).ToArray());
        if (d.Length != 14) return v;
        return $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}";
    }

    private static string FormatDoc(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        var d = new string(v.Where(char.IsDigit).ToArray());
        if (d.Length == 14) return $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}";
        if (d.Length == 11) return $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}";
        return v;
    }
}

// Projection helpers (internal use only)
internal static class Entidades
{
    internal record ClienteNome(
        string Nome, string CpfCnpj, string Logradouro, string Numero,
        string Bairro, string Municipio, string UF, string CEP, string? IE);
}
