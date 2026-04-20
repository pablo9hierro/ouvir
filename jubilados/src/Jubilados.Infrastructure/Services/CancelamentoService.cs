using System.Text;
using System.Xml;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using Jubilados.Application.Configuration;
using Jubilados.Application.DTOs;
using Jubilados.Application.Interfaces;
using Jubilados.Domain.Enums;
using Jubilados.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jubilados.Infrastructure.Services;

/// <summary>
/// Cancela NF-e (tpEvento=110111) via webservice SEFAZ RecepcaoEvento.
/// Prazo: até 24 horas após autorização.
/// </summary>
public class CancelamentoService : ICancelamentoService
{
    private readonly JubiladosDbContext _db;
    private readonly ICertificadoService _certificadoService;
    private readonly NFeOptions _options;
    private readonly ILogger<CancelamentoService> _logger;

    public CancelamentoService(
        JubiladosDbContext db,
        ICertificadoService certificadoService,
        IOptions<NFeOptions> options,
        ILogger<CancelamentoService> logger)
    {
        _db = db;
        _certificadoService = certificadoService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CancelamentoResultDto> CancelarAsync(
        CancelarNFeDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Cancelamento] NotaId={Id}", dto.NotaFiscalId);

        if (string.IsNullOrWhiteSpace(dto.Justificativa) || dto.Justificativa.Trim().Length < 15)
            throw new InvalidOperationException("Justificativa deve ter no mínimo 15 caracteres.");

        var nota = await _db.NotasFiscais
            .FirstOrDefaultAsync(n => n.Id == dto.NotaFiscalId && n.EmpresaId == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Nota {dto.NotaFiscalId} não encontrada.");

        if (nota.CStat != "100" && nota.Status != StatusNota.Autorizada)
            throw new InvalidOperationException("Cancelamento só pode ser enviado para NF-e autorizada (cStat=100).");

        // Em homologação (ambiente=2) não há prazo; em produção o prazo é 24h
        if (_options.Ambiente != "2" &&
            nota.AutorizadaEm.HasValue &&
            (DateTime.UtcNow - nota.AutorizadaEm.Value).TotalHours > 24)
            throw new InvalidOperationException("Prazo de cancelamento expirado (máximo 24 horas após autorização).");

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {dto.EmpresaId} não encontrada.");

        if (string.IsNullOrEmpty(empresa.CertificadoBase64))
            throw new InvalidOperationException("Certificado digital não configurado para esta empresa.");

        var certificado = _certificadoService.CarregarCertificado(
            empresa.CertificadoBase64!, empresa.CertificadoSenha!);

        var cnpj = new string(empresa.CNPJ.Where(char.IsDigit).ToArray()).PadLeft(14, '0');
        var chave = nota.ChaveAcesso;
        var dhEvento = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-ddTHH:mm:sszzz");
        const string tpEvento = "110111";
        var idEvento = $"ID{tpEvento}{chave}01";
        var idLote = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % int.MaxValue);

        // Monta e assina evento via DOM — mesmo padrão do ManifestacaoService
        var enviEventoDoc = ConstruirEAssinarEvento(
            idEvento, chave!, cnpj, nota.Protocolo ?? "",
            dto.Justificativa.Trim(), dhEvento, idLote, _options.CodigoUF, _options.Ambiente, certificado);

        var soap = MontarSoap(enviEventoDoc);
        _logger.LogInformation("[Cancelamento] SOAP (primeiros 3000): {S}", soap.Length > 3000 ? soap[..3000] : soap);

        // Cancelamento usa endpoint do estado (SVRS/estado), não o AN
        // O AN (hom1.nfe.fazenda.gov.br) só aceita Manifestação do Destinatário (cOrgao=91)
        var urlEvento = _options.UrlEvento;
        const string soapAction = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4/nfeRecepcaoEvento";

        string cStat, xMotivo, protocolo;
        try
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificado);
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var content = new StringContent(soap, Encoding.UTF8);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"");
            var response = await http.PostAsync(urlEvento, content, cancellationToken);
            var retorno = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[Cancelamento] Resposta: {R}", retorno.Length > 2000 ? retorno[..2000] : retorno);
            (cStat, xMotivo, protocolo) = InterpretarRetorno(retorno);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Cancelamento] Erro HTTP.");
            return new CancelamentoResultDto(false, "999", $"Erro de comunicação: {ex.Message}");
        }

        // cStat 135 = Evento registrado e vinculado à NF-e
        if (cStat is "135" or "136")
        {
            nota.Status = StatusNota.Cancelada;
            nota.CStat = cStat;
            nota.XMotivo = xMotivo;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[Cancelamento] Nota {Id} cancelada com protocolo {P}", nota.Id, protocolo);
        }

        return new CancelamentoResultDto(cStat is "135" or "136", cStat, xMotivo, protocolo);
    }

    private static (string cStat, string xMotivo, string protocolo) InterpretarRetorno(string xml)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

            // Caminho completo igual ao ManifestacaoService
            var infEvento = doc.SelectSingleNode(
                "//nfe:retEnvEvento/nfe:retEvento/nfe:infEvento", ns);
            if (infEvento is not null)
            {
                var cs   = infEvento.SelectSingleNode("nfe:cStat",   ns)?.InnerText ?? "999";
                var xm   = infEvento.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? "";
                var prot = infEvento.SelectSingleNode("nfe:nProt",   ns)?.InnerText ?? "";
                return (cs, xm, prot);
            }
            // Fallback: cStat do lote
            var csAny = doc.SelectSingleNode("//*[local-name()='cStat']")?.InnerText  ?? "999";
            var xmAny = doc.SelectSingleNode("//*[local-name()='xMotivo']")?.InnerText ?? "Resposta não reconhecida";
            return (csAny, xmAny, "");
        }
        catch
        {
            return ("999", "Erro ao interpretar resposta SEFAZ", "");
        }
    }

    // ─── Construção do evento via DOM ────────────────────────────────────────

    private const string NfeNs  = "http://www.portalfiscal.inf.br/nfe";
    private const string WsdlNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";
    private const string SoapNs = "http://www.w3.org/2003/05/soap-envelope";

    private static XmlDocument ConstruirEAssinarEvento(
        string idEvento, string chave, string cnpj, string nProt,
        string justificativa, string dhEvento, int idLote,
        string cOrgao, string tpAmb,
        X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };

        var enviEvento = doc.CreateElement("envEvento", NfeNs);
        enviEvento.SetAttribute("versao", "1.00");
        doc.AppendChild(enviEvento);

        var elLote = doc.CreateElement("idLote", NfeNs);
        elLote.InnerText = idLote.ToString();
        enviEvento.AppendChild(elLote);

        var evento = doc.CreateElement("evento", NfeNs);
        evento.SetAttribute("versao", "1.00");
        enviEvento.AppendChild(evento);

        var infEvento = doc.CreateElement("infEvento", NfeNs);
        infEvento.SetAttribute("Id", idEvento);
        evento.AppendChild(infEvento);

        void Add(string name, string value)
        {
            var el = doc.CreateElement(name, NfeNs);
            el.InnerText = value;
            infEvento.AppendChild(el);
        }

        Add("cOrgao",     cOrgao); // código IBGE da UF emitente
        Add("tpAmb",      tpAmb);
        Add("CNPJ",       cnpj);
        Add("chNFe",      chave);
        Add("dhEvento",   dhEvento);
        Add("tpEvento",   "110111");
        Add("nSeqEvento", "1");
        Add("verEvento",  "1.00");

        var detEvento = doc.CreateElement("detEvento", NfeNs);
        detEvento.SetAttribute("versao", "1.00");
        infEvento.AppendChild(detEvento);

        var elDesc = doc.CreateElement("descEvento", NfeNs);
        elDesc.InnerText = "Cancelamento";
        detEvento.AppendChild(elDesc);

        var elProt = doc.CreateElement("nProt", NfeNs);
        elProt.InnerText = nProt;
        detEvento.AppendChild(elProt);

        var elJust = doc.CreateElement("xJust", NfeNs);
        elJust.InnerText = justificativa;
        detEvento.AppendChild(elJust);

        AssinarInfEvento(doc, infEvento, idEvento, cert);
        return doc;
    }

    private sealed class NFeSignedXml : SignedXml
    {
        public NFeSignedXml(XmlDocument doc) : base(doc) { }
        public override XmlElement? GetIdElement(XmlDocument document, string idValue) =>
            base.GetIdElement(document, idValue)
            ?? document.SelectSingleNode($"//*[@Id='{idValue}']") as XmlElement;
    }

    private static void AssinarInfEvento(
        XmlDocument doc, XmlElement infEvento, string idEvento, X509Certificate2 cert)
    {
        var signedXml = new NFeSignedXml(doc) { SigningKey = cert.GetRSAPrivateKey() };
        signedXml.SignedInfo.SignatureMethod        = SignedXml.XmlDsigRSASHA1Url;
        signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;

        var reference = new System.Security.Cryptography.Xml.Reference
            { Uri = $"#{idEvento}", DigestMethod = SignedXml.XmlDsigSHA1Url };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signedXml.KeyInfo = keyInfo;
        signedXml.ComputeSignature();

        // Signature como último filho de <evento> (irmão de <infEvento>)
        infEvento.ParentNode!.AppendChild(signedXml.GetXml());
    }

    private static string MontarSoap(XmlDocument enviEventoDoc)
    {
        var soapDoc = new XmlDocument();

        var envelope = soapDoc.CreateElement("soap12", "Envelope", SoapNs);
        envelope.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        envelope.SetAttribute("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");
        soapDoc.AppendChild(envelope);

        var body = soapDoc.CreateElement("soap12", "Body", SoapNs);
        envelope.AppendChild(body);

        var nfeDadosMsg = soapDoc.CreateElement("nfeDadosMsg", WsdlNs);
        body.AppendChild(nfeDadosMsg);

        nfeDadosMsg.AppendChild(soapDoc.ImportNode(enviEventoDoc.DocumentElement!, true));
        return soapDoc.OuterXml;
    }
}
