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
                    ClienteId = null,
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

    /// <inheritdoc/>
    public async Task<ImportarXmlResultDto> ImportarXmlEntradaAsync(
        Guid empresaId, string xmlBase64, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Entrada] Importando XML para EmpresaId={Id}", empresaId);

        string xmlContent;
        try
        {
            var bytes = Convert.FromBase64String(xmlBase64);
            xmlContent = Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return new ImportarXmlResultDto(false, "XML inválido: não é base64 válido.");
        }

        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(xmlContent);
        }
        catch
        {
            return new ImportarXmlResultDto(false, "XML inválido: não é XML bem-formado.");
        }

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

        var infNFe = doc.SelectSingleNode("//nfe:infNFe", ns);
        if (infNFe is null)
            return new ImportarXmlResultDto(false, "XML não contém elemento infNFe.");

        var chave = infNFe.Attributes?["Id"]?.Value?.Replace("NFe", "") ?? "";
        var numDoc = int.TryParse(infNFe.SelectSingleNode("nfe:ide/nfe:nNF", ns)?.InnerText, out var n) ? n : 0;
        var serie  = infNFe.SelectSingleNode("nfe:ide/nfe:serie", ns)?.InnerText ?? "1";
        var natOp  = infNFe.SelectSingleNode("nfe:ide/nfe:natOp", ns)?.InnerText ?? "Compra";
        var dhEmi  = infNFe.SelectSingleNode("nfe:ide/nfe:dhEmi", ns)?.InnerText;
        var emitidaEm = DateTime.TryParse(dhEmi, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow;

        // Verifica se já existe
        var jaExiste = await _db.NotasFiscais
            .AsNoTracking()
            .AnyAsync(nf => nf.EmpresaId == empresaId && nf.ChaveAcesso == chave, cancellationToken);
        if (jaExiste)
            return new ImportarXmlResultDto(false, $"Nota com chave {chave} já importada.");

        var produtosCriados = new List<ProdutoImportadoDto>();
        decimal valorTotal = 0;

        var nota = new Jubilados.Domain.Entities.NotaFiscal
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ChaveAcesso = chave,
            Numero = numDoc,
            Serie = serie,
            NaturezaOperacao = natOp,
            Status = Jubilados.Domain.Enums.StatusNota.Autorizada,
            CStat = "100",
            XMotivo = "Importado via XML",
            TipoOperacao = "0",  // entrada
            EmitidaEm = emitidaEm,
            AutorizadaEm = emitidaEm,
            XmlEnvio = xmlContent
        };

        var detNos = infNFe.SelectNodes("nfe:det", ns);
        if (detNos is not null)
        {
            int nItem = 1;
            foreach (XmlNode det in detNos)
            {
                var prod = det.SelectSingleNode("nfe:prod", ns);
                if (prod is null) continue;

                var nomeProd  = prod.SelectSingleNode("nfe:xProd", ns)?.InnerText ?? "Produto Importado";
                var ncm       = prod.SelectSingleNode("nfe:NCM", ns)?.InnerText ?? "00000000";
                var cfop      = prod.SelectSingleNode("nfe:CFOP", ns)?.InnerText ?? "1102";
                var unid      = prod.SelectSingleNode("nfe:uCom", ns)?.InnerText ?? "UN";
                var qtd       = decimal.TryParse(prod.SelectSingleNode("nfe:qCom", ns)?.InnerText,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var q) ? q : 1;
                var vUnit     = decimal.TryParse(prod.SelectSingleNode("nfe:vUnCom", ns)?.InnerText,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var vu) ? vu : 0;
                var vProd     = decimal.TryParse(prod.SelectSingleNode("nfe:vProd", ns)?.InnerText,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var vp) ? vp : qtd * vUnit;
                var ean       = prod.SelectSingleNode("nfe:cEAN", ns)?.InnerText ?? "";
                if (ean == "SEM GTIN") ean = "";

                // Cria produto se não existir (por NCM+nome)
                var existeProd = await _db.Produtos.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.EmpresaId == empresaId
                        && p.NCM == ncm && p.Nome == nomeProd, cancellationToken);

                Guid produtoId;
                if (existeProd is null)
                {
                    var novoProd = new Jubilados.Domain.Entities.Produto
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = empresaId,
                        Nome = nomeProd,
                        NCM = ncm,
                        CFOP = cfop,
                        Unidade = unid,
                        Preco = vUnit,
                        EAN = ean,
                        CSOSN = "400",
                        CST = "",
                        AliquotaICMS = 0,
                        AliquotaIPI = 0,
                        AliquotaPIS = 0,
                        AliquotaCOFINS = 0
                    };
                    _db.Produtos.Add(novoProd);
                    produtoId = novoProd.Id;
                    produtosCriados.Add(new ProdutoImportadoDto(novoProd.Id, nomeProd, ncm));
                    _logger.LogInformation("[Entrada] Produto criado: {Nome} NCM={NCM}", nomeProd, ncm);
                }
                else
                {
                    produtoId = existeProd.Id;
                }

                nota.Itens.Add(new Jubilados.Domain.Entities.NotaItem
                {
                    ProdutoId = produtoId,
                    NumeroItem = nItem++,
                    Quantidade = qtd,
                    Unidade = unid,
                    ValorUnitario = vUnit,
                    ValorDesconto = 0,
                    ValorTotal = vProd,
                    BaseICMS = 0,
                    AliquotaICMS = 0,
                    ValorICMS = 0,
                    AliquotaIPI = 0,
                    ValorIPI = 0,
                    AliquotaPIS = 0,
                    ValorPIS = 0,
                    AliquotaCOFINS = 0,
                    ValorCOFINS = 0
                });
                valorTotal += vProd;
            }
        }

        nota.ValorProdutos = valorTotal;
        nota.ValorTotal = valorTotal;

        _db.NotasFiscais.Add(nota);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[Entrada] Nota importada Id={Id}, {Prods} produtos criados", nota.Id, produtosCriados.Count);
        return new ImportarXmlResultDto(
            Sucesso: true,
            Mensagem: $"Nota importada com sucesso. {produtosCriados.Count} produto(s) criado(s).",
            NotaFiscalId: nota.Id,
            ProdutosCriados: produtosCriados);
    }
}
