using Jubilados.Application.Configuration;
using Jubilados.Infrastructure.Services;
using Xunit;

namespace Jubilados.UnitTests;

public class UfWebserviceConfigTests
{
    [Fact]
    public void PB_bate_byte_a_byte_com_NFeOptions_default()
    {
        // Guard de regressão: PB é a única empresa real em produção hoje.
        // Se este teste quebrar, a tabela nova divergiu do comportamento
        // atual (NFeOptions) -- não pode ir pra produção assim.
        var options = new NFeOptions();
        var pb = UfWebserviceConfig.PorUf["PB"];

        Assert.Equal(options.CodigoUF, pb.CodigoUF);
        Assert.Equal(options.SefazUrlHomologacao, pb.UrlAutorizacaoHom);
        Assert.Equal(options.SefazUrlProducao, pb.UrlAutorizacaoProd);
        Assert.Equal(options.SefazUrlConsultaHom, pb.UrlConsultaHom);
        Assert.Equal(options.SefazUrlConsultaProd, pb.UrlConsultaProd);
        Assert.Equal(options.SefazUrlStatusHom, pb.UrlStatusHom);
        Assert.Equal(options.SefazUrlStatusProd, pb.UrlStatusProd);
        Assert.Equal(options.SefazUrlInutilizacaoHom, pb.UrlInutilizacaoHom);
        Assert.Equal(options.SefazUrlInutilizacaoProd, pb.UrlInutilizacaoProd);
        Assert.Equal(options.SefazUrlEventoHom, pb.UrlEventoHom);
        Assert.Equal(options.SefazUrlEventoProd, pb.UrlEventoProd);
        Assert.Equal(options.UrlNfceAutorizacaoHom, pb.UrlNfceAutorizacaoHom);
        Assert.Equal(options.UrlNfceAutorizacaoProd, pb.UrlNfceAutorizacaoProd);
        Assert.Equal(options.UrlNfceQrCodeHom, pb.UrlNfceQrCodeHom);
        Assert.Equal(options.UrlNfceQrCodeProd, pb.UrlNfceQrCodeProd);
    }

    [Theory]
    [InlineData("PB")] [InlineData("AC")] [InlineData("AL")] [InlineData("AP")]
    [InlineData("DF")] [InlineData("ES")] [InlineData("PA")] [InlineData("RO")]
    [InlineData("SC")] [InlineData("SE")] [InlineData("TO")]
    public void Toda_UF_cadastrada_tem_formato_valido(string uf)
    {
        var cfg = UfWebserviceConfig.PorUf[uf];

        Assert.Matches("^[0-9]{2}$", cfg.CodigoUF);
        foreach (var url in new[]
        {
            cfg.UrlAutorizacaoHom, cfg.UrlAutorizacaoProd,
            cfg.UrlConsultaHom, cfg.UrlConsultaProd,
            cfg.UrlStatusHom, cfg.UrlStatusProd,
            cfg.UrlInutilizacaoHom, cfg.UrlInutilizacaoProd,
            cfg.UrlEventoHom, cfg.UrlEventoProd,
            cfg.UrlNfceAutorizacaoHom, cfg.UrlNfceAutorizacaoProd,
            cfg.UrlNfceQrCodeHom, cfg.UrlNfceQrCodeProd,
        })
        {
            Assert.False(string.IsNullOrWhiteSpace(url), $"URL vazia em {uf}");
            Assert.StartsWith("http", url);
        }
    }

    [Theory]
    [InlineData("RN")]
    [InlineData("RR")]
    public void UF_nao_confirmada_cai_no_fallback_PB(string uf)
    {
        // RN e RR foram propositalmente deixadas de fora (URL em transição /
        // endereço frágil, ver comentário em UfWebserviceConfig.PorUf) --
        // este teste garante que continuam caindo no fallback, não em
        // exceção nem em URL vazia.
        Assert.False(UfWebserviceConfig.PorUf.ContainsKey(uf));

        var options = new NFeOptions();
        var resolved = UfWebserviceConfig.Resolve(uf, options);

        Assert.Equal(options.CodigoUF, resolved.CodigoUF);
        Assert.Equal(options.UrlNfceQrCodeHom, resolved.UrlNfceQrCodeHom);
    }

    [Fact]
    public void UF_nula_ou_vazia_cai_no_fallback_PB()
    {
        var options = new NFeOptions();
        Assert.Equal(options.CodigoUF, UfWebserviceConfig.Resolve(null, options).CodigoUF);
        Assert.Equal(options.CodigoUF, UfWebserviceConfig.Resolve("", options).CodigoUF);
    }
}
