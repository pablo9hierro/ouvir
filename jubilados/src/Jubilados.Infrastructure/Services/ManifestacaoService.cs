using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Jubilados.Application.DTOs;
using Jubilados.Application.Interfaces;
using Jubilados.Domain.Enums;
using Jubilados.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jubilados.Infrastructure.Services;

/// <summary>
/// Manifestação do Destinatário via NFeRecepcaoEvento4 (SEFAZ Nacional AN).
///
/// Tipos: 210210 CienciaOperacao | 210200 ConfirmacaoOperacao
///        210220 Desconhecimento | 210240 OperacaoNaoRealizada
///
/// Abordagem: DOM puro, como NFeService. Assina infEvento dentro do
/// documento enviEvento completo para garantir o mesmo contexto de namespace
/// que a SEFAZ utilizará na verificação.
/// </summary>
public class ManifestacaoService : IManifestacaoService
{
    private readonly JubiladosDbContext _db;
    private readonly ICertificadoService _certificadoService;
    private readonly ILogger<ManifestacaoService> _logger;

    private const string UrlHomologacao = "https://hom1.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
    private const string NfeNs  = "http://www.portalfiscal.inf.br/nfe";
    private const string WsdlNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";
    private const string SoapNs = "http://www.w3.org/2003/05/soap-envelope";
    private const string DsigNs = "http://www.w3.org/2000/09/xmldsig#";

    private static readonly Dictionary<string, (int tpEvento, string descEvento)> Tipos = new()
    {
        ["CienciaOperacao"]      = (210210, "Ciencia da Operacao"),
        ["ConfirmacaoOperacao"]  = (210200, "Confirmacao da Operacao"),
        ["Desconhecimento"]      = (210220, "Desconhecimento da Operacao"),
        ["OperacaoNaoRealizada"] = (210240, "Operacao nao Realizada"),
    };

    public ManifestacaoService(
        JubiladosDbContext db,
        ICertificadoService certificadoService,
        ILogger<ManifestacaoService> logger)
    {
        _db = db;
        _certificadoService = certificadoService;
        _logger = logger;
    }

    public async Task<ManifestacaoResultDto> ManifestarNFeAsync(
        ManifestarNFeDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Manifestação] Iniciando. NotaId={NotaId} Tipo={Tipo}",
            dto.NotaFiscalId, dto.TipoManifestacao);

        if (!Tipos.TryGetValue(dto.TipoManifestacao, out var tipoInfo))
            throw new ArgumentException(
                $"Tipo inválido: {dto.TipoManifestacao}. " +
                "Use: CienciaOperacao | ConfirmacaoOperacao | Desconhecimento | OperacaoNaoRealizada");

        var nota = await _db.NotasFiscais
            .FirstOrDefaultAsync(n => n.Id == dto.NotaFiscalId, cancellationToken)
            ?? throw new InvalidOperationException($"Nota {dto.NotaFiscalId} não encontrada.");

        if (nota.EmpresaId != dto.EmpresaId)
            throw new UnauthorizedAccessException("Nota não pertence a esta empresa.");

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException("Empresa não encontrada.");

        var cert     = _certificadoService.CarregarCertificado(empresa.CertificadoBase64!, empresa.CertificadoSenha!);
        var cnpj     = new string(empresa.CNPJ.Where(char.IsDigit).ToArray());
        var dhEvento = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var nSeq     = 1;
        var idLote   = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % int.MaxValue);

        // ID = "ID" + tpEvento(6) + chNFe(44) + nSeqEvento(2 padded) = 54 chars
        var idEvento = $"ID{tipoInfo.tpEvento}{nota.ChaveAcesso!}{nSeq:D2}";

        // 1. Monta e assina o documento enviEvento completo via DOM
        var enviEventoDoc = ConstruirEAssinarEnviEvento(
            idEvento, nota.ChaveAcesso!, cnpj, tipoInfo, dto.Justificativa,
            dhEvento, nSeq, idLote, cert);

        // 2. Encapsula no SOAP via DOM (mesmo padrão que NFeService)
        var soapXml = MontarSoap(enviEventoDoc);

        // Debug: salvar SOAP completo para inspeção
        try { System.IO.File.WriteAllText("/tmp/manifestacao_soap.xml", soapXml); } catch { }
        _logger.LogInformation("[Manifestação] SOAP (primeiros 5000): {Xml}",
            soapXml.Length > 5000 ? soapXml[..5000] : soapXml);

        string retornoXml;
        try
        {
            using var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(cert);
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            const string soapAction =
                "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4/nfeRecepcaoEvento";

            var content = new StringContent(soapXml, Encoding.UTF8);
            content.Headers.ContentType =
                System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                    $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"");

            var response = await http.PostAsync(UrlHomologacao, content, cancellationToken);
            retornoXml   = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[Manifestação] Resposta SEFAZ HTTP {Status}: {Body}",
                (int)response.StatusCode,
                retornoXml.Length > 2000 ? retornoXml[..2000] : retornoXml);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Manifestação] Erro HTTP.");
            return new ManifestacaoResultDto(false, "999", $"Erro de comunicação: {ex.Message}");
        }

        var (cStat, xMotivo) = InterpretarRetorno(retornoXml);

        if (cStat is "135" or "136")
        {
            nota.Manifestada      = true;
            nota.TipoManifestacao = Enum.TryParse<TipoManifestacao>(dto.TipoManifestacao, out var tm) ? tm : null;
            nota.AtualizadoEm     = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[Manifestação] Sucesso. cStat={CStat}", cStat);
        }
        else
        {
            _logger.LogWarning("[Manifestação] Falhou. cStat={CStat} | {XMotivo}", cStat, xMotivo);
        }

        return new ManifestacaoResultDto(cStat is "135" or "136", cStat, xMotivo);
    }

    // ─── Construção e assinatura via DOM ─────────────────────────────────────

    private static XmlDocument ConstruirEAssinarEnviEvento(
        string idEvento, string chNFe, string cnpj,
        (int tpEvento, string descEvento) tipoInfo,
        string? justificativa, string dhEvento, int nSeq, int idLote,
        X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };

        // <envEvento versao="1.00" xmlns="...nfe...">  (sem 'i' — conforme XSD da SEFAZ e DFe.NET)
        var enviEvento = doc.CreateElement("envEvento", NfeNs);
        enviEvento.SetAttribute("versao", "1.00");
        doc.AppendChild(enviEvento);

        // <idLote>
        var elIdLote = doc.CreateElement("idLote", NfeNs);
        elIdLote.InnerText = idLote.ToString();
        enviEvento.AppendChild(elIdLote);

        // <evento versao="1.00">
        var evento = doc.CreateElement("evento", NfeNs);
        evento.SetAttribute("versao", "1.00");
        enviEvento.AppendChild(evento);

        // <infEvento Id="...">
        var infEvento = doc.CreateElement("infEvento", NfeNs);
        infEvento.SetAttribute("Id", idEvento);
        evento.AppendChild(infEvento);

        void Add(string name, string value)
        {
            var el = doc.CreateElement(name, NfeNs);
            el.InnerText = value;
            infEvento.AppendChild(el);
        }

        Add("cOrgao",     "91");
        Add("tpAmb",      "2");
        Add("CNPJ",       cnpj);
        Add("chNFe",      chNFe);
        Add("dhEvento",   dhEvento);
        Add("tpEvento",   tipoInfo.tpEvento.ToString());
        Add("nSeqEvento", nSeq.ToString());
        Add("verEvento",  "1.00");

        // <detEvento versao="1.00">
        var detEvento = doc.CreateElement("detEvento", NfeNs);
        detEvento.SetAttribute("versao", "1.00");
        infEvento.AppendChild(detEvento);

        var elDesc = doc.CreateElement("descEvento", NfeNs);
        elDesc.InnerText = tipoInfo.descEvento;
        detEvento.AppendChild(elDesc);

        if (tipoInfo.tpEvento == 210240)
        {
            var elJust = doc.CreateElement("xJust", NfeNs);
            elJust.InnerText = justificativa ?? "Operacao nao Realizada";
            detEvento.AppendChild(elJust);
        }

        // Assina <infEvento> dentro do documento enviEvento
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

        var reference = new Reference { Uri = $"#{idEvento}", DigestMethod = SignedXml.XmlDsigSHA1Url };
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

    // ─── Envelope SOAP 1.2 via DOM (mesmo padrão que NFeService) ────────────

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

        // Importa o nó raiz (enviEvento) para o documento SOAP
        var importedEnviEvento = soapDoc.ImportNode(enviEventoDoc.DocumentElement!, true);
        nfeDadosMsg.AppendChild(importedEnviEvento);

        return soapDoc.OuterXml;
    }

    // ─── Interpretação da resposta ───────────────────────────────────────────

    private (string cStat, string xMotivo) InterpretarRetorno(string retornoXml)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(retornoXml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", NfeNs);

            // Tenta ler cStat do evento individual primeiro
            var infEvento = doc.SelectSingleNode(
                "//nfe:retEnvEvento/nfe:retEvento/nfe:infEvento", ns);
            if (infEvento is not null)
            {
                var cStat   = infEvento.SelectSingleNode("nfe:cStat",   ns)?.InnerText ?? "999";
                var xMotivo = infEvento.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? string.Empty;
                return (cStat, xMotivo);
            }

            // Fallback: cStat do retEnvEvento (erro de lote)
            var cs = doc.SelectSingleNode("//nfe:retEnvEvento/nfe:cStat",   ns)?.InnerText ?? "999";
            var xm = doc.SelectSingleNode("//nfe:retEnvEvento/nfe:xMotivo", ns)?.InnerText ?? "Erro desconhecido";
            return (cs, xm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Manifestação] Erro ao interpretar retorno.");
            return ("999", "Erro ao interpretar resposta da SEFAZ");
        }
    }
}
