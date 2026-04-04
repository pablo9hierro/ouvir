using Jubilados.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Jubilados.Application.Services;

/// <summary>
/// Serviço de carregamento de certificado digital A1 (.pfx) armazenado em base64 no banco.
/// Suporta multi-tenant: cada empresa possui seu próprio certificado.
/// </summary>
public class CertificadoService : ICertificadoService
{
    private readonly ILogger<CertificadoService> _logger;

    public CertificadoService(ILogger<CertificadoService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public X509Certificate2 CarregarCertificado(string base64, string senha)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new ArgumentException("Base64 do certificado não pode ser vazio.", nameof(base64));

        if (string.IsNullOrWhiteSpace(senha))
            throw new ArgumentException("Senha do certificado não pode ser vazia.", nameof(senha));

        try
        {
            var bytes = Convert.FromBase64String(base64);

            // Windows SChannel não suporta EphemeralKeySet para mTLS; usa UserKeySet no lugar.
            // Linux/macOS (Docker) usa EphemeralKeySet que é mais seguro (sem persistência em disco).
            var flags = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet
                : X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;

            var certificado = new X509Certificate2(bytes, senha, flags);

            if (!CertificadoValido(certificado))
            {
                _logger.LogWarning("Certificado carregado mas está vencido. Validade: {Validade}", certificado.NotAfter);
                throw new InvalidOperationException($"Certificado digital está vencido. Validade: {certificado.NotAfter:dd/MM/yyyy}");
            }

            _logger.LogInformation("Certificado carregado com sucesso. CN={CN} | Validade={Validade}",
                certificado.GetNameInfo(X509NameType.SimpleName, false),
                certificado.NotAfter.ToString("dd/MM/yyyy"));

            return certificado;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Base64 do certificado é inválido.");
            throw new ArgumentException("O base64 fornecido não é um certificado válido.", nameof(base64), ex);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogError(ex, "Erro criptográfico ao carregar certificado. Senha incorreta ou arquivo corrompido.");
            throw new InvalidOperationException("Não foi possível carregar o certificado. Verifique a senha e o arquivo .pfx.", ex);
        }
    }

    /// <inheritdoc />
    public bool CertificadoValido(X509Certificate2 certificado)
    {
        var agora = DateTime.Now;
        return agora >= certificado.NotBefore && agora <= certificado.NotAfter;
    }

    /// <inheritdoc />
    public DateTime ObterValidade(string base64, string senha)
    {
        var cert = CarregarCertificado(base64, senha);
        return cert.NotAfter;
    }
}
