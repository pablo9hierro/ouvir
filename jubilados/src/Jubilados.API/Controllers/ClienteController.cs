using Jubilados.Domain.Entities;
using Jubilados.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jubilados.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ClienteController : ControllerBase
{
    private readonly JubiladosDbContext _db;

    public ClienteController(JubiladosDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> ListarAsync([FromQuery] Guid empresaId, CancellationToken ct)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });

        var clientes = await _db.Clientes
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .Select(c => new { c.Id, c.Nome, c.CPF_CNPJ, c.Email })
            .ToListAsync(ct);

        return Ok(clientes);
    }

    [HttpPost]
    public async Task<IActionResult> CriarAsync([FromBody] Cliente cliente, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cliente.Nome) || string.IsNullOrWhiteSpace(cliente.CPF_CNPJ))
            return BadRequest(new { erro = "Nome e CPF/CNPJ são obrigatórios." });

        if (cliente.EmpresaId == Guid.Empty)
            return BadRequest(new { erro = "EmpresaId é obrigatório." });

        try
        {
            cliente.Id = Guid.NewGuid();
            cliente.CriadoEm = DateTime.UtcNow;
            cliente.AtualizadoEm = DateTime.UtcNow;

            _db.Clientes.Add(cliente);
            await _db.SaveChangesAsync(ct);

            return Ok(new { cliente.Id, cliente.Nome });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { erro = ex.InnerException?.Message ?? ex.Message });
        }
    }
}
