using System.Net.Http.Json;
using Jubilados.Domain.Entities;
using Jubilados.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jubilados.EndToEndTests;

public class CadastroSmokeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CadastroSmokeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Index2_ExibeNovasAbasDeCadastro()
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var html = await client.GetStringAsync("/index2.html");

        Assert.Contains("Cad. Produtos", html);
        Assert.Contains("Cad. Destinatarios", html);
        Assert.Contains("Cad. Fornecedores", html);
    }

    [Fact]
    public async Task CadastroApis_CriamProdutoDestinatarioEFornecedor()
    {
        var empresaId = await SeedEmpresaAsync();
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var produtoResponse = await client.PostAsJsonAsync("/api/produto", new
        {
            empresaId,
            nome = "Brita 1",
            descricao = "Saco 25kg",
            ncm = "25171000",
            cfop = "5102",
            cst = "00",
            csosn = "102",
            unidade = "SC",
            preco = 12.5m,
            quantidadeEstoque = 30m,
            aliquotaICMS = 0m,
            aliquotaIPI = 0m,
            aliquotaPIS = 0.65m,
            aliquotaCOFINS = 3m
        });
        produtoResponse.EnsureSuccessStatusCode();

        var clienteResponse = await client.PostAsJsonAsync("/api/cliente", new
        {
            empresaId,
            nome = "Cliente Final",
            cpf_CNPJ = "12345678901",
            email = "cliente@teste.com",
            telefone = "83999999999",
            logradouro = "Rua B",
            numero = "10",
            bairro = "Centro",
            municipio = "Joao Pessoa",
            uf = "PB",
            cep = "58000000",
            pais = "Brasil",
            codigoPais = "1058"
        });
        clienteResponse.EnsureSuccessStatusCode();

        var fornecedorResponse = await client.PostAsJsonAsync("/api/fornecedor", new
        {
            empresaId,
            nome = "Fornecedor X",
            cpf_CNPJ = "98765432000199",
            municipio = "Joao Pessoa",
            uf = "PB"
        });
        fornecedorResponse.EnsureSuccessStatusCode();

        var produtos = await client.GetStringAsync($"/api/produto?empresaId={empresaId}");
        var clientes = await client.GetStringAsync($"/api/cliente?empresaId={empresaId}");
        var fornecedores = await client.GetStringAsync($"/api/fornecedor?empresaId={empresaId}");

        Assert.Contains("Brita 1", produtos);
        Assert.Contains("Cliente Final", clientes);
        Assert.Contains("Fornecedor X", fornecedores);
    }

    private async Task<Guid> SeedEmpresaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JubiladosDbContext>();

        var empresa = new Empresa
        {
            Id = Guid.NewGuid(),
            CNPJ = Guid.NewGuid().ToString("N")[..14],
            RazaoSocial = "Empresa E2E Ltda",
            NomeFantasia = "Empresa E2E",
            InscricaoEstadual = "123456789",
            Logradouro = "Rua Teste",
            Numero = "1",
            Bairro = "Centro",
            Municipio = "Joao Pessoa",
            UF = "PB",
            CEP = "58000000"
        };

        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        return empresa.Id;
    }
}