using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
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
/// Serviço de emissão de NFe.
/// Implementa INFeService da camada Application.
/// </summary>
public class NFeService : INFeService
{
    private readonly JubiladosDbContext _db;
    private readonly ICertificadoService _certificadoService;
    private readonly NFeOptions _options;
    private readonly ILogger<NFeService> _logger;

    public NFeService(
        JubiladosDbContext db,
        ICertificadoService certificadoService,
        IOptions<NFeOptions> options,
        ILogger<NFeService> logger)
    {
        _db = db;
        _certificadoService = certificadoService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<NFeResultDto> EmitirNFeAsync(EmitirNFeDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[NFe] Iniciando emissão para EmpresaId={EmpresaId}", dto.EmpresaId);

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {dto.EmpresaId} não encontrada.");

        if (string.IsNullOrEmpty(empresa.CertificadoBase64))
            throw new InvalidOperationException("Empresa não possui certificado digital configurado.");

        var certificado = _certificadoService.CarregarCertificado(
            empresa.CertificadoBase64, empresa.CertificadoSenha!);

        // Destinatário: cliente do banco ou destino avulso
        Cliente? cliente = null;
        if (dto.ClienteId.HasValue && dto.ClienteId.Value != Guid.Empty)
        {
            cliente = await _db.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == dto.ClienteId.Value && c.EmpresaId == dto.EmpresaId, cancellationToken)
                ?? throw new InvalidOperationException($"Cliente {dto.ClienteId} não encontrado.");
        }

        var produtoIds = dto.Itens.Select(i => i.ProdutoId).ToList();
        var produtos = await _db.Produtos.AsNoTracking()
            .Where(p => produtoIds.Contains(p.Id) && p.EmpresaId == dto.EmpresaId)
            .ToListAsync(cancellationToken);

        var ultimoNumero = await _db.NotasFiscais
            .Where(n => n.EmpresaId == dto.EmpresaId && n.Serie == dto.Serie)
            .MaxAsync(n => (int?)n.Numero, cancellationToken) ?? 0;

        var nota = MontarNotaFiscal(dto, empresa, cliente, produtos, ultimoNumero + 1);
        CalcularTotais(nota, dto);

        var xmlNFe = GerarXmlNFe(nota, empresa, cliente, produtos, dto);
        _logger.LogInformation("[NFe] XML gerado (primeiros 2000 chars): {Xml}",
            xmlNFe.Length > 2000 ? xmlNFe[..2000] : xmlNFe);
        var xmlAssinado = AssinarXml(xmlNFe, certificado);
        _logger.LogInformation("[NFe] XML assinado (primeiros 500 chars): {Xml}",
            xmlAssinado.Length > 500 ? xmlAssinado[..500] : xmlAssinado);
        nota.XmlEnvio = xmlAssinado;

        var (cStat, xMotivo, protocolo) = await EnviarParaSefazAsync(
            xmlAssinado, empresa.CNPJ, certificado, cancellationToken);

        nota.CStat = cStat;
        nota.XMotivo = xMotivo;
        nota.Protocolo = protocolo;

        if (cStat == "100")
        {
            nota.Status = StatusNota.Autorizada;
            nota.AutorizadaEm = DateTime.UtcNow;
            _logger.LogInformation("[NFe] Autorizada! Protocolo={Protocolo}", protocolo);
        }
        else
        {
            nota.Status = StatusNota.Rejeitada;
            _logger.LogWarning("[NFe] Rejeitada. cStat={CStat} | {XMotivo}", cStat, xMotivo);
        }

        _db.NotasFiscais.Add(nota);
        await _db.SaveChangesAsync(cancellationToken);

        return new NFeResultDto(
            Sucesso: cStat == "100",
            CStat: cStat,
            XMotivo: xMotivo,
            NotaFiscalId: nota.Id,
            ChaveAcesso: nota.ChaveAcesso,
            Protocolo: protocolo);
    }

    public async Task<NFeDetalheDto?> ConsultarNotaAsync(Guid notaFiscalId, CancellationToken cancellationToken = default)
    {
        var nota = await _db.NotasFiscais.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == notaFiscalId, cancellationToken);
        if (nota is null) return null;

        return new NFeDetalheDto(
            nota.Id, nota.ChaveAcesso, nota.Numero, nota.Serie,
            nota.Status.ToString(), nota.ValorTotal, nota.EmitidaEm,
            nota.Protocolo, nota.CStat, nota.XMotivo, nota.Manifestada);
    }

    public async Task<ConsultarSefazResultDto> ConsultarSefazAsync(
        ConsultarSefazDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[NFe] ConsultarSefaz chave={Chave}", dto.ChaveAcesso);

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {dto.EmpresaId} não encontrada.");

        var certificado = _certificadoService.CarregarCertificado(
            empresa.CertificadoBase64!, empresa.CertificadoSenha!);

        const string wsdlNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeConsultaProtocolo4";
        const string nfeNs  = "http://www.portalfiscal.inf.br/nfe";
        const string soapNs = "http://www.w3.org/2003/05/soap-envelope";
        const string soapAction = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeConsultaProtocolo4/nfeConsultaNF";

        var soapDoc = new XmlDocument();
        var envelope = soapDoc.CreateElement("soap12", "Envelope", soapNs);
        envelope.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        envelope.SetAttribute("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");
        soapDoc.AppendChild(envelope);

        var header = soapDoc.CreateElement("soap12", "Header", soapNs);
        envelope.AppendChild(header);
        var cabec = soapDoc.CreateElement("nfeCabecMsg", wsdlNs);
        var elCUF = soapDoc.CreateElement("cUF", wsdlNs); elCUF.InnerText = _options.CodigoUF;
        var elVer = soapDoc.CreateElement("versaoDados", wsdlNs); elVer.InnerText = "4.00";
        cabec.AppendChild(elCUF); cabec.AppendChild(elVer);
        header.AppendChild(cabec);

        var body = soapDoc.CreateElement("soap12", "Body", soapNs);
        envelope.AppendChild(body);
        var nfeDadosMsg = soapDoc.CreateElement("nfeDadosMsg", wsdlNs);
        body.AppendChild(nfeDadosMsg);

        var consSit = soapDoc.CreateElement("consSitNFe", nfeNs);
        consSit.SetAttribute("versao", "4.00");
        var elTpAmb = soapDoc.CreateElement("tpAmb", nfeNs); elTpAmb.InnerText = _options.Ambiente;
        var elXServ = soapDoc.CreateElement("xServ", nfeNs); elXServ.InnerText = "CONSULTAR";
        var elChNFe = soapDoc.CreateElement("chNFe", nfeNs); elChNFe.InnerText = dto.ChaveAcesso;
        consSit.AppendChild(elTpAmb); consSit.AppendChild(elXServ); consSit.AppendChild(elChNFe);
        nfeDadosMsg.AppendChild(consSit);

        var xml = soapDoc.OuterXml;
        _logger.LogInformation("[NFe] ConsultarSefaz SOAP: {Xml}", xml.Length > 2000 ? xml[..2000] : xml);

        try
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificado);
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var content = new StringContent(xml, Encoding.UTF8);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"");
            var response = await http.PostAsync(_options.UrlConsulta, content, cancellationToken);
            var retorno = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[NFe] ConsultarSefaz resposta: {Body}", retorno.Length > 2000 ? retorno[..2000] : retorno);
            return InterpretarConsultaProtocolo(retorno, _logger);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[NFe] ConsultarSefaz erro HTTP.");
            return new ConsultarSefazResultDto(false, "999", $"Erro de comunicação: {ex.Message}");
        }
    }

    public async Task<StatusServicoResultDto> ConsultarStatusAsync(
        Guid empresaId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[NFe] ConsultarStatus EmpresaId={Id}", empresaId);

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == empresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {empresaId} não encontrada.");

        var certificado = _certificadoService.CarregarCertificado(
            empresa.CertificadoBase64!, empresa.CertificadoSenha!);

        const string wsdlNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4";
        const string nfeNs  = "http://www.portalfiscal.inf.br/nfe";
        const string soapNs = "http://www.w3.org/2003/05/soap-envelope";
        const string soapAction = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4/nfeStatusServicoNF";

        var soapDoc = new XmlDocument();
        var envelope = soapDoc.CreateElement("soap12", "Envelope", soapNs);
        envelope.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        envelope.SetAttribute("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");
        soapDoc.AppendChild(envelope);

        var header = soapDoc.CreateElement("soap12", "Header", soapNs);
        envelope.AppendChild(header);
        var cabec = soapDoc.CreateElement("nfeCabecMsg", wsdlNs);
        var elCUF = soapDoc.CreateElement("cUF", wsdlNs); elCUF.InnerText = _options.CodigoUF;
        var elVer = soapDoc.CreateElement("versaoDados", wsdlNs); elVer.InnerText = "4.00";
        cabec.AppendChild(elCUF); cabec.AppendChild(elVer);
        header.AppendChild(cabec);

        var body = soapDoc.CreateElement("soap12", "Body", soapNs);
        envelope.AppendChild(body);
        var nfeDadosMsg = soapDoc.CreateElement("nfeDadosMsg", wsdlNs);
        body.AppendChild(nfeDadosMsg);

        var consStat = soapDoc.CreateElement("consStatServ", nfeNs);
        consStat.SetAttribute("versao", "4.00");
        var elTpAmb = soapDoc.CreateElement("tpAmb", nfeNs); elTpAmb.InnerText = _options.Ambiente;
        var elCuf2  = soapDoc.CreateElement("cUF", nfeNs);   elCuf2.InnerText  = _options.CodigoUF;
        var elXServ = soapDoc.CreateElement("xServ", nfeNs); elXServ.InnerText = "STATUS";
        consStat.AppendChild(elTpAmb); consStat.AppendChild(elCuf2); consStat.AppendChild(elXServ);
        nfeDadosMsg.AppendChild(consStat);

        var xml = soapDoc.OuterXml;

        try
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificado);
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var content = new StringContent(xml, Encoding.UTF8);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"");
            var response = await http.PostAsync(_options.UrlStatus, content, cancellationToken);
            var retorno = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[NFe] ConsultarStatus resposta: {Body}", retorno.Length > 2000 ? retorno[..2000] : retorno);
            return InterpretarStatus(retorno, _logger);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[NFe] ConsultarStatus erro HTTP.");
            return new StatusServicoResultDto(false, "999", $"Erro de comunicação: {ex.Message}");
        }
    }

    public async Task<InutilizarResultDto> InutilizarAsync(
        InutilizarDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[NFe] Inutilizar Serie={Serie} [{Ini}-{Fin}]",
            dto.Serie, dto.NumeroInicial, dto.NumeroFinal);

        if (dto.NumeroFinal < dto.NumeroInicial)
            throw new ArgumentException("NumeroFinal deve ser >= NumeroInicial.");

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {dto.EmpresaId} não encontrada.");

        var certificado = _certificadoService.CarregarCertificado(
            empresa.CertificadoBase64!, empresa.CertificadoSenha!);

        var cnpj = Limpar(empresa.CNPJ).PadLeft(14, '0');
        var ano  = DateTime.Now.ToString("yy");
        var serie = dto.Serie.PadLeft(3, '0');
        var nNFIni = dto.NumeroInicial.ToString().PadLeft(9, '0');
        var nNFFin = dto.NumeroFinal.ToString().PadLeft(9, '0');
        var idInut = $"ID{_options.CodigoUF}{ano}{cnpj}55{serie}{nNFIni}{nNFFin}";

        var xmlInut = $@"<inutNFe versao=""4.00"" xmlns=""http://www.portalfiscal.inf.br/nfe"">
  <infInut Id=""{idInut}"">
    <tpAmb>{_options.Ambiente}</tpAmb>
    <xServ>INUTILIZAR</xServ>
    <cUF>{_options.CodigoUF}</cUF>
    <ano>{ano}</ano>
    <CNPJ>{cnpj}</CNPJ>
    <mod>55</mod>
    <serie>{int.Parse(dto.Serie)}</serie>
    <nNFIni>{dto.NumeroInicial}</nNFIni>
    <nNFFin>{dto.NumeroFinal}</nNFFin>
    <xJust>{XmlEnc(dto.Justificativa)}</xJust>
  </infInut>
</inutNFe>";

        var xmlAssinado = AssinarXml(xmlInut, certificado, "infInut");

        const string wsdlNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeInutilizacao4";
        const string soapNs = "http://www.w3.org/2003/05/soap-envelope";
        const string soapAction = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeInutilizacao4/nfeInutilizacaoNF";

        var soapDoc = new XmlDocument();
        var envelope = soapDoc.CreateElement("soap12", "Envelope", soapNs);
        envelope.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        envelope.SetAttribute("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");
        soapDoc.AppendChild(envelope);

        var header = soapDoc.CreateElement("soap12", "Header", soapNs);
        envelope.AppendChild(header);
        var cabec = soapDoc.CreateElement("nfeCabecMsg", wsdlNs);
        var elCUF = soapDoc.CreateElement("cUF", wsdlNs); elCUF.InnerText = _options.CodigoUF;
        var elVer = soapDoc.CreateElement("versaoDados", wsdlNs); elVer.InnerText = "4.00";
        cabec.AppendChild(elCUF); cabec.AppendChild(elVer);
        header.AppendChild(cabec);

        var body = soapDoc.CreateElement("soap12", "Body", soapNs);
        envelope.AppendChild(body);
        var nfeDadosMsg = soapDoc.CreateElement("nfeDadosMsg", wsdlNs);
        body.AppendChild(nfeDadosMsg);

        var inutDoc = new XmlDocument { PreserveWhitespace = false };
        inutDoc.LoadXml(xmlAssinado);
        nfeDadosMsg.AppendChild(soapDoc.ImportNode(inutDoc.DocumentElement!, true));

        var xml = soapDoc.OuterXml;
        _logger.LogInformation("[NFe] Inutilizar SOAP: {Xml}", xml.Length > 3000 ? xml[..3000] : xml);

        try
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificado);
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var content = new StringContent(xml, Encoding.UTF8);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"");
            var response = await http.PostAsync(_options.UrlInutilizacao, content, cancellationToken);
            var retorno = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[NFe] Inutilizar resposta: {Body}", retorno.Length > 2000 ? retorno[..2000] : retorno);
            return InterpretarInutilizacao(retorno, _logger);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[NFe] Inutilizar erro HTTP.");
            return new InutilizarResultDto(false, "999", $"Erro de comunicação: {ex.Message}");
        }
    }

    public async Task<(Guid Id, string RazaoSocial)?> BuscarEmpresaPorCnpjAsync(
        string cnpj14Digitos, CancellationToken cancellationToken = default)
    {
        var empresa = await _db.Empresas.AsNoTracking()
            .Where(e => e.CNPJ.Replace(".", "").Replace("/", "").Replace("-", "") == cnpj14Digitos)
            .Select(e => new { e.Id, e.RazaoSocial })
            .FirstOrDefaultAsync(cancellationToken);
        return empresa is null ? null : (empresa.Id, empresa.RazaoSocial);
    }

    public async Task<IList<NuvemFiscalNotaDto>> ListarNotasPorEmpresaAsync(
        Guid empresaId, CancellationToken cancellationToken = default)
    {
        return await _db.NotasFiscais.AsNoTracking()
            .Where(n => n.EmpresaId == empresaId)
            .OrderByDescending(n => n.EmitidaEm)
            .Select(n => new NuvemFiscalNotaDto(
                n.Id,
                n.TipoOperacao,
                n.ChaveAcesso,
                n.Numero,
                n.Serie,
                n.NaturezaOperacao,
                n.Status.ToString(),
                n.ValorTotal,
                n.EmitidaEm,
                n.Protocolo,
                n.CStat,
                n.Manifestada))
            .ToListAsync(cancellationToken);
    }

    public async Task<(string? Xml, string? ChaveAcesso)?> ObterXmlAsync(
        Guid notaFiscalId, CancellationToken cancellationToken = default)
    {
        var nota = await _db.NotasFiscais.AsNoTracking()
            .Where(n => n.Id == notaFiscalId)
            .Select(n => new { n.XmlEnvio, n.ChaveAcesso, n.CStat })
            .FirstOrDefaultAsync(cancellationToken);

        if (nota is null) return null;
        return (nota.XmlEnvio, nota.ChaveAcesso);
    }

    public async Task<CceResultDto> EnviarCceAsync(CceDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[CCe] Enviando para NotaId={Id}", dto.NotaFiscalId);

        var nota = await _db.NotasFiscais.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == dto.NotaFiscalId, cancellationToken)
            ?? throw new InvalidOperationException($"Nota {dto.NotaFiscalId} não encontrada.");

        if (nota.CStat != "100")
            throw new InvalidOperationException("CCe só pode ser enviada para NF-e autorizada (cStat=100).");

        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpresaId, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {dto.EmpresaId} não encontrada.");

        var certificado = _certificadoService.CarregarCertificado(
            empresa.CertificadoBase64!, empresa.CertificadoSenha!);

        var cnpj = Limpar(empresa.CNPJ).PadLeft(14, '0');
        var chave = nota.ChaveAcesso;
        var dhEvento = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        const string tpEvento = "110110";
        const string nSeq = "1";
        var idEvento = $"ID{tpEvento}{chave}{nSeq.PadLeft(2, '0')}";

        var xCondUso = "A Carta de Correcao e disciplinada pelo paragrafo 1o-A do art. 7o do Convenio S/N, de 15 de dezembro de 1970 e pode ser utilizada para regularizacao de erro ocorrido na emissao de documento fiscal, desde que o erro nao esteja relacionado com: I - as variaveis que determinam o valor do imposto tais como: base de calculo, aliquota, diferenca de preco, quantidade, valor da operacao ou da prestacao; II - a correcao de dados cadastrais que implique mudanca do remetente ou do destinatario; III - a data de emissao ou de saida.";

        var xmlEvento = $@"<envEvento versao=""1.00"" xmlns=""http://www.portalfiscal.inf.br/nfe"">
  <idLote>1</idLote>
  <evento versao=""1.00"">
    <infEvento Id=""{idEvento}"">
      <cOrgao>{_options.CodigoUF}</cOrgao>
      <tpAmb>{_options.Ambiente}</tpAmb>
      <CNPJ>{cnpj}</CNPJ>
      <chNFe>{chave}</chNFe>
      <dhEvento>{dhEvento}</dhEvento>
      <tpEvento>{tpEvento}</tpEvento>
      <nSeqEvento>{nSeq}</nSeqEvento>
      <verEvento>1.00</verEvento>
      <detEvento versao=""1.00"">
        <descEvento>Carta de Correcao</descEvento>
        <xCorrecao>{XmlEnc(dto.CorrecaoTexto)}</xCorrecao>
        <xCondUso>{xCondUso}</xCondUso>
      </detEvento>
    </infEvento>
  </evento>
</envEvento>";

        var xmlAssinado = AssinarXml(xmlEvento, certificado, "infEvento");

        const string wsdlNs = "http://www.portalfiscal.inf.br/nfe/wsdl/RecepcaoEvento4";
        const string soapNs = "http://www.w3.org/2003/05/soap-envelope";
        const string soapAction = "http://www.portalfiscal.inf.br/nfe/wsdl/RecepcaoEvento4/nfeRecepcaoEvento";

        var soapDoc = new XmlDocument();
        var envelope = soapDoc.CreateElement("soap12", "Envelope", soapNs);
        envelope.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        envelope.SetAttribute("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");
        soapDoc.AppendChild(envelope);

        var header = soapDoc.CreateElement("soap12", "Header", soapNs);
        var cabec = soapDoc.CreateElement("nfeCabecMsg", wsdlNs);
        var elCUF = soapDoc.CreateElement("cUF", wsdlNs); elCUF.InnerText = _options.CodigoUF;
        var elVer = soapDoc.CreateElement("versaoDados", wsdlNs); elVer.InnerText = "1.00";
        cabec.AppendChild(elCUF); cabec.AppendChild(elVer);
        header.AppendChild(cabec);
        envelope.AppendChild(header);

        var body = soapDoc.CreateElement("soap12", "Body", soapNs);
        var nfeDadosMsg = soapDoc.CreateElement("nfeDadosMsg", wsdlNs);
        var eventoDoc = new XmlDocument { PreserveWhitespace = false };
        eventoDoc.LoadXml(xmlAssinado);
        nfeDadosMsg.AppendChild(soapDoc.ImportNode(eventoDoc.DocumentElement!, true));
        body.AppendChild(nfeDadosMsg);
        envelope.AppendChild(body);

        var soap = soapDoc.OuterXml;
        _logger.LogInformation("[CCe] SOAP: {Xml}", soap.Length > 2000 ? soap[..2000] : soap);

        try
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificado);
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var content = new StringContent(soap, Encoding.UTF8);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"");
            var response = await http.PostAsync(_options.UrlEvento, content, cancellationToken);
            var retorno = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[CCe] Resposta: {Body}", retorno.Length > 2000 ? retorno[..2000] : retorno);
            return InterpretarCce(retorno, _logger);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[CCe] Erro HTTP.");
            return new CceResultDto(false, "999", $"Erro de comunicação: {ex.Message}");
        }
    }

    private static CceResultDto InterpretarCce(string xml, ILogger? log)
    {
        try
        {
            var doc = new XmlDocument(); doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

            var infEvento = doc.SelectSingleNode("//nfe:infEvento", ns);
            if (infEvento is not null)
            {
                var cs = infEvento.SelectSingleNode("nfe:cStat", ns)?.InnerText ?? "999";
                var xm = infEvento.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? "";
                var prot = infEvento.SelectSingleNode("nfe:nProt", ns)?.InnerText;
                return new CceResultDto(cs is "135" or "136", cs, xm, prot);
            }
            var csAny = doc.SelectSingleNode("//*[local-name()='cStat']")?.InnerText ?? "999";
            var xmAny = doc.SelectSingleNode("//*[local-name()='xMotivo']")?.InnerText ?? "Sem resposta";
            return new CceResultDto(false, csAny, xmAny);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "[CCe] Erro ao interpretar resposta.");
            return new CceResultDto(false, "999", "Erro ao interpretar resposta SEFAZ");
        }
    }

    // ── Privados ──────────────────────────────────────────────────────────────

    private NotaFiscal MontarNotaFiscal(EmitirNFeDto dto, Empresa empresa,
        Cliente? cliente, List<Produto> produtos, int numero)
    {
        var nota = new NotaFiscal
        {
            EmpresaId = empresa.Id,
            ClienteId = cliente?.Id ?? Guid.Empty,
            Numero = numero,
            Serie = dto.Serie,
            NaturezaOperacao = dto.NaturezaOperacao,
            ValorFrete = dto.ValorFrete,
            ValorSeguro = dto.ValorSeguro,
            ValorDesconto = dto.ValorDesconto,
            ValorOutros = dto.ValorOutros,
            Status = StatusNota.Enviada
        };

        int nItem = 1;
        foreach (var itemDto in dto.Itens)
        {
            var produto = produtos.FirstOrDefault(p => p.Id == itemDto.ProdutoId)
                ?? throw new InvalidOperationException($"Produto {itemDto.ProdutoId} não encontrado.");

            var valorTotal = (itemDto.Quantidade * itemDto.ValorUnitario) - itemDto.ValorDesconto;
            nota.Itens.Add(new NotaItem
            {
                ProdutoId = produto.Id,
                NumeroItem = nItem++,
                Quantidade = itemDto.Quantidade,
                Unidade = produto.Unidade,
                ValorUnitario = itemDto.ValorUnitario,
                ValorDesconto = itemDto.ValorDesconto,
                ValorTotal = valorTotal,
                BaseICMS = valorTotal,
                AliquotaICMS = produto.AliquotaICMS,
                ValorICMS = Math.Round(valorTotal * produto.AliquotaICMS / 100, 2),
                AliquotaIPI = produto.AliquotaIPI,
                ValorIPI = Math.Round(valorTotal * produto.AliquotaIPI / 100, 2),
                AliquotaPIS = produto.AliquotaPIS,
                ValorPIS = Math.Round(valorTotal * produto.AliquotaPIS / 100, 2),
                AliquotaCOFINS = produto.AliquotaCOFINS,
                ValorCOFINS = Math.Round(valorTotal * produto.AliquotaCOFINS / 100, 2)
            });
        }
        return nota;
    }

    private void CalcularTotais(NotaFiscal nota, EmitirNFeDto dto)
    {
        nota.ValorProdutos = nota.Itens.Sum(i => i.ValorTotal + i.ValorDesconto);
        nota.ValorICMS = nota.Itens.Sum(i => i.ValorICMS);
        nota.ValorIPI = nota.Itens.Sum(i => i.ValorIPI);
        nota.ValorPIS = nota.Itens.Sum(i => i.ValorPIS);
        nota.ValorCOFINS = nota.Itens.Sum(i => i.ValorCOFINS);
        nota.ValorTotal = nota.Itens.Sum(i => i.ValorTotal)
                        + nota.ValorFrete + nota.ValorSeguro
                        + nota.ValorOutros + nota.ValorIPI;
    }

    private string GerarXmlNFe(NotaFiscal nota, Empresa empresa, Cliente? cliente,
        List<Produto> produtos, EmitirNFeDto dto)
    {
        var cUF = _options.CodigoUF;
        var cNF = new Random().Next(10000000, 99999999).ToString();
        var dEmi = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        var ambiente = _options.Ambiente;

        var chave = GerarChaveAcesso(cUF, empresa.CNPJ, nota.Serie, nota.Numero.ToString(), cNF);
        nota.ChaveAcesso = chave;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">");
        sb.AppendLine($"  <infNFe Id=\"NFe{chave}\" versao=\"4.00\">");
        sb.AppendLine("    <ide>");
        sb.AppendLine($"      <cUF>{cUF}</cUF>");
        sb.AppendLine($"      <cNF>{cNF}</cNF>");
        sb.AppendLine($"      <natOp>{XmlEnc(nota.NaturezaOperacao)}</natOp>");
        sb.AppendLine("      <mod>55</mod>");
        sb.AppendLine($"      <serie>{nota.Serie}</serie>");
        sb.AppendLine($"      <nNF>{nota.Numero}</nNF>");
        sb.AppendLine($"      <dhEmi>{dEmi}</dhEmi>");
        sb.AppendLine("      <tpNF>1</tpNF>");
        sb.AppendLine("      <idDest>1</idDest>");
        sb.AppendLine($"      <cMunFG>{_options.CodigoMunicipio}</cMunFG>");
        sb.AppendLine("      <tpImp>1</tpImp>");
        sb.AppendLine("      <tpEmis>1</tpEmis>");
        sb.AppendLine($"      <cDV>{chave[^1]}</cDV>");
        sb.AppendLine($"      <tpAmb>{ambiente}</tpAmb>");
        sb.AppendLine("      <finNFe>1</finNFe>");
        sb.AppendLine("      <indFinal>1</indFinal>");
        sb.AppendLine("      <indPres>1</indPres>");
        sb.AppendLine("      <procEmi>0</procEmi>");
        sb.AppendLine("      <verProc>1.0.0</verProc>");
        sb.AppendLine("    </ide>");

        sb.AppendLine("    <emit>");
        sb.AppendLine($"      <CNPJ>{Limpar(empresa.CNPJ)}</CNPJ>");
        sb.AppendLine($"      <xNome>{XmlEnc(ambiente == "2" ? "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL" : empresa.RazaoSocial)}</xNome>");
        sb.AppendLine($"      <xFant>{XmlEnc(empresa.NomeFantasia)}</xFant>");
        sb.AppendLine("      <enderEmit>");
        sb.AppendLine($"        <xLgr>{XmlEnc(empresa.Logradouro)}</xLgr>");
        sb.AppendLine($"        <nro>{XmlEnc(empresa.Numero)}</nro>");
        if (!string.IsNullOrWhiteSpace(empresa.Complemento))
            sb.AppendLine($"        <xCpl>{XmlEnc(empresa.Complemento)}</xCpl>");
        sb.AppendLine($"        <xBairro>{XmlEnc(empresa.Bairro)}</xBairro>");
        sb.AppendLine($"        <cMun>{_options.CodigoMunicipio}</cMun>");
        sb.AppendLine($"        <xMun>{XmlEnc(empresa.Municipio)}</xMun>");
        sb.AppendLine($"        <UF>{empresa.UF}</UF>");
        sb.AppendLine($"        <CEP>{Limpar(empresa.CEP)}</CEP>");
        sb.AppendLine("        <cPais>1058</cPais>");
        sb.AppendLine("        <xPais>Brasil</xPais>");
        sb.AppendLine("      </enderEmit>");
        sb.AppendLine($"      <IE>{empresa.InscricaoEstadual}</IE>");
        sb.AppendLine("      <CRT>1</CRT>");
        sb.AppendLine("    </emit>");

        sb.AppendLine("    <dest>");
        if (cliente is not null)
        {
            // Destinatário identificado (cliente cadastrado)
            var cpfCnpj = Limpar(cliente.CPF_CNPJ);
            if (!string.IsNullOrWhiteSpace(cpfCnpj))
            {
                sb.AppendLine(cpfCnpj.Length == 14
                    ? $"      <CNPJ>{cpfCnpj}</CNPJ>"
                    : $"      <CPF>{cpfCnpj}</CPF>");
            }
            sb.AppendLine($"      <xNome>{XmlEnc(ambiente == "2" ? "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL" : cliente.Nome)}</xNome>");
            sb.AppendLine("      <enderDest>");
            sb.AppendLine($"        <xLgr>{XmlEnc(cliente.Logradouro)}</xLgr>");
            sb.AppendLine($"        <nro>{XmlEnc(cliente.Numero)}</nro>");
            sb.AppendLine($"        <xBairro>{XmlEnc(cliente.Bairro)}</xBairro>");
            sb.AppendLine($"        <cMun>{cliente.CodigoMunicipio}</cMun>");
            sb.AppendLine($"        <xMun>{XmlEnc(cliente.Municipio)}</xMun>");
            sb.AppendLine($"        <UF>{cliente.UF}</UF>");
            sb.AppendLine($"        <CEP>{Limpar(cliente.CEP)}</CEP>");
            sb.AppendLine("        <cPais>1058</cPais>");
            sb.AppendLine("        <xPais>Brasil</xPais>");
            sb.AppendLine("      </enderDest>");
            var temIE = !string.IsNullOrWhiteSpace(cliente.InscricaoEstadual);
            sb.AppendLine($"      <indIEDest>{(temIE ? "1" : "9")}</indIEDest>");
            if (temIE)
                sb.AppendLine($"      <IE>{cliente.InscricaoEstadual}</IE>");
        }
        else
        {
            // Destinatário avulso (consumidor não identificado — NF-e saída balcão)
            var destCpfCnpj = Limpar(dto.DestinatarioCpfCnpj ?? "");
            if (destCpfCnpj.Length == 14)
                sb.AppendLine($"      <CNPJ>{destCpfCnpj}</CNPJ>");
            else if (destCpfCnpj.Length == 11)
                sb.AppendLine($"      <CPF>{destCpfCnpj}</CPF>");
            // else: sem identificação (consumidor final não identificado)
            var destNome = string.IsNullOrWhiteSpace(dto.DestinatarioNome) ? "CONSUMIDOR NAO IDENTIFICADO" : dto.DestinatarioNome;
            sb.AppendLine($"      <xNome>{XmlEnc(ambiente == "2" ? "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL" : destNome)}</xNome>");
            sb.AppendLine("      <indIEDest>9</indIEDest>");
        }
        sb.AppendLine("    </dest>");

        int nItem = 1;
        foreach (var item in nota.Itens)
        {
            var produto = produtos.First(p => p.Id == item.ProdutoId);
            sb.AppendLine($"    <det nItem=\"{nItem++}\">");
            sb.AppendLine("      <prod>");
            sb.AppendLine($"        <cProd>{produto.Id.ToString()[..8].ToUpper()}</cProd>");
            sb.AppendLine("        <cEAN>SEM GTIN</cEAN>");
            sb.AppendLine($"        <xProd>{XmlEnc(produto.Nome)}</xProd>");
            sb.AppendLine($"        <NCM>{produto.NCM}</NCM>");
            sb.AppendLine($"        <CFOP>{produto.CFOP}</CFOP>");
            sb.AppendLine($"        <uCom>{produto.Unidade}</uCom>");
            sb.AppendLine($"        <qCom>{item.Quantidade:F4}</qCom>");
            sb.AppendLine($"        <vUnCom>{item.ValorUnitario:F10}</vUnCom>");
            sb.AppendLine($"        <vProd>{item.ValorTotal + item.ValorDesconto:F2}</vProd>");
            sb.AppendLine("        <cEANTrib>SEM GTIN</cEANTrib>");
            sb.AppendLine($"        <uTrib>{produto.Unidade}</uTrib>");
            sb.AppendLine($"        <qTrib>{item.Quantidade:F4}</qTrib>");
            sb.AppendLine($"        <vUnTrib>{item.ValorUnitario:F10}</vUnTrib>");
            sb.AppendLine("        <indTot>1</indTot>");
            sb.AppendLine("      </prod>");
            sb.AppendLine("      <imposto>");
            sb.AppendLine("        <ICMS><ICMSSN102>");
            sb.AppendLine("          <orig>0</orig>");
            sb.AppendLine($"          <CSOSN>{produto.CSOSN.PadLeft(3, '0')}</CSOSN>");
            sb.AppendLine("        </ICMSSN102></ICMS>");
            sb.AppendLine("        <PIS><PISNT><CST>07</CST></PISNT></PIS>");
            sb.AppendLine("        <COFINS><COFINSNT><CST>07</CST></COFINSNT></COFINS>");
            sb.AppendLine("      </imposto>");
            sb.AppendLine("    </det>");
        }

        sb.AppendLine("    <total><ICMSTot>");
        sb.AppendLine("      <vBC>0.00</vBC><vICMS>0.00</vICMS><vICMSDeson>0.00</vICMSDeson>");
        sb.AppendLine("      <vFCPUFDest>0.00</vFCPUFDest><vICMSUFDest>0.00</vICMSUFDest><vICMSUFRemet>0.00</vICMSUFRemet>");
        sb.AppendLine("      <vFCP>0.00</vFCP><vBCST>0.00</vBCST><vST>0.00</vST><vFCPST>0.00</vFCPST><vFCPSTRet>0.00</vFCPSTRet>");
        sb.AppendLine($"      <vProd>{nota.ValorProdutos:F2}</vProd>");
        sb.AppendLine($"      <vFrete>{nota.ValorFrete:F2}</vFrete><vSeg>{nota.ValorSeguro:F2}</vSeg>");
        sb.AppendLine($"      <vDesc>{nota.ValorDesconto:F2}</vDesc>");
        sb.AppendLine("      <vII>0.00</vII><vIPI>0.00</vIPI><vIPIDevol>0.00</vIPIDevol>");
        sb.AppendLine("      <vPIS>0.00</vPIS><vCOFINS>0.00</vCOFINS>");
        sb.AppendLine($"      <vOutro>{nota.ValorOutros:F2}</vOutro><vNF>{nota.ValorTotal:F2}</vNF>");
        sb.AppendLine("    </ICMSTot></total>");

        sb.AppendLine("    <transp><modFrete>9</modFrete></transp>");
        sb.AppendLine("    <pag><detPag>");
        sb.AppendLine("      <tPag>01</tPag>");
        sb.AppendLine($"      <vPag>{nota.ValorTotal:F2}</vPag>");
        sb.AppendLine("    </detPag></pag>");
        sb.AppendLine("  </infNFe>");
        sb.AppendLine("</NFe>");
        return sb.ToString();
    }

    // SignedXml subclass to resolve custom Id attribute (NF-e uses Id="NFe..." not xml:id)
    private sealed class NFeSignedXml : SignedXml
    {
        public NFeSignedXml(XmlDocument doc) : base(doc) { }
        public override XmlElement? GetIdElement(XmlDocument document, string idValue)
        {
            var elem = base.GetIdElement(document, idValue);
            if (elem == null)
                elem = document.SelectSingleNode($"//*[@Id='{idValue}']") as XmlElement;
            return elem;
        }
    }

    private static string AssinarXml(string xmlContent, X509Certificate2 certificado)
        => AssinarXml(xmlContent, certificado, "infNFe");

    private static string AssinarXml(string xmlContent, X509Certificate2 certificado, string signedTag)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.LoadXml(xmlContent);

        var signedXml = new NFeSignedXml(doc) { SigningKey = certificado.GetRSAPrivateKey() };
        var idAttr = doc.DocumentElement!.SelectSingleNode($"//*[@Id]")!.Attributes!["Id"]!.Value;
        var reference = new Reference { Uri = $"#{idAttr}" };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        reference.DigestMethod = SignedXml.XmlDsigSHA1Url;
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificado));
        signedXml.KeyInfo = keyInfo;
        signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
        signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        signedXml.ComputeSignature();

        var xmlSig = signedXml.GetXml();
        var signedElement = doc.GetElementsByTagName(signedTag)[0]!;
        signedElement.ParentNode!.InsertAfter(doc.ImportNode(xmlSig, true), signedElement);
        return doc.DocumentElement!.OuterXml;
    }

    private static ConsultarSefazResultDto InterpretarConsultaProtocolo(string xml, ILogger? log)
    {
        try
        {
            var doc = new XmlDocument(); doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

            // Consulta sem erro: retConsSitNFe/protNFe/infProt
            var infProt = doc.SelectSingleNode("//nfe:retConsSitNFe/nfe:protNFe/nfe:infProt", ns);
            if (infProt is not null)
            {
                var cs = infProt.SelectSingleNode("nfe:cStat", ns)?.InnerText ?? "999";
                var xm = infProt.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? string.Empty;
                var prot = infProt.SelectSingleNode("nfe:nProt", ns)?.InnerText;
                var dh = infProt.SelectSingleNode("nfe:dhRecbto", ns)?.InnerText;
                return new ConsultarSefazResultDto(cs == "100", cs, xm, prot, dh);
            }
            // Rejeição no nível do retConsSitNFe
            var cStat = doc.SelectSingleNode("//nfe:retConsSitNFe/nfe:cStat", ns)?.InnerText;
            var xMotivo = doc.SelectSingleNode("//nfe:retConsSitNFe/nfe:xMotivo", ns)?.InnerText;
            if (cStat is not null) return new ConsultarSefazResultDto(false, cStat, xMotivo ?? string.Empty);
            // Fallback
            var csAny = doc.SelectSingleNode("//*[local-name()='cStat']")?.InnerText ?? "999";
            var xmAny = doc.SelectSingleNode("//*[local-name()='xMotivo']")?.InnerText ?? "Sem resposta";
            return new ConsultarSefazResultDto(false, csAny, xmAny);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "[NFe] Erro interpretar ConsultaProtocolo.");
            return new ConsultarSefazResultDto(false, "999", "Erro ao interpretar resposta SEFAZ");
        }
    }

    private static StatusServicoResultDto InterpretarStatus(string xml, ILogger? log)
    {
        try
        {
            var doc = new XmlDocument(); doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

            var ret = doc.SelectSingleNode("//nfe:retConsStatServ", ns);
            if (ret is not null)
            {
                var cs = ret.SelectSingleNode("nfe:cStat", ns)?.InnerText ?? "999";
                var xm = ret.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? string.Empty;
                var dh = ret.SelectSingleNode("nfe:dhRecbto", ns)?.InnerText;
                var tm = ret.SelectSingleNode("nfe:tMed", ns)?.InnerText;
                return new StatusServicoResultDto(cs == "107", cs, xm, dh, tm);
            }
            var csAny = doc.SelectSingleNode("//*[local-name()='cStat']")?.InnerText ?? "999";
            var xmAny = doc.SelectSingleNode("//*[local-name()='xMotivo']")?.InnerText ?? "Sem resposta";
            return new StatusServicoResultDto(false, csAny, xmAny);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "[NFe] Erro interpretar Status.");
            return new StatusServicoResultDto(false, "999", "Erro ao interpretar resposta SEFAZ");
        }
    }

    private static InutilizarResultDto InterpretarInutilizacao(string xml, ILogger? log)
    {
        try
        {
            var doc = new XmlDocument(); doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

            var infInut = doc.SelectSingleNode("//nfe:retInutNFe/nfe:infInut", ns);
            if (infInut is not null)
            {
                var cs = infInut.SelectSingleNode("nfe:cStat", ns)?.InnerText ?? "999";
                var xm = infInut.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? string.Empty;
                var prot = infInut.SelectSingleNode("nfe:nProt", ns)?.InnerText;
                var dh = infInut.SelectSingleNode("nfe:dhRecbto", ns)?.InnerText;
                return new InutilizarResultDto(cs == "102", cs, xm, prot, dh);
            }
            var csAny = doc.SelectSingleNode("//*[local-name()='cStat']")?.InnerText ?? "999";
            var xmAny = doc.SelectSingleNode("//*[local-name()='xMotivo']")?.InnerText ?? "Sem resposta";
            return new InutilizarResultDto(false, csAny, xmAny);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "[NFe] Erro interpretar Inutilização.");
            return new InutilizarResultDto(false, "999", "Erro ao interpretar resposta SEFAZ");
        }
    }

    private async Task<(string cStat, string xMotivo, string protocolo)> EnviarParaSefazAsync(
        string xmlAssinado, string cnpj, X509Certificate2 certificado, CancellationToken ct)
    {
        var url = _options.IsHomologacao
            ? _options.SefazUrlHomologacao
            : _options.SefazUrlProducao;

        var idLote = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var soapAction = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4/nfeAutorizacaoLote";
        const string soapNs = "http://www.w3.org/2003/05/soap-envelope";
        const string wsdlNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4";
        const string nfeNs  = "http://www.portalfiscal.inf.br/nfe";

        // Build SOAP using XmlDocument (ImportNode) to guarantee correct namespace handling
        var soapDoc = new XmlDocument();
        var envelope = soapDoc.CreateElement("soap12", "Envelope", soapNs);
        // Add xsi and xsd namespace declarations (some ASMX servers require these)
        envelope.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        envelope.SetAttribute("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");
        soapDoc.AppendChild(envelope);

        // Header with nfeCabecMsg (required by NFeAutorizacao4 WSDL)
        var header = soapDoc.CreateElement("soap12", "Header", soapNs);
        envelope.AppendChild(header);
        var cabecMsg = soapDoc.CreateElement("nfeCabecMsg", wsdlNs);
        var elCUF = soapDoc.CreateElement("cUF", wsdlNs);
        elCUF.InnerText = _options.CodigoUF;
        var elVersao = soapDoc.CreateElement("versaoDados", wsdlNs);
        elVersao.InnerText = "4.00";
        cabecMsg.AppendChild(elCUF);
        cabecMsg.AppendChild(elVersao);
        header.AppendChild(cabecMsg);

        // Body — document/literal: nfeDadosMsg goes DIRECTLY in the body (no nfeAutorizacaoLote wrapper)
        var body = soapDoc.CreateElement("soap12", "Body", soapNs);
        envelope.AppendChild(body);
        var nfeDadosMsg = soapDoc.CreateElement("nfeDadosMsg", wsdlNs);

        // enviNFe wrapper
        var enviNFe = soapDoc.CreateElement("enviNFe", nfeNs);
        enviNFe.SetAttribute("versao", "4.00");
        var elIdLote = soapDoc.CreateElement("idLote", nfeNs);
        elIdLote.InnerText = idLote.ToString();
        var elIndSinc = soapDoc.CreateElement("indSinc", nfeNs);
        elIndSinc.InnerText = "1";
        enviNFe.AppendChild(elIdLote);
        enviNFe.AppendChild(elIndSinc);

        // Import signed NFe XML into SOAP document (preserves signature integrity)
        var nfeDoc = new XmlDocument { PreserveWhitespace = false };
        nfeDoc.LoadXml(xmlAssinado);
        var importedNfe = soapDoc.ImportNode(nfeDoc.DocumentElement!, true);
        enviNFe.AppendChild(importedNfe);
        nfeDadosMsg.AppendChild(enviNFe);
        body.AppendChild(nfeDadosMsg);

        var lote = soapDoc.OuterXml;
        _logger.LogInformation("[NFe] SOAP enviado para {Url} (primeiros 3000 chars): {Xml}",
            url, lote.Length > 3000 ? lote[..3000] : lote);

        try
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificado);
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };

            // Content-Type SOAP 1.2 com action sem double-quoting
            var content = new StringContent(lote, Encoding.UTF8);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"");

            var response = await http.PostAsync(url, content, ct);
            var retorno = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("[NFe] Resposta SEFAZ HTTP {Status}: {Body}",
                (int)response.StatusCode, retorno.Length > 3000 ? retorno[..3000] : retorno);
            return InterpretarRetorno(retorno, _logger);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[NFe] Erro HTTP SEFAZ.");
            return ("999", $"Erro de comunicação: {ex.Message}", string.Empty);
        }
    }

    private static (string, string, string) InterpretarRetorno(string xml, ILogger? log = null)
    {
        try
        {
            var doc = new XmlDocument(); doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

            // Resposta síncrona OK: protNFe/infProt (note: é protNFe, não retProt)
            var prot = doc.SelectSingleNode("//nfe:protNFe/nfe:infProt", ns);
            if (prot is not null)
                return (prot.SelectSingleNode("nfe:cStat", ns)?.InnerText ?? "999",
                        prot.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? "Sem resposta",
                        prot.SelectSingleNode("nfe:nProt", ns)?.InnerText ?? string.Empty);

            // Rejeição a nível de lote: retEnviNFe/cStat sem protNFe
            var cStatLote = doc.SelectSingleNode("//nfe:retEnviNFe/nfe:cStat", ns)?.InnerText;
            var xMotivoLote = doc.SelectSingleNode("//nfe:retEnviNFe/nfe:xMotivo", ns)?.InnerText;
            if (cStatLote is not null)
                return (cStatLote, xMotivoLote ?? "Rejeitado pelo lote", string.Empty);

            // Fault SOAP ou outro erro: tenta extrair qualquer cStat/xMotivo
            var cStatAny = doc.SelectSingleNode("//*[local-name()='cStat']")?.InnerText;
            var xMotivoAny = doc.SelectSingleNode("//*[local-name()='xMotivo']")?.InnerText;
            if (cStatAny is not null)
                return (cStatAny, xMotivoAny ?? "Erro desconhecido", string.Empty);

            log?.LogWarning("[NFe] Não encontrou cStat no XML da SEFAZ. XML: {Xml}", xml.Length > 500 ? xml[..500] : xml);
            return ("999", "Resposta SEFAZ sem cStat", string.Empty);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "[NFe] Falha ao parsear XML SEFAZ. Conteúdo: {Xml}", xml.Length > 500 ? xml[..500] : xml);
            return ("999", "Erro ao interpretar resposta SEFAZ", string.Empty);
        }
    }

    private static string GerarChaveAcesso(string cUF, string cnpj, string serie, string nNF, string cNF)
    {
        var aamm = DateTime.Now.ToString("yyMM");
        var chave = $"{cUF}{aamm}{Limpar(cnpj).PadLeft(14, '0')}55{serie.PadLeft(3, '0')}{nNF.PadLeft(9, '0')}1{cNF.PadLeft(8, '0')}";
        var dv = CalcularDV(chave);
        return chave + dv;
    }

    private static int CalcularDV(string chave)
    {
        var pesos = new[] { 2, 3, 4, 5, 6, 7, 8, 9 };
        int soma = 0, idx = 0;
        for (int i = chave.Length - 1; i >= 0; i--)
            soma += int.Parse(chave[i].ToString()) * pesos[idx++ % 8];
        var r = soma % 11;
        return r < 2 ? 0 : 11 - r;
    }

    private static string Limpar(string v) => new(v?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());
    private static string XmlEnc(string v) => System.Security.SecurityElement.Escape(v ?? string.Empty)!;
}
