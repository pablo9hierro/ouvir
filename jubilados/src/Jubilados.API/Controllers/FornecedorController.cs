using Jubilados.Domain.Entities;
using Jubilados.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jubilados.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class FornecedorController : ControllerBase
{
    private readonly JubiladosDbContext _db;

    public FornecedorController(JubiladosDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> ListarAsync([FromQuery] Guid empresaId, CancellationToken ct)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });

        var fornecedores = await _db.Fornecedores
            .AsNoTracking()
            .Where(f => f.EmpresaId == empresaId)
            .OrderBy(f => f.Nome)
            .Select(f => new
            {
                f.Id,
                f.Nome,
                cpf_CNPJ = f.CPF_CNPJ,
                f.InscricaoEstadual,
                f.Email,
                f.Telefone,
                f.Logradouro,
                f.Numero,
                f.Complemento,
                f.Bairro,
                f.Municipio,
                f.CodigoMunicipio,
                f.UF,
                f.CEP
            })
            .ToListAsync(ct);

        return Ok(fornecedores);
    }

    [HttpPost]
    public async Task<IActionResult> CriarAsync([FromBody] Fornecedor fornecedor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fornecedor.Nome) || string.IsNullOrWhiteSpace(fornecedor.CPF_CNPJ))
            return BadRequest(new { erro = "Nome e CPF/CNPJ são obrigatórios." });

        if (fornecedor.EmpresaId == Guid.Empty)
            return BadRequest(new { erro = "EmpresaId é obrigatório." });

        fornecedor.Id = Guid.NewGuid();
        fornecedor.CriadoEm = DateTime.UtcNow;
        fornecedor.AtualizadoEm = DateTime.UtcNow;

        try
        {
            _db.Fornecedores.Add(fornecedor);
            await _db.SaveChangesAsync(ct);

            return Ok(new { fornecedor.Id, fornecedor.Nome });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { erro = ex.InnerException?.Message ?? ex.Message });
        }
    }
}