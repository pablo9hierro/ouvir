namespace Jubilados.Domain.Entities;

public class NotaItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotaFiscalId { get; set; }
    public Guid ProdutoId { get; set; }

    public int NumeroItem { get; set; }
    public decimal Quantidade { get; set; }
    public string Unidade { get; set; } = "UN";
    public decimal ValorUnitario { get; set; }
    public decimal ValorDesconto { get; set; }
    public decimal ValorTotal { get; set; }

    // Tributos calculados por item
    public decimal BaseICMS { get; set; }
    public decimal AliquotaICMS { get; set; }
    public decimal ValorICMS { get; set; }
    public decimal AliquotaIPI { get; set; }
    public decimal ValorIPI { get; set; }
    public decimal AliquotaPIS { get; set; }
    public decimal ValorPIS { get; set; }
    public decimal AliquotaCOFINS { get; set; }
    public decimal ValorCOFINS { get; set; }

    // Relacionamentos
    public NotaFiscal NotaFiscal { get; set; } = null!;
    public Produto Produto { get; set; } = null!;
}
