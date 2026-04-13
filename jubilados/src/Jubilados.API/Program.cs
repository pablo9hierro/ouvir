using System.Globalization;
using Jubilados.Application.Configuration;
using Jubilados.Application.Interfaces;
using Jubilados.Application.Services;
using Jubilados.Infrastructure.Data;
using Jubilados.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

// Garante que todos os formatos numéricos usem ponto (.) como separador decimal
// independente da cultura do SO (pt-BR usaria vírgula e quebraria o XML da SEFAZ)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// ── Configuração ──────────────────────────────────────────────────────────────
builder.Services.Configure<NFeOptions>(builder.Configuration.GetSection(NFeOptions.Section));

// ── Banco de Dados ───────────────────────────────────────────────────────────
// Usa sempre Supabase (appsettings.json) com senha injetada via env DB_PASSWORD
string connectionString;
connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' não configurada.");
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
if (!string.IsNullOrEmpty(dbPassword))
    connectionString = connectionString.Replace("${DB_PASSWORD}", dbPassword);
var maskedConn = System.Text.RegularExpressions.Regex.Replace(
    connectionString, @"Password=[^;]+", "Password=***");
Console.WriteLine($"[STARTUP] Usando Supabase: {maskedConn}");

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
