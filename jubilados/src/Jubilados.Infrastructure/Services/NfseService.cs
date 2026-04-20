using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Jubilados.Application.DTOs;
using Jubilados.Application.Interfaces;
using Jubilados.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jubilados.Infrastructure.Services;

/// <summary>
/// Emite NFS-e via webservice municipal ABRASF 2.04.
/// URL padrão para João Pessoa/PB.
/// </summary>
public class NfseService : INfseService
{
    private readonly JubiladosDbContext _db;
    private readonly ICertificadoService _certificadoService;
    private readonly ILogger<NfseService> _logger;

    // URL ABRASF 2.04 – João Pessoa/PB (alterar por cidade conforme necessário)
    private const string UrlNfse = "https://nfse.joaopessoa.pb.gov.br/service/v2/NfseService";

    public NfseService(
        JubiladosDbContext db,
        ICertificadoService certificadoService,
        ILogger<NfseService> logger)
    {
        _db = db;
        _certificadoService = certificadoService;
        _logger = logger;
    }

    public async Task<NfseResultDto> EmitirNfseAsync(
        EmitirNfseDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[NFS-e] Emitindo para EmpresaId={Id}", dto.EmpresaId);

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {dto.EmpresaId} não encontrada.");

        if (string.IsNullOrEmpty(empresa.CertificadoBase64))
            throw new InvalidOperationException("Certificado digital não configurado para esta empresa.");

        if (!empresa.EmiteNfse)
            throw new InvalidOperationException("Esta empresa não está configurada para emissão de NFS-e.");

        var certificado = _certificadoService.CarregarCertificado(
            empresa.CertificadoBase64!, empresa.CertificadoSenha!);

        var cnpj = new string(empresa.CNPJ.Where(char.IsDigit).ToArray()).PadLeft(14, '0');
        var im = empresa.InscricaoMunicipal ?? "";
        var dhEmi = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var codigoCidade = "2507507"; // João Pessoa (IBGE)
        var discriminacao = dto.Discriminacao ?? dto.NomeServico;
        var aliqIss = dto.AliquotaISS > 0 ? dto.AliquotaISS : empresa.AliquotaIss;

        var xmlRps = $@"<GerarNfseEnvio xmlns=""http://www.abrasf.org.br/nfse.xsd"">
  <Rps>
    <InfDeclaracaoPrestacaoServico>
      <Rps>
        <IdentificacaoRps>
          <Numero>1</Numero>
          <Serie>RPS</Serie>
          <Tipo>1</Tipo>
        </IdentificacaoRps>
        <DataEmissao>{dhEmi}</DataEmissao>
        <Status>1</Status>
      </Rps>
      <Competencia>{dhEmi[..7]}-01</Competencia>
      <Servico>
        <Valores>
          <ValorServicos>{dto.ValorServico.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}</ValorServicos>
          <ValorDeducoes>0.00</ValorDeducoes>
          <ValorPis>0.00</ValorPis>
          <ValorCofins>0.00</ValorCofins>
          <ValorInss>0.00</ValorInss>
          <ValorIr>0.00</ValorIr>
          <ValorCsll>0.00</ValorCsll>
          <IssRetido>2</IssRetido>
          <ValorIss>{Math.Round(dto.ValorServico * aliqIss / 100, 2).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}</ValorIss>
          <ValorIssRetido>0.00</ValorIssRetido>
          <OutrasRetencoes>0.00</OutrasRetencoes>
          <BaseCalculo>{dto.ValorServico.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}</BaseCalculo>
          <Aliquota>{(aliqIss / 100m).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}</Aliquota>
          <ValorLiquidoNfse>{dto.ValorServico.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}</ValorLiquidoNfse>
          <DescontoCondicionado>0.00</DescontoCondicionado>
          <DescontoIncondicionado>0.00</DescontoIncondicionado>
        </Valores>
        <ItemListaServico>{dto.CodigoServico ?? "14.01"}</ItemListaServico>
        <CodigoCnae>{empresa.CNAE ?? "6201501"}</CodigoCnae>
        <CodigoTributacaoMunicipio>{dto.CodigoServico ?? "1401"}</CodigoTributacaoMunicipio>
        <Discriminacao>{XmlEnc(discriminacao)}</Discriminacao>
        <CodigoMunicipio>{codigoCidade}</CodigoMunicipio>
        <CodigoPais>1058</CodigoPais>
        <ExigibilidadeISS>1</ExigibilidadeISS>
        <MunicipioIncidencia>{codigoCidade}</MunicipioIncidencia>
      </Servico>
      <Prestador>
        <CpfCnpj><Cnpj>{cnpj}</Cnpj></CpfCnpj>
        <InscricaoMunicipal>{im}</InscricaoMunicipal>
      </Prestador>
      {GerarTomador(dto)}
      <OptanteSimplesNacional>{(empresa.CRT == 1 ? "1" : "2")}</OptanteSimplesNacional>
      <IncentivoFiscal>2</IncentivoFiscal>
    </InfDeclaracaoPrestacaoServico>
  </Rps>
</GerarNfseEnvio>";

        string xmlAssinado;
        try
        {
            xmlAssinado = AssinarXml(xmlRps, certificado);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NFS-e] Erro ao assinar XML.");
            return new NfseResultDto(false, Erro: $"Erro ao assinar XML: {ex.Message}");
        }

        const string soapAction = "http://www.abrasf.org.br/nfse.xsd/GerarNfse";
        var soapEnvelope = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <GerarNfse xmlns=""http://www.abrasf.org.br/nfse.xsd"">
      <nfseCabecMsg><cabecalho xmlns=""http://www.abrasf.org.br/nfse.xsd"" versao=""2.04""><versaoDados>2.04</versaoDados></cabecalho></nfseCabecMsg>
      <nfseDadosMsg>{xmlAssinado}</nfseDadosMsg>
    </GerarNfse>
  </soap:Body>
</soap:Envelope>";

        try
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificado);
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", soapAction);
            var response = await http.PostAsync(UrlNfse, content, cancellationToken);
            var retorno = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[NFS-e] Resposta: {R}", retorno.Length > 2000 ? retorno[..2000] : retorno);
            return InterpretarRetorno(retorno);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[NFS-e] Erro HTTP.");
            return new NfseResultDto(false, Erro: $"Erro de comunicação: {ex.Message}");
        }
    }

    private static string GerarTomador(EmitirNfseDto dto)
    {
        if (string.IsNullOrEmpty(dto.CpfCnpjTomador))
            return "";

        var cpfCnpj = new string(dto.CpfCnpjTomador.Where(char.IsDigit).ToArray());
        var tagDoc = cpfCnpj.Length == 14
            ? $"<Cnpj>{cpfCnpj}</Cnpj>"
            : $"<Cpf>{cpfCnpj.PadLeft(11, '0')}</Cpf>";

        return $@"<Tomador>
        <IdentificacaoTomador>
          <CpfCnpj>{tagDoc}</CpfCnpj>
        </IdentificacaoTomador>
        <RazaoSocial>{XmlEnc(dto.NomeTomador ?? "Tomador")}</RazaoSocial>
      </Tomador>";
    }

    private static NfseResultDto InterpretarRetorno(string xml)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            // Verifica ListaMensagemRetorno (erros)
            var erros = doc.GetElementsByTagName("ListaMensagemRetorno");
            if (erros.Count > 0)
            {
                var msg = doc.SelectSingleNode("//*[local-name()='Mensagem']")?.InnerText ?? "Erro desconhecido";
                return new NfseResultDto(false, Erro: msg);
            }
            // Número da NFS-e
            var numero = doc.SelectSingleNode("//*[local-name()='Numero']")?.InnerText;
            var protocolo = doc.SelectSingleNode("//*[local-name()='CodigoVerificacao']")?.InnerText;
            if (!string.IsNullOrEmpty(numero))
                return new NfseResultDto(true, NumeroNfse: numero, Protocolo: protocolo, XmlRetorno: xml);
            return new NfseResultDto(false, Erro: "Resposta não reconhecida: " + xml[..Math.Min(xml.Length, 500)]);
        }
        catch (Exception ex)
        {
            return new NfseResultDto(false, Erro: $"Erro ao interpretar resposta: {ex.Message}");
        }
    }

    private sealed class NfseSignedXml : SignedXml
    {
        public NfseSignedXml(XmlDocument doc) : base(doc) { }
        public override XmlElement? GetIdElement(XmlDocument document, string idValue)
        {
            var elem = base.GetIdElement(document, idValue);
            if (elem is null)
                elem = document.SelectSingleNode($"//*[@Id='{idValue}']") as XmlElement;
            return elem;
        }
    }

    private static string AssinarXml(string xmlContent, X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.LoadXml(xmlContent);
        // NFS-e ABRASF: assina InfDeclaracaoPrestacaoServico
        var infEl = doc.GetElementsByTagName("InfDeclaracaoPrestacaoServico")[0] as XmlElement;
        if (infEl is not null && !infEl.HasAttribute("Id"))
            infEl.SetAttribute("Id", "infDeclaracao");
        var signedXml = new NfseSignedXml(doc) { SigningKey = cert.GetRSAPrivateKey() };
        var idVal = infEl?.GetAttribute("Id") ?? "infDeclaracao";
        var reference = new System.Security.Cryptography.Xml.Reference { Uri = $"#{idVal}" };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        reference.DigestMethod = SignedXml.XmlDsigSHA1Url;
        signedXml.AddReference(reference);
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signedXml.KeyInfo = keyInfo;
        signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
        signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        signedXml.ComputeSignature();
        var xmlSig = signedXml.GetXml();
        doc.DocumentElement!.AppendChild(doc.ImportNode(xmlSig, true));
        return doc.DocumentElement.OuterXml;
    }

    private static string XmlEnc(string? v) =>
        System.Security.SecurityElement.Escape(v ?? "") ?? "";
}
