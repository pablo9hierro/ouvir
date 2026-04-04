namespace Jubilados.Domain.Entities;

public class Produto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmpresaId { get; set; }

    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string NCM { get; set; } = string.Empty;    // Nomenclatura Comum do Mercosul
    public string CFOP { get; set; } = string.Empty;   // Código Fiscal de Operações
    public string CST { get; set; } = string.Empty;    // Código de Situação Tributária
    public string CSOSN { get; set; } = string.Empty;  // Simples Nacional
    public string CEST { get; set; } = string.Empty;   // Código Especificador Substituição Tributária
    public string Unidade { get; set; } = "UN";

    public decimal Preco { get; set; }
    public decimal AliquotaICMS { get; set; }
    public decimal AliquotaIPI { get; set; }
    public decimal AliquotaPIS { get; set; }
    public decimal AliquotaCOFINS { get; set; }

    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    // Relacionamentos
    public Empresa Empresa { get; set; } = null!;
    public ICollection<NotaItem> NotaItens { get; set; } = new List<NotaItem>();
}
