using Jubilados.Application.DTOs;
using Jubilados.Domain.Entities;
using Jubilados.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jubilados.IntegrationTests;

/// <summary>
/// Testes de integração para a feature La Cesta (emissão em lote de NF-e).
/// Cobre: listagem de produtos via DbContext, validação de payload e montagem do DTO de emissão.
/// </summary>
public class LaCestaTests
{
    // ── helpers ──────────────────────────────────────────────────────────────────

    private static JubiladosDbContext CriarDbInMemory()
        => new JubiladosDbContext(
            new DbContextOptionsBuilder<JubiladosDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);

    private static Produto CriarProduto(Guid empresaId, string nome, decimal preco,
        string ncm = "84713012", string cfop = "5102", string csosn = "400")
        => new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = nome,
            Preco = preco,
            NCM = ncm,
            CFOP = cfop,
            CSOSN = csosn,
            Unidade = "UN",
            Ativo = true
        };

    // ── 1. ListarProdutos ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarProdutos_RetornaListaComCamposObrigatorios()
    {
        // Arrange
        using var db = CriarDbInMemory();
        var empresaId = Guid.NewGuid();
        var produtos = new[]
        {
            CriarProduto(empresaId, "Teclado Mecânico", 350.00m),
            CriarProduto(empresaId, "Mouse Sem Fio",     120.00m),
            CriarProduto(empresaId, "Monitor 24\"",      899.90m),
        };
        db.Produtos.AddRange(produtos);
        await db.SaveChangesAsync();

        // Act — simula a query que o ProdutoController executa para GET /api/produto
        var resultado = await db.Produtos
            .AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.Ativo)
            .ToListAsync();

        // Assert
        Assert.Equal(3, resultado.Count);
        foreach (var p in resultado)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Nome),  $"Produto {p.Id}: Nome ausente");
            Assert.True(p.Preco > 0,                         $"Produto {p.Nome}: Preco deve ser > 0");
            Assert.False(string.IsNullOrWhiteSpace(p.NCM),   $"Produto {p.Nome}: NCM ausente");
            Assert.False(string.IsNullOrWhiteSpace(p.CFOP),  $"Produto {p.Nome}: CFOP ausente");
        }
    }

    [Fact]
    public async Task ListarProdutos_FiltroEmpresaIsolaRegistros()
    {
        // Garante que produtos de empresas diferentes não se misturam.
        using var db = CriarDbInMemory();
        var empresa1 = Guid.NewGuid();
        var empresa2 = Guid.NewGuid();

        db.Produtos.AddRange(
            CriarProduto(empresa1, "Produto E1-A", 100m),
            CriarProduto(empresa1, "Produto E1-B", 200m),
            CriarProduto(empresa2, "Produto E2-A", 300m));
        await db.SaveChangesAsync();

        var resultadoE1 = await db.Produtos
            .AsNoTracking()
            .Where(p => p.EmpresaId == empresa1 && p.Ativo)
            .ToListAsync();

        Assert.Equal(2, resultadoE1.Count);
        Assert.All(resultadoE1, p => Assert.Equal(empresa1, p.EmpresaId));
    }

    // ── 2. GerarCesta_ValidaPayload ───────────────────────────────────────────────

    [Fact]
    public void GerarCesta_PayloadValido_SemErros()
    {
        // Arrange — monta payload equivalente ao que gerarCesta() constrói no front-end
        var empresaId = Guid.NewGuid();
        var prod1 = CriarProduto(empresaId, "Impressora",    650.00m);
        var prod2 = CriarProduto(empresaId, "Papel A4 (500fls)", 30.00m);
        var prod3 = CriarProduto(empresaId, "Cartucho Preto",  55.00m);

        var dto = new EmitirNFeDto(
            EmpresaId:          empresaId,
            ClienteId:          null,
            NaturezaOperacao:   "Venda de Mercadoria",
            Serie:              "1",
            Itens: new List<ItemNFeDto>
            {
                new ItemNFeDto(prod1.Id, 1, prod1.Preco),
                new ItemNFeDto(prod2.Id, 3, prod2.Preco),
                new ItemNFeDto(prod3.Id, 2, prod3.Preco),
            },
            FormaPagamento: "17",   // PIX
            Ambiente:       "2"     // Homologação
        );

        // Assert — DTO deve estar íntegro
        Assert.Equal(empresaId, dto.EmpresaId);
        Assert.Equal(3, dto.Itens.Count);
        Assert.Equal("2", dto.Ambiente);
        Assert.All(dto.Itens, it =>
        {
            Assert.NotEqual(Guid.Empty, it.ProdutoId);
            Assert.True(it.Quantidade > 0);
            Assert.True(it.ValorUnitario > 0);
        });
    }

    [Fact]
    public void GerarCesta_ValidacaoCamposObrigatoriosPorProduto_DetectaAusencias()
    {
        // Simula a lógica de validarPayloadCesta() do front-end em C#.
        var erros = new List<string>();

        // Produto sem NCM
        var semNcm = new { nome = "Sem NCM", ncm = (string?)null, cfop = "5102", csosn = "400" };
        // Produto sem CFOP
        var semCfop = new { nome = "Sem CFOP", ncm = "84713012", cfop = (string?)null, csosn = "400" };
        // Produto ok
        var ok = new { nome = "OK", ncm = "84713012", cfop = "5102", csosn = "400" };

        foreach (var p in new[] { semNcm, semCfop, ok })
        {
            if (string.IsNullOrWhiteSpace(p.ncm))
                erros.Add($"Produto \"{p.nome}\" sem NCM");
            if (string.IsNullOrWhiteSpace(p.cfop))
                erros.Add($"Produto \"{p.nome}\" sem CFOP");
            if (string.IsNullOrWhiteSpace(p.csosn))
                erros.Add($"Produto \"{p.nome}\" sem CST/CSOSN/cClassTrib");
        }

        Assert.Equal(2, erros.Count);
        Assert.Contains(erros, e => e.Contains("Sem NCM") && e.Contains("NCM"));
        Assert.Contains(erros, e => e.Contains("Sem CFOP") && e.Contains("CFOP"));
    }

    // ── 3. EmitirCesta_Homologacao ────────────────────────────────────────────────

    [Fact]
    public void EmitirCesta_DtoHomologacao_AmbienteCorreto()
    {
        // Verifica que o campo Ambiente do DTO é transmitido corretamente.
        var dto = new EmitirNFeDto(
            EmpresaId:        Guid.NewGuid(),
            ClienteId:        null,
            NaturezaOperacao: "Venda de Mercadoria",
            Serie:            "1",
            Itens: new List<ItemNFeDto>
            {
                new ItemNFeDto(Guid.NewGuid(), 1, 100.00m)
            },
            FormaPagamento: "01",
            Ambiente:       "2"    // Homologação — não deve gerar NF-e real
        );

        Assert.Equal("2", dto.Ambiente);
    }

    [Fact]
    public void EmitirCesta_DtoProducao_AmbienteCorreto()
    {
        var dto = new EmitirNFeDto(
            EmpresaId:        Guid.NewGuid(),
            ClienteId:        null,
            NaturezaOperacao: "Venda de Mercadoria",
            Serie:            "1",
            Itens: new List<ItemNFeDto>
            {
                new ItemNFeDto(Guid.NewGuid(), 2, 250.00m)
            },
            FormaPagamento: "03",
            Ambiente:       "1"    // Produção
        );

        Assert.Equal("1", dto.Ambiente);
    }

    [Fact]
    public void EmitirCesta_DtoSemAmbiente_PreservaNull()
    {
        // Quando Ambiente é null, o serviço deve usar _options.Ambiente (comportamento padrão).
        var dto = new EmitirNFeDto(
            EmpresaId:        Guid.NewGuid(),
            ClienteId:        null,
            NaturezaOperacao: "Venda de Mercadoria",
            Serie:            "1",
            Itens: new List<ItemNFeDto>
            {
                new ItemNFeDto(Guid.NewGuid(), 1, 50.00m)
            }
        );

        Assert.Null(dto.Ambiente);
    }
}
