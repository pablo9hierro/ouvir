using System.Text;
using Jubilados.Application.DTOs;
using Jubilados.Application.Interfaces;
using Jubilados.Domain.Enums;
using Jubilados.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jubilados.Infrastructure.Services;

/// <summary>
/// Gera arquivo EFD Contribuições (PIS/COFINS) – ATO COTEPE/PIS-COFINS nº 36/2019.
/// Layout simplificado: Blocos 0, C, M, 9.
/// </summary>
public class SpedContribuicoesService : ISpedContribuicoesService
{
    private readonly JubiladosDbContext _db;
    private readonly ILogger<SpedContribuicoesService> _logger;

    public SpedContribuicoesService(JubiladosDbContext db, ILogger<SpedContribuicoesService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> GerarEfdContribuicoesAsync(
        SpedContribuicoesDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[EFD-Contrib] Gerando de {Ini} a {Fim}", dto.DataInicio, dto.DataFim);

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

        var sb = new StringBuilder();
        var contadores = new Dictionary<string, int>();

        void Add(string reg, params string[] campos)
        {
            sb.AppendLine($"|{reg}|{string.Join("|", campos)}|");
            contadores[reg] = contadores.GetValueOrDefault(reg) + 1;
        }

        var cnpj = new string(empresa.CNPJ.Where(char.IsDigit).ToArray()).PadLeft(14, '0');
        var dInicio = dto.DataInicio.ToString("ddMMyyyy");
        var dFim    = dto.DataFim.ToString("ddMMyyyy");
        var dtGer   = DateTime.Now.ToString("ddMMyyyy");

        // indRegCum: 01=não-cumulativo (Lucro Real), 02=cumulativo (Lucro Presumido/Simples)
        var indRegCum = empresa.CRT == 1 ? "02" : "01";

        // ── BLOCO 0 ──────────────────────────────────────────────────────────
        Add("0000",
            "006",              // VER_LEIAUTE
            "0",                // TIPO_ESCRIT (0=original)
            "0",                // IND_SIT_ESP
            dtGer, dInicio, dFim,
            empresa.RazaoSocial, cnpj,
            empresa.UF,
            empresa.InscricaoEstadual,
            "",                 // CNPJ_RAI
            indRegCum,
            "0",                // IND_APRO_CRED (0=regime competência)
            "",                 // COD_TIPO_CONT
            "",                 // IND_REG_CUM
            "0"                 // IND_OPT_ENTR_EFD (0=não optante)
        );
        Add("0001", "0");
        Add("0100",
            empresa.NomeFantasia ?? empresa.RazaoSocial,
            cnpj, "",
            empresa.Logradouro, empresa.Numero, empresa.Complemento ?? "", empresa.Bairro,
            empresa.Municipio, empresa.UF, empresa.CEP.Replace("-", ""), empresa.Telefone ?? "",
            empresa.Email ?? "");
        Add("0990", contadores.Values.Sum().ToString());

        // ── BLOCO C: Documentos Fiscais ───────────────────────────────────────
        Add("C001", "0");
        decimal totalReceitaPis = 0, totalReceitaCofins = 0;
        decimal totalVlPis = 0, totalVlCofins = 0;

        foreach (var nota in notas)
        {
            var aliqPis   = (double)empresa.AliquotaPis;
            var aliqCofins = (double)empresa.AliquotaCofins;
            var vlBc      = (double)nota.ValorProdutos;
            var vlPis     = Math.Round(vlBc * aliqPis / 100, 2);
            var vlCofins  = Math.Round(vlBc * aliqCofins / 100, 2);

            // CST PIS/COFINS: 01=tributado alíquota básica, 07=isento
            var cst = empresa.CRT == 1 ? "07" : "01";

            Add("C010",
                cnpj, "55",
                nota.Serie, nota.Numero.ToString(),
                nota.EmitidaEm.ToLocalTime().ToString("ddMMyyyy"),
                nota.ValorTotal.ToString("F2"),
                "0");  // IND_EMIT

            Add("C100",
                "1",  // IND_OPER
                "1",  // IND_EMIT
                "",   // COD_PART
                "55", "00",
                nota.Serie, nota.Numero.ToString(),
                nota.ChaveAcesso,
                nota.EmitidaEm.ToLocalTime().ToString("ddMMyyyy"),
                nota.AutorizadaEm?.ToLocalTime().ToString("ddMMyyyy") ?? "",
                nota.ValorTotal.ToString("F2"),
                "0",  // IND_PGTO
                nota.ValorDesconto.ToString("F2"),
                nota.ValorDesconto.ToString("F2"),
                "0", nota.ValorFrete.ToString("F2"), nota.ValorSeguro.ToString("F2"),
                nota.ValorOutros.ToString("F2"),
                nota.ValorICMS.ToString("F2"), nota.ValorICMS.ToString("F2"),
                "0.00", "0.00",
                nota.ValorIPI.ToString("F2"),
                nota.ValorPIS.ToString("F2"),
                nota.ValorCOFINS.ToString("F2"),
                "0.00", "0.00");

            Add("C170",
                "1",
                nota.ValorProdutos.ToString("F2"),
                cst,
                vlBc.ToString("F2"),
                aliqPis.ToString("F4"),
                vlPis.ToString("F2"),
                cst,
                vlBc.ToString("F2"),
                aliqCofins.ToString("F4"),
                vlCofins.ToString("F2"),
                "");

            totalReceitaPis += nota.ValorProdutos;
            totalReceitaCofins += nota.ValorProdutos;
            totalVlPis += (decimal)vlPis;
            totalVlCofins += (decimal)vlCofins;
        }

        Add("C990", contadores.Where(x => x.Key.StartsWith("C")).Sum(x => x.Value).ToString());

        // ── BLOCO M: Apuração PIS/COFINS ─────────────────────────────────────
        Add("M001", "0");

        // M200 – Apuração PIS (cumulativo ou não-cumulativo)
        Add("M200",
            totalVlPis.ToString("F2"),  // VL_TOT_CONT_NC_PER
            "0.00", "0.00", "0.00", "0.00", "0.00", "0.00", "0.00",
            totalVlPis.ToString("F2"),  // VL_TOT_CONT_CUM_PER
            "0.00",
            totalVlPis.ToString("F2"),  // VL_TOT_CONT_APUR
            "0.00",                     // VL_TOT_CRED
            totalVlPis.ToString("F2"),  // VL_TOT_CONT_NT_DED
            "0.00",                     // VL_TOT_CONT_EXT
            totalVlPis.ToString("F2"),  // VL_TOT_CONT_PER
            "0.00",
            totalVlPis.ToString("F2")   // VL_CONT_PER
        );

        Add("M205",
            totalReceitaPis.ToString("F2"),     // VL_TOT_REC
            totalVlPis.ToString("F2"),           // COD_REC
            totalVlPis.ToString("F2"));

        // M600 – Apuração COFINS
        Add("M600",
            totalVlCofins.ToString("F2"),
            "0.00", "0.00", "0.00", "0.00", "0.00", "0.00", "0.00",
            totalVlCofins.ToString("F2"),
            "0.00",
            totalVlCofins.ToString("F2"),
            "0.00",
            totalVlCofins.ToString("F2"),
            "0.00",
            totalVlCofins.ToString("F2"),
            "0.00",
            totalVlCofins.ToString("F2")
        );

        Add("M605",
            totalReceitaCofins.ToString("F2"),
            totalVlCofins.ToString("F2"),
            totalVlCofins.ToString("F2"));

        Add("M990", contadores.Where(x => x.Key.StartsWith("M")).Sum(x => x.Value).ToString());

        // ── BLOCO 9: Encerramento ─────────────────────────────────────────────
        Add("9001", "0");
        foreach (var (reg, qtd) in contadores.OrderBy(x => x.Key))
            Add("9900", reg, qtd.ToString());
        Add("9900", "9900", (contadores.Count + 3).ToString());
        Add("9900", "9990", "1");
        Add("9900", "9999", "1");
        var totalLinhas = sb.ToString().Split('\n').Count(l => l.StartsWith('|'));
        Add("9990", (totalLinhas + 1).ToString());
        Add("9999", (totalLinhas + 2).ToString());

        return sb.ToString();
    }
}
