using Jubilados.Infrastructure.Services;
using Xunit;

namespace Jubilados.UnitTests;

public class CardGroupBuilderTests
{
    [Theory]
    [InlineData("01")] // dinheiro
    [InlineData("15")] // boleto
    [InlineData("99")] // outros
    public void Nao_monta_card_pra_forma_pagamento_nao_cartao_pix(string formaPagamento)
    {
        var xml = CardGroupBuilder.Montar(formaPagamento, "master", "AUTH123");
        Assert.Equal(string.Empty, xml);
    }

    [Theory]
    [InlineData("03")]
    [InlineData("04")]
    [InlineData("17")]
    public void Nao_monta_card_sem_codigo_autorizacao(string formaPagamento)
    {
        Assert.Equal(string.Empty, CardGroupBuilder.Montar(formaPagamento, "master", null));
        Assert.Equal(string.Empty, CardGroupBuilder.Montar(formaPagamento, "master", "  "));
    }

    [Fact]
    public void Monta_card_com_bandeira_mapeada()
    {
        var xml = CardGroupBuilder.Montar("03", "master", "AUTH123");
        Assert.Contains("<tpIntegra>1</tpIntegra>", xml);
        Assert.Contains($"<CNPJ>{CardGroupBuilder.MercadoPagoCnpj}</CNPJ>", xml);
        Assert.Contains("<tBand>02</tBand>", xml);
        Assert.Contains("<cAut>AUTH123</cAut>", xml);
    }

    [Fact]
    public void Bandeira_desconhecida_cai_em_outros()
    {
        var xml = CardGroupBuilder.Montar("04", "bandeira-nova-que-nao-existe", "AUTH999");
        Assert.Contains("<tBand>99</tBand>", xml);
    }

    [Fact]
    public void Bandeira_ausente_cai_em_outros_mas_ainda_monta_card()
    {
        var xml = CardGroupBuilder.Montar("17", null, "AUTH777");
        Assert.Contains("<tBand>99</tBand>", xml);
        Assert.Contains("<cAut>AUTH777</cAut>", xml);
    }
}
