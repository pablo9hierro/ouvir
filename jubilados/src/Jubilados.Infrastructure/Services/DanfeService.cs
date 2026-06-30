using Jubilados.Application.Interfaces;
using Jubilados.Domain.Entities;
using Jubilados.Domain.Enums;
using Jubilados.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Xml;

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

        _logger.LogInformation("[DANFE] Gerando PDF (modelo {Modelo}) para nota #{Numero}", nota.Modelo, nota.Numero);

        QuestPDF.Settings.License = LicenseType.Community;

        // Modelo 65 = NFC-e (Cupom Fiscal Eletrônico): layout simplificado e compacto,
        // com QR Code, conforme MOC – Anexo III (Especificações do DANFE NFC-e e Código QR).
        // Modelo 55 = NF-e: DANFE tradicional em folha A4 (layout abaixo).
        if (string.Equals(nota.Modelo, "65", StringComparison.Ordinal))
            return GerarDanfeNfce(nota, empresa, cliente, produtos);

        var tributosExtras = ExtrairTributosExtrasDaNFe(nota.XmlEnvio, nota.XmlRetorno);
        var valorTotalTributosPago = tributosExtras.ValorTotalTributos > 0
            ? tributosExtras.ValorTotalTributos
            : nota.ValorICMS + nota.ValorIPI + nota.ValorPIS + nota.ValorCOFINS + tributosExtras.ValorIbs + tributosExtras.ValorCbs;

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
                                c.ConstantColumn(18);  // #
                                c.RelativeColumn(4);   // DESCRIÇÃO
                                c.ConstantColumn(30);  // NCM
                                c.ConstantColumn(26);  // CFOP
                                c.ConstantColumn(32);  // CST/CSOSN
                                c.ConstantColumn(18);  // UN
                                c.ConstantColumn(30);  // QTD
                                c.ConstantColumn(40);  // VL.UNIT
                                c.ConstantColumn(40);  // VL.TOTAL
                                c.ConstantColumn(24);  // %ICMS
                                c.ConstantColumn(34);  // VL.ICMS
                                c.ConstantColumn(22);  // %IPI
                                c.ConstantColumn(34);  // VL.IPI
                            });

                            static IContainer HeaderCell(IContainer c) =>
                                c.Background(Colors.Grey.Lighten2).Padding(2);

                            t.Header(h =>
                            {
                                h.Cell().Element(HeaderCell).Text("#").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("DESCRIÇÃO DO PRODUTO").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("NCM").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("CFOP").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("CST/CSOSN").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("UN").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("QTD").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("VL.UNIT").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("VL.TOTAL").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("%ICMS").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("VL.ICMS").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("%IPI").FontSize(5).Bold();
                                h.Cell().Element(HeaderCell).Text("VL.IPI").FontSize(5).Bold();
                            });

                            static IContainer DataCell(IContainer c) =>
                                c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2);

                            foreach (var item in nota.Itens.OrderBy(i => i.NumeroItem))
                            {
                                var prod = produtos.TryGetValue(item.ProdutoId, out var p) ? p : null;
                                var cstCsosn = !string.IsNullOrWhiteSpace(prod?.CSOSN) ? prod!.CSOSN.Trim()
                                             : !string.IsNullOrWhiteSpace(prod?.CST)   ? prod!.CST.Trim()
                                             : "";
                                t.Cell().Element(DataCell).Text(item.NumeroItem.ToString()).FontSize(6);
                                t.Cell().Element(DataCell).Text(prod?.Nome ?? "—").FontSize(6);
                                t.Cell().Element(DataCell).Text(prod?.NCM ?? "").FontSize(6);
                                t.Cell().Element(DataCell).Text(prod?.CFOP ?? "").FontSize(6);
                                t.Cell().Element(DataCell).Text(cstCsosn).FontSize(6);
                                t.Cell().Element(DataCell).Text(item.Unidade).FontSize(6);
                                t.Cell().Element(DataCell).Text(item.Quantidade.ToString("F2")).FontSize(6);
                                t.Cell().Element(DataCell).Text(item.ValorUnitario.ToString("N2")).FontSize(6);
                                t.Cell().Element(DataCell).Text(item.ValorTotal.ToString("N2")).FontSize(6);
                                t.Cell().Element(DataCell).Text(item.AliquotaICMS > 0 ? item.AliquotaICMS.ToString("F2") : "").FontSize(6);
                                t.Cell().Element(DataCell).Text(item.ValorICMS > 0 ? item.ValorICMS.ToString("N2") : "").FontSize(6);
                                t.Cell().Element(DataCell).Text(item.AliquotaIPI > 0 ? item.AliquotaIPI.ToString("F2") : "").FontSize(6);
                                t.Cell().Element(DataCell).Text(item.ValorIPI > 0 ? item.ValorIPI.ToString("N2") : "").FontSize(6);
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

                    // ── Totais IBS/CBS e tributos pagos ────────────────────
                    col.Item().PaddingTop(2).Border(1).Padding(3).Row(r =>
                    {
                        static void TotalFiscal(RowDescriptor row, string label, decimal value)
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(label).FontSize(5).FontColor(Colors.Grey.Darken2);
                                c.Item().Text($"R$ {value:N2}").Bold().FontSize(7);
                            });
                        }

                        TotalFiscal(r, "BC IBS", tributosExtras.BaseIbs);
                        TotalFiscal(r, "V. IBS", tributosExtras.ValorIbs);
                        TotalFiscal(r, "BC CBS", tributosExtras.BaseCbs);
                        TotalFiscal(r, "V. CBS", tributosExtras.ValorCbs);
                        TotalFiscal(r, "FRETE", nota.ValorFrete);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("VALOR PAGO DOS IMPOSTOS").FontSize(5).FontColor(Colors.Grey.Darken2).Bold();
                            c.Item().Text($"R$ {valorTotalTributosPago:N2}").Bold().FontSize(9).FontColor(Colors.Blue.Darken2);
                        });
                    });

                    // ── Transportador / Frete ────────────────────────────────
                    col.Item().PaddingTop(2).Border(1).Padding(3).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("TRANSPORTADOR / TIPO DE FRETE").FontSize(5).FontColor(Colors.Grey.Darken2).Bold();
                            c.Item().Text(DescricaoFrete(nota.ModalidadeFrete)).FontSize(7);
                        });
                    });

                    // ── Dados do Pagamento ────────────────────────────────────
                    col.Item().PaddingTop(2).Border(1).Padding(3).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("DADOS DO PAGAMENTO").FontSize(5).FontColor(Colors.Grey.Darken2).Bold();
                            c.Item().PaddingTop(1).Row(pr =>
                            {
                                pr.RelativeItem().Column(ic =>
                                {
                                    ic.Item().Text("FORMA DE PAGAMENTO").FontSize(5).FontColor(Colors.Grey.Darken2);
                                    ic.Item().Text(DescricaoPagamento(nota.FormaPagamento)).FontSize(7);
                                });
                                pr.RelativeItem().Column(ic =>
                                {
                                    ic.Item().Text("VALOR PAGO").FontSize(5).FontColor(Colors.Grey.Darken2);
                                    ic.Item().Text($"R$ {nota.ValorTotal:N2}").FontSize(7).Bold();
                                });
                            });
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
                        if (!string.IsNullOrWhiteSpace(tributosExtras.ReservadoAoFisco))
                            info.Item().Text($"RESERVADO AO FISCO: {tributosExtras.ReservadoAoFisco}").FontSize(6);
                        info.Item().Text("Documento emitido por sistema autorizado. Consulte a validade em www.nfe.fazenda.gov.br").FontSize(6);
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }

    // ── DANFE NFC-e (modelo 65) — layout simplificado/compacto com QR Code ───
    // Referência: MOC – Anexo III (Especificações Técnicas do DANFE NFC-e e do
    // Código QR), publicado pela SEFAZ. Diferente do DANFE modelo 55 (folha A4,
    // detalhado), o DANFE NFC-e é impresso pelo próprio contribuinte em impressora
    // comum/térmica (largura usual de 80mm), trazendo: identificação resumida do
    // emitente, "Não permite aproveitamento de crédito de ICMS", lista simplificada
    // de itens, totais, forma de pagamento, identificação do consumidor (se houver),
    // chave de acesso, dados da autorização e o QR Code para consulta do documento.
    private byte[] GerarDanfeNfce(NotaFiscal nota, Empresa? empresa, Entidades.ClienteNome? cliente,
        Dictionary<Guid, Produto> produtos)
    {
        var qrCodeUrl = ExtrairTextoDaNFe(nota.XmlEnvio, nota.XmlRetorno, "qrCode");
        var urlConsulta = ExtrairTextoDaNFe(nota.XmlEnvio, nota.XmlRetorno, "urlChave");
        var qrImagem = GerarImagemQrCode(qrCodeUrl);
        var homologacao = (nota.XMotivo ?? string.Empty).Contains("HOMOLOG", StringComparison.OrdinalIgnoreCase);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                // Largura de bobina térmica de 80mm (padrão de impressão do DANFE
                // NFC-e em impressora não fiscal), altura contínua/automática.
                page.ContinuousSize(80, Unit.Millimetre);
                page.MarginVertical(12);
                page.MarginHorizontal(10);
                page.DefaultTextStyle(x => x.FontSize(7).FontFamily("Arial"));

                page.Content().Column(col =>
                {
                    col.Spacing(2);

                    col.Item().AlignCenter().Text(empresa?.RazaoSocial ?? "").Bold().FontSize(8);
                    if (!string.IsNullOrWhiteSpace(empresa?.NomeFantasia))
                        col.Item().AlignCenter().Text(empresa!.NomeFantasia).FontSize(6);
                    col.Item().AlignCenter().Text($"{empresa?.Logradouro}, {empresa?.Numero} - {empresa?.Bairro}").FontSize(6);
                    col.Item().AlignCenter().Text($"{empresa?.Municipio}/{empresa?.UF} - CEP {empresa?.CEP}").FontSize(6);
                    col.Item().AlignCenter().Text($"CNPJ: {FormatCnpj(empresa?.CNPJ ?? "")}   IE: {empresa?.InscricaoEstadual}").FontSize(6);

                    col.Item().PaddingVertical(2).LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);

                    col.Item().AlignCenter().Text("DANFE NFC-e").Bold().FontSize(8);
                    col.Item().AlignCenter().Text("Documento Auxiliar da Nota Fiscal de Consumidor Eletrônica").FontSize(5);
                    col.Item().AlignCenter().Text("Não permite aproveitamento de crédito de ICMS").FontSize(5).Italic();

                    col.Item().PaddingVertical(2).LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);

                    // ── Itens ─────────────────────────────────────────────
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(5);  // descrição
                            c.ConstantColumn(28); // qtd un
                            c.ConstantColumn(35); // vl unit
                            c.ConstantColumn(35); // vl total
                        });

                        t.Header(h =>
                        {
                            h.Cell().Text("DESCRIÇÃO").FontSize(5).Bold();
                            h.Cell().AlignRight().Text("QTD UN").FontSize(5).Bold();
                            h.Cell().AlignRight().Text("VL UNIT").FontSize(5).Bold();
                            h.Cell().AlignRight().Text("VL TOTAL").FontSize(5).Bold();
                        });

                        foreach (var item in nota.Itens.OrderBy(i => i.NumeroItem))
                        {
                            var prod = produtos.TryGetValue(item.ProdutoId, out var p) ? p : null;
                            t.Cell().Text($"{item.NumeroItem:00} {prod?.Nome ?? "—"}").FontSize(6);
                            t.Cell().AlignRight().Text($"{item.Quantidade:N2} {item.Unidade}").FontSize(6);
                            t.Cell().AlignRight().Text(item.ValorUnitario.ToString("N2")).FontSize(6);
                            t.Cell().AlignRight().Text(item.ValorTotal.ToString("N2")).FontSize(6);
                        }
                    });

                    col.Item().PaddingVertical(2).LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);

                    // ── Totais ────────────────────────────────────────────
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("QTD. TOTAL DE ITENS").FontSize(6);
                        r.ConstantItem(50).AlignRight().Text(nota.Itens.Sum(i => i.Quantidade).ToString("N2")).FontSize(6);
                    });
                    if (nota.ValorDesconto > 0)
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text("DESCONTOS").FontSize(6);
                            r.ConstantItem(50).AlignRight().Text($"R$ {nota.ValorDesconto:N2}").FontSize(6);
                        });
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("VALOR TOTAL R$").Bold().FontSize(8);
                        r.ConstantItem(60).AlignRight().Text(nota.ValorTotal.ToString("N2")).Bold().FontSize(10).FontColor(Colors.Green.Darken2);
                    });
                    col.Item().Text($"FORMA DE PAGAMENTO: {DescreverFormaPagamento(nota.XmlEnvio, nota.XmlRetorno)}").FontSize(6);

                    col.Item().PaddingVertical(2).LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);

                    // ── Consumidor ────────────────────────────────────────
                    col.Item().Text("CONSUMIDOR").FontSize(5).FontColor(Colors.Grey.Darken2).Bold();
                    col.Item().Text(cliente is not null
                        ? $"{cliente.Nome} — {FormatDoc(cliente.CpfCnpj)}"
                        : "CPF/CNPJ não informado").FontSize(6);

                    col.Item().PaddingVertical(2).LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);

                    // ── Chave de acesso e protocolo ───────────────────────
                    col.Item().AlignCenter().Text("CHAVE DE ACESSO").FontSize(5).FontColor(Colors.Grey.Darken2);
                    col.Item().AlignCenter().Text(FormatChave(nota.ChaveAcesso)).FontFamily("Courier New").FontSize(6).Bold();
                    col.Item().AlignCenter().Text($"N° {nota.Numero:D9}  Série {nota.Serie}  —  {nota.EmitidaEm.ToLocalTime():dd/MM/yyyy HH:mm:ss}").FontSize(5);
                    if (!string.IsNullOrWhiteSpace(nota.Protocolo))
                        col.Item().AlignCenter().Text($"Protocolo de autorização: {nota.Protocolo}").FontSize(5);
                    if (nota.Status != StatusNota.Autorizada)
                        col.Item().AlignCenter().Text($"⚠ {nota.XMotivo}").FontColor(Colors.Red.Medium).Bold().FontSize(6);

                    // ── QR Code ───────────────────────────────────────────
                    if (qrImagem is not null)
                    {
                        col.Item().PaddingTop(4).AlignCenter().Width(120).Image(qrImagem);
                        col.Item().AlignCenter().Text("Consulte pela Chave de Acesso em:").FontSize(5);
                        if (!string.IsNullOrWhiteSpace(urlConsulta))
                            col.Item().AlignCenter().Text(urlConsulta!).FontSize(5);
                    }

                    if (homologacao)
                        col.Item().PaddingTop(4).Background(Colors.Yellow.Lighten3).Padding(3)
                            .Text("*** EMITIDA EM AMBIENTE DE HOMOLOGAÇÃO — SEM VALOR FISCAL ***")
                            .Bold().FontSize(6).FontColor(Colors.Red.Medium).AlignCenter();

                    col.Item().PaddingTop(4).AlignCenter()
                        .Text("Tributos Totais Incidentes (Lei Federal 12.741/2012): consulte o cupom completo / portal do contribuinte.")
                        .FontSize(4.5f).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static string DescreverFormaPagamento(string? xmlEnvio, string? xmlRetorno)
    {
        var xml = string.IsNullOrWhiteSpace(xmlRetorno) ? xmlEnvio : xmlRetorno;
        if (string.IsNullOrWhiteSpace(xml)) return "Não informada";

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var tPag = doc.SelectSingleNode("//*[local-name()='detPag']/*[local-name()='tPag']")?.InnerText;
            return tPag switch
            {
                "01" => "Dinheiro",
                "02" => "Cheque",
                "03" => "Cartão de Crédito",
                "04" => "Cartão de Débito",
                "05" => "Crédito Loja",
                "10" => "Vale Alimentação",
                "11" => "Vale Refeição",
                "12" => "Vale Presente",
                "13" => "Vale Combustível",
                "15" => "Boleto Bancário",
                "17" => "PIX",
                "99" => "Outros",
                null or "" => "Não informada",
                _ => $"Código {tPag}"
            };
        }
        catch
        {
            return "Não informada";
        }
    }

    private byte[]? GerarImagemQrCode(string? conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo))
            return null;

        try
        {
            using var geradorQr = new QRCoder.QRCodeGenerator();
            using var dadosQr = geradorQr.CreateQrCode(conteudo, QRCoder.QRCodeGenerator.ECCLevel.M);
            var pngQr = new QRCoder.PngByteQRCode(dadosQr);
            return pngQr.GetGraphic(8);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DANFE] Falha ao gerar QR Code da NFC-e.");
            return null;
        }
    }

    private static string? ExtrairTextoDaNFe(string? xmlEnvio, string? xmlRetorno, string localName)
    {
        var xml = string.IsNullOrWhiteSpace(xmlRetorno) ? xmlEnvio : xmlRetorno;
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return LerPrimeiroTexto(doc, localName);
        }
        catch
        {
            return null;
        }
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

    private static DanfeTributosExtras ExtrairTributosExtrasDaNFe(string? xmlEnvio, string? xmlRetorno)
    {
        var xml = string.IsNullOrWhiteSpace(xmlRetorno) ? xmlEnvio : xmlRetorno;
        if (string.IsNullOrWhiteSpace(xml))
            return DanfeTributosExtras.Vazio;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var baseIbs = SomarNodesDecimal(doc, "vBCIBS", "vBCIBSCBS", "vBCIBSMono", "vBCIBSUF");
            var valorIbs = SomarNodesDecimal(doc, "vIBS", "vIBSUF", "vIBSMun", "vIBSMono");
            var baseCbs = SomarNodesDecimal(doc, "vBCCBS", "vBCIBSCBS", "vBCCBSMono");
            var valorCbs = SomarNodesDecimal(doc, "vCBS", "vCBSMono");
            var valorTotalTributos = SomarNodesDecimal(doc, "vTotTrib");
            var reservadoAoFisco = LerPrimeiroTexto(doc, "infAdFisco");

            return new DanfeTributosExtras(baseIbs, valorIbs, baseCbs, valorCbs, valorTotalTributos, reservadoAoFisco);
        }
        catch
        {
            return DanfeTributosExtras.Vazio;
        }
    }

    private static decimal SomarNodesDecimal(XmlDocument doc, params string[] localNames)
    {
        decimal soma = 0m;
        foreach (var localName in localNames)
        {
            var nodes = doc.SelectNodes($"//*[local-name()='{localName}']");
            if (nodes is null) continue;

            foreach (XmlNode node in nodes)
            {
                if (node is null || string.IsNullOrWhiteSpace(node.InnerText))
                    continue;

                if (decimal.TryParse(node.InnerText, NumberStyles.Any, CultureInfo.InvariantCulture, out var valor))
                    soma += valor;
            }
        }
        return soma;
    }

    private static string DescricaoFrete(string? codigo) => codigo switch
    {
        "0" => "0 - Por Conta do Emitente",
        "1" => "1 - Por Conta do Destinatário",
        "2" => "2 - Por Conta de Terceiros",
        "3" => "3 - Próprio por Conta do Remetente",
        "4" => "4 - Próprio por Conta do Destinatário",
        "9" => "9 - Sem Frete",
        _   => $"{codigo} - Sem Frete"
    };

    private static string DescricaoPagamento(string? codigo) => codigo switch
    {
        "01" => "Dinheiro",
        "02" => "Cheque",
        "03" => "Cartão de Crédito",
        "04" => "Cartão de Débito",
        "15" => "Boleto Bancário",
        "17" => "Pix",
        "90" => "Sem Pagamento",
        "99" => "Outros",
        _    => $"Código {codigo}"
    };

    private static string? LerPrimeiroTexto(XmlDocument doc, string localName)
    {
        var node = doc.SelectSingleNode($"//*[local-name()='{localName}']");
        if (node is null || string.IsNullOrWhiteSpace(node.InnerText))
            return null;

        return node.InnerText.Trim();
    }

    private sealed record DanfeTributosExtras(
        decimal BaseIbs,
        decimal ValorIbs,
        decimal BaseCbs,
        decimal ValorCbs,
        decimal ValorTotalTributos,
        string? ReservadoAoFisco)
    {
        public static DanfeTributosExtras Vazio => new(0m, 0m, 0m, 0m, 0m, null);
    }
}

// Projection helpers (internal use only)
internal static class Entidades
{
    internal record ClienteNome(
        string Nome, string CpfCnpj, string Logradouro, string Numero,
        string Bairro, string Municipio, string UF, string CEP, string? IE);
}
