using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Jubilados.Application.Configuration;
using Jubilados.Application.Interfaces;
using Jubilados.Infrastructure.Data;
using Jubilados.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jubilados.IntegrationTests;

public class SoapContractTests
{
    [Fact]
    public void NotaEntradaService_MantemEnvelopeSoap12DocumentLiteral()
    {
        using var db = new JubiladosDbContext(new DbContextOptionsBuilder<JubiladosDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

        var service = new NotaEntradaService(
            db,
            new FakeCertificadoService(),
            Options.Create(new NFeOptions
            {
                Ambiente = "2",
                CodigoUF = "25"
            }),
            NullLogger<NotaEntradaService>.Instance);

        var method = typeof(NotaEntradaService).GetMethod("MontarEnvelopeDistribuicao", BindingFlags.Instance | BindingFlags.NonPublic);
        var xml = Assert.IsType<string>(method!.Invoke(service, new object[] { "21362844000152", 15L }));

        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var body = doc.SelectSingleNode("/*[local-name()='Envelope']/*[local-name()='Body']");
        Assert.NotNull(body);
        Assert.Equal("nfeDadosMsg", body!.FirstChild?.LocalName);
        Assert.Null(body.SelectSingleNode("./*[local-name()='nfeDistDFeInteresse']"));
        Assert.Equal("25", doc.SelectSingleNode("//*[local-name()='cUFAutor']")?.InnerText);
        Assert.Equal("2", doc.SelectSingleNode("//*[local-name()='tpAmb']")?.InnerText);
    }

    [Fact]
    public void NfseService_MantemEnvelopeSoapComCabecalhoEDadosSeparados()
    {
        var method = typeof(NfseService).GetMethod("MontarEnvelopeSoapGerarNfse", BindingFlags.Static | BindingFlags.NonPublic);
        var xml = Assert.IsType<string>(method!.Invoke(null, new object[] { "<TesteAssinado />" }));

        var doc = new XmlDocument();
        doc.LoadXml(xml);

        Assert.Equal("Envelope", doc.DocumentElement?.LocalName);
        Assert.NotNull(doc.SelectSingleNode("//*[local-name()='GerarNfse']/*[local-name()='nfseCabecMsg']"));
        var dados = doc.SelectSingleNode("//*[local-name()='GerarNfse']/*[local-name()='nfseDadosMsg']");
        Assert.NotNull(dados);
        Assert.Contains("TesteAssinado", dados!.InnerXml);
    }

    private sealed class FakeCertificadoService : ICertificadoService
    {
        public X509Certificate2 CarregarCertificado(string base64, string senha) => throw new NotSupportedException();
        public bool CertificadoValido(X509Certificate2 certificado) => true;
        public DateTime ObterValidade(string base64, string senha) => DateTime.UtcNow.AddYears(1);
    }
}