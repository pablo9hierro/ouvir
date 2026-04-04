using Jubilados.Application.DTOs;

namespace Jubilados.Application.Interfaces;

public interface INFeService
{
    /// <summary>Emite uma NFe na SEFAZ (homologação ou produção).</summary>
    Task<NFeResultDto> EmitirNFeAsync(EmitirNFeDto dto, CancellationToken cancellationToken = default);

    /// <summary>Consulta situação de uma NFe pelo Id (banco local).</summary>
    Task<NFeDetalheDto?> ConsultarNotaAsync(Guid notaFiscalId, CancellationToken cancellationToken = default);

    /// <summary>Consulta situação de uma NFe diretamente na SEFAZ pelo chave de acesso.</summary>
    Task<ConsultarSefazResultDto> ConsultarSefazAsync(ConsultarSefazDto dto, CancellationToken cancellationToken = default);

    /// <summary>Consulta status do serviço SEFAZ.</summary>
    Task<StatusServicoResultDto> ConsultarStatusAsync(Guid empresaId, CancellationToken cancellationToken = default);

    /// <summary>Inutiliza faixa de numeração.</summary>
    Task<InutilizarResultDto> InutilizarAsync(InutilizarDto dto, CancellationToken cancellationToken = default);

    /// <summary>Busca empresa por CNPJ (14 dígitos limpos). Retorna Id + RazaoSocial ou null.</summary>
    Task<(Guid Id, string RazaoSocial)?> BuscarEmpresaPorCnpjAsync(string cnpj14Digitos, CancellationToken cancellationToken = default);

    /// <summary>Lista todas as notas (entrada + saída) de uma empresa.</summary>
    Task<IList<NuvemFiscalNotaDto>> ListarNotasPorEmpresaAsync(Guid empresaId, CancellationToken cancellationToken = default);
}

public interface InotaEntradaService
{
    /// <summary>Consulta NFes de entrada via NFeDistribuicaoDFe.</summary>
    Task<EntradaResultDto> ConsultarNotasEntradaAsync(ConsultarEntradaDto dto, CancellationToken cancellationToken = default);
}

public interface IManifestacaoService
{
    /// <summary>Manifesta o destinatário de uma NFe na SEFAZ.</summary>
    Task<ManifestacaoResultDto> ManifestarNFeAsync(ManifestarNFeDto dto, CancellationToken cancellationToken = default);
}
