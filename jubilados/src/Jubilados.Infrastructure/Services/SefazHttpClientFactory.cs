using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Jubilados.Infrastructure.Services;

/// <summary>
/// Cria HttpClient configurado para mTLS com os webservices SOAP da SEFAZ/SVRS.
/// Usa SocketsHttpHandler (em vez de HttpClientHandler) com AllowRenegotiation=true,
/// pois o endpoint de recepção de eventos da SVRS solicita o certificado do
/// cliente via renegociação TLS pós-handshake — algo que o OpenSSL (Linux)
/// recusa por padrão, gerando "Connection reset by peer".
/// </summary>
internal static class SefazHttpClientFactory
{
    public static HttpClient Criar(X509Certificate2 certificado, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection { certificado },
                EnabledSslProtocols = SslProtocols.Tls12,
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                AllowRenegotiation = true
            }
        };

        return new HttpClient(handler) { Timeout = timeout };
    }
}
