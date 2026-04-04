using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Jubilados.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jubilados.API.Controllers;

[ApiController]
[Route("api/diagnostic")]
public class DiagnosticController : ControllerBase
{
    private readonly JubiladosDbContext _db;
    private readonly ILogger<DiagnosticController> _log;

    public DiagnosticController(JubiladosDbContext db, ILogger<DiagnosticController> log)
    {
        _db = db;
        _log = log;
    }

    // GET /api/diagnostic/sefaz?empresaId=...
    [HttpGet("sefaz")]
    public async Task<IActionResult> TestSefaz([FromQuery] Guid empresaId, CancellationToken ct)
    {
        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct);
        if (empresa is null) return NotFound(new { erro = "Empresa não encontrada." });
        if (string.IsNullOrEmpty(empresa.CertificadoBase64))
            return BadRequest(new { erro = "Empresa sem certificado." });

        X509Certificate2? cert = null;
        try
        {
            var bytes = Convert.FromBase64String(empresa.CertificadoBase64);
            cert = new X509Certificate2(bytes, empresa.CertificadoSenha,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex)
        {
            return BadRequest(new { erro = $"Certificado inválido: {ex.Message}" });
        }

        var statusSoap = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap12:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                 xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
                 xmlns:soap12=""http://www.w3.org/2003/05/soap-envelope"">
  <soap12:Body>
    <nfeStatusServicoNF xmlns=""http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4"">
      <nfeDadosMsg>
        <consStatServ versao=""4.00"" xmlns=""http://www.portalfiscal.inf.br/nfe"">
          <tpAmb>2</tpAmb><cUF>25</cUF><xServ>STATUS</xServ>
        </consStatServ>
      </nfeDadosMsg>
    </nfeStatusServicoNF>
  </soap12:Body>
</soap12:Envelope>";

        var candidates = new[]
        {
            ("SVRS",       "https://hom.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx"),
            ("SVRS-ALT",   "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx"),
            ("SVAN-F01",   "https://hom.nfe.fazenda.gov.br/NfeStatusServico/NFeStatusServico4.asmx"),
            ("SVAN-F02",   "https://hom.nfe.fazenda.gov.br/NFeStatusServico/NFeStatusServico4.asmx"),
            ("SVAN-F03",   "https://hom.nfe.fazenda.gov.br/NfeStatusServico/NfeStatusServico4.asmx"),
            ("SVAN-F04",   "https://hom.nfe.fazenda.gov.br/NfeStatusServico4/NFeStatusServico4.asmx"),
            ("SP-homolg",  "https://homologacao.nfe.fazenda.sp.gov.br/ws/NfeStatusServico4.asmx"),
            ("SP-homolg2", "https://homologacao.nfe.fazenda.sp.gov.br/ws/nfestatusservico4.asmx"),
        };

        var results = new List<object>();

        foreach (var (name, url) in candidates)
        {
            try
            {
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                if (cert is not null) handler.ClientCertificates.Add(cert);

                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                var content = new StringContent(statusSoap, Encoding.UTF8, "application/soap+xml");
                content.Headers.ContentType!.Parameters.Add(
                    new NameValueHeaderValue("action",
                        "\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4/nfeStatusServicoNF\""));

                var resp = await http.PostAsync(url, content, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                var snippet = body.Length > 300 ? body[..300] : body;

                results.Add(new { name, url, status = (int)resp.StatusCode, snippet });
                _log.LogInformation("[DIAG] {Name} → HTTP {Code}: {Snippet}", name, (int)resp.StatusCode, snippet);
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException?.Message ?? ex.Message;
                results.Add(new { name, url, status = 0, snippet = msg });
                _log.LogWarning("[DIAG] {Name} → ERRO: {Msg}", name, msg);
            }
        }

        return Ok(new { cert_cn = cert?.Subject, results });
    }
}
