using Jubilados.Application.DTOs;
using Jubilados.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Jubilados.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class NfseController : ControllerBase
{
    private readonly INfseService _nfseService;
    private readonly ILogger<NfseController> _logger;

    public NfseController(INfseService nfseService, ILogger<NfseController> logger)
    {
        _nfseService = nfseService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/nfse/emitir — Emite uma NFS-e via webservice municipal ABRASF 2.04.
    /// </summary>
    [HttpPost("emitir")]
    [ProducesResponseType(typeof(NfseResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Emitir([FromBody] EmitirNfseDto dto, CancellationToken cancellationToken)
    {
        if (dto.EmpresaId == Guid.Empty)
            return BadRequest(new { erro = "EmpresaId é obrigatório." });
        if (dto.ValorServico <= 0)
            return BadRequest(new { erro = "ValorServico deve ser maior que zero." });
        if (string.IsNullOrWhiteSpace(dto.NomeServico))
            return BadRequest(new { erro = "NomeServico é obrigatório." });

        try
        {
            var resultado = await _nfseService.EmitirNfseAsync(dto, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Erro ao emitir NFS-e.");
            return StatusCode(500, new { erro = "Erro interno." });
        }
    }
}
