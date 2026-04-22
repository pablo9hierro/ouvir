using Jubilados.Application.DTOs;
using Jubilados.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jubilados.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class NFeController : ControllerBase
{
    private readonly INFeService _nfeService;
    private readonly InotaEntradaService _entradaService;
    private readonly IManifestacaoService _manifestacaoService;
    private readonly IDanfeService _danfeService;
    private readonly ISpedService _spedService;
    private readonly ICancelamentoService _cancelamentoService;
    private readonly ILogger<NFeController> _logger;

    public NFeController(
        INFeService nfeService,
        InotaEntradaService entradaService,
        IManifestacaoService manifestacaoService,
        IDanfeService danfeService,
        ISpedService spedService,
        ICancelamentoService cancelamentoService,
        ILogger<NFeController> logger)
    {
        _nfeService = nfeService;
        _entradaService = entradaService;
        _manifestacaoService = manifestacaoService;
        _danfeService = danfeService;
        _spedService = spedService;
        _cancelamentoService = cancelamentoService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/nfe/emitir — Emite uma NFe na SEFAZ.
    /// </summary>
    [HttpPost("emitir")]
    [ProducesResponseType(typeof(NFeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Emitir(
        [FromBody] EmitirNFeDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.Itens is null || dto.Itens.Count == 0)
            return BadRequest(new { erro = "A nota fiscal deve possuir ao menos 1 item." });

        try
        {
            var resultado = await _nfeService.EmitirNFeAsync(dto, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[API] Erro de negócio ao emitir NFe.");
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro inesperado ao emitir NFe.");
            return StatusCode(500, new { erro = "Erro interno. Consulte os logs." });
        }
    }

    /// <summary>
    /// POST /api/nfe/emitir-nfce — Emite um NFC-e (Cupom Fiscal Eletrônico mod=65) na SEFAZ.
    /// </summary>
    [HttpPost("emitir-nfce")]
    [ProducesResponseType(typeof(NfceResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> EmitirNFCe(
        [FromBody] EmitirNFCeDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.Itens is null || dto.Itens.Count == 0)
            return BadRequest(new { erro = "O cupom fiscal deve possuir ao menos 1 item." });
        try
        {
            var resultado = await _nfeService.EmitirNFCeAsync(dto, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[API] Erro de negócio ao emitir NFC-e.");
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro inesperado ao emitir NFC-e.");
            return StatusCode(500, new { erro = "Erro interno. Consulte os logs.", detalhe = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/nfe/{id} — Consulta detalhes de uma NFe pelo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NFeDetalheDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Consultar(Guid id, CancellationToken cancellationToken)
    {
        var nota = await _nfeService.ConsultarNotaAsync(id, cancellationToken);
        if (nota is null) return NotFound(new { erro = $"NFe {id} não encontrada." });
        return Ok(nota);
    }

    /// <summary>
    /// POST /api/nfe/consultar — Consulta situação de NFe com filtros.
    /// Body: { "empresaId": "...", "chaveAcesso": "..." }
    /// </summary>
    [HttpPost("consultar")]
    [ProducesResponseType(typeof(NFeDetalheDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConsultarPorChave(
        [FromBody] ConsultarNFeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChaveAcesso) && request.NotaId == Guid.Empty)
            return BadRequest(new { erro = "Informe ChaveAcesso ou NotaId." });

        var nota = await _nfeService.ConsultarNotaAsync(request.NotaId, cancellationToken);
        if (nota is null) return NotFound(new { erro = "NFe não encontrada." });
        return Ok(nota);
    }

    /// <summary>
    /// GET /api/nfe/entrada?empresaId={id}&amp;ultimoNSU={nsu} — Consulta NFes de entrada.
    /// </summary>
    [HttpGet("entrada")]
    [ProducesResponseType(typeof(EntradaResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConsultarEntrada(
        [FromQuery] Guid empresaId,
        [FromQuery] long ultimoNSU = 0,
        CancellationToken cancellationToken = default)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });

        try
        {
            var resultado = await _entradaService.ConsultarNotasEntradaAsync(
                new ConsultarEntradaDto(empresaId, ultimoNSU), cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao consultar NFes de entrada.");
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }

    /// <summary>
    /// POST /api/nfe/manifestar — Manifesta o destinatário de uma NFe.
    /// </summary>
    [HttpPost("manifestar")]
    [ProducesResponseType(typeof(ManifestacaoResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Manifestar(
        [FromBody] ManifestarNFeDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.NotaFiscalId == Guid.Empty || dto.EmpresaId == Guid.Empty)
            return BadRequest(new { erro = "NotaFiscalId e EmpresaId são obrigatórios." });

        if (string.IsNullOrWhiteSpace(dto.TipoManifestacao))
            return BadRequest(new
            {
                erro = "TipoManifestacao é obrigatório.",
                valoresValidos = new[]
                {
                    "CienciaOperacao", "ConfirmacaoOperacao",
                    "Desconhecimento", "OperacaoNaoRealizada"
                }
            });

        try
        {
            var resultado = await _manifestacaoService.ManifestarNFeAsync(dto, cancellationToken);
            return Ok(resultado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao manifestar NFe.");
            return StatusCode(500, new { erro = "Erro interno.", detalhe = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/nfe/consultar-sefaz — Consulta situação de NFe diretamente na SEFAZ.
    /// Body: { "empresaId": "...", "chaveAcesso": "..." }
    /// </summary>
    [HttpPost("consultar-sefaz")]
    [ProducesResponseType(typeof(ConsultarSefazResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConsultarSefaz(
        [FromBody] ConsultarSefazDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.EmpresaId == Guid.Empty || string.IsNullOrWhiteSpace(dto.ChaveAcesso))
            return BadRequest(new { erro = "EmpresaId e ChaveAcesso são obrigatórios." });
        if (dto.ChaveAcesso.Length != 44)
            return BadRequest(new { erro = "ChaveAcesso deve ter 44 dígitos." });

        try
        {
            var resultado = await _nfeService.ConsultarSefazAsync(dto, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao consultar SEFAZ.");
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }

    /// <summary>
    /// GET /api/nfe/status?empresaId={id} — Consulta status do serviço SEFAZ.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(StatusServicoResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Status(
        [FromQuery] Guid empresaId,
        CancellationToken cancellationToken)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });

        try
        {
            var resultado = await _nfeService.ConsultarStatusAsync(empresaId, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao consultar status SEFAZ.");
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }

    /// <summary>
    /// POST /api/nfe/inutilizar — Inutiliza faixa de numeração na SEFAZ.
    /// </summary>
    [HttpPost("inutilizar")]
    [ProducesResponseType(typeof(InutilizarResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Inutilizar(
        [FromBody] InutilizarDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.EmpresaId == Guid.Empty)
            return BadRequest(new { erro = "EmpresaId é obrigatório." });
        if (string.IsNullOrWhiteSpace(dto.Justificativa) || dto.Justificativa.Length < 15)
            return BadRequest(new { erro = "Justificativa deve ter ao menos 15 caracteres." });
        if (dto.NumeroInicial <= 0 || dto.NumeroFinal < dto.NumeroInicial)
            return BadRequest(new { erro = "Numeração inválida." });

        try
        {
            var resultado = await _nfeService.InutilizarAsync(dto, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao inutilizar numeração.");
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }

    /// <summary>
    /// GET /api/nfe/nuvem-fiscal?cnpj={cnpj} — Lista todas as NF-e (entrada e saída) vinculadas a um CNPJ.
    /// </summary>
    [HttpGet("nuvem-fiscal")]
    [ProducesResponseType(typeof(NuvemFiscalResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> NuvemFiscal(
        [FromQuery] string cnpj,
        CancellationToken cancellationToken)
    {
        var cnpjLimpo = new string((cnpj ?? "").Where(char.IsDigit).ToArray());
        if (cnpjLimpo.Length != 14)
            return BadRequest(new { erro = "CNPJ inválido. Informe 14 dígitos." });

        var empresa = await _nfeService.BuscarEmpresaPorCnpjAsync(cnpjLimpo, cancellationToken);
        if (empresa is null)
            return NotFound(new { erro = $"Nenhuma empresa com CNPJ {cnpj} encontrada." });

        var notas = await _nfeService.ListarNotasPorEmpresaAsync(empresa.Value.Id, cancellationToken);

        var itens = notas.Select(n => new NuvemFiscalItemDto(
            n.Id,
            n.EmpresaId,
            n.TipoOperacao == "0" ? "Entrada" : "Saída",
            n.ChaveAcesso,
            n.Numero,
            n.Serie,
            n.NaturezaOperacao,
            n.Status,
            n.ValorTotal,
            n.EmitidaEm,
            n.Protocolo,
            n.CStat,
            n.Manifestada
        )).ToList();

        return Ok(new NuvemFiscalResultDto(
            Sucesso: true,
            CNPJ: cnpjLimpo,
            RazaoSocial: empresa.Value.RazaoSocial,
            Total: itens.Count,
            Notas: itens
        ));
    }

    /// <summary>
    /// GET /api/nfe/{id}/xml — Baixa o XML da NF-e autorizada.
    /// </summary>
    [HttpGet("{id:guid}/xml")]
    public async Task<IActionResult> DownloadXml(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _nfeService.ObterXmlAsync(id, cancellationToken);
            if (result is null) return NotFound(new { erro = "NF-e não encontrada." });

            var (xml, chave) = result.Value;
            if (string.IsNullOrWhiteSpace(xml))
                return NotFound(new { erro = "XML não disponível para esta NF-e." });

            var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
            var fileName = $"NFe_{chave}.xml";
            return File(bytes, "application/xml", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao baixar XML da NFe {Id}", id);
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }

    /// <summary>
    /// GET /api/nfe/{id}/danfe — Gera e baixa o DANFE em PDF.
    /// </summary>
    [HttpGet("{id:guid}/danfe")]
    public async Task<IActionResult> DownloadDanfe(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var pdf = await _danfeService.GerarDanfeAsync(id, cancellationToken);
            return File(pdf, "application/pdf", $"DANFE_{id}.pdf");
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao gerar DANFE {Id}", id);
            return StatusCode(500, new { erro = "Erro interno ao gerar DANFE." });
        }
    }

    /// <summary>
    /// POST /api/nfe/cce — Envia Carta de Correção Eletrônica (CCe) para uma NF-e autorizada.
    /// Funciona em homologação e produção.
    /// Body: { "empresaId": "...", "notaFiscalId": "...", "correcaoTexto": "min 15 chars" }
    /// </summary>
    [HttpPost("cce")]
    [ProducesResponseType(typeof(CceResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnviarCce([FromBody] CceDto dto, CancellationToken cancellationToken)
    {
        if (dto.EmpresaId == Guid.Empty || dto.NotaFiscalId == Guid.Empty)
            return BadRequest(new { erro = "EmpresaId e NotaFiscalId são obrigatórios." });
        if (string.IsNullOrWhiteSpace(dto.CorrecaoTexto) || dto.CorrecaoTexto.Trim().Length < 15)
            return BadRequest(new { erro = "O texto de correção deve ter no mínimo 15 caracteres." });
        if (dto.CorrecaoTexto.Trim().Length > 1000)
            return BadRequest(new { erro = "O texto de correção deve ter no máximo 1000 caracteres." });

        try
        {
            var resultado = await _nfeService.EnviarCceAsync(dto, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao enviar CCe.");
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }

    /// <summary>
    /// GET /api/nfe/sped?empresaId={id}&amp;dataInicio=2026-01-01&amp;dataFim=2026-01-31
    /// Gera o arquivo SPED EFD ICMS/IPI para download.
    /// Prazo: mensal — até dia 15 do mês seguinte (PB/regra geral).
    /// </summary>
    [HttpGet("sped")]
    public async Task<IActionResult> GerarSped(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime dataInicio,
        [FromQuery] DateTime dataFim,
        CancellationToken cancellationToken)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });
        if (dataFim < dataInicio)
            return BadRequest(new { erro = "dataFim deve ser maior ou igual a dataInicio." });

        try
        {
            var conteudo = await _spedService.GerarSpedAsync(
                new SpedDto(empresaId, dataInicio, dataFim.AddDays(1).AddSeconds(-1)), cancellationToken);
            var bytes = System.Text.Encoding.UTF8.GetBytes(conteudo);
            var fileName = $"SPED_EFD_{dataInicio:yyyyMM}_{dataFim:yyyyMM}.txt";
            return File(bytes, "text/plain; charset=utf-8", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao gerar SPED.");
            return StatusCode(500, new { erro = "Erro interno ao gerar SPED." });
        }
    }
    /// <summary>
    /// POST /api/nfe/cancelar — Cancela uma NF-e autorizada (prazo 24 horas).
    /// </summary>
    [HttpPost("cancelar")]
    public async Task<IActionResult> Cancelar([FromBody] CancelarNFeDto dto, CancellationToken cancellationToken)
    {
        if (dto.EmpresaId == Guid.Empty || dto.NotaFiscalId == Guid.Empty)
            return BadRequest(new { erro = "EmpresaId e NotaFiscalId são obrigatórios." });
        if (string.IsNullOrWhiteSpace(dto.Justificativa) || dto.Justificativa.Trim().Length < 15)
            return BadRequest(new { erro = "Justificativa deve ter no mínimo 15 caracteres." });
        try
        {
            var resultado = await _cancelamentoService.CancelarAsync(dto, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao cancelar NF-e.");
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }

    /// <summary>
    /// POST /api/nfe/importar-xml-entrada — Importa XML de NF-e de entrada, criando produtos automaticamente.
    /// </summary>
    [HttpPost("importar-xml-entrada")]
    public async Task<IActionResult> ImportarXmlEntrada(
        [FromBody] ImportarXmlRequest req, CancellationToken cancellationToken)
    {
        if (req.EmpresaId == Guid.Empty)
            return BadRequest(new { erro = "EmpresaId é obrigatório." });
        if (string.IsNullOrWhiteSpace(req.XmlBase64))
            return BadRequest(new { erro = "XmlBase64 é obrigatório." });
        try
        {
            var resultado = await _entradaService.ImportarXmlEntradaAsync(
                req.EmpresaId, req.XmlBase64, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao importar XML entrada.");
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }

    /// <summary>
    /// GET /api/nfe/exportar-xml-lote?empresaId=&amp;dataInicio=&amp;dataFim=
    /// Exporta XMLs de NF-e autorizadas em um arquivo ZIP.
    /// </summary>
    [HttpGet("exportar-xml-lote")]
    public async Task<IActionResult> ExportarXmlLote(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime dataInicio,
        [FromQuery] DateTime dataFim,
        CancellationToken cancellationToken)
    {
        if (empresaId == Guid.Empty)
            return BadRequest(new { erro = "empresaId é obrigatório." });
        if (dataFim < dataInicio)
            return BadRequest(new { erro = "dataFim deve ser >= dataInicio." });

        try
        {
            // Busca empresa para obter CNPJ
            var empresa = await (
                from e in HttpContext.RequestServices
                    .GetRequiredService<Jubilados.Infrastructure.Data.JubiladosDbContext>().Empresas
                    .AsNoTracking()
                where e.Id == empresaId
                select new { e.CNPJ }
            ).FirstOrDefaultAsync(cancellationToken);
            if (empresa is null) return NotFound(new { erro = "Empresa não encontrada." });

            var db = HttpContext.RequestServices
                .GetRequiredService<Jubilados.Infrastructure.Data.JubiladosDbContext>();

            var notas = await db.NotasFiscais
                .AsNoTracking()
                .Where(n => n.EmpresaId == empresaId
                         && n.CStat == "100"
                         && n.EmitidaEm >= dataInicio.ToUniversalTime()
                         && n.EmitidaEm <= dataFim.ToUniversalTime()
                         && n.XmlEnvio != null)
                .OrderBy(n => n.EmitidaEm)
                .Select(n => new { n.ChaveAcesso, n.XmlEnvio })
                .ToListAsync(cancellationToken);

            if (!notas.Any())
                return NotFound(new { erro = "Nenhuma NF-e autorizada encontrada no período." });

            using var ms = new System.IO.MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                foreach (var nota in notas)
                {
                    var entry = zip.CreateEntry($"{nota.ChaveAcesso}-nfe.xml");
                    using var entryStream = entry.Open();
                    var bytes = System.Text.Encoding.UTF8.GetBytes(nota.XmlEnvio!);
                    await entryStream.WriteAsync(bytes, cancellationToken);
                }
            }
            ms.Seek(0, System.IO.SeekOrigin.Begin);
            var cnpj = new string(empresa.CNPJ.Where(char.IsDigit).ToArray());
            var fileName = $"NFe_{cnpj}_{dataInicio:yyyyMM}_{dataFim:yyyyMM}.zip";
            return File(ms.ToArray(), "application/zip", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao exportar XML lote.");
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }

}

// ── Request helpers ───────────────────────────────────────────────────────────

public record ConsultarNFeRequest(Guid EmpresaId, Guid NotaId = default, string? ChaveAcesso = null);
public record ImportarXmlRequest(Guid EmpresaId, string XmlBase64);
