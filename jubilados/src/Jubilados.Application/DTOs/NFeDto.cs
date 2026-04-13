namespace Jubilados.Application.DTOs;

// ── Entrada ──────────────────────────────────────────────────────────────────

/// <summary>
/// DTO para emissão de NF-e.
/// Para NF-e de SAÍDA (venda ao consumidor): ClienteId pode ser null.
///   - Nesse caso, informe DestinatarioNome (opcional) e DestinatarioCpfCnpj (opcional).
///   - Sem identificação: emitido como "CONSUMIDOR NAO IDENTIFICADO".
/// Para NF-e de ENTRADA: ClienteId é obrigatório (identifica o remetente).
/// </summary>
public record EmitirNFeDto(
    Guid EmpresaId,
    Guid? ClienteId,            // null = consumidor não identificado (NF-e saída)
    string NaturezaOperacao,
    string Serie,
    IList<ItemNFeDto> Itens,
    decimal ValorFrete = 0,
    decimal ValorSeguro = 0,
    decimal ValorDesconto = 0,
    decimal ValorOutros = 0,
    string? InformacaoComplementar = null,
    // Destinatário avulso (quando ClienteId é null)
    string? DestinatarioCpfCnpj = null,
    string? DestinatarioNome = null
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
    string? Protocolo = null,
    bool Contingencia = false
);

public record EmitirNFCeDto(
    Guid EmpresaId,
    IList<ItemNFeDto> Itens,
    string Serie = "1",
    string? CpfConsumidor = null,   // CPF do consumidor identificado (opcional)
    decimal ValorFrete = 0,
    decimal ValorDesconto = 0,
    string FormaPagamento = "01",   // 01=Dinheiro 03=Cartao Credito 04=Cartao Debito 05=Credito Loja 10=Vale Alimentacao 99=Outros
    string? InformacaoComplementar = null
);

public record NfceResultDto(
    bool Sucesso,
    string CStat,
    string XMotivo,
    Guid? NotaFiscalId = null,
    string? ChaveAcesso = null,
    string? Protocolo = null,
    string? QrCodeUrl = null
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

// ── CCe (Carta de Correção Eletrônica) ────────────────────────────────────────

/// <summary>DTO para envio de Carta de Correção Eletrônica (tpEvento=110110).</summary>
public record CceDto(
    Guid EmpresaId,
    Guid NotaFiscalId,
    string CorrecaoTexto  // min 15, max 1000 chars
);

public record CceResultDto(
    bool Sucesso,
    string CStat,
    string XMotivo,
    string? Protocolo = null
);

// ── SPED ──────────────────────────────────────────────────────────────────────

public record SpedDto(
    Guid EmpresaId,
    DateTime DataInicio,
    DateTime DataFim
);
