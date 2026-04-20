# ARQUITETURA E PADRÕES DO PROJETO JUBILADOS

> **OBRIGATÓRIO:** Todos os agentes de IA devem seguir rigorosamente este documento ao desenvolver código neste projeto.

---

## 📐 VISÃO GERAL DO SISTEMA

**Jubilados** é um SaaS multi-tenant para emissão de Nota Fiscal Eletrônica (NF-e, NFC-e, NFS-e) e conformidade fiscal brasileira (SEFAZ, SPED, ABRASF).

### Stack Tecnológica

- **Backend:** ASP.NET Core 8, C# 12, EF Core 8
- **Banco de Dados:** PostgreSQL 15+ (Supabase)
- **Autenticação:** Supabase Auth (JWT Bearer)
- **Frontend:** HTML/CSS/JavaScript vanilla (SPA em `wwwroot/index2.html`)
- **Container:** Docker + docker-compose
- **Relatórios:** QuestPDF (DANFE)

### Conformidade Fiscal

- **SEFAZ PB:** Emissão NF-e 4.00, NFC-e 4.00
- **SPED:** EFD ICMS/IPI (Bloco C/E/H), EFD Contribuições (PIS/COFINS)
- **NFS-e:** Padrão ABRASF 2.04 (João Pessoa/PB)
- **Certificação:** A1 (PFX armazenado em base64)
- **Assinatura XML:** SHA-256 com certificado ICP-Brasil

---

## 🏗️ ARQUITETURA LIMPA (CLEAN ARCHITECTURE)

### Estrutura de Pastas

```
jubilados/
├── migrations/                    ← SQL puro, numeração sequencial
│   ├── 001_create_jubilados_tables.sql
│   ├── 002_add_nfce_csc_to_empresa.sql
│   ├── 003_add_modelo_nfce.sql
│   ├── 004_auth_usuario_empresa.sql
│   └── 005_empresa_dados_fiscais.sql
│
├── src/
│   ├── Jubilados.Domain/          ← Entidades, Enums, Lógica de Negócio
│   │   ├── Entities/
│   │   │   ├── Empresa.cs
│   │   │   ├── Produto.cs
│   │   │   ├── Cliente.cs
│   │   │   ├── NotaFiscal.cs
│   │   │   ├── NotaItem.cs
│   │   │   ├── Usuario.cs
│   │   │   └── UsuarioEmpresa.cs
│   │   └── Enums/
│   │       ├── StatusNota.cs
│   │       └── TipoManifestacao.cs
│   │
│   ├── Jubilados.Application/     ← DTOs, Interfaces, Serviços de Aplicação
│   │   ├── DTOs/
│   │   │   └── NFeDto.cs
│   │   ├── Interfaces/
│   │   │   ├── ICertificadoService.cs
│   │   │   ├── INFeService.cs
│   │   │   ├── IDanfeService.cs
│   │   │   ├── ISpedService.cs
│   │   │   ├── IManifestacaoService.cs
│   │   │   └── InotaEntradaService.cs
│   │   ├── Services/
│   │   │   ├── CertificadoService.cs
│   │   │   ├── ManifestacaoService.cs
│   │   │   ├── NFeService.cs
│   │   │   └── NotaEntradaService.cs
│   │   └── Configuration/
│   │       └── NFeOptions.cs
│   │
│   ├── Jubilados.Infrastructure/  ← EF Core, Acesso a Dados, Integrações Externas
│   │   ├── Data/
│   │   │   ├── JubiladosDbContext.cs
│   │   │   └── Configurations/
│   │   │       ├── EmpresaConfiguration.cs
│   │   │       ├── ProdutoConfiguration.cs
│   │   │       ├── ClienteConfiguration.cs
│   │   │       ├── NotaFiscalConfiguration.cs
│   │   │       ├── NotaItemConfiguration.cs
│   │   │       ├── UsuarioConfiguration.cs
│   │   │       └── UsuarioEmpresaConfiguration.cs
│   │   └── Services/
│   │       ├── DanfeService.cs
│   │       ├── SpedService.cs
│   │       ├── NFeService.cs
│   │       ├── NotaEntradaService.cs
│   │       └── ManifestacaoService.cs
│   │
│   └── Jubilados.API/             ← Controllers, Program.cs, Configuração
│       ├── Controllers/
│       │   ├── AuthController.cs
│       │   ├── EmpresaController.cs
│       │   ├── ProdutoController.cs
│       │   ├── ClienteController.cs
│       │   ├── NFeController.cs
│       │   └── DiagnosticController.cs
│       ├── wwwroot/
│       │   ├── index2.html        ← SPA principal (abas)
│       │   ├── login.html         ← Autenticação Supabase
│       │   └── onboarding.html    ← Wizard fiscal multi-passo
│       ├── Program.cs
│       ├── appsettings.json
│       └── appsettings.Development.json
│
├── docker-compose.yml
├── Dockerfile
└── jubilados.sln
```

---

## 🎯 REGRAS OBRIGATÓRIAS

### 1. **NUNCA QUEBRE O QUE FUNCIONA**

✅ Funcionalidades já implementadas (não alterar sem necessidade):
- Emissão NF-e/NFC-e (modelo 55/65)
- Consulta de NF-e
- Carta de Correção Eletrônica (CCe)
- Cancelamento de NF-e
- Inutilização de numeração
- Manifestação do Destinatário
- Geração de DANFE (QuestPDF)
- SPED Fiscal (Blocos C)

### 2. **MIGRATIONS SQL — SEMPRE MANUAIS**

❌ **PROIBIDO:**
- `database.EnsureCreated()`
- EF Core Migrations (`Add-Migration`, `dotnet ef migrations add`)
- Qualquer alteração automática de schema

✅ **CORRETO:**
- Criar arquivo `migrations/00N_descricao.sql`
- Numeração sequencial: `001`, `002`, `003`...
- SQL puro, idempotente quando possível
- Executar manualmente no Supabase Studio

**Exemplo de migration:**
```sql
-- migrations/006_add_quantidade_estoque_produto.sql
ALTER TABLE produtos ADD COLUMN IF NOT EXISTS quantidade_estoque DECIMAL(10,2) DEFAULT 0;
COMMENT ON COLUMN produtos.quantidade_estoque IS 'Estoque atual - usado no Bloco H do SPED EFD';
```

### 3. **CONVENÇÕES DE NOMENCLATURA**

#### Banco de Dados (snake_case)
```sql
CREATE TABLE notas_fiscais (          -- plural, snake_case
    id                  UUID,
    empresa_id          UUID,          -- FK com sufixo _id
    numero_nota         INTEGER,
    chave_acesso        VARCHAR(44),
    criado_em           TIMESTAMPTZ,   -- timestamp com sufixo _em
    atualizado_em       TIMESTAMPTZ
);
```

#### C# (PascalCase)
```csharp
// Entidades: singular, PascalCase
public class NotaFiscal
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }              // FK: PascalCase
    public int NumeroNota { get; set; }
    public string ChaveAcesso { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
    
    // Navegação: singular se 1:1, plural se 1:N
    public Empresa Empresa { get; set; }
    public ICollection<NotaItem> NotaItens { get; set; }
}
```

#### DTOs (Record types preferencialmente)
```csharp
// Application/DTOs/NFeDto.cs
public record EmitirNFeDto(
    Guid EmpresaId,
    Guid ClienteId,
    string NaturezaOperacao,
    string? IndPres,                   // Nullable com ?
    List<ItemNFeDto> Itens
);
```

### 4. **DEPENDENCY INJECTION (DI)**

**Regra:** Todos os serviços DEVEM ser injetados via DI em `Program.cs`.

```csharp
// ✅ CORRETO: registrar em Program.cs
builder.Services.AddScoped<INFeService, NFeService>();
builder.Services.AddScoped<IDanfeService, DanfeService>();
builder.Services.AddScoped<ISpedService, SpedService>();

// ✅ CORRETO: injetar no controller
public class NFeController : ControllerBase
{
    private readonly INFeService _nfeService;
    private readonly JubiladosDbContext _db;
    
    public NFeController(INFeService nfeService, JubiladosDbContext db)
    {
        _nfeService = nfeService;
        _db = db;
    }
}

// ❌ ERRADO: instanciar diretamente
var service = new NFeService(); // NUNCA FAÇA ISSO!
```

**Ciclo de Vida:**
- `AddScoped` → Para serviços que acessam banco de dados (padrão)
- `AddSingleton` → Para configurações imutáveis (`NFeOptions`)
- `AddTransient` → Raramente usado (serviços sem estado)

### 5. **ENTITY FRAMEWORK CORE — BOAS PRÁTICAS**

#### Consultas de Leitura
```csharp
// ✅ SEMPRE use AsNoTracking() para leitura
var produtos = await _db.Produtos
    .AsNoTracking()
    .Where(p => p.EmpresaId == empresaId && p.Ativo)
    .ToListAsync();

// ❌ NUNCA carregue tudo
var all = await _db.Produtos.ToListAsync();  // PERFORMANCE RUIM!
```

#### Timestamps Automáticos
O `JubiladosDbContext` já implementa atualização automática de `CriadoEm` e `AtualizadoEm` no `SaveChangesAsync()`. **Não altere manualmente.**

```csharp
// ✅ CORRETO: deixe o DbContext gerenciar
var produto = new Produto { Nome = "...", EmpresaId = id };
_db.Produtos.Add(produto);
await _db.SaveChangesAsync();  // CriadoEm e AtualizadoEm automáticos

// ❌ ERRADO: setar manualmente
produto.CriadoEm = DateTime.UtcNow;  // Redundante!
```

#### Relacionamentos
```csharp
// ✅ CORRETO: configurar via Fluent API nas classes Configuration
public class ProdutoConfiguration : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.ToTable("produtos");
        builder.HasKey(p => p.Id);
        
        builder.HasOne(p => p.Empresa)
            .WithMany(e => e.Produtos)
            .HasForeignKey(p => p.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.Property(p => p.Nome).IsRequired().HasMaxLength(150);
        builder.Property(p => p.Preco).HasPrecision(10, 2);
    }
}
```

### 6. **AUTENTICAÇÃO E AUTORIZAÇÃO**

#### JWT Bearer (Supabase Auth)

**Variáveis de ambiente obrigatórias:**
```bash
SUPABASE_URL=https://uguqpkwaowvnefsorqoa.supabase.co
SUPABASE_ANON_KEY=eyJ...
SUPABASE_JWT_SECRET=secret-ultra-seguro-64-chars
```

**Controllers protegidos:**
```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]  // ← OBRIGATÓRIO em todos os controllers (exceto AuthController)
public class EmpresaController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetEmpresa(Guid id)
    {
        // Obter usuário autenticado do JWT
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        
        // Validar acesso à empresa
        var acesso = await _db.UsuarioEmpresas
            .FirstOrDefaultAsync(ue => ue.SupabaseUserId.ToString() == userId 
                                    && ue.EmpresaId == id);
        if (acesso == null)
            return Forbid();
        
        // ...lógica
    }
}
```

**Frontend — localStorage e fetchAuth:**
```javascript
// ✅ CORRETO: usar fetchAuth() em todas as chamadas de API
const API = 'https://api.jubilados.com.br';

function fetchAuth(url, options = {}) {
    const token = localStorage.getItem('sb_token');
    if (!token) {
        window.location.href = '/login.html';
        return Promise.reject('Não autenticado');
    }
    
    options.headers = {
        ...options.headers,
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
    };
    
    return fetch(url, options);
}

// Uso:
const res = await fetchAuth(API + '/api/empresa/' + empresaId);
const empresa = await res.json();
```

### 7. **FORMATAÇÃO NUMÉRICA — INVARIANT CULTURE**

**CRÍTICO:** XML da SEFAZ exige separador decimal `.` (ponto), não vírgula.

```csharp
// ✅ CORRETO: já configurado no Program.cs (NÃO REMOVER!)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

// ✅ CORRETO: ao formatar números para XML
decimal valor = 1234.56m;
string xml = $"<vProd>{valor.ToString("F2", CultureInfo.InvariantCulture)}</vProd>";
// Resultado: <vProd>1234.56</vProd>

// ❌ ERRADO: usar cultura pt-BR
string xml = $"<vProd>{valor.ToString("F2")}</vProd>";  
// Resultado: <vProd>1234,56</vProd> → SEFAZ rejeita!
```

### 8. **VARIÁVEIS DE AMBIENTE (SECRETS)**

**Hierarquia de configuração:**
1. Variáveis de ambiente (prioritário)
2. `appsettings.Development.json` (apenas dev local)
3. `appsettings.json` (valores padrão)

```csharp
// ✅ CORRETO: ler secrets
var jwtSecret = Environment.GetEnvironmentVariable("SUPABASE_JWT_SECRET")
    ?? builder.Configuration["Supabase:JwtSecret"]
    ?? throw new InvalidOperationException("JWT Secret não configurado!");

// ❌ NUNCA hardcode secrets no código
const string API_KEY = "minha-chave-secreta";  // PROIBIDO!
```

**Variáveis obrigatórias:**
- `DATABASE_URL` ou `DB_PASSWORD`
- `SUPABASE_URL`
- `SUPABASE_ANON_KEY`
- `SUPABASE_JWT_SECRET`

**Variáveis opcionais:**
- `NFE_AMBIENTE` (1=produção, 2=homologação)
- `NFE_TIMEOUT_SEFAZ` (padrão: 60000ms)

---

## 🎨 FRONTEND — DESIGN SYSTEM

### Estrutura HTML (SPA de uma página)

**`wwwroot/index2.html`** — Sistema de abas:

```html
<div class="container">
    <!-- Header com logo, empresa selecionada, botão sair -->
    <header>...</header>
    
    <!-- Alertas globais (certificado vencendo, etc) -->
    <div id="alertasGlobais"></div>
    
    <!-- Navegação por abas -->
    <div class="tabs">
        <button class="tab-btn active" onclick="showTab('dashboard',this)">📊 Dashboard</button>
        <button class="tab-btn" onclick="showTab('empresa',this)">🏢 Empresa</button>
        <button class="tab-btn" onclick="showTab('produtos',this)">📦 Produtos</button>
        <button class="tab-btn" onclick="showTab('clientes',this)">👥 Clientes</button>
        <button class="tab-btn" onclick="showTab('emitir',this)">📄 Emitir NF-e</button>
        <button class="tab-btn" onclick="showTab('nuvem',this)">☁️ Nuvem Fiscal</button>
        <button class="tab-btn" onclick="showTab('entrada',this)">📥 Notas Entrada</button>
        <button class="tab-btn" onclick="showTab('sped',this)">📊 SPED EFD</button>
        <button class="tab-btn" onclick="showTab('nfse',this)">🧾 NFS-e</button>
    </div>
    
    <!-- Conteúdo das abas (display:none exceto ativa) -->
    <div id="tab-dashboard" class="tab-content active">...</div>
    <div id="tab-empresa" class="tab-content">...</div>
    <!-- ... demais abas -->
</div>
```

### CSS Variables (Design System)

**OBRIGATÓRIO:** Todo HTML novo deve usar as CSS vars existentes em `:root`.

```css
:root {
    /* Cores principais */
    --primary: #2563eb;
    --primary-dark: #1e40af;
    --success: #10b981;
    --warning: #f59e0b;
    --error: #ef4444;
    --bg: #f8fafc;
    --card-bg: #ffffff;
    --text: #1e293b;
    --text-muted: #64748b;
    --border: #e2e8f0;
    
    /* Espaçamentos */
    --spacing-xs: 4px;
    --spacing-sm: 8px;
    --spacing-md: 16px;
    --spacing-lg: 24px;
    --spacing-xl: 32px;
    
    /* Bordas */
    --radius: 8px;
    --radius-sm: 4px;
    --radius-lg: 12px;
    
    /* Sombras */
    --shadow-sm: 0 1px 2px rgba(0,0,0,.05);
    --shadow: 0 4px 6px rgba(0,0,0,.07);
    --shadow-lg: 0 10px 15px rgba(0,0,0,.1);
}
```

### Componentes Reutilizáveis

#### Card
```html
<div class="card">
    <h2>Título do Card</h2>
    <p class="subtitle">Descrição opcional</p>
    <!-- Conteúdo -->
</div>
```

#### Botões
```html
<button class="btn">Primário</button>
<button class="btn btn-outline">Secundário</button>
<button class="btn btn-success">Sucesso</button>
<button class="btn btn-error">Perigo</button>
```

#### Alertas
```html
<div class="alert alert-info">ℹ️ Informação</div>
<div class="alert alert-warn">⚠️ Atenção</div>
<div class="alert alert-error">❌ Erro</div>
<div class="alert alert-success">✅ Sucesso</div>
```

#### Badges (Status)
```html
<span class="badge badge-success">Autorizada</span>
<span class="badge badge-warn">Pendente</span>
<span class="badge badge-error">Rejeitada</span>
<span class="badge badge-gray">Cancelada</span>
```

#### Grid Responsivo
```html
<div class="grid2">
    <div class="field">
        <label>Campo 1</label>
        <input type="text">
    </div>
    <div class="field">
        <label>Campo 2</label>
        <input type="text">
    </div>
</div>
```

### JavaScript — Padrões

```javascript
// Estado global da aplicação (singleton)
const estado = {
    empresaId: null,
    usuarioId: null,
    empresaNome: '',
    produtos: [],
    clientes: [],
    notasCache: []
};

// Funções globais (reutilizáveis)
async function fetchAuth(url, options = {}) { /* ... */ }
function showTab(tabId, btn) { /* ... */ }
function showToast(msg, tipo = 'info') { /* ... */ }
function formatCNPJ(cnpj) { /* ... */ }
function formatCPF(cpf) { /* ... */ }
function formatMoeda(valor) { /* ... */ }

// Inicialização
document.addEventListener('DOMContentLoaded', async () => {
    verificarAutenticacao();
    await carregarDadosIniciais();
    verificarCertificados();
});
```

---

## 📋 FISCAL — REGRAS DE NEGÓCIO

### NF-e 4.00 — Campos Dinâmicos

**CRT (Código de Regime Tributário):**
- `1` = Simples Nacional / MEI
- `2` = Lucro Presumido
- `3` = Lucro Real

**idDest (Destino da Operação):**
```csharp
var idDest = empresa.UF == cliente.UF ? "1" : "2";  // 1=interna, 2=interestadual
```

**indPres (Indicador de Presença):**
- `1` = Operação presencial (padrão)
- `2` = Operação não presencial, pela Internet
- `4` = NFC-e em operação com entrega a domicílio

**Tributação de ICMS:**
```csharp
// Se CRT = 1 (Simples Nacional)
if (empresa.CRT == 1)
{
    // Usar ICMSSN com CSOSN do produto
    sb.AppendLine($"      <ICMSSN{produto.CSOSN}>");
    sb.AppendLine($"        <orig>0</orig>");
    sb.AppendLine($"        <CSOSN>{produto.CSOSN}</CSOSN>");
    sb.AppendLine($"      </ICMSSN{produto.CSOSN}>");
}
else  // CRT = 2 ou 3 (regime normal)
{
    // Usar CST ICMS 00/20/40/60 do produto
    sb.AppendLine($"      <ICMS{produto.CST}>");
    sb.AppendLine($"        <orig>0</orig>");
    sb.AppendLine($"        <CST>{produto.CST}</CST>");
    sb.AppendLine($"        <vBC>{vBC}</vBC>");
    sb.AppendLine($"        <pICMS>{produto.AliquotaICMS}</pICMS>");
    sb.AppendLine($"        <vICMS>{vICMS}</vICMS>");
    sb.AppendLine($"      </ICMS{produto.CST}>");
}
```

**PIS/COFINS:**
```csharp
// Simples Nacional: CST 07 (NT - Não Tributado)
if (empresa.CRT == 1)
{
    sb.AppendLine("      <PISNT><CST>07</CST></PISNT>");
    sb.AppendLine("      <COFINSNT><CST>07</CST></COFINSNT>");
}
else  // Regime normal: usar alíquotas da empresa
{
    sb.AppendLine($"      <PISAliq>");
    sb.AppendLine($"        <CST>{empresa.CstPisPadrao}</CST>");
    sb.AppendLine($"        <vBC>{vBC}</vBC>");
    sb.AppendLine($"        <pPIS>{empresa.AliquotaPis.ToString("F2")}</pPIS>");
    sb.AppendLine($"        <vPIS>{vPIS.ToString("F2")}</vPIS>");
    sb.AppendLine($"      </PISAliq>");
    // Idem para COFINS
}
```

### DIFAL (Diferencial de Alíquota Interestadual)

**Quando aplicar:**
- `idDest = 2` (interestadual)
- Destinatário = consumidor final não contribuinte (`indIEDest = 9`)

**Cálculo:**
```csharp
if (idDest == "2" && cliente.IndIEDest == "9")
{
    decimal aliqInterna = 18m;  // Alíquota interna do estado de destino (ex: PB)
    decimal aliqInter = cliente.UF switch
    {
        "SE" or "CO" or "AC" or "AM" or "RR" or "AP" or "PA" or "TO" or "ES" => 7m,
        _ => 12m
    };
    
    decimal vBCUFDest = vBC;
    decimal vICMSUFDest = vBCUFDest * (aliqInterna - aliqInter) / 100;
    decimal vICMSUFRemet = 0;  // Se ano >= 2019, 100% para UF destino
    
    // Preencher grupo ICMSUFDest no XML
}
```

### SPED Fiscal — Blocos Obrigatórios

**Bloco 0 — Abertura e Identificação:**
- `0000`: cabeçalho (CNPJ, IE, período)
- `0100`: dados do contabilista
- `0150`: cadastro de participantes (clientes/fornecedores)
- `0200`: tabela de itens e serviços (produtos)

**Bloco C — Documentos Fiscais:**
- `C100`: NF-e emitida (cabeçalho)
- `C170`: itens da nota
- `C190`: totais por CST

**Bloco E — Apuração ICMS:**
- `E100`: período de apuração
- `E110`: totais débitos/créditos ICMS

**Bloco H — Inventário:**
- `H005`: data e valor total do inventário
- `H010`: itens do inventário (produto por produto)

---

## 🔒 SEGURANÇA

### 1. **Certificado Digital A1**

**Armazenamento:**
```csharp
// ✅ CORRETO: armazenar como Base64 no banco
var certBytes = File.ReadAllBytes(pfxPath);
empresa.CertificadoBase64 = Convert.ToBase64String(certBytes);
empresa.CertificadoSenha = senha;  // ⚠️ Idealmente criptografar com AES

// ✅ CORRETO: carregar para assinatura
var certBytes = Convert.FromBase64String(empresa.CertificadoBase64);
var cert = new X509Certificate2(certBytes, empresa.CertificadoSenha);
```

**Validação de validade:**
```csharp
if (empresa.CertificadoValidade < DateTime.UtcNow.AddDays(30))
    return new AlertaDto("Certificado vence em breve!", urgente: true);
```

### 2. **Injeção SQL**

**Sempre usar EF Core com consultas parametrizadas:**
```csharp
// ✅ CORRETO: LINQ to Entities (seguro)
var produto = await _db.Produtos
    .FirstOrDefaultAsync(p => p.Id == produtoId);

// ❌ ERRADO: SQL raw com concatenação
var sql = $"SELECT * FROM produtos WHERE id = '{produtoId}'";  // VULNERÁVEL!
```

### 3. **CORS**

**Configurar origens permitidas em produção:**
```csharp
// Development (appsettings.Development.json)
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin());  // OK para dev
});

// Production (appsettings.json)
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("https://app.jubilados.com.br")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});
```

---

## 📝 DOCUMENTAÇÃO DE CÓDIGO

### XML Comments (obrigatório para métodos públicos)

```csharp
/// <summary>
/// Emite uma NF-e modelo 55 ou NFC-e modelo 65 via webservice SEFAZ.
/// </summary>
/// <param name="dto">Dados da nota a ser emitida (empresa, cliente, itens).</param>
/// <param name="ct">Token de cancelamento para timeout da requisição HTTP.</param>
/// <returns>
/// Resultado contendo chave de acesso, protocolo de autorização e XML assinado,
/// ou lista de erros de validação da SEFAZ (cStat != 100).
/// </returns>
/// <exception cref="InvalidOperationException">Certificado digital expirado ou inválido.</exception>
/// <exception cref="HttpRequestException">Falha de comunicação com SEFAZ (timeout, rede).</exception>
public async Task<EmitirNFeResultDto> EmitirAsync(EmitirNFeDto dto, CancellationToken ct)
{
    // ...
}
```

### TODO Comments (para itens pendentes)

```csharp
// TODO: Implementar validação de GTIN (código de barras) via API GS1
// TODO: Adicionar suporte a múltiplas formas de pagamento na mesma nota
// FIXME: Cálculo de FCP (Fundo de Combate à Pobreza) incorreto para PB
// HACK: Workaround temporário — SEFAZ PB não valida tag vFCPUFDest em homologação
```

---

## 🧪 TESTES (Futuro)

**Estrutura (a ser implementada):**
```
tests/
├── Jubilados.UnitTests/
│   ├── Domain/
│   ├── Application/
│   └── Infrastructure/
└── Jubilados.IntegrationTests/
    ├── API/
    └── Database/
```

**Frameworks sugeridos:**
- xUnit
- FluentAssertions
- Moq (para mocks)
- Testcontainers (PostgreSQL em Docker para testes de integração)

---

## 🚀 DEPLOY

### Variáveis de Ambiente (Produção)

```bash
# Banco de Dados
DATABASE_URL=postgresql://user:pass@db.supabase.co:5432/postgres

# Supabase Auth
SUPABASE_URL=https://projeto.supabase.co
SUPABASE_ANON_KEY=eyJ...
SUPABASE_JWT_SECRET=seu-jwt-secret-64-chars

# NF-e
NFE_AMBIENTE=1                    # 1=produção, 2=homologação
NFE_TIMEOUT_SEFAZ=60000           # ms

# CORS (opcional)
ALLOWED_ORIGINS=https://app.jubilados.com.br
```

### Docker Build

```bash
docker build -t jubilados-api:latest .
docker run -p 8080:8080 \
    -e DATABASE_URL=$DATABASE_URL \
    -e SUPABASE_JWT_SECRET=$JWT_SECRET \
    jubilados-api:latest
```

---

## 📚 REFERÊNCIAS TÉCNICAS

### NF-e 4.00
- **Manual de Integração:** [http://www.nfe.fazenda.gov.br/portal/principal.aspx](http://www.nfe.fazenda.gov.br/portal/principal.aspx)
- **Schema XSD:** [http://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=mO+01SUfWPA=](http://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=mO+01SUfWPA=)
- **SEFAZ PB Homologação:** `https://nfe.fazenda.pb.gov.br/nfehomologacao/services/NFeAutorizacao4`
- **SEFAZ PB Produção:** `https://nfe.fazenda.pb.gov.br/nfe/services/NFeAutorizacao4`

### SPED Fiscal
- **Guia Prático:** [http://sped.rfb.gov.br/pasta/show/1644](http://sped.rfb.gov.br/pasta/show/1644)
- **Leiaute Bloco K (versão atual):** Versão 019

### NFS-e ABRASF
- **Schema ABRASF 2.04:** [http://www.abrasf.org.br/arquivos/nfse.xsd](http://www.abrasf.org.br/arquivos/nfse.xsd)
- **João Pessoa/PB:** [https://notasjp.joaopessoa.pb.gov.br](https://notasjp.joaopessoa.pb.gov.br)

---

## 🤖 INSTRUÇÕES PARA AGENTES DE IA

### Ao Implementar Nova Funcionalidade:

1. **Ler este documento completamente** antes de começar
2. **Verificar se a funcionalidade não quebra** nada existente
3. **Criar migration SQL** se alterar schema de banco
4. **Seguir arquitetura limpa:**
   - Entidade → `Domain/Entities`
   - Interface → `Application/Interfaces`
   - DTO → `Application/DTOs`
   - Service → `Application/Services` ou `Infrastructure/Services`
   - Controller → `API/Controllers`
5. **Registrar serviços em `Program.cs`** via DI
6. **Usar convenções de nomenclatura** (snake_case no DB, PascalCase no C#)
7. **Testar manualmente** após implementar
8. **NÃO criar arquivos .md de documentação** (salvo este)

### Ao Corrigir Bugs:

1. **Verificar o repositório de memória** (`/memories/repo/jubilados-nfe-fixes.md`)
2. **Ler logs de erro** no terminal ou console
3. **Adicionar validações** para evitar recorrência
4. **Atualizar memória** com solução para referência futura

### Prioridades:

1. **Conformidade fiscal** (erros da SEFAZ são críticos!)
2. **Segurança** (certificados, JWT, SQL injection)
3. **Performance** (AsNoTracking, índices de banco)
4. **UX consistente** (design system do index2.html)

---

## 📞 CONTATO E SUPORTE

**Desenvolvedor Principal:** Jubilados Team  
**Ambiente Supabase:** `uguqpkwaowvnefsorqoa`  
**Região:** South America (São Paulo)

---

**Versão do Documento:** 1.0  
**Última Atualização:** 2026-04-20  
**Status:** ✅ ATIVO — Obrigatório para todos os agentes de IA
