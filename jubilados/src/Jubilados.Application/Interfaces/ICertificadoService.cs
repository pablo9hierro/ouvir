using System.Security.Cryptography.X509Certificates;

namespace Jubilados.Application.Interfaces;

public interface ICertificadoService
{
    /// <summary>
    /// Carrega certificado X.509 a partir de base64 + senha (multi-tenant).
    /// </summary>
    X509Certificate2 CarregarCertificado(string base64, string senha);

    /// <summary>
    /// Valida se o certificado está dentro da validade.
    /// </summary>
    bool CertificadoValido(X509Certificate2 certificado);

    /// <summary>
    /// Retorna a data de expiração do certificado.
    /// </summary>
    DateTime ObterValidade(string base64, string senha);
}
