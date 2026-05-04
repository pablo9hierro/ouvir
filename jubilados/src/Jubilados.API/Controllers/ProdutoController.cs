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
            .OrderBy(p => p.Nome)
            .Select(p => new
            {
                p.Id,
                p.Nome,
                p.Descricao,
                p.NCM,
                p.CFOP,
                p.CST,
                p.CSOSN,
                p.EAN,
                p.Preco,
                p.Unidade,
                p.QuantidadeEstoque
            })
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

    /// <summary>
    /// GET /api/produto/estoque?empresaId={id}&amp;dtIni={date}&amp;dtFim={date}
    /// Retorna saldo de estoque por produto com totais de entradas e saídas via NF-e.
    /// </summary>
    [HttpGet("estoque")]
    public async Task<IActionResult> EstoqueAsync(
        [FromQuery] Guid empresaId,
        [FromQuery] string? dtIni = null,
        [FromQuery] string? dtFim = null,
        CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });

        try
        {
            DateTime? ini = DateTime.TryParse(dtIni, out var d1) ? DateTime.SpecifyKind(d1.Date, DateTimeKind.Utc) : null;
            DateTime? fim = DateTime.TryParse(dtFim, out var d2) ? DateTime.SpecifyKind(d2.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : null;

            var produtos = await _db.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.Ativo)
                .Select(p => new
                {
                    p.Id, p.Nome, p.Unidade, p.Preco, p.NCM, p.QuantidadeEstoque
                })
                .ToListAsync(ct);

            // Carrega movimentações de itens de notas autorizadas da empresa no período
            var itensQuery = _db.NotaItens
                .AsNoTracking()
                .Include(i => i.NotaFiscal)
                .Where(i => i.NotaFiscal.EmpresaId == empresaId
                            && i.NotaFiscal.Status == Jubilados.Domain.Enums.StatusNota.Autorizada);
            if (ini.HasValue) itensQuery = itensQuery.Where(i => i.NotaFiscal.EmitidaEm >= ini.Value);
            if (fim.HasValue) itensQuery = itensQuery.Where(i => i.NotaFiscal.EmitidaEm <= fim.Value);

            var movs = await itensQuery
                .Select(i => new
                {
                    i.ProdutoId,
                    i.Quantidade,
                    TipoOperacao = i.NotaFiscal.TipoOperacao   // "0"=entrada, "1"=saída
                })
                .ToListAsync(ct);

            var entradas = movs.Where(m => m.TipoOperacao == "0")
                .GroupBy(m => m.ProdutoId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantidade));
            var saidas = movs.Where(m => m.TipoOperacao == "1")
                .GroupBy(m => m.ProdutoId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantidade));

            var itens = produtos.Select(p => new
            {
                p.Id,
                p.Nome,
                p.Unidade,
                p.NCM,
                PrecoUnitario = p.Preco,
                SaldoAtual = p.QuantidadeEstoque,
                TotalEntradas = entradas.TryGetValue(p.Id, out var e) ? e : 0m,
                TotalSaidas   = saidas.TryGetValue(p.Id, out var s) ? s : 0m,
                ValorEstoque  = p.QuantidadeEstoque * p.Preco
            }).ToList();

            return Ok(new
            {
                sucesso = true,
                empresaId,
                dtIni, dtFim,
                totalProdutos = itens.Count,
                valorTotalEstoque = itens.Sum(i => i.ValorEstoque),
                itens
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { sucesso = false, erro = ex.InnerException?.Message ?? ex.Message });
        }
    }

    /// <summary>
    /// GET /api/produto/estoque/csv?empresaId={id}&amp;dtIni={date}&amp;dtFim={date}
    /// Exporta o balanço de estoque como CSV (abre no Excel).
    /// </summary>
    [HttpGet("estoque/csv")]
    public async Task<IActionResult> EstoqueCsvAsync(
        [FromQuery] Guid empresaId,
        [FromQuery] string? dtIni = null,
        [FromQuery] string? dtFim = null,
        CancellationToken ct = default)
    {
        var result = await EstoqueAsync(empresaId, dtIni, dtFim, ct) as OkObjectResult;
        if (result is null) return BadRequest();

        dynamic data = result.Value!;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("sep=;");  // hint for Excel PT-BR
        sb.AppendLine("Produto;NCM;Unidade;Preco Unitario;Saldo Atual;Total Entradas (NF);Total Saidas (NF);Valor em Estoque");

        foreach (var i in (IEnumerable<dynamic>)data.itens)
            sb.AppendLine($"{i.Nome};{i.NCM};{i.Unidade};{i.PrecoUnitario:F2};{i.SaldoAtual:F3};{i.TotalEntradas:F3};{i.TotalSaidas:F3};{i.ValorEstoque:F2}");

        sb.AppendLine($";;;;;;TOTAL;{data.valorTotalEstoque:F2}");

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))
            .ToArray();

        var nome = $"Estoque_{empresaId}_{DateTime.Today:yyyyMMdd}.csv";
        return File(bytes, "text/csv; charset=utf-8", nome);
    }
}
