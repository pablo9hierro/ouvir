using Jubilados.Domain.Entities;
using Jubilados.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jubilados.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ProdutoController : ControllerBase
{
    private readonly JubiladosDbContext _db;

    public ProdutoController(JubiladosDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> ListarAsync([FromQuery] Guid empresaId, CancellationToken ct)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });

        var produtos = await _db.Produtos
            .AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.Ativo)
            .Select(p => new { p.Id, p.Nome, p.NCM, p.CFOP, p.Preco, p.Unidade })
            .ToListAsync(ct);

        return Ok(produtos);
    }

    [HttpPost]
    public async Task<IActionResult> CriarAsync([FromBody] Produto produto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(produto.Nome) || string.IsNullOrWhiteSpace(produto.NCM))
            return BadRequest(new { erro = "Nome e NCM são obrigatórios." });

        if (produto.EmpresaId == Guid.Empty)
            return BadRequest(new { erro = "EmpresaId é obrigatório." });

        produto.Id = Guid.NewGuid();
        produto.Ativo = true;
        produto.CriadoEm = DateTime.UtcNow;
        produto.AtualizadoEm = DateTime.UtcNow;

        try
        {
            _db.Produtos.Add(produto);
            await _db.SaveChangesAsync(ct);
            return Ok(new { produto.Id, produto.Nome });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { erro = ex.InnerException?.Message ?? ex.Message });
        }
    }
}
