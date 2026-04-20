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

    // NFC-e: Código de Segurança do Contribuinte (CSC)
    public string? NfceCscId { get; set; }
    public string? NfceCscToken { get; set; }

    // ── Dados Fiscais (onboarding) ─────────────────────────────────────────
    /// <summary>material_construcao | ferragens | vestuario | deposito_bebidas | conveniencia_mercadinho | farmacia | petshop | distribuidora | restaurante | outros</summary>
    public string? Ramo { get; set; }
    /// <summary>mei | simples_nacional | lucro_presumido | lucro_real</summary>
    public string? RegimeTributario { get; set; }
    /// <summary>CRT: 1=Simples Nacional/MEI, 2=Lucro Presumido, 3=Lucro Real</summary>
    public int CRT { get; set; } = 1;
    public string? CNAE { get; set; }
    public string? InscricaoMunicipal { get; set; }
    public bool EmiteNfse { get; set; } = false;
    public bool InscritoSuframa { get; set; } = false;

    // Tributação padrão para NF-e
    /// <summary>CSOSN padrão para Simples Nacional: 102|103|300|400|500</summary>
    public string? CsosnPadrao { get; set; } = "102";
    /// <summary>CST ICMS padrão para Lucro Presumido/Real: 00|20|40|60</summary>
    public string? CstIcmsPadrao { get; set; }
    /// <summary>CST PIS padrão: 01|02|07|49|50|99</summary>
    public string? CstPisPadrao { get; set; }
    public string? CstCofinsPadrao { get; set; }
    public decimal AliquotaPis { get; set; } = 0.65m;
    public decimal AliquotaCofins { get; set; } = 3.00m;
    public decimal AliquotaIss { get; set; } = 5.00m;

    // ICMS-ST
    public bool OperaComoSubstitutoTributario { get; set; } = false;
    public decimal? MvaPadrao { get; set; }
    public bool PossuiStBebidas { get; set; } = false;

    // Contador (Lucro Presumido / Real)
    public string? ContadorNome { get; set; }
    public string? ContadorCrc { get; set; }
    public string? ContadorEmail { get; set; }

    // Faixa do Simples (Anexo I/II/III/IV/V)
    public string? FaixaSimples { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    // Relacionamentos
    public ICollection<Produto> Produtos { get; set; } = new List<Produto>();
    public ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
    public ICollection<NotaFiscal> NotasFiscais { get; set; } = new List<NotaFiscal>();
}
