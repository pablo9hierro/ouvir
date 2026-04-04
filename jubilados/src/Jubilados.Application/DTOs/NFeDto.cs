namespace Jubilados.Application.DTOs;

// ── Entrada ──────────────────────────────────────────────────────────────────

public record EmitirNFeDto(
    Guid EmpresaId,
    Guid ClienteId,
    string NaturezaOperacao,
    string Serie,
    IList<ItemNFeDto> Itens,
    decimal ValorFrete = 0,
    decimal ValorSeguro = 0,
    decimal ValorDesconto = 0,
    decimal ValorOutros = 0,
    string? InformacaoComplementar = null
);

public record ItemNFeDto(
    Guid ProdutoId,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorDesconto = 0
);

public record ConsultarEntradaDto(
    Guid EmpresaId,
    long UltimoNSU = 0
);

public record ManifestarNFeDto(
    Guid NotaFiscalId,
    Guid EmpresaId,
    string TipoManifestacao,   // CienciaOperacao | ConfirmacaoOperacao | Desconhecimento | OperacaoNaoRealizada
    string? Justificativa = null
);

// ── Saída ────────────────────────────────────────────────────────────────────

public record NFeResultDto(
    bool Sucesso,
    string CStat,
    string XMotivo,
    Guid? NotaFiscalId = null,
    string? ChaveAcesso = null,
    string? Protocolo = null
);

public record NFeDetalheDto(
    Guid Id,
    string ChaveAcesso,
    int Numero,
    string Serie,
    string Status,
    decimal ValorTotal,
    DateTime EmitidaEm,
    string? Protocolo,
    string? CStat,
    string? XMotivo,
    bool Manifestada
);

public record EntradaResultDto(
    bool Sucesso,
    int TotalNotas,
    IList<Guid> NotasImportadas
);

public record ManifestacaoResultDto(
    bool Sucesso,
    string CStat,
    string XMotivo
);

public record ConsultarSefazDto(
    Guid EmpresaId,
    string ChaveAcesso
);

public record ConsultarSefazResultDto(
    bool Sucesso,
    string CStat,
    string XMotivo,
    string? Protocolo = null,
    string? DhRecbto = null
);

public record StatusServicoResultDto(
    bool Sucesso,
    string CStat,
    string XMotivo,
    string? DhRecbto = null,
    string? TMed = null
);

public record InutilizarDto(
    Guid EmpresaId,
    string Serie,
    int NumeroInicial,
    int NumeroFinal,
    string Justificativa
);

public record InutilizarResultDto(
    bool Sucesso,
    string CStat,
    string XMotivo,
    string? Protocolo = null,
    string? DhRecbto = null
);

public record NuvemFiscalItemDto(
    Guid Id,
    string Tipo,            // "Saída" ou "Entrada"
    string ChaveAcesso,
    int Numero,
    string Serie,
    string NaturezaOperacao,
    string Status,
    decimal ValorTotal,
    DateTime EmitidaEm,
    string? Protocolo,
    string? CStat,
    bool Manifestada
);

public record NuvemFiscalResultDto(
    bool Sucesso,
    string CNPJ,
    string RazaoSocial,
    int Total,
    IList<NuvemFiscalItemDto> Notas
);

/// <summary>Projeção interna usada pelo serviço para montar NuvemFiscalItemDto.</summary>
public record NuvemFiscalNotaDto(
    Guid Id,
    string TipoOperacao,
    string ChaveAcesso,
    int Numero,
    string Serie,
    string NaturezaOperacao,
    string Status,
    decimal ValorTotal,
    DateTime EmitidaEm,
    string? Protocolo,
    string? CStat,
    bool Manifestada
);
