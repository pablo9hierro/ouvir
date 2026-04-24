using Jubilados.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Jubilados.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly JubiladosDbContext _db;
    // Mesmo segredo usado em Program.cs para validar — lê JWT_SECRET ou usa fallback dev
    private static readonly string JwtSecret =
        Environment.GetEnvironmentVariable("JWT_SECRET")
        ?? "jubilados-dev-secret-jwt-key-32bytes!!";

    public AuthController(JubiladosDbContext db) => _db = db;

    // ── Login local ───────────────────────────────────────────────────────────
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Senha))
            return BadRequest(new { error = "E-mail e senha são obrigatórios." });

        var user = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == req.Email.ToLower().Trim());
        if (user is null || !VerificarSenha(req.Senha, user.SenhaHash))
            return Unauthorized(new { error = "Credenciais inválidas." });

        var token = GerarJwt(user.Id, user.Email);
        return Ok(new { token });
    }

    // ── Cadastro local ────────────────────────────────────────────────────────
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Senha))
            return BadRequest(new { error = "E-mail e senha são obrigatórios." });

        if (req.Senha.Length < 6)
            return BadRequest(new { error = "A senha deve ter ao menos 6 caracteres." });

        var emailNorm = req.Email.ToLower().Trim();
        if (await _db.Usuarios.AnyAsync(u => u.Email == emailNorm))
            return Conflict(new { error = "E-mail já cadastrado. Faça login." });

        var user = new Domain.Entities.Usuario
        {
            Id        = Guid.NewGuid(),
            Email     = emailNorm,
            SenhaHash = HashSenha(req.Senha),
            CriadoEm  = DateTime.UtcNow
        };
        _db.Usuarios.Add(user);

        // Cria vínculo de usuario_empresa sem empresa ainda (vai para onboarding)
        _db.UsuarioEmpresas.Add(new Domain.Entities.UsuarioEmpresa
        {
            SupabaseUserId      = user.Id,
            OnboardingConcluido = false,
            CriadoEm            = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        var token = GerarJwt(user.Id, user.Email);
        return Ok(new { token });
    }

    // ── Recuperar senha ───────────────────────────────────────────────────────
    [HttpPost("recuperar-senha")]
    [AllowAnonymous]
    public async Task<IActionResult> RecuperarSenha([FromBody] RecuperarRequest req, CancellationToken ct)
    {
        const string okMsg = "Se o e-mail estiver cadastrado, voce recebera um link em breve.";
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { error = "E-mail obrigatorio." });

        var email = req.Email.ToLower().Trim();
        var user  = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return Ok(new { message = okMsg }); // evita enumeracao de e-mails

        // Token URL-safe de 32 bytes = 43 chars base64
        var tokenBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');

        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO password_reset_tokens (id, usuario_id, token, expires_at) VALUES (gen_random_uuid(), {0}, {1}, {2})",
            user.Id, token, DateTime.UtcNow.AddHours(1));

        var baseUrl  = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? $"{Request.Scheme}://{Request.Host}";
        var resetUrl = $"{baseUrl}/resetar-senha.html?token={Uri.EscapeDataString(token)}";

        _ = EnviarEmailAsync(email, "Redefinicao de senha - Jubilados NF-e",
            $"Voce solicitou a redefinicao da sua senha.\n\n"
          + $"Clique no link abaixo para criar uma nova senha:\n{resetUrl}\n\n"
          + "Este link expira em 1 hora. Ignore este e-mail se nao foi voce quem solicitou.");

        return Ok(new { message = okMsg });
    }

    // ── Resetar senha via token (link do e-mail) ──────────────────────────────
    [HttpPost("resetar-senha")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetarSenha([FromBody] ResetarSenhaRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.NovaSenha))
            return BadRequest(new { error = "Token e nova senha sao obrigatorios." });

        if (req.NovaSenha.Length < 6)
            return BadRequest(new { error = "A senha deve ter ao menos 6 caracteres." });

        var conn = _db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            Guid usuarioId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT usuario_id FROM password_reset_tokens
                    WHERE token = @t AND expires_at > NOW() AND used_at IS NULL LIMIT 1";
                var p = cmd.CreateParameter(); p.ParameterName = "@t"; p.Value = req.Token;
                cmd.Parameters.Add(p);
                var result = await cmd.ExecuteScalarAsync(ct);
                if (result is null or DBNull)
                    return BadRequest(new { error = "Link invalido ou expirado. Solicite um novo." });
                usuarioId = (Guid)result;
            }

            var newHash = HashSenha(req.NovaSenha);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE usuarios SET senha_hash = @h WHERE id = @id";
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@h";  p1.Value = newHash;    cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@id"; p2.Value = usuarioId;  cmd.Parameters.Add(p2);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE password_reset_tokens SET used_at = NOW() WHERE token = @t";
                var p = cmd.CreateParameter(); p.ParameterName = "@t"; p.Value = req.Token;
                cmd.Parameters.Add(p);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            return Ok(new { message = "Senha redefinida com sucesso! Faca login com a nova senha." });
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    // ── Alterar senha (usuario logado) ────────────────────────────────────────
    [HttpPost("alterar-senha")]
    [Authorize]
    public async Task<IActionResult> AlterarSenha([FromBody] AlterarSenhaRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SenhaAtual) || string.IsNullOrWhiteSpace(req.NovaSenha))
            return BadRequest(new { error = "Senha atual e nova senha sao obrigatorias." });

        if (req.NovaSenha.Length < 6)
            return BadRequest(new { error = "A nova senha deve ter ao menos 6 caracteres." });

        var userId = ObterSupabaseUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var user = await _db.Usuarios.FindAsync(new object[] { userId }, ct);
        if (user is null) return NotFound();

        if (!VerificarSenha(req.SenhaAtual, user.SenhaHash))
            return BadRequest(new { error = "Senha atual incorreta." });

        user.SenhaHash = HashSenha(req.NovaSenha);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Senha alterada com sucesso!" });
    }

    // ── Perfil (protegido) ────────────────────────────────────────────────────
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = ObterSupabaseUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var vínculo = await _db.UsuarioEmpresas
            .Include(u => u.Empresa)
            .FirstOrDefaultAsync(u => u.SupabaseUserId == userId);

        if (vínculo is null)
        {
            vínculo = new Domain.Entities.UsuarioEmpresa
            {
                SupabaseUserId      = userId,
                OnboardingConcluido = false
            };
            _db.UsuarioEmpresas.Add(vínculo);
            await _db.SaveChangesAsync();
        }

        return Ok(new
        {
            supabaseUserId      = vínculo.SupabaseUserId,
            empresaId           = vínculo.EmpresaId,
            onboardingConcluido = vínculo.OnboardingConcluido,
            empresa             = vínculo.Empresa is null ? null : new
            {
                id               = vínculo.Empresa.Id,
                cnpj             = vínculo.Empresa.CNPJ,
                razaoSocial      = vínculo.Empresa.RazaoSocial,
                nomeFantasia     = vínculo.Empresa.NomeFantasia,
                uf               = vínculo.Empresa.UF,
                crt              = vínculo.Empresa.CRT,
                ramo             = vínculo.Empresa.Ramo,
                regimeTributario = vínculo.Empresa.RegimeTributario,
                emiteNfse        = vínculo.Empresa.EmiteNfse,
                certificadoValidade = vínculo.Empresa.CertificadoValidade
            }
        });
    }

    [HttpPut("onboarding-concluido")]
    [Authorize]
    public async Task<IActionResult> MarcarOnboardingConcluido()
    {
        var userId = ObterSupabaseUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var vínculo = await _db.UsuarioEmpresas
            .FirstOrDefaultAsync(u => u.SupabaseUserId == userId);

        if (vínculo is null) return NotFound("Vínculo de usuário não encontrado.");
        if (vínculo.EmpresaId is null) return BadRequest("Associe uma empresa antes de concluir o onboarding.");

        vínculo.OnboardingConcluido = true;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("associar-empresa")]
    [Authorize]
    public async Task<IActionResult> AssociarEmpresa([FromBody] AssociarEmpresaRequest req)
    {
        var userId = ObterSupabaseUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.CNPJ == req.CNPJ);
        if (empresa is null) return NotFound("Empresa não encontrada com esse CNPJ.");

        var vínculo = await _db.UsuarioEmpresas
            .FirstOrDefaultAsync(u => u.SupabaseUserId == userId);

        if (vínculo is null)
        {
            vínculo = new Domain.Entities.UsuarioEmpresa { SupabaseUserId = userId };
            _db.UsuarioEmpresas.Add(vínculo);
        }

        vínculo.EmpresaId = empresa.Id;
        await _db.SaveChangesAsync();
        return Ok(new { empresaId = empresa.Id });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Guid ObterSupabaseUserId()
    {
        var sub = User.FindFirstValue("sub")
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private static string GerarJwt(Guid userId, string email)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: new[]
            {
                new Claim("sub",   userId.ToString()),
                new Claim("email", email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            },
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashSenha(string senha)
    {
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            senha, salt, 100_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + "." + Convert.ToBase64String(hash);
    }

    private static bool VerificarSenha(string senha, string senhaHash)
    {
        var parts = senhaHash.Split('.');
        if (parts.Length != 2) return false;
        var salt     = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual   = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            senha, salt, 100_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static async Task EnviarEmailAsync(string para, string assunto, string corpo)
    {
        var host = Environment.GetEnvironmentVariable("SMTP_HOST");
        if (string.IsNullOrEmpty(host))
        {
            Console.WriteLine($"[EMAIL-SIMULADO] Para: {para}\nAssunto: {assunto}\n{corpo}\n");
            return;
        }
        try
        {
            var port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 587;
            var user = Environment.GetEnvironmentVariable("SMTP_USER") ?? "";
            var pass = Environment.GetEnvironmentVariable("SMTP_PASS") ?? "";
            var from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? user;
            using var client = new System.Net.Mail.SmtpClient(host, port)
            {
                Credentials = new System.Net.NetworkCredential(user, pass),
                EnableSsl   = true
            };
            var msg = new System.Net.Mail.MailMessage(from, para, assunto, corpo);
            await client.SendMailAsync(msg);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SMTP-ERROR] {ex.Message}");
        }
    }

    public record LoginRequest(string Email, string Senha);
    public record RegisterRequest(string Email, string Senha);
    public record RecuperarRequest(string Email);
    public record ResetarSenhaRequest(string Token, string NovaSenha);
    public record AlterarSenhaRequest(string SenhaAtual, string NovaSenha);
    public record AssociarEmpresaRequest(string CNPJ);
}
