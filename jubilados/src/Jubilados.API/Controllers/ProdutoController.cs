using Jubilados.Application.DTOs;
using Jubilados.Domain.Entities;
using Jubilados.Infrastructure.Data;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jubilados.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ProdutoController : ControllerBase
{
    private readonly JubiladosDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    private const string PortalClassificacaoUrl = "https://dfe-portal.svrs.rs.gov.br/DFE/ClassificacaoTributaria";
    private const string CacheKeyClassTrib = "classificacao-tributaria-portal";
    private const string FallbackFileName = "classificacao-tributaria-fallback.json";

    public ProdutoController(
        JubiladosDbContext db,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    private sealed record ClassificacaoTributariaItem(
        string Codigo,
        string Cst,
        string CstDescricao,
        string Descricao,
        decimal ReducaoPercentualIbs,
        decimal ReducaoPercentualCbs,
        string TipoAliquota,
        bool ExigeTributacao,
        bool ReducaoBaseCalculo,
        bool ReducaoAliquota,
        bool TransferenciaCredito,
        bool Diferimento,
        bool Monofasica,
        bool CreditoPresumidoIbsZfm,
        bool AjusteCredito,
        bool TributacaoRegular,
        bool CreditoPresumido,
        bool EstornoCredito,
        string DfeRelacionados,
        string NumeroAnexo
    );

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
                p.CEST,
                p.CstIbsCbs,
                p.CClassTrib,
                p.ReducaoIbs,
                p.ReducaoCbs,
                p.TipoAliquotaIbsCbs,
                p.EAN,
                p.Preco,
                p.Unidade,
                p.QuantidadeEstoque,
                p.CodigoInterno
            })
            .ToListAsync(ct);

        return Ok(produtos);
    }

    [HttpPost]
    public async Task<IActionResult> CriarAsync([FromBody] Produto produto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(produto.Nome)
            || string.IsNullOrWhiteSpace(produto.NCM)
            || string.IsNullOrWhiteSpace(produto.CFOP)
            || string.IsNullOrWhiteSpace(produto.CClassTrib))
            return BadRequest(new { erro = "Nome, NCM, CFOP e cClassTrib (IBS/CBS) são obrigatórios." });

        if (produto.EmpresaId == Guid.Empty)
            return BadRequest(new { erro = "EmpresaId é obrigatório." });

        var classificacao = await ObterClassificacaoPorCodigoAsync(produto.CClassTrib, ct);
        if (classificacao is null)
            return BadRequest(new { erro = "cClassTrib inválido. Use um código oficial do Portal de Classificação Tributária." });

        if (!string.IsNullOrWhiteSpace(produto.CstIbsCbs)
            && !string.Equals(produto.CstIbsCbs, classificacao.Cst, StringComparison.Ordinal))
            return BadRequest(new { erro = "CST IBS/CBS não confere com o cClassTrib informado." });

        produto.CstIbsCbs = classificacao.Cst;
        produto.CClassTrib = classificacao.Codigo;
        produto.ReducaoIbs = classificacao.ReducaoPercentualIbs;
        produto.ReducaoCbs = classificacao.ReducaoPercentualCbs;
        produto.TipoAliquotaIbsCbs = classificacao.TipoAliquota;
        if (string.IsNullOrWhiteSpace(produto.CST))
            produto.CST = classificacao.Cst;

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

    [HttpGet("classificacao-tributaria")]
    public async Task<IActionResult> ListarClassificacaoTributariaAsync(CancellationToken ct)
    {
        var itens = await ObterClassificacoesTributariasAsync(ct);
        return Ok(new
        {
            fonte = PortalClassificacaoUrl,
            total = itens.Count,
            itens
        });
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

    /// <summary>
    /// PUT /api/produto/{id}/complementar
    /// Salva os dados fiscais de venda que o lojista preenche manualmente
    /// após a importação automática via XML de entrada.
    /// </summary>
    [HttpPut("{id:guid}/complementar")]
    public async Task<IActionResult> ComplementarAsync(
        Guid id, [FromBody] ComplementarProdutoDto dto, CancellationToken ct)
    {
        var produto = await _db.Produtos.FindAsync([id], ct);
        if (produto is null)
            return NotFound(new { erro = "Produto não encontrado." });

        if (dto.PrecoVenda <= 0)
            return BadRequest(new { erro = "Preço de venda deve ser maior que zero." });

        if (string.IsNullOrWhiteSpace(dto.CClassTrib))
            return BadRequest(new { erro = "cClassTrib (IBS/CBS) é obrigatório." });

        var classificacao = await ObterClassificacaoPorCodigoAsync(dto.CClassTrib, ct);
        if (classificacao is null)
            return BadRequest(new { erro = "cClassTrib inválido. Use um código oficial do Portal de Classificação Tributária." });

        if (!string.IsNullOrWhiteSpace(dto.CstIbsCbs)
            && !string.Equals(dto.CstIbsCbs, classificacao.Cst, StringComparison.Ordinal))
            return BadRequest(new { erro = "CST IBS/CBS não confere com o cClassTrib informado." });

        produto.Preco        = dto.PrecoVenda;
        produto.CClassTrib   = classificacao.Codigo;
        produto.CstIbsCbs    = classificacao.Cst;
        produto.ReducaoIbs   = classificacao.ReducaoPercentualIbs;
        produto.ReducaoCbs   = classificacao.ReducaoPercentualCbs;
        produto.TipoAliquotaIbsCbs = classificacao.TipoAliquota;
        if (dto.CodigoInterno is not null) produto.CodigoInterno = dto.CodigoInterno;
        if (dto.Categoria     is not null) produto.Categoria     = dto.Categoria;
        if (dto.Organizacao   is not null) produto.Organizacao   = dto.Organizacao;
        if (dto.Padronizacao  is not null) produto.Padronizacao  = dto.Padronizacao;
        if (!string.IsNullOrWhiteSpace(dto.Cfop)) produto.CFOP   = dto.Cfop;

        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            sucesso = true,
            id,
            produto.Preco,
            produto.CFOP,
            produto.CClassTrib,
            produto.CstIbsCbs,
            produto.ReducaoIbs,
            produto.ReducaoCbs,
            produto.TipoAliquotaIbsCbs
        });
    }

    /// <summary>PUT /api/produto/{id} — atualiza todos os campos do produto.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> AtualizarAsync(Guid id, [FromBody] Produto dto, CancellationToken ct)
    {
        var produto = await _db.Produtos.FindAsync([id], ct);
        if (produto is null) return NotFound(new { erro = "Produto não encontrado." });

        if (string.IsNullOrWhiteSpace(dto.Nome) || string.IsNullOrWhiteSpace(dto.NCM) || string.IsNullOrWhiteSpace(dto.CFOP))
            return BadRequest(new { erro = "Nome, NCM e CFOP são obrigatórios." });

        if (!string.IsNullOrWhiteSpace(dto.CClassTrib))
        {
            var classificacao = await ObterClassificacaoPorCodigoAsync(dto.CClassTrib, ct);
            if (classificacao is null)
                return BadRequest(new { erro = "cClassTrib inválido." });
            produto.CClassTrib        = classificacao.Codigo;
            produto.CstIbsCbs         = classificacao.Cst;
            produto.ReducaoIbs        = classificacao.ReducaoPercentualIbs;
            produto.ReducaoCbs        = classificacao.ReducaoPercentualCbs;
            produto.TipoAliquotaIbsCbs = classificacao.TipoAliquota;
        }

        produto.Nome        = dto.Nome.Trim();
        produto.Descricao   = dto.Descricao;
        produto.NCM         = dto.NCM.Trim();
        produto.CFOP        = dto.CFOP.Trim();
        produto.CST         = dto.CST ?? produto.CST;
        produto.CSOSN       = dto.CSOSN ?? produto.CSOSN;
        produto.EAN         = string.IsNullOrWhiteSpace(dto.EAN) ? produto.EAN : dto.EAN;
        produto.CEST        = dto.CEST ?? produto.CEST;
        produto.Unidade     = string.IsNullOrWhiteSpace(dto.Unidade) ? produto.Unidade : dto.Unidade;
        produto.Preco       = dto.Preco > 0 ? dto.Preco : produto.Preco;
        produto.QuantidadeEstoque = dto.QuantidadeEstoque;
        produto.AtualizadoEm = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new { produto.Id, produto.Nome });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> ExcluirAsync(Guid id, CancellationToken ct)
    {
        var produto = await _db.Produtos.FindAsync([id], ct);
        if (produto is null)
            return NotFound(new { erro = "Produto não encontrado." });

        produto.Ativo = false;
        produto.AtualizadoEm = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { sucesso = true, id });
    }

    [HttpDelete("todos")]
    public async Task<IActionResult> ExcluirTodosAsync([FromQuery] Guid empresaId, CancellationToken ct)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });

        var produtos = await _db.Produtos
            .Where(p => p.EmpresaId == empresaId && p.Ativo)
            .ToListAsync(ct);

        var agora = DateTime.UtcNow;
        foreach (var p in produtos) { p.Ativo = false; p.AtualizadoEm = agora; }
        await _db.SaveChangesAsync(ct);
        return Ok(new { sucesso = true, excluidos = produtos.Count });
    }

    private async Task<ClassificacaoTributariaItem?> ObterClassificacaoPorCodigoAsync(string codigo, CancellationToken ct)
    {
        var itens = await ObterClassificacoesTributariasAsync(ct);
        return itens.FirstOrDefault(x => x.Codigo == codigo.Trim());
    }

    private Task<IReadOnlyList<ClassificacaoTributariaItem>> ObterClassificacoesTributariasAsync(CancellationToken ct)
    {
        return _cache.GetOrCreateAsync(CacheKeyClassTrib, async cacheEntry =>
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var html = await client.GetStringAsync(PortalClassificacaoUrl, ct);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var rows = doc.DocumentNode.SelectNodes("//table/tbody/tr[contains(@class,'accordion-toggle')]");
                var itens = ExtrairClassificacoesDoHtml(rows);
                if (itens.Count > 0)
                    return itens;
            }
            catch
            {
                // Fallback local aplicado abaixo.
            }

            return await CarregarFallbackLocalAsync(ct);
        })!;
    }

    private static IReadOnlyList<ClassificacaoTributariaItem> ExtrairClassificacoesDoHtml(HtmlNodeCollection? rows)
    {
        if (rows is null || rows.Count == 0)
            return Array.Empty<ClassificacaoTributariaItem>();

        var itens = new List<ClassificacaoTributariaItem>(capacity: 180);
        foreach (var row in rows)
        {
            var tds = row.SelectNodes("./td");
            if (tds is null || tds.Count < 11)
                continue;

            var cst = TextoLimpo(tds[1]);
            var cstDescricao = TextoLimpo(tds[2]);

            var exigeTributacao = IconeAtivo(tds[3]);
            var reducaoBaseCalculo = IconeAtivo(tds[4]);
            var reducaoAliquota = IconeAtivo(tds[5]);
            var transferenciaCredito = IconeAtivo(tds[6]);
            var diferimento = IconeAtivo(tds[7]);
            var monofasica = IconeAtivo(tds[8]);
            var creditoPresumidoIbsZfm = IconeAtivo(tds[9]);
            var ajusteCredito = IconeAtivo(tds[10]);

            var detailsRow = row.SelectSingleNode("following-sibling::tr[1]");
            var childRows = detailsRow?.SelectNodes(".//table[contains(@id,'tab-filho-cst-')]/tbody/tr");
            if (childRows is null) continue;

            foreach (var child in childRows)
            {
                var ctds = child.SelectNodes("./td");
                if (ctds is null || ctds.Count < 12)
                    continue;

                var codigo = TextoLimpo(ctds[0]);
                if (!Regex.IsMatch(codigo, "^\\d{6}$"))
                    continue;

                itens.Add(new ClassificacaoTributariaItem(
                    Codigo: codigo,
                    Cst: cst,
                    CstDescricao: cstDescricao,
                    Descricao: TextoLimpo(ctds[1]),
                    ReducaoPercentualIbs: ParseDecimal(TextoLimpo(ctds[2])),
                    ReducaoPercentualCbs: ParseDecimal(TextoLimpo(ctds[3])),
                    TipoAliquota: TextoLimpo(ctds[7]),
                    ExigeTributacao: exigeTributacao,
                    ReducaoBaseCalculo: reducaoBaseCalculo,
                    ReducaoAliquota: reducaoAliquota,
                    TransferenciaCredito: transferenciaCredito,
                    Diferimento: diferimento,
                    Monofasica: monofasica,
                    CreditoPresumidoIbsZfm: creditoPresumidoIbsZfm,
                    AjusteCredito: ajusteCredito,
                    TributacaoRegular: IconeAtivo(ctds[4]),
                    CreditoPresumido: IconeAtivo(ctds[5]),
                    EstornoCredito: IconeAtivo(ctds[6]),
                    DfeRelacionados: TextoLimpo(ctds[8]),
                    NumeroAnexo: TextoLimpo(ctds[9])
                ));
            }
        }

        return itens;
    }

    private async Task<IReadOnlyList<ClassificacaoTributariaItem>> CarregarFallbackLocalAsync(CancellationToken ct)
    {
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, FallbackFileName);
        if (!System.IO.File.Exists(fallbackPath))
            fallbackPath = Path.Combine(Directory.GetCurrentDirectory(), FallbackFileName);

        if (!System.IO.File.Exists(fallbackPath))
            return Array.Empty<ClassificacaoTributariaItem>();

        await using var stream = System.IO.File.OpenRead(fallbackPath);
        var fallback = await JsonSerializer.DeserializeAsync<List<ClassificacaoTributariaItem>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            ct);
        return fallback ?? new List<ClassificacaoTributariaItem>();
    }

    private static bool IconeAtivo(HtmlNode? td)
    {
        var icon = td?.SelectSingleNode(".//i");
        var classes = icon?.GetAttributeValue("class", string.Empty) ?? string.Empty;
        return classes.Contains("glyphicon-ok-sign", StringComparison.OrdinalIgnoreCase);
    }

    private static string TextoLimpo(HtmlNode? node)
    {
        var value = HtmlEntity.DeEntitize(node?.InnerText ?? string.Empty);
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        var normalized = value.Replace("%", string.Empty).Replace(',', '.').Trim();
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }
}
