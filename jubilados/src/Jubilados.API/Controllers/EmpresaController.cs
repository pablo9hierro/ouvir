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
        // Não retornar o certificado em GET público
        empresa.CertificadoBase64 = null;
        empresa.CertificadoSenha = null;
        return Ok(empresa);
    }

    [HttpPost]
    public async Task<IActionResult> CriarAsync([FromBody] Empresa empresa, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(empresa.CNPJ) || string.IsNullOrWhiteSpace(empresa.RazaoSocial))
            return BadRequest(new { erro = "CNPJ e RazaoSocial são obrigatórios." });

        try
        {
            empresa.Id = Guid.NewGuid();
            _db.Empresas.Add(empresa);
            await _db.SaveChangesAsync(ct);
            return Ok(new { empresa.Id });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            _logger.LogError(ex, "Erro ao cadastrar empresa CNPJ={CNPJ}", empresa.CNPJ);
            var msg = ex.InnerException?.Message ?? ex.Message;
            if (msg.Contains("unique", StringComparison.OrdinalIgnoreCase) || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { erro = "Já existe uma empresa cadastrada com este CNPJ." });
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

}

public record AtualizarCertificadoRequest(string Base64, string Senha, DateTime? Validade);
