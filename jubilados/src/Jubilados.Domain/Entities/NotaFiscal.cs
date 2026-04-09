using Jubilados.Domain.Enums;

namespace Jubilados.Domain.Entities;

public class NotaFiscal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmpresaId { get; set; }
    public Guid? ClienteId { get; set; }

    public int Numero { get; set; }
    public string Serie { get; set; } = "1";
    public string ChaveAcesso { get; set; } = string.Empty;
    public StatusNota Status { get; set; } = StatusNota.Rascunho;

    // Tipo: 0=Entrada, 1=Saída
    public string TipoOperacao { get; set; } = "1";

    // Natureza da operação
    public string NaturezaOperacao { get; set; } = "Venda de Produto";

    // Valores totais
    public decimal ValorProdutos { get; set; }
    public decimal ValorDesconto { get; set; }
    public decimal ValorFrete { get; set; }
    public decimal ValorSeguro { get; set; }
    public decimal ValorOutros { get; set; }
    public decimal ValorICMS { get; set; }
    public decimal ValorIPI { get; set; }
    public decimal ValorPIS { get; set; }
    public decimal ValorCOFINS { get; set; }
    public decimal ValorTotal { get; set; }

    // XML e protocolo
    public string? XmlEnvio { get; set; }
    public string? XmlRetorno { get; set; }
    public string? Protocolo { get; set; }
    public string? CStat { get; set; }
    public string? XMotivo { get; set; }

    // NFe entrada (distribuição)
    public long NSU { get; set; }
    public bool Manifestada { get; set; } = false;
    public TipoManifestacao? TipoManifestacao { get; set; }

    public DateTime EmitidaEm { get; set; } = DateTime.UtcNow;
    public DateTime? AutorizadaEm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    // Relacionamentos
    public Empresa Empresa { get; set; } = null!;
    public Cliente Cliente { get; set; } = null!;
    public ICollection<NotaItem> Itens { get; set; } = new List<NotaItem>();
}
