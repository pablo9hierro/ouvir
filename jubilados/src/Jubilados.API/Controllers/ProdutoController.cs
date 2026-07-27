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

    private static readonly (string Nome, string NCM, string Unidade, decimal Preco)[] _lcSeedProdutos =
    [
        ("BACIA SANIT CONV SAVEIRO BCO", "69109000", "UN", 158.88m),
        ("PERFIL CALHA PLUVIAL PVC 125MM X 3M", "39259090", "UN", 96.36m),
        ("HASTE ATERRAMENTO 8MM 1.20M PCT 10UN", "72149990", "PC", 88.43m),
        ("THINNER 5L 2750", "38140090", "UN", 73.33m),
        ("BROXA PLAST RET 180X80MM CX 12UN", "96034090", "CX", 66.96m),
        ("REPARO VOLANTE 1/2 E 3/4 PCT 12UN", "84819090", "PC", 60.99m),
        ("FERROLHO CHATO 700 C/PORTA CAD 3 ZINC CX 06UN", "83014000", "CX", 55.54m),
        ("FECH SOBREPOR 701/100 PTO INOX", "83014000", "UN", 44.46m),
        ("ADITIVO PLASTIFICANTE VEDALIT 3.6L", "38244000", "GL", 44.42m),
        ("FITA MANTA ALU 30CM X 10M SUPERFITA", "68071000", "RL", 43.44m),
        ("ARMARIO BANHEIRO 33X34CM TIVOLI BCO", "94037000", "UN", 39.64m),
        ("ASSENTO SANIT ALMOF BCO", "39222000", "UN", 37.20m),
        ("REFLETOR LED 100W PTO", "94054200", "UN", 36.77m),
        ("PA QUAD N3 C/CB Y 71CM", "82011000", "UN", 31.56m),
        ("BALDE PLAST 12L C/PEGA", "39259090", "UN", 6.98m),
    ];

    [HttpPost("seed-lacesta")]
    public async Task<IActionResult> SeedLaCestaAsync([FromQuery] Guid empresaId, CancellationToken ct)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });

        var classificacao = await ObterClassificacaoPorCodigoAsync("000001", ct);
        var agora = DateTime.UtcNow;

        var existentes = await _db.Produtos
            .Where(p => p.EmpresaId == empresaId && p.CodigoInterno == "_lcseed_")
            .ToListAsync(ct);

        if (existentes.Count > 0)
        {
            foreach (var e in existentes) { e.Ativo = true; e.AtualizadoEm = agora; }
            await _db.SaveChangesAsync(ct);
            return Ok(existentes.Select(ToSeedDto));
        }

        var novos = _lcSeedProdutos.Select(s => new Produto
        {
            Id              = Guid.NewGuid(),
            EmpresaId       = empresaId,
            Nome            = s.Nome,
            NCM             = s.NCM,
            Unidade         = s.Unidade,
            Preco           = s.Preco,
            CFOP            = "5102",
            CST             = "00",
            CSOSN           = "400",
            CClassTrib      = classificacao?.Codigo ?? "000001",
            CstIbsCbs       = classificacao?.Cst ?? "000",
            ReducaoIbs      = classificacao?.ReducaoPercentualIbs ?? 0,
            ReducaoCbs      = classificacao?.ReducaoPercentualCbs ?? 0,
            TipoAliquotaIbsCbs = classificacao?.TipoAliquota ?? "",
            CodigoInterno   = "_lcseed_",
            AliquotaICMS    = 0,
            AliquotaIPI     = 0,
            AliquotaPIS     = 0.65m,
            AliquotaCOFINS  = 3m,
            QuantidadeEstoque = 999,
            Ativo           = true,
            CriadoEm        = agora,
            AtualizadoEm    = agora,
        }).ToList();

        _db.Produtos.AddRange(novos);
        await _db.SaveChangesAsync(ct);
        return Ok(novos.Select(ToSeedDto));
    }

    private static object ToSeedDto(Produto p) => new
    {
        p.Id, p.Nome, p.NCM, p.CFOP, p.CSOSN, p.CST,
        p.CClassTrib, p.CstIbsCbs, p.Unidade, p.Preco, p.CodigoInterno
    };

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
