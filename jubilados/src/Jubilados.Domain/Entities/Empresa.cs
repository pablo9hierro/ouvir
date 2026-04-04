namespace Jubilados.Domain.Entities;

public class Empresa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CNPJ { get; set; } = string.Empty;
    public string RazaoSocial { get; set; } = string.Empty;
    public string NomeFantasia { get; set; } = string.Empty;
    public string InscricaoEstadual { get; set; } = string.Empty;
    public string Logradouro { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Complemento { get; set; } = string.Empty;
    public string Bairro { get; set; } = string.Empty;
    public string Municipio { get; set; } = string.Empty;
    public string UF { get; set; } = string.Empty;
    public string CEP { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Certificado digital A1 armazenado como base64
    public string? CertificadoBase64 { get; set; }
    public string? CertificadoSenha { get; set; }
    public DateTime? CertificadoValidade { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    // Relacionamentos
    public ICollection<Produto> Produtos { get; set; } = new List<Produto>();
    public ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
    public ICollection<NotaFiscal> NotasFiscais { get; set; } = new List<NotaFiscal>();
}
