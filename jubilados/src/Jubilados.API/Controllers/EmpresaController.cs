using Jubilados.Domain.Entities;
using Jubilados.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jubilados.API.Controllers;

/// <summary>
/// CRUD básico de Empresas.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class EmpresaController : ControllerBase
{
    private readonly JubiladosDbContext _db;
    private readonly ILogger<EmpresaController> _logger;

    public EmpresaController(JubiladosDbContext db, ILogger<EmpresaController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> ListarAsync(CancellationToken ct)
    {
        try
        {
            var empresas = await _db.Empresas
                .AsNoTracking()
                .Select(e => new { e.Id, e.CNPJ, e.RazaoSocial, e.Email, e.CertificadoValidade })
                .ToListAsync(ct);
            return Ok(empresas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar empresas");
            return StatusCode(500, new { erro = ex.Message, detalhe = ex.InnerException?.Message, tipo = ex.GetType().FullName });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterAsync(Guid id, CancellationToken ct)
    {
        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (empresa is null) return NotFound();
        return Ok(new {
            empresa.Id,
            empresa.CNPJ,
            empresa.RazaoSocial,
            empresa.NomeFantasia,
            empresa.InscricaoEstadual,
            empresa.Logradouro,
            empresa.Numero,
            empresa.Complemento,
            empresa.Bairro,
            empresa.Municipio,
            empresa.UF,
            empresa.CEP,
            empresa.Telefone,
            empresa.Email,
            empresa.CertificadoValidade,
            empresa.CriadoEm,
            empresa.AtualizadoEm,
            certificadoSalvo = empresa.CertificadoBase64 != null,
            cscSalvo = empresa.NfceCscId != null
        });
    }

    [HttpPost]
    public async Task<IActionResult> CriarAsync([FromBody] AtualizarEmpresaRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CNPJ) || string.IsNullOrWhiteSpace(req.RazaoSocial))
            return BadRequest(new { erro = "CNPJ e RazaoSocial sao obrigatorios." });

        var cnpjLimpo = new string(req.CNPJ.Where(char.IsDigit).ToArray()).PadLeft(14, '0');

        try
        {
            var empresa = new Empresa
            {
                Id                            = Guid.NewGuid(),
                CNPJ                          = cnpjLimpo,
                RazaoSocial                   = req.RazaoSocial?.Trim() ?? string.Empty,
                NomeFantasia                  = req.NomeFantasia?.Trim() ?? string.Empty,
                InscricaoEstadual             = req.InscricaoEstadual?.Trim() ?? string.Empty,
                Logradouro                    = req.Logradouro?.Trim() ?? string.Empty,
                Numero                        = req.Numero?.Trim() ?? string.Empty,
                Complemento                   = req.Complemento?.Trim() ?? string.Empty,
                Bairro                        = req.Bairro?.Trim() ?? string.Empty,
                Municipio                     = req.Municipio?.Trim() ?? string.Empty,
                UF                            = req.UF?.Trim().ToUpper() ?? string.Empty,
                CEP                           = new string((req.CEP ?? "").Where(char.IsDigit).ToArray()),
                Telefone                      = req.Telefone?.Trim() ?? string.Empty,
                Email                         = req.Email?.Trim().ToLower() ?? string.Empty,
                Ramo                          = req.Ramo,
                RegimeTributario              = req.RegimeTributario,
                CRT                           = req.CRT > 0 ? req.CRT : 1,
                FaixaSimples                  = req.FaixaSimples,
                EmiteNfse                     = req.EmiteNfse ?? false,
                InscricaoMunicipal            = req.InscricaoMunicipal?.Trim(),
                InscritoSuframa               = req.InscritoSuframa ?? false,
                ContadorNome                  = req.ContadorNome?.Trim(),
                ContadorCrc                   = req.ContadorCrc?.Trim(),
                ContadorEmail                 = req.ContadorEmail?.Trim().ToLower(),
                AliquotaPis                   = req.AliquotaPis ?? 0.65m,
                AliquotaCofins                = req.AliquotaCofins ?? 3.00m,
                CsosnPadrao                   = req.CsosnPadrao ?? "400",
                CstIcmsPadrao                 = req.CstIcmsPadrao,
                CstPisPadrao                  = req.CstPisPadrao,
                CstCofinsPadrao               = req.CstCofinsPadrao,
                OperaComoSubstitutoTributario = req.OperaComoSubstitutoTributario ?? false,
                MvaPadrao                     = req.MvaPadrao,
                PossuiStBebidas               = req.PossuiStBebidas ?? false,
                CNAE                          = req.CNAE?.Trim(),
                CriadoEm                      = DateTime.UtcNow,
                AtualizadoEm                  = DateTime.UtcNow
            };
            _db.Empresas.Add(empresa);
            await _db.SaveChangesAsync(ct);
            return Ok(new { empresa.Id });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            _logger.LogError(ex, "Erro ao cadastrar empresa CNPJ={CNPJ}", cnpjLimpo);
            var msg = ex.InnerException?.Message ?? ex.Message;
            if (msg.Contains("unique", StringComparison.OrdinalIgnoreCase) || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { erro = "Ja existe uma empresa cadastrada com este CNPJ." });
            return StatusCode(500, new { erro = "Erro ao salvar empresa: " + msg });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao cadastrar empresa");
            return StatusCode(500, new { erro = ex.Message });
        }
    }

    [HttpPut("{id:guid}/certificado")]
    public async Task<IActionResult> AtualizarCertificadoAsync(
        Guid id, [FromBody] AtualizarCertificadoRequest req, CancellationToken ct)
    {
        try
        {
            var empresa = await _db.Empresas.FindAsync(new object[] { id }, ct);
            if (empresa is null) return NotFound();

            empresa.CertificadoBase64 = req.Base64;
            empresa.CertificadoSenha = req.Senha;
            empresa.CertificadoValidade = req.Validade;
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao salvar certificado empresa {Id}", id);
            return StatusCode(500, new { erro = ex.Message, detalhe = ex.InnerException?.Message });
        }
    }

    [HttpPut("{id:guid}/csc")]
    public async Task<IActionResult> AtualizarCscAsync(
        Guid id, [FromBody] AtualizarCscRequest req, CancellationToken ct)
    {
        var empresa = await _db.Empresas.FindAsync(new object[] { id }, ct);
        if (empresa is null) return NotFound();
        empresa.NfceCscId = req.CscId;
        empresa.NfceCscToken = req.CscToken;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// GET /api/empresa/por-cnpj/{cnpj}
    /// Busca empresa pelo CNPJ (14 dígitos, sem máscara).
    /// </summary>
    [HttpGet("por-cnpj/{cnpj}")]
    public async Task<IActionResult> BuscarPorCnpjAsync(string cnpj, CancellationToken ct)
    {
        var cnpjLimpo = new string(cnpj.Where(char.IsDigit).ToArray()).PadLeft(14, '0');
        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e =>
                e.CNPJ == cnpjLimpo ||
                e.CNPJ.Replace(".", "").Replace("/", "").Replace("-", "") == cnpjLimpo, ct);
        if (empresa is null) return NotFound(new { erro = "Empresa não encontrada com este CNPJ." });
        return Ok(new { empresa.Id, empresa.CNPJ, empresa.RazaoSocial, empresa.NomeFantasia,
            empresa.InscricaoEstadual, empresa.Logradouro, empresa.Numero, empresa.Complemento,
            empresa.Bairro, empresa.Municipio, empresa.UF, empresa.CEP,
            empresa.Telefone, empresa.Email, empresa.CRT, empresa.RegimeTributario, empresa.Ramo });
    }

    /// <summary>
    /// PUT /api/empresa/{id}
    /// Atualiza os dados cadastrais completos de uma empresa.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> AtualizarAsync(Guid id, [FromBody] AtualizarEmpresaRequest req, CancellationToken ct)
    {
        var empresa = await _db.Empresas.FindAsync(new object[] { id }, ct);
        if (empresa is null) return NotFound();

        empresa.CNPJ                         = new string(req.CNPJ.Where(char.IsDigit).ToArray()).PadLeft(14, '0');
        empresa.RazaoSocial                  = req.RazaoSocial?.Trim() ?? empresa.RazaoSocial;
        empresa.NomeFantasia                 = req.NomeFantasia?.Trim() ?? empresa.NomeFantasia;
        empresa.InscricaoEstadual            = req.InscricaoEstadual?.Trim() ?? empresa.InscricaoEstadual;
        empresa.Logradouro                   = req.Logradouro?.Trim() ?? empresa.Logradouro;
        empresa.Numero                       = req.Numero?.Trim() ?? empresa.Numero;
        empresa.Complemento                  = req.Complemento?.Trim() ?? empresa.Complemento;
        empresa.Bairro                       = req.Bairro?.Trim() ?? empresa.Bairro;
        empresa.Municipio                    = req.Municipio?.Trim() ?? empresa.Municipio;
        empresa.UF                           = req.UF?.Trim().ToUpper() ?? empresa.UF;
        empresa.CEP                          = new string((req.CEP ?? "").Where(char.IsDigit).ToArray());
        empresa.Telefone                     = req.Telefone?.Trim() ?? empresa.Telefone;
        empresa.Email                        = req.Email?.Trim().ToLower() ?? empresa.Email;
        empresa.Ramo                         = req.Ramo ?? empresa.Ramo;
        empresa.RegimeTributario             = req.RegimeTributario ?? empresa.RegimeTributario;
        empresa.CRT                          = req.CRT > 0 ? req.CRT : empresa.CRT;
        empresa.FaixaSimples                 = req.FaixaSimples ?? empresa.FaixaSimples;
        empresa.EmiteNfse                    = req.EmiteNfse ?? empresa.EmiteNfse;
        empresa.InscricaoMunicipal           = req.InscricaoMunicipal?.Trim() ?? empresa.InscricaoMunicipal;
        empresa.InscritoSuframa              = req.InscritoSuframa ?? empresa.InscritoSuframa;
        empresa.ContadorNome                 = req.ContadorNome?.Trim() ?? empresa.ContadorNome;
        empresa.ContadorCrc                  = req.ContadorCrc?.Trim() ?? empresa.ContadorCrc;
        empresa.ContadorEmail                = req.ContadorEmail?.Trim().ToLower() ?? empresa.ContadorEmail;
        empresa.AliquotaPis                  = req.AliquotaPis ?? empresa.AliquotaPis;
        empresa.AliquotaCofins               = req.AliquotaCofins ?? empresa.AliquotaCofins;
        empresa.CsosnPadrao                  = req.CsosnPadrao ?? empresa.CsosnPadrao;
        empresa.CstIcmsPadrao                = req.CstIcmsPadrao ?? empresa.CstIcmsPadrao;
        empresa.CstPisPadrao                 = req.CstPisPadrao ?? empresa.CstPisPadrao;
        empresa.CstCofinsPadrao              = req.CstCofinsPadrao ?? empresa.CstCofinsPadrao;
        empresa.OperaComoSubstitutoTributario = req.OperaComoSubstitutoTributario ?? empresa.OperaComoSubstitutoTributario;
        empresa.MvaPadrao                    = req.MvaPadrao ?? empresa.MvaPadrao;
        empresa.PossuiStBebidas              = req.PossuiStBebidas ?? empresa.PossuiStBebidas;
        empresa.CNAE                         = req.CNAE?.Trim() ?? empresa.CNAE;
        empresa.AtualizadoEm                 = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new { empresa.Id });
    }

    /// <summary>
    /// GET /api/empresa/{id}/alerta-certificado
    /// Retorna alerta sobre validade do certificado digital.
    /// </summary>
    [HttpGet("{id:guid}/alerta-certificado")]
    public async Task<IActionResult> AlertaCertificadoAsync(Guid id, CancellationToken ct)
    {
        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (empresa is null) return NotFound();

        if (empresa.CertificadoValidade is null)
            return Ok(new { expiradoOuFaltando = true, diasRestantes = 0, mensagem = "Sem data de validade cadastrada." });

        var dias = (int)(empresa.CertificadoValidade.Value - DateTime.UtcNow).TotalDays;
        var expirado = dias <= 30;
        string? mensagem = dias < 0
            ? $"Certificado EXPIRADO há {-dias} dias!"
            : dias <= 30
                ? $"Certificado expira em {dias} dias!"
                : null;

        return Ok(new { expiradoOuFaltando = expirado, diasRestantes = dias, mensagem });
    }
}

public record AtualizarCertificadoRequest(string Base64, string Senha, DateTime? Validade);
public record AtualizarCscRequest(string CscId, string CscToken);

public record AtualizarEmpresaRequest(
    string CNPJ, string? RazaoSocial, string? NomeFantasia, string? InscricaoEstadual,
    string? Logradouro, string? Numero, string? Complemento, string? Bairro,
    string? Municipio, string? UF, string? CEP, string? Telefone, string? Email,
    string? Ramo, string? RegimeTributario, int CRT, string? FaixaSimples,
    bool? EmiteNfse, string? InscricaoMunicipal, bool? InscritoSuframa,
    string? ContadorNome, string? ContadorCrc, string? ContadorEmail,
    decimal? AliquotaPis, decimal? AliquotaCofins,
    string? CsosnPadrao, string? CstIcmsPadrao, string? CstPisPadrao, string? CstCofinsPadrao,
    bool? OperaComoSubstitutoTributario, decimal? MvaPadrao, bool? PossuiStBebidas,
    string? CNAE);
