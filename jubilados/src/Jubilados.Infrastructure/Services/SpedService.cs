using System.Text;
using Jubilados.Application.DTOs;
using Jubilados.Application.Interfaces;
using Jubilados.Domain.Enums;
using Jubilados.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jubilados.Infrastructure.Services;

/// <summary>
/// Gera arquivo SPED EFD ICMS/IPI (ATO COTEPE/ICMS nº 09/2008 e alterações).
/// Layout simplificado para Simples Nacional (CRT=1), incluindo:
///   Bloco 0: Abertura e identificação
///   Bloco C: Documentos fiscais (NF-e modelo 55)
///   Bloco 9: Encerramento e totais de registros
/// Prazo legal: mensal — entrega até o dia 15 do mês seguinte (PB / regra geral SEFAZ).
/// </summary>
public class SpedService : ISpedService
{
    private readonly JubiladosDbContext _db;
    private readonly ILogger<SpedService> _logger;

    public SpedService(JubiladosDbContext db, ILogger<SpedService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> GerarSpedAsync(SpedDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SPED] Gerando EFD de {Ini} a {Fim}", dto.DataInicio, dto.DataFim);

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {dto.EmpresaId} não encontrada.");

        var notas = await _db.NotasFiscais
            .Include(n => n.Itens)
            .AsNoTracking()
            .Where(n => n.EmpresaId == dto.EmpresaId
                        && n.CStat == "100"
                        && n.EmitidaEm >= dto.DataInicio.ToUniversalTime()
                        && n.EmitidaEm <= dto.DataFim.ToUniversalTime())
            .OrderBy(n => n.EmitidaEm)
            .ToListAsync(cancellationToken);

        var produtoIds = notas.SelectMany(n => n.Itens).Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await _db.Produtos.AsNoTracking()
            .Where(p => produtoIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var clienteIds = notas.Where(n => n.ClienteId != Guid.Empty).Select(n => n.ClienteId).Distinct().ToList();
        var clientes = await _db.Clientes.AsNoTracking()
            .Where(c => clienteIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var sb = new StringBuilder();
        var contadores = new Dictionary<string, int>();

        void Add(string registro, params string[] campos)
        {
            var linha = $"|{registro}|{string.Join("|", campos)}|";
            sb.AppendLine(linha);
            contadores[registro] = contadores.GetValueOrDefault(registro) + 1;
        }

        var cnpj = Limpar(empresa.CNPJ).PadLeft(14, '0');
        var dInicio = dto.DataInicio.ToString("ddMMyyyy");
        var dFim    = dto.DataFim.ToString("ddMMyyyy");
        var dtGer   = DateTime.Now.ToString("ddMMyyyy");

        // ── BLOCO 0: Abertura e identificação ──────────────────────────────────
        Add("0000",
            "LEIAUTE 17",           // VER_LEIAUTE
            dtGer,                  // DT_CRI
            dInicio,                // DT_INI
            dFim,                   // DT_FIN
            empresa.RazaoSocial,    // NOME
            cnpj,                   // CNPJ
            "",                     // CPF
            Limpar(empresa.InscricaoEstadual), // UF
            "25",                   // COD_MUN (Paraíba código geral)
            "",                     // SUFRAMA
            "1",                    // IND_PERFIL (A=Perfil A completo, simplificado=C mas usamos A)
            "1"                     // IND_ATIV (1=Industrial/comércio)
        );

        Add("0001", "0"); // Bloco 0 aberto

        Add("0005",
            empresa.NomeFantasia ?? empresa.RazaoSocial, // FANTASIA
            "",       // CEP
            empresa.Logradouro,  // END
            empresa.Numero,      // NUM
            "",                  // COMPL
            empresa.Bairro,      // BAIRRO
            "",                  // FONE
            "",                  // FAX
            "",                  // EMAIL
            empresa.InscricaoEstadual  // IE
        );

        // 0100 – Dados do contabilista (obrigatório — preenchido em branco)
        Add("0100", "", "", "", "", "", "", "", "", "", "", "");

        // 0150 – Participantes (empresa emissora + cada cliente/fornecedor)
        var participanteIdx = new Dictionary<string, string>();
        string NextPart() => (participanteIdx.Count + 1).ToString("D4");

        // Empresa como participante quando ela é destinatária (notas entrada)
        participanteIdx[cnpj] = NextPart();
        Add("0150",
            participanteIdx[cnpj], "1", "", empresa.RazaoSocial, cnpj, "",
            "PB", "25", empresa.Municipio, empresa.InscricaoEstadual, "",
            empresa.Logradouro, empresa.Numero, empresa.Complemento ?? "", empresa.Bairro,
            empresa.CEP.Replace("-", "").Replace(".", ""));

        foreach (var (cliId, cli) in clientes)
        {
            var cliCnpjCpf = Limpar(cli.CPF_CNPJ);
            if (!participanteIdx.ContainsKey(cliCnpjCpf))
            {
                participanteIdx[cliCnpjCpf] = NextPart();
                var indPessoa = cliCnpjCpf.Length == 14 ? "1" : "2";
                Add("0150",
                    participanteIdx[cliCnpjCpf], indPessoa, "", cli.Nome,
                    cliCnpjCpf.Length == 14 ? cliCnpjCpf : "",
                    cliCnpjCpf.Length == 11 ? cliCnpjCpf : "",
                    cli.UF, cli.CodigoMunicipio, cli.Municipio, cli.InscricaoEstadual ?? "",
                    "", cli.Logradouro, cli.Numero, "", cli.Bairro, Limpar(cli.CEP));
            }
        }

        // 0190 – Unidades de medida
        var unidades = produtos.Values.Select(p => p.Unidade).Distinct().ToList();
        foreach (var un in unidades)
            Add("0190", un, $"Unidade {un}");

        // 0200 – Itens (produtos)
        foreach (var prod in produtos.Values)
        {
            Add("0200",
                prod.Id.ToString()[..8].ToUpper(), // COD_ITEM
                prod.Nome,                          // DESCR_ITEM
                "",                                 // COD_BARRA
                "",                                 // COD_ANT_ITEM
                prod.Unidade,                       // UNID_INV
                "00",                               // TIPO_ITEM (Mercadoria para Revenda)
                prod.NCM,                           // COD_NCM
                "",                                 // EX_IPI
                prod.CFOP,                          // COD_GEN (classe NCM — simplificado)
                "",                                 // COD_LST
                prod.Preco.ToString("F2")               // ALIQ_ICMS
            );
        }

        Add("0990", contadores.Values.Sum().ToString()); // fecha bloco 0

        // ── BLOCO C: Notas Fiscais (mod 55) ────────────────────────────────────
        var blocoCCount = 0;
        Add("C001", "0"); blocoCCount++;

        foreach (var nota in notas)
        {
            var indOp  = nota.TipoOperacao == "0" ? "0" : "1"; // 0=entrada, 1=saída
            var cliCnpj = nota.ClienteId != Guid.Empty && clientes.TryGetValue(nota.ClienteId, out var cliN)
                ? Limpar(cliN.CPF_CNPJ)
                : "";
            var partKey = cliCnpj.Length > 0 && participanteIdx.ContainsKey(cliCnpj)
                ? participanteIdx[cliCnpj]
                : "0001";
            var cfopNota = nota.Itens.FirstOrDefault()?.let(i =>
                produtos.TryGetValue(i.ProdutoId, out var pr) ? pr.CFOP : "5102") ?? "5102";

            Add("C100",
                indOp,                                       // IND_OPER
                "1",                                         // IND_EMIT (1=emissão própria)
                partKey,                                     // COD_PART
                "55",                                        // COD_MOD
                "00",                                        // COD_SIT (00=regular)
                nota.Serie,                                  // SER
                nota.Numero.ToString(),                      // NUM_DOC
                nota.ChaveAcesso,                            // CHV_NFE
                nota.EmitidaEm.ToLocalTime().ToString("ddMMyyyy"), // DT_DOC
                nota.AutorizadaEm?.ToLocalTime().ToString("ddMMyyyy") ?? "", // DT_E_S
                nota.ValorTotal.ToString("F2"),              // VL_DOC
                "0",                                         // IND_PGTO (0=à vista)
                nota.ValorDesconto.ToString("F2"),           // VL_ABAT_NT
                nota.ValorDesconto.ToString("F2"),           // VL_DESC
                "0",                                         // IND_FRT (0=emitente)
                nota.ValorFrete.ToString("F2"),              // VL_FRT
                nota.ValorSeguro.ToString("F2"),             // VL_SEG
                nota.ValorOutros.ToString("F2"),             // VL_OUT_DA
                nota.ValorICMS.ToString("F2"),               // VL_BC_ICMS
                nota.ValorICMS.ToString("F2"),               // VL_ICMS
                "0.00",                                      // VL_BC_ICMS_ST
                "0.00",                                      // VL_ICMS_ST
                nota.ValorIPI.ToString("F2"),                // VL_IPI
                nota.ValorPIS.ToString("F2"),                // VL_PIS
                nota.ValorCOFINS.ToString("F2"),             // VL_COFINS
                "0.00",                                      // VL_PIS_ST
                "0.00"                                       // VL_COFINS_ST
            );
            blocoCCount++;

            // C170 – Itens da nota
            foreach (var item in nota.Itens.OrderBy(i => i.NumeroItem))
            {
                var itemProd = produtos.TryGetValue(item.ProdutoId, out var pr) ? pr : null;
                Add("C170",
                    item.NumeroItem.ToString(),              // NUM_ITEM
                    itemProd?.Id.ToString()[..8].ToUpper() ?? "", // COD_ITEM
                    itemProd?.Nome ?? "",                    // DESCR_COMPL
                    item.Quantidade.ToString("F3"),          // QTD
                    item.Unidade,                            // UNID
                    item.ValorUnitario.ToString("F2"),       // VL_ITEM
                    item.ValorDesconto.ToString("F2"),       // VL_DESC
                    itemProd?.CSOSN.PadLeft(3, '0') ?? "400", // IND_MOV / CSOSN
                    "0.00", "0.00",                          // VL_BC_ICMS, VL_ICMS
                    "0.00", "0.00",                          // VL_BC_ICMS_ST, VL_ICMS_ST
                    itemProd?.CFOP ?? "5102",                // CFOP
                    item.ValorTotal.ToString("F2"),          // VL_BC_IPI
                    "0.00",                                  // ALIQ_IPI
                    item.ValorIPI.ToString("F2"),            // VL_IPI
                    item.AliquotaICMS.ToString("F2"),        // ALIQ_ICMS
                    item.BaseICMS.ToString("F2"),            // VL_BC_ICMS (duplicado)
                    item.ValorICMS.ToString("F2"),           // VL_ICMS_OA
                    "0.00"                                   // VL_OUTROS
                );
                blocoCCount++;
            }

            // C190 – Registro analítico (ICMS por CFOP)
            Add("C190",
                cfopNota,                                    // CFOP
                nota.ValorICMS.ToString("F2"),               // CST_ICMS / ALIQ_ICMS (simplificado)
                "0",                                         // ALIQ_ICMS
                "0.00",                                      // VL_BC_ICMS
                "0.00",                                      // VL_ICMS
                "0.00",                                      // VL_BC_ICMS_ST
                "0.00",                                      // VL_ICMS_ST
                nota.ValorIPI.ToString("F2"),                // VL_RED_BC
                nota.ValorProdutos.ToString("F2"),           // VL_OUTROS (vl venda)
                "0.00"                                       // VL_DESC
            );
            blocoCCount++;
        }

        Add("C990", blocoCCount.ToString()); // fecha bloco C

        // ── BLOCO 9: Encerramento ──────────────────────────────────────────────
        Add("9001", "0");

        // 9900 – Totais de registros por tipo
        foreach (var (reg, qtd) in contadores.OrderBy(x => x.Key))
        {
            Add("9900", reg, qtd.ToString());
        }
        Add("9900", "9900", (contadores.Count + 3).ToString()); // linha 9900 se auto-referencia
        Add("9900", "9990", "1");
        Add("9900", "9999", "1");

        var totalLinhas = sb.ToString().Split('\n').Count(l => l.StartsWith('|'));
        Add("9990", (totalLinhas + 1).ToString()); // fecha bloco 9
        Add("9999", (totalLinhas + 2).ToString()); // fim de arquivo

        _logger.LogInformation("[SPED] Gerado com {Total} registros de C100", notas.Count);
        return sb.ToString();
    }

    private static string Limpar(string v) =>
        new(v?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());
}

// extension helper local
internal static class NullableHelper
{
    internal static TResult? let<T, TResult>(this T? v, Func<T, TResult> f) where T : class
        => v is null ? default : f(v);
}
