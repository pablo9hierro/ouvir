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

    /// <summary>Envia Carta de Correção Eletrônica (CCe, tpEvento=110110) para uma NF-e.</summary>
    Task<CceResultDto> EnviarCceAsync(CceDto dto, CancellationToken cancellationToken = default);

    /// <summary>Retorna o XML da NF-e armazenado no banco para download.</summary>
    Task<(string? Xml, string? ChaveAcesso)?> ObterXmlAsync(Guid notaFiscalId, CancellationToken cancellationToken = default);

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

public interface IDanfeService
{
    /// <summary>Gera o PDF DANFE de uma NF-e a partir do ID.</summary>
    Task<byte[]> GerarDanfeAsync(Guid notaFiscalId, CancellationToken cancellationToken = default);
}

public interface ISpedService
{
    /// <summary>Gera o arquivo SPED EFD ICMS/IPI em texto para download.</summary>
    Task<string> GerarSpedAsync(SpedDto dto, CancellationToken cancellationToken = default);
}
