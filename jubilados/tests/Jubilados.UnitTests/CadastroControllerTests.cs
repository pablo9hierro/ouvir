using System.Text.Json;
using Jubilados.API.Controllers;
using Jubilados.Domain.Entities;
using Jubilados.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jubilados.UnitTests;

public class CadastroControllerTests
{
    [Fact]
    public async Task ProdutoController_ListarAsync_RetornaCamposDeEstoqueECadastro()
    {
        await using var db = CreateDbContext();
        var empresaId = SeedEmpresa(db);
        db.Produtos.Add(new Produto
        {
            EmpresaId = empresaId,
            Nome = "Argamassa AC3",
            Descricao = "Saco 20kg",
            NCM = "32149000",
            CFOP = "5102",
            CST = "00",
            CSOSN = "102",
            Unidade = "SC",
            EAN = "7891234567890",
            Preco = 39.90m,
            QuantidadeEstoque = 18.5m,
            Ativo = true
        });
        await db.SaveChangesAsync();

        var controller = new ProdutoController(db);
        var result = await controller.ListarAsync(empresaId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Argamassa AC3", json);
        Assert.Contains("QuantidadeEstoque", json);
        Assert.Contains("EAN", json);
    }

    [Fact]
    public async Task ClienteController_CriarAsync_PersisteDestinatario()
    {
        await using var db = CreateDbContext();
        var empresaId = SeedEmpresa(db);
        var controller = new ClienteController(db);

        var result = await controller.CriarAsync(new Cliente
        {
            EmpresaId = empresaId,
            Nome = "Maria da Silva",
            CPF_CNPJ = "12345678901",
            Email = "maria@teste.com"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await db.Clientes.CountAsync());
    }

    [Fact]
    public async Task FornecedorController_CriarAsync_PersisteFornecedor()
    {
        await using var db = CreateDbContext();
        var empresaId = SeedEmpresa(db);
        var controller = new FornecedorController(db);

        var result = await controller.CriarAsync(new Fornecedor
        {
            EmpresaId = empresaId,
            Nome = "Fornecedor Base",
            CPF_CNPJ = "98765432000199",
            Municipio = "Joao Pessoa",
            UF = "PB"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await db.Fornecedores.CountAsync());
    }

    [Fact]
    public async Task FornecedorController_CriarAsync_RejeitaDocumentoAusente()
    {
        await using var db = CreateDbContext();
        var empresaId = SeedEmpresa(db);
        var controller = new FornecedorController(db);

        var result = await controller.CriarAsync(new Fornecedor
        {
            EmpresaId = empresaId,
            Nome = "Sem Documento"
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static JubiladosDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JubiladosDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new JubiladosDbContext(options);
    }

    private static Guid SeedEmpresa(JubiladosDbContext db)
    {
        var empresa = new Empresa
        {
            Id = Guid.NewGuid(),
            CNPJ = "21362844000152",
            RazaoSocial = "Empresa Teste Ltda",
            NomeFantasia = "Empresa Teste",
            InscricaoEstadual = "123456789",
            Logradouro = "Rua A",
            Numero = "100",
            Bairro = "Centro",
            Municipio = "Joao Pessoa",
            UF = "PB",
            CEP = "58000000"
        };

        db.Empresas.Add(empresa);
        db.SaveChanges();
        return empresa.Id;
    }
}