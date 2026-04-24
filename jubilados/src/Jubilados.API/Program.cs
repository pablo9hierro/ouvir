using System.Globalization;
using System.Text;
using Jubilados.Application.Configuration;
using Jubilados.Application.Interfaces;
using Jubilados.Application.Services;
using Jubilados.Infrastructure.Data;
using Jubilados.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// Garante que todos os formatos numéricos usem ponto (.) como separador decimal
// independente da cultura do SO (pt-BR usaria vírgula e quebraria o XML da SEFAZ)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// ── Configuração ──────────────────────────────────────────────────────────────
builder.Services.Configure<NFeOptions>(builder.Configuration.GetSection(NFeOptions.Section));

// ── Banco de Dados ───────────────────────────────────────────────────────────
// Suporte a DATABASE_URL (Railway native PostgreSQL) OU appsettings (Supabase)
string connectionString;
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':');
    connectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={Uri.UnescapeDataString(userInfo[1])};SSL Mode=Require;Trust Server Certificate=true";
    Console.WriteLine($"[STARTUP] Usando DATABASE_URL Railway → Host={uri.Host}:{uri.Port}");
}
else
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' não configurada.");
    var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
    if (!string.IsNullOrEmpty(dbPassword))
        connectionString = connectionString.Replace("${DB_PASSWORD}", dbPassword);
    var maskedConn = System.Text.RegularExpressions.Regex.Replace(
        connectionString, @"Password=[^;]+", "Password=***");
    Console.WriteLine($"[STARTUP] Usando Supabase: {maskedConn}");
}

builder.Services.AddDbContext<JubiladosDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.CommandTimeout(60);
        npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(3), null);
    }));

// ── Serviços da Aplicação (DI) ────────────────────────────────────────────────
builder.Services.AddScoped<ICertificadoService, CertificadoService>();
builder.Services.AddScoped<INFeService, NFeService>();
builder.Services.AddScoped<InotaEntradaService, NotaEntradaService>();
builder.Services.AddScoped<IManifestacaoService, ManifestacaoService>();
builder.Services.AddScoped<IDanfeService, DanfeService>();
builder.Services.AddScoped<ISpedService, SpedService>();
builder.Services.AddScoped<ICancelamentoService, CancelamentoService>();
builder.Services.AddScoped<INfseService, NfseService>();

// ── Controllers + Validação ───────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        opts.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Jubilados — SaaS NFe API",
        Version = "v1",
        Description = "API para emissão de Nota Fiscal Eletrônica (NFe) multi-tenant.",
        Contact = new OpenApiContact { Name = "Jubilados SaaS" }
    });
});

// ── CORS (ajuste origens em produção) ────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// ── Health Check ──────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "supabase-postgres");

// ── JWT Bearer Authentication (auth local) ────────────────────────────────────
// JWT_SECRET é o segredo usado tanto para assinar (AuthController) quanto para validar.
// Fallback = DevSecret hardcoded (igual ao AuthController) — funciona sem configuração extra.
const string devFallbackSecret = "jubilados-dev-secret-jwt-key-32bytes!!";
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? devFallbackSecret;
Console.WriteLine($"[STARTUP] JWT: {(jwtSecret == devFallbackSecret ? "usando DevSecret (fallback)" : "usando JWT_SECRET env var")}");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer           = false,
            ValidateAudience         = false,
            ClockSkew                = TimeSpan.Zero
        };
    });

// ── Build App ─────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Schema + Seed automático (sempre, para Railway e Supabase) ───────────────
using (var startupScope = app.Services.CreateScope())
{
    var db = startupScope.ServiceProvider.GetRequiredService<JubiladosDbContext>();
    try
    {
        // EnsureCreated cria as tabelas se não existirem (Railway Postgres vazio)
        var criado = await db.Database.EnsureCreatedAsync();
        Console.WriteLine(criado ? "[STARTUP] Tabelas criadas." : "[STARTUP] Tabelas já existem.");

        // Migrations manuais: garante colunas adicionadas após EnsureCreated
        await db.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE empresas
                ADD COLUMN IF NOT EXISTS nfce_csc_id    VARCHAR(10)  NULL,
                ADD COLUMN IF NOT EXISTS nfce_csc_token VARCHAR(64)  NULL;");
        await db.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE notas_fiscais
                ADD COLUMN IF NOT EXISTS modelo VARCHAR(2) NULL;");
        // Migration 006b: garante modelo NOT NULL com default '55' (IF NOT EXISTS deixou NULL para registros antigos)
        await db.Database.ExecuteSqlRawAsync(@"
            UPDATE notas_fiscais SET modelo = '55' WHERE modelo IS NULL;
            ALTER TABLE notas_fiscais ALTER COLUMN modelo SET DEFAULT '55';
            ALTER TABLE notas_fiscais ALTER COLUMN modelo SET NOT NULL;");

        // Migration 004: tabela usuario_empresa (auth Supabase)
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS usuario_empresa (
                id                   UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
                supabase_user_id     UUID        NOT NULL UNIQUE,
                empresa_id           UUID        REFERENCES empresas(id) ON DELETE SET NULL,
                onboarding_concluido BOOLEAN     NOT NULL DEFAULT false,
                criado_em            TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_usuario_empresa_supabase_user_id
                ON usuario_empresa (supabase_user_id);");

        // Migration 005: dados fiscais da empresa + estoque produto
        await db.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS ramo                              VARCHAR(50);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS regime_tributario                 VARCHAR(30);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS crt                               INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS cnae                              VARCHAR(7);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS inscricao_municipal               VARCHAR(30);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS emite_nfse                        BOOLEAN NOT NULL DEFAULT false;
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS inscrito_suframa                  BOOLEAN NOT NULL DEFAULT false;
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS csosn_padrao                      VARCHAR(3) DEFAULT '102';
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS cst_icms_padrao                   VARCHAR(3);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS cst_pis_padrao                    VARCHAR(2);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS cst_cofins_padrao                 VARCHAR(2);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS aliquota_pis                      DECIMAL(5,2) DEFAULT 0.65;
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS aliquota_cofins                   DECIMAL(5,2) DEFAULT 3.00;
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS aliquota_iss                      DECIMAL(5,2) DEFAULT 5.00;
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS opera_como_substituto_tributario  BOOLEAN DEFAULT false;
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS mva_padrao                        DECIMAL(5,2);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS possui_st_bebidas                 BOOLEAN DEFAULT false;
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS contador_nome                     VARCHAR(100);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS contador_crc                      VARCHAR(20);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS contador_email                    VARCHAR(100);
            ALTER TABLE empresas ADD COLUMN IF NOT EXISTS faixa_simples                     VARCHAR(10);
            ALTER TABLE produtos ADD COLUMN IF NOT EXISTS quantidade_estoque                DECIMAL(15,4) DEFAULT 0;
            ALTER TABLE produtos ADD COLUMN IF NOT EXISTS ean                               VARCHAR(14);");

        // Migration 006: tabela de usuarios locais (auth sem Supabase)
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS usuarios (
                id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
                email      VARCHAR(255) NOT NULL UNIQUE,
                senha_hash TEXT         NOT NULL,
                criado_em  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            );");

        // Migration 007: expande cnae para VARCHAR(10)
        await db.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE empresas ALTER COLUMN cnae TYPE VARCHAR(10);");

        // Migration 008: tokens de recuperacao de senha
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS password_reset_tokens (
                id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
                usuario_id  UUID        NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
                token       VARCHAR(64) NOT NULL UNIQUE,
                expires_at  TIMESTAMPTZ NOT NULL,
                used_at     TIMESTAMPTZ NULL,
                criado_em   TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_prt_token ON password_reset_tokens (token);");

        Console.WriteLine("[STARTUP] Migrations manuais aplicadas.");

        // Seed: insere dados iniciais se a empresa de Orlando não existir
        var empresaId = Guid.Parse("dd104b57-010a-4458-8699-d63807e205d3");
        if (!await db.Empresas.AnyAsync(e => e.Id == empresaId))
        {
            var empresa = new Jubilados.Domain.Entities.Empresa
            {
                Id             = empresaId,
                CNPJ           = "21362844000152",
                RazaoSocial    = "Orlando Lucindo de Pontes Neto Materiais de Construcao",
                NomeFantasia   = "Azulzao da Construcao",
                InscricaoEstadual = "162421303",
                Logradouro     = "R OTAVIANO D MONTEIRO P FILHO",
                Numero         = "19",
                Bairro         = "FUNCIONARIOS",
                Municipio      = "JOAO PESSOA",
                UF             = "PB",
                CEP            = "58078340",
                Telefone       = "83998347220",
                Email          = "",
                CriadoEm      = DateTime.UtcNow,
                AtualizadoEm  = DateTime.UtcNow
            };
            db.Empresas.Add(empresa);

            var produto = new Jubilados.Domain.Entities.Produto
            {
                Id          = Guid.Parse("9ae15d08-81e9-4767-911e-8faf96ad2821"),
                EmpresaId   = empresaId,
                Nome        = "Produto Teste Homologacao",
                NCM         = "84713012",
                CFOP        = "5102",
                CST         = "00",
                Unidade     = "UN",
                Preco       = 100.00m,
                AliquotaICMS = 0m,
                AliquotaPIS  = 0.65m,
                AliquotaCOFINS = 3.00m,
                Ativo       = true,
                CriadoEm   = DateTime.UtcNow,
                AtualizadoEm = DateTime.UtcNow
            };
            db.Produtos.Add(produto);

            var cliente = new Jubilados.Domain.Entities.Cliente
            {
                Id               = Guid.Parse("0865f76e-bff7-48ef-8b02-f24b6a468e3d"),
                EmpresaId        = empresaId,
                Nome             = "WURTH DO BRASIL PECAS DE FIXACAO LTDA",
                CPF_CNPJ         = "43648971003170",
                InscricaoEstadual = "161585078",
                Logradouro       = "ROD BR-230",
                Numero           = "SN",
                Bairro           = "CRISTO REDENTOR",
                Municipio        = "JOAO PESSOA",
                UF               = "PB",
                CEP              = "58071680",
                CriadoEm        = DateTime.UtcNow,
                AtualizadoEm    = DateTime.UtcNow
            };
            db.Clientes.Add(cliente);

            await db.SaveChangesAsync();
            Console.WriteLine("[STARTUP] Seed inicial inserido.");
        }

        // Seed: usuário local pablo9hierro@gmail.com / 123456
        var userSeedEmail = "pablo9hierro@gmail.com";
        var userSeedId    = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001");
        if (!await db.Usuarios.AnyAsync(u => u.Email == userSeedEmail))
        {
            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                "123456", salt, 100_000,
                System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            var senhaHash = Convert.ToBase64String(salt) + "." + Convert.ToBase64String(hash);

            db.Usuarios.Add(new Jubilados.Domain.Entities.Usuario
            {
                Id        = userSeedId,
                Email     = userSeedEmail,
                SenhaHash = senhaHash,
                CriadoEm  = DateTime.UtcNow
            });

            // Vincula o usuário à empresa seed com onboarding já concluído
            if (!await db.UsuarioEmpresas.AnyAsync(u => u.SupabaseUserId == userSeedId))
            {
                db.UsuarioEmpresas.Add(new Jubilados.Domain.Entities.UsuarioEmpresa
                {
                    SupabaseUserId      = userSeedId,
                    EmpresaId           = empresaId,
                    OnboardingConcluido = true,
                    CriadoEm            = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
            Console.WriteLine("[STARTUP] Usuário seed pablo9hierro@gmail.com criado.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[STARTUP] Erro schema/seed: {ex.Message}");
    }
}

// ── Middleware Pipeline ───────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Jubilados NFe API v1");
    c.RoutePrefix = string.Empty; // Swagger na raiz
});

app.UseStaticFiles();

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

// Endpoint de diagnóstico temporário
app.MapGet("/diag", () => {
    var dbPwd = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "";
    var rawConn = builder.Configuration.GetConnectionString("DefaultConnection") ?? "";
    var resolved = rawConn.Replace("${DB_PASSWORD}", dbPwd);
    var masked = System.Text.RegularExpressions.Regex.Replace(resolved, @"Password=[^;]+", "Password=***");
    return new {
        dbPasswordSet = !string.IsNullOrEmpty(dbPwd),
        dbPasswordLength = dbPwd.Length,
        dbPasswordFirst3 = dbPwd.Length >= 3 ? dbPwd.Substring(0, 3) + "***" : "(curta demais)",
        connMasked = masked,
        placeholderSubstituted = !resolved.Contains("${DB_PASSWORD}")
    };
});

app.Run();
