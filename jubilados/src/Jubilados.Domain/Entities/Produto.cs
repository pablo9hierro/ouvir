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
    // Nullable: produto de empresa fora do Simples Nacional (CRT != 1) usa CST,
    // nunca CSOSN -- string nao-anulavel fazia o [ApiController] do
    // ProdutoController rejeitar qualquer PUT com csosn=null como "CSOSN field
    // is required", mesmo pra empresas que corretamente nao preenchem CSOSN.
    // Coluna Postgres ja aceita NULL (ProdutoConfiguration nunca marcou
    // IsRequired pra CSOSN); so a entidade C# estava incorreta.
    public string? CSOSN { get; set; }
    public string CEST { get; set; } = string.Empty;   // Código Especificador Substituição Tributária
    public string Unidade { get; set; } = "UN";
    /// <summary>Origem da mercadoria (grupo ICMS): 0=Nacional, 1-8=conforme tabela oficial (importado etc).</summary>
    public string Origem { get; set; } = "0";

    public decimal Preco { get; set; }
    public decimal AliquotaICMS { get; set; }
    public decimal AliquotaIPI { get; set; }
    public decimal AliquotaPIS { get; set; }
    public decimal AliquotaCOFINS { get; set; }

    // Reforma Tributaria (IBS/CBS)
    public string? CstIbsCbs { get; set; }
    public string? CClassTrib { get; set; }
    public decimal? ReducaoIbs { get; set; }
    public decimal? ReducaoCbs { get; set; }
    public string? TipoAliquotaIbsCbs { get; set; }

    public string? EAN { get; set; }
    /// <summary>Quantidade em estoque atual — usado no Bloco H do SPED EFD.</summary>
    public decimal QuantidadeEstoque { get; set; } = 0;

    // ── Campos de cadastro complementar (preenchidos manualmente pelo lojista) ──
    /// <summary>Código interno / SKU próprio da loja.</summary>
    public string? CodigoInterno { get; set; }
    /// <summary>Categoria do produto (ex: Bebidas, Alimentos).</summary>
    public string? Categoria { get; set; }
    /// <summary>Organização/setor onde o produto está alocado.</summary>
    public string? Organizacao { get; set; }
    /// <summary>Padronização de embalagem/apresentação (ex: Unitário, Caixa).</summary>
    public string? Padronizacao { get; set; }

    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    // Relacionamentos
    public Empresa? Empresa { get; set; }
    public ICollection<NotaItem> NotaItens { get; set; } = new List<NotaItem>();
}
