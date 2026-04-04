using Jubilados.Application.DTOs;
using Jubilados.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Jubilados.API.Controllers;

/// <summary>
/// Controller REST para operações de NFe.
/// Base: /api/nfe
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class NFeController : ControllerBase
{
    private readonly INFeService _nfeService;
    private readonly InotaEntradaService _entradaService;
    private readonly IManifestacaoService _manifestacaoService;
    private readonly ILogger<NFeController> _logger;

    public NFeController(
        INFeService nfeService,
        InotaEntradaService entradaService,
        IManifestacaoService manifestacaoService,
        ILogger<NFeController> logger)
    {
        _nfeService = nfeService;
        _entradaService = entradaService;
        _manifestacaoService = manifestacaoService;
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
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao manifestar NFe.");
            return StatusCode(500, new { erro = "Erro interno." });
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
}

// ── Request helpers ───────────────────────────────────────────────────────────

public record ConsultarNFeRequest(Guid EmpresaId, Guid NotaId = default, string? ChaveAcesso = null);
