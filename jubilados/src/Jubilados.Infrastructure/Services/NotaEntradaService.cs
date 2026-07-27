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

    public async Task<ImportarXmlResultDto> PrevisualizarXmlEntradaAsync(
        Guid empresaId, string xmlBase64, CancellationToken cancellationToken = default)
    {
        var (lida, erro) = await TentarLerXmlEntradaAsync(empresaId, xmlBase64, cancellationToken);
        if (erro is not null)
            return erro;

        var produtos = lida!.Itens.Select(i => new ProdutoImportadoDto(
            ItemNumero: i.ItemNumero,
            Id: i.ProdutoExistenteId ?? Guid.Empty,
            Nome: i.Nome,
            NCM: i.Ncm,
            Unidade: i.Unidade,
            EAN: string.IsNullOrWhiteSpace(i.Ean) ? null : i.Ean,
            CEST: string.IsNullOrWhiteSpace(i.Cest) ? null : i.Cest,
            Custo: i.ValorUnitario,
            JaExistia: i.ProdutoExistenteId.HasValue
        )).ToList();

        FornecedorImportadoDto? fornecedor = null;
        if (lida.Fornecedor is not null)
        {
            fornecedor = new FornecedorImportadoDto(
                Id: lida.Fornecedor.IdExistente ?? Guid.Empty,
                Nome: lida.Fornecedor.Nome,
                CpfCnpj: lida.Fornecedor.CpfCnpj,
                JaExistia: lida.Fornecedor.IdExistente.HasValue);
        }

        return new ImportarXmlResultDto(
            Sucesso: true,
            Mensagem: "Pré-visualização carregada. Preencha todos os complementos fiscais e clique em Enviar importação.",
            ChaveAcesso: lida.ChaveAcesso,
            ProdutosCriados: produtos,
            Fornecedor: fornecedor);
    }

    public async Task<ImportarXmlResultDto> ImportarXmlEntradaAsync(
        Guid empresaId,
        string xmlBase64,
        IReadOnlyList<ComplementoImportacaoProdutoDto> complementos,
        CancellationToken cancellationToken = default)
    {
        var (lida, erro) = await TentarLerXmlEntradaAsync(empresaId, xmlBase64, cancellationToken);
        if (erro is not null)
            return erro;

        if (complementos is null || complementos.Count == 0)
            return new ImportarXmlResultDto(false, "Informe os complementos fiscais obrigatórios de todos os itens.", lida!.ChaveAcesso);

        var compPorItem = new Dictionary<int, ComplementoImportacaoProdutoDto>();
        foreach (var comp in complementos)
        {
            if (compPorItem.ContainsKey(comp.ItemNumero))
                return new ImportarXmlResultDto(false, $"Complemento duplicado para o item {comp.ItemNumero}.", lida!.ChaveAcesso);

            if (comp.PrecoVenda <= 0)
                return new ImportarXmlResultDto(false, $"Item {comp.ItemNumero}: preço de venda deve ser maior que zero.", lida!.ChaveAcesso);

            if (string.IsNullOrWhiteSpace(comp.CClassTrib) || comp.CClassTrib.Trim().Length != 6 || !comp.CClassTrib.Trim().All(char.IsDigit))
                return new ImportarXmlResultDto(false, $"Item {comp.ItemNumero}: cClassTrib obrigatório e deve ter 6 dígitos.", lida!.ChaveAcesso);

            if (string.IsNullOrWhiteSpace(comp.CstIbsCbs) || comp.CstIbsCbs.Trim().Length != 3 || !comp.CstIbsCbs.Trim().All(char.IsDigit))
                return new ImportarXmlResultDto(false, $"Item {comp.ItemNumero}: CST IBS/CBS obrigatório e deve ter 3 dígitos.", lida!.ChaveAcesso);

            if (string.IsNullOrWhiteSpace(comp.Cfop) || comp.Cfop.Trim().Length != 4 || !comp.Cfop.Trim().All(char.IsDigit))
                return new ImportarXmlResultDto(false, $"Item {comp.ItemNumero}: CFOP obrigatório e deve ter 4 dígitos.", lida!.ChaveAcesso);

            compPorItem[comp.ItemNumero] = comp;
        }

        foreach (var item in lida!.Itens)
        {
            if (!compPorItem.ContainsKey(item.ItemNumero))
                return new ImportarXmlResultDto(false, $"Item {item.ItemNumero}: complemento fiscal pendente.", lida.ChaveAcesso);
        }

        var strategy = _db.Database.CreateExecutionStrategy();

        var (produtosImportados, notaId, fornecedorId) = await strategy.ExecuteAsync(async () =>
        {
            using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            Guid? fId = lida!.Fornecedor?.IdExistente;
            if (lida.Fornecedor is not null && !fId.HasValue && !string.IsNullOrWhiteSpace(lida.Fornecedor.CpfCnpj))
            {
                var novoFornecedor = new Fornecedor
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    Nome = lida.Fornecedor.Nome,
                    CPF_CNPJ = lida.Fornecedor.CpfCnpj,
                    InscricaoEstadual = lida.Fornecedor.InscricaoEstadual,
                    Logradouro = lida.Fornecedor.Logradouro,
                    Numero = lida.Fornecedor.Numero,
                    Complemento = lida.Fornecedor.Complemento,
                    Bairro = lida.Fornecedor.Bairro,
                    Municipio = lida.Fornecedor.Municipio,
                    CodigoMunicipio = lida.Fornecedor.CodigoMunicipio,
                    UF = lida.Fornecedor.Uf,
                    CEP = lida.Fornecedor.Cep
                };
                _db.Fornecedores.Add(novoFornecedor);
                fId = novoFornecedor.Id;
            }

            // Permite re-importar: remove nota anterior se existir
            var notaAnterior = await _db.NotasFiscais
                .FirstOrDefaultAsync(n => n.EmpresaId == empresaId && n.ChaveAcesso == lida!.ChaveAcesso, cancellationToken);
            if (notaAnterior is not null)
            {
                _db.NotasFiscais.Remove(notaAnterior);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var nota = new NotaFiscal
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ChaveAcesso = lida.ChaveAcesso,
                Numero = lida.Numero,
                Serie = lida.Serie,
                NaturezaOperacao = lida.NaturezaOperacao,
                Status = StatusNota.Autorizada,
                CStat = "100",
                XMotivo = "Importado via XML com complementos validados",
                TipoOperacao = "0",
                EmitidaEm = lida.EmitidaEm,
                AutorizadaEm = lida.EmitidaEm,
                XmlEnvio = lida.XmlConteudo
            };

            var lista = new List<ProdutoImportadoDto>();
            decimal valorTotal = 0m;

            foreach (var item in lida.Itens)
            {
                var comp = compPorItem[item.ItemNumero];
                Produto produto;
                var jaExistia = item.ProdutoExistenteId.HasValue;

                if (jaExistia)
                {
                    produto = await _db.Produtos.FirstOrDefaultAsync(p => p.Id == item.ProdutoExistenteId!.Value, cancellationToken)
                        ?? new Produto { Id = Guid.NewGuid(), EmpresaId = empresaId };
                    if (produto.EmpresaId == Guid.Empty)
                        _db.Produtos.Add(produto);
                }
                else
                {
                    produto = new Produto
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = empresaId,
                        Nome = item.Nome,
                        NCM = item.Ncm,
                        Unidade = string.IsNullOrWhiteSpace(item.Unidade) ? "UN" : item.Unidade,
                        EAN = string.IsNullOrWhiteSpace(item.Ean) ? null : item.Ean,
                        CEST = string.IsNullOrWhiteSpace(item.Cest) ? string.Empty : item.Cest,
                        CSOSN = "102",
                        CST = "00",
                        QuantidadeEstoque = Math.Round(item.Quantidade)
                    };
                    _db.Produtos.Add(produto);
                }

                produto.Nome = string.IsNullOrWhiteSpace(produto.Nome) ? item.Nome : produto.Nome;
                produto.NCM = string.IsNullOrWhiteSpace(produto.NCM) ? item.Ncm : produto.NCM;
                produto.Unidade = string.IsNullOrWhiteSpace(item.Unidade) ? (string.IsNullOrWhiteSpace(produto.Unidade) ? "UN" : produto.Unidade) : item.Unidade;
                produto.EAN = string.IsNullOrWhiteSpace(item.Ean) ? produto.EAN : item.Ean;
                produto.CEST = string.IsNullOrWhiteSpace(item.Cest) ? (produto.CEST ?? string.Empty) : item.Cest;
                produto.Preco = comp.PrecoVenda;
                produto.CFOP = comp.Cfop.Trim();
                produto.CClassTrib = comp.CClassTrib.Trim();
                produto.CstIbsCbs = comp.CstIbsCbs.Trim();
                if (!string.IsNullOrWhiteSpace(comp.Csosn))
                    produto.CSOSN = comp.Csosn.Trim();
                // Adiciona quantidade comprada ao estoque (arredondado para inteiro)
                produto.QuantidadeEstoque += Math.Round(item.Quantidade);
                produto.CodigoInterno = string.IsNullOrWhiteSpace(comp.CodigoInterno) ? produto.CodigoInterno : comp.CodigoInterno.Trim();
                produto.Categoria = string.IsNullOrWhiteSpace(comp.Categoria) ? produto.Categoria : comp.Categoria.Trim();
                produto.Organizacao = string.IsNullOrWhiteSpace(comp.Organizacao) ? produto.Organizacao : comp.Organizacao.Trim();
                produto.Padronizacao = string.IsNullOrWhiteSpace(comp.Padronizacao) ? produto.Padronizacao : comp.Padronizacao.Trim();

                nota.Itens.Add(new NotaItem
                {
                    ProdutoId = produto.Id,
                    NumeroItem = item.ItemNumero,
                    Quantidade = item.Quantidade,
                    Unidade = string.IsNullOrWhiteSpace(item.Unidade) ? "UN" : item.Unidade,
                    ValorUnitario = item.ValorUnitario,
                    ValorDesconto = 0,
                    ValorTotal = item.ValorTotal,
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

                valorTotal += item.ValorTotal;
                lista.Add(new ProdutoImportadoDto(
                    ItemNumero: item.ItemNumero,
                    Id: produto.Id,
                    Nome: item.Nome,
                    NCM: item.Ncm,
                    Unidade: item.Unidade,
                    EAN: string.IsNullOrWhiteSpace(item.Ean) ? null : item.Ean,
                    CEST: string.IsNullOrWhiteSpace(item.Cest) ? null : item.Cest,
                    Custo: item.ValorUnitario,
                    JaExistia: jaExistia
                ));
            }

            nota.ValorProdutos = valorTotal;
            nota.ValorTotal = valorTotal;
            _db.NotasFiscais.Add(nota);
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return (lista, nota.Id, fId);
        });

        var novos = produtosImportados.Count(p => !p.JaExistia);
        var fornecedorDto = lida!.Fornecedor is null
            ? null
            : new FornecedorImportadoDto(
                Id: fornecedorId ?? Guid.Empty,
                Nome: lida.Fornecedor.Nome,
                CpfCnpj: lida.Fornecedor.CpfCnpj,
                JaExistia: lida.Fornecedor.IdExistente.HasValue);

        return new ImportarXmlResultDto(
            Sucesso: true,
            Mensagem: $"Nota importada com sucesso. {novos} produto(s) criado(s), {produtosImportados.Count - novos} já existia(m).",
            ChaveAcesso: lida!.ChaveAcesso,
            NotaFiscalId: notaId,
            ProdutosCriados: produtosImportados,
            Fornecedor: fornecedorDto);
    }

    private async Task<(XmlEntradaLida? Lida, ImportarXmlResultDto? Erro)> TentarLerXmlEntradaAsync(
        Guid empresaId,
        string xmlBase64,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Entrada] Lendo XML para EmpresaId={Id}", empresaId);

        string xmlContent;
        try
        {
            var bytes = Convert.FromBase64String(xmlBase64);
            xmlContent = Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return (null, new ImportarXmlResultDto(false, "XML inválido: não é base64 válido."));
        }

        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(xmlContent);
        }
        catch
        {
            return (null, new ImportarXmlResultDto(false, "XML inválido: não é XML bem-formado."));
        }

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

        var infNFe = doc.SelectSingleNode("//nfe:infNFe", ns);
        if (infNFe is null)
            return (null, new ImportarXmlResultDto(false, "XML não contém elemento infNFe."));

        var chave = infNFe.Attributes?["Id"]?.Value?.Replace("NFe", "") ?? string.Empty;

        var numero = int.TryParse(infNFe.SelectSingleNode("nfe:ide/nfe:nNF", ns)?.InnerText, out var n) ? n : 0;
        var serie = infNFe.SelectSingleNode("nfe:ide/nfe:serie", ns)?.InnerText ?? "1";
        var natOp = infNFe.SelectSingleNode("nfe:ide/nfe:natOp", ns)?.InnerText ?? "Compra";
        var dhEmi = infNFe.SelectSingleNode("nfe:ide/nfe:dhEmi", ns)?.InnerText;
        var emitidaEm = DateTime.TryParse(dhEmi, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow;

        FornecedorXmlLido? fornecedor = null;
        var emitNode = infNFe.SelectSingleNode("nfe:emit", ns);
        if (emitNode is not null)
        {
            var cpfCnpj = (emitNode.SelectSingleNode("nfe:CNPJ", ns)?.InnerText
                          ?? emitNode.SelectSingleNode("nfe:CPF", ns)?.InnerText
                          ?? string.Empty).Trim();
            var end = emitNode.SelectSingleNode("nfe:enderEmit", ns);

            Guid? fornecedorExistenteId = null;
            if (!string.IsNullOrWhiteSpace(cpfCnpj))
            {
                fornecedorExistenteId = await _db.Fornecedores
                    .AsNoTracking()
                    .Where(f => f.EmpresaId == empresaId && f.CPF_CNPJ == cpfCnpj)
                    .Select(f => (Guid?)f.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            fornecedor = new FornecedorXmlLido
            {
                IdExistente = fornecedorExistenteId,
                CpfCnpj = cpfCnpj,
                Nome = emitNode.SelectSingleNode("nfe:xNome", ns)?.InnerText ?? "Fornecedor Importado",
                InscricaoEstadual = emitNode.SelectSingleNode("nfe:IE", ns)?.InnerText ?? string.Empty,
                Logradouro = end?.SelectSingleNode("nfe:xLgr", ns)?.InnerText ?? string.Empty,
                Numero = end?.SelectSingleNode("nfe:nro", ns)?.InnerText ?? string.Empty,
                Complemento = end?.SelectSingleNode("nfe:xCpl", ns)?.InnerText ?? string.Empty,
                Bairro = end?.SelectSingleNode("nfe:xBairro", ns)?.InnerText ?? string.Empty,
                Municipio = end?.SelectSingleNode("nfe:xMun", ns)?.InnerText ?? string.Empty,
                CodigoMunicipio = end?.SelectSingleNode("nfe:cMun", ns)?.InnerText ?? string.Empty,
                Uf = end?.SelectSingleNode("nfe:UF", ns)?.InnerText ?? string.Empty,
                Cep = end?.SelectSingleNode("nfe:CEP", ns)?.InnerText ?? string.Empty
            };
        }

        var itens = new List<ItemXmlLido>();
        var detNos = infNFe.SelectNodes("nfe:det", ns);
        if (detNos is not null)
        {
            foreach (XmlNode det in detNos)
            {
                var prod = det.SelectSingleNode("nfe:prod", ns);
                if (prod is null) continue;

                var itemNumero = int.TryParse(det.Attributes?["nItem"]?.Value, out var nItem) ? nItem : (itens.Count + 1);
                var nome = prod.SelectSingleNode("nfe:xProd", ns)?.InnerText ?? "Produto Importado";
                var ncm = prod.SelectSingleNode("nfe:NCM", ns)?.InnerText ?? "00000000";
                var cfop = prod.SelectSingleNode("nfe:CFOP", ns)?.InnerText ?? "1102";
                var unidade = prod.SelectSingleNode("nfe:uCom", ns)?.InnerText ?? "UN";
                var cest = prod.SelectSingleNode("nfe:CEST", ns)?.InnerText ?? string.Empty;
                var ean = prod.SelectSingleNode("nfe:cEAN", ns)?.InnerText ?? string.Empty;
                if (ean == "SEM GTIN") ean = string.Empty;

                var qtd = decimal.TryParse(prod.SelectSingleNode("nfe:qCom", ns)?.InnerText,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var q) ? q : 1;
                var vUn = decimal.TryParse(prod.SelectSingleNode("nfe:vUnCom", ns)?.InnerText,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var vu) ? vu : 0;
                var vTotal = decimal.TryParse(prod.SelectSingleNode("nfe:vProd", ns)?.InnerText,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var vp) ? vp : qtd * vUn;

                var produtoExistenteId = await _db.Produtos
                    .AsNoTracking()
                    .Where(p => p.EmpresaId == empresaId && p.NCM == ncm && p.Nome == nome)
                    .Select(p => (Guid?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                itens.Add(new ItemXmlLido
                {
                    ItemNumero = itemNumero,
                    Nome = nome,
                    Ncm = ncm,
                    Cfop = cfop,
                    Unidade = unidade,
                    Cest = cest,
                    Ean = ean,
                    Quantidade = qtd,
                    ValorUnitario = vUn,
                    ValorTotal = vTotal,
                    ProdutoExistenteId = produtoExistenteId
                });
            }
        }

        var lida = new XmlEntradaLida
        {
            ChaveAcesso = chave,
            Numero = numero,
            Serie = serie,
            NaturezaOperacao = natOp,
            EmitidaEm = emitidaEm,
            XmlConteudo = xmlContent,
            Fornecedor = fornecedor,
            Itens = itens
        };

        return (lida, null);
    }

    private sealed class XmlEntradaLida
    {
        public required string ChaveAcesso { get; init; }
        public required int Numero { get; init; }
        public required string Serie { get; init; }
        public required string NaturezaOperacao { get; init; }
        public required DateTime EmitidaEm { get; init; }
        public required string XmlConteudo { get; init; }
        public FornecedorXmlLido? Fornecedor { get; init; }
        public required List<ItemXmlLido> Itens { get; init; }
    }

    private sealed class FornecedorXmlLido
    {
        public Guid? IdExistente { get; init; }
        public required string CpfCnpj { get; init; }
        public required string Nome { get; init; }
        public required string InscricaoEstadual { get; init; }
        public required string Logradouro { get; init; }
        public required string Numero { get; init; }
        public required string Complemento { get; init; }
        public required string Bairro { get; init; }
        public required string Municipio { get; init; }
        public required string CodigoMunicipio { get; init; }
        public required string Uf { get; init; }
        public required string Cep { get; init; }
    }

    private sealed class ItemXmlLido
    {
        public required int ItemNumero { get; init; }
        public required string Nome { get; init; }
        public required string Ncm { get; init; }
        public required string Cfop { get; init; }
        public required string Unidade { get; init; }
        public required string Cest { get; init; }
        public required string Ean { get; init; }
        public required decimal Quantidade { get; init; }
        public required decimal ValorUnitario { get; init; }
        public required decimal ValorTotal { get; init; }
        public Guid? ProdutoExistenteId { get; init; }
    }
}
