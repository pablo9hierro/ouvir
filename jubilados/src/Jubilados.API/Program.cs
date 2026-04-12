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

// ── Banco de Dados (Supabase PostgreSQL via EF Core) ─────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' não configurada.");

// Substitui ${DB_PASSWORD} pelo valor da variável de ambiente DB_PASSWORD (Railway/Docker)
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
if (!string.IsNullOrEmpty(dbPassword))
    connectionString = connectionString.Replace("${DB_PASSWORD}", dbPassword);

// Log da connection string mascarada para diagnóstico em produção
var maskedConn = System.Text.RegularExpressions.Regex.Replace(
    connectionString, @"Password=[^;]+", "Password=***");
Console.WriteLine($"[STARTUP] ConnectionString: {maskedConn}");

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

// ── Migrations automáticas (opcional — recomendado só em dev) ─────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<JubiladosDbContext>();
    await db.Database.MigrateAsync();
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
