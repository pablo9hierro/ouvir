using System.Text;
using System.Xml;
using Jubilados.Application.Configuration;
using Jubilados.Application.DTOs;
using Jubilados.Application.Interfaces;
using Jubilados.Domain.Entities;
using Jubilados.Domain.Enums;
using Jubilados.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jubilados.Infrastructure.Services;

/// <summary>
/// Consulta NFes de entrada via NFeDistribuicaoDFe (webservice SEFAZ Nacional).
/// Controla NSU para não rebaixar notas já consultadas.
/// </summary>
public class NotaEntradaService : InotaEntradaService
{
    private readonly JubiladosDbContext _db;
    private readonly ICertificadoService _certificadoService;
    private readonly NFeOptions _options;
    private readonly ILogger<NotaEntradaService> _logger;

    private const string UrlHomologacao = "https://hom1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx";
    private const string UrlProducao = "https://www.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx";

    public NotaEntradaService(
        JubiladosDbContext db,
        ICertificadoService certificadoService,
        IOptions<NFeOptions> options,
        ILogger<NotaEntradaService> logger)
    {
        _db = db;
        _certificadoService = certificadoService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EntradaResultDto> ConsultarNotasEntradaAsync(
        ConsultarEntradaDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Entrada] Iniciando consulta de notas para EmpresaId={EmpresaId} NSU={NSU}",
            dto.EmpresaId, dto.UltimoNSU);

        var empresa = await _db.Empresas
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {dto.EmpresaId} não encontrada.");

        if (string.IsNullOrEmpty(empresa.CertificadoBase64))
            throw new InvalidOperationException("Empresa sem certificado digital.");

        var certificado = _certificadoService.CarregarCertificado(
            empresa.CertificadoBase64, empresa.CertificadoSenha!);

        var cnpjLimpo = new string(empresa.CNPJ.Where(char.IsDigit).ToArray());
        var notasImportadas = new List<Guid>();
        var ultimoNSU = dto.UltimoNSU;
        var continuar = true;

        while (continuar)
        {
            var envelope = MontarEnvelopeDistribuicao(cnpjLimpo, ultimoNSU);

            string retornoXml;
            try
            {
                var handler = new HttpClientHandler();
                handler.ClientCertificates.Add(certificado);
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

                const string soapAction = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe/nfeDistDFeInteresse";
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
                var content = new StringContent(envelope, Encoding.UTF8);
                content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                    $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"");
                var response = await http.PostAsync(UrlHomologacao, content, cancellationToken);
                retornoXml = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("[Entrada] Resposta SEFAZ HTTP {Status}: {Body}",
                    (int)response.StatusCode, retornoXml.Length > 2000 ? retornoXml[..2000] : retornoXml);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[Entrada] Erro HTTP ao consultar SEFAZ.");
                break;
            }

            var notas = ProcessarRetornoDistribuicao(retornoXml, dto.EmpresaId, ref ultimoNSU, out continuar);

            foreach (var nota in notas)
            {
                var jaExiste = await _db.NotasFiscais
                    .AnyAsync(n => n.ChaveAcesso == nota.ChaveAcesso, cancellationToken);

                if (!jaExiste)
                {
                    _db.NotasFiscais.Add(nota);
                    notasImportadas.Add(nota.Id);
                }
            }

            if (notasImportadas.Count > 0)
                await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Entrada] Lote processado. NSU atual={NSU} | Importadas={Qtd}",
                ultimoNSU, notasImportadas.Count);
        }

        return new EntradaResultDto(true, notasImportadas.Count, notasImportadas);
    }

    private string MontarEnvelopeDistribuicao(string cnpj, long ultimoNSU)
    {
        var nsuPad = ultimoNSU.ToString().PadLeft(15, '0');
        // SOAP 1.2 document/literal: nfeDadosMsg diretamente no Body (sem wrapper nfeDistDFeInteresse)
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap12:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                 xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
                 xmlns:soap12=""http://www.w3.org/2003/05/soap-envelope"">
  <soap12:Body>
    <nfeDadosMsg xmlns=""http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe"">
      <distDFeInt versao=""1.01"" xmlns=""http://www.portalfiscal.inf.br/nfe"">
        <tpAmb>{_options.Ambiente}</tpAmb>
        <cUFAutor>{_options.CodigoUF}</cUFAutor>
        <CNPJ>{cnpj}</CNPJ>
        <distNSU>
          <ultNSU>{nsuPad}</ultNSU>
        </distNSU>
      </distDFeInt>
    </nfeDadosMsg>
  </soap12:Body>
</soap12:Envelope>";
    }

    private List<NotaFiscal> ProcessarRetornoDistribuicao(
        string retornoXml, Guid empresaId, ref long ultimoNSU, out bool hasMore)
    {
        hasMore = false;
        var notas = new List<NotaFiscal>();

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(retornoXml);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

            var cStat = doc.SelectSingleNode("//nfe:retDistDFeInt/nfe:cStat", ns)?.InnerText;
            if (cStat != "137" && cStat != "138")
            {
                var xMotivo = doc.SelectSingleNode("//nfe:retDistDFeInt/nfe:xMotivo", ns)?.InnerText;
                _logger.LogWarning("[Entrada] SEFAZ retornou cStat={CStat} | {XMotivo}", cStat, xMotivo);
                return notas;
            }

            var maxNSUNode = doc.SelectSingleNode("//nfe:retDistDFeInt/nfe:maxNSU", ns);
            if (maxNSUNode is not null && long.TryParse(maxNSUNode.InnerText, out var maxNSU))
            {
                hasMore = maxNSU > ultimoNSU;
                ultimoNSU = maxNSU;
            }

            var docZip = doc.SelectNodes("//nfe:retDistDFeInt/nfe:loteDistDFeInt/nfe:docZip", ns);
            if (docZip is null) return notas;

            foreach (XmlNode dz in docZip)
            {
                var nsuAttr = dz.Attributes?["NSU"]?.Value;
                var schema = dz.Attributes?["schema"]?.Value ?? string.Empty;

                string xmlDoc;
                try
                {
                    var bytes = Convert.FromBase64String(dz.InnerText);
                    using var ms = new System.IO.MemoryStream(bytes);
                    using var gz = new System.IO.Compression.GZipStream(ms,
                        System.IO.Compression.CompressionMode.Decompress);
                    using var sr = new System.IO.StreamReader(gz, Encoding.UTF8);
                    xmlDoc = sr.ReadToEnd();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Entrada] Erro ao descompactar docZip NSU={NSU}", nsuAttr);
                    continue;
                }

                if (!schema.Contains("procNFe") && !schema.Contains("nfeProc")) continue;

                var chaveAcesso = ExtrairChaveAcesso(xmlDoc);
                if (string.IsNullOrEmpty(chaveAcesso)) continue;

                var nota = new NotaFiscal
                {
                    EmpresaId = empresaId,
                    ClienteId = Guid.Empty,
                    ChaveAcesso = chaveAcesso,
                    NSU = long.TryParse(nsuAttr, out var n) ? n : 0,
                    XmlRetorno = xmlDoc,
                    Status = StatusNota.Autorizada,
                    TipoOperacao = "0",
                    NaturezaOperacao = "Compra",
                    Numero = int.TryParse(chaveAcesso.Substring(25, 9), out var num) ? num : 0,
                    Serie = chaveAcesso.Substring(22, 3)
                };

                notas.Add(nota);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Entrada] Erro ao processar retorno de distribuição.");
        }

        return notas;
    }

    private static string ExtrairChaveAcesso(string xmlDoc)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlDoc);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
            var infNFe = doc.SelectSingleNode("//nfe:infNFe", ns);
            var chave = infNFe?.Attributes?["Id"]?.Value;
            return chave?.Replace("NFe", string.Empty) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
