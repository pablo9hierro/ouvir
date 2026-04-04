# 🧾 Jubilados — SaaS NFe

Backend C# (.NET 8 Web API) para emissão de Nota Fiscal Eletrônica (NFe) multi-tenant, conectado ao Supabase (PostgreSQL).

---

## 🏗️ Arquitetura

```
jubilados/
├── src/
│   ├── Jubilados.Domain/          # Entidades e Enums
│   ├── Jubilados.Application/     # Serviços, Interfaces, DTOs, Configuração
│   ├── Jubilados.Infrastructure/  # DbContext, Configurações EF Core
│   └── Jubilados.API/             # Controllers, Program.cs
├── migrations/                    # SQL de migração do banco
├── Dockerfile
├── docker-compose.yml
└── .env.example
```

---

## ⚡ Pré-requisitos

| Ferramenta | Versão |
|---|---|
| .NET SDK | 8.0+ |
| Docker + Docker Compose | qualquer recente |
| Conta Supabase | ativa |

---

## 🚀 Início Rápido

### 1. Configurar o banco de dados (Supabase)

Acesse o **SQL Editor** do seu projeto Supabase:
👉 https://supabase.com/dashboard/project/uguqpkwaowvnefsorqoa/sql

Cole e execute o conteúdo de `migrations/001_create_jubilados_tables.sql`.

### 2. Configurar variáveis de ambiente

```bash
cp .env.example .env
# Edite .env e preencha DB_PASSWORD com a senha do PostgreSQL Supabase
```

### 3. Rodar com Docker

```bash
docker-compose up --build
```

API disponível em: http://localhost:8080  
Swagger UI: http://localhost:8080

### 4. Rodar localmente (sem Docker)

```bash
# Instalar dependências
cd src/Jubilados.API
dotnet restore

# Defina a connection string no appsettings.Development.json
# (substitua ${DB_PASSWORD} pela senha real)

dotnet run
```

---

## 📦 Instalar biblioteca Zeus (NFe)

Após instalar o .NET SDK, adicione os pacotes NFe:

```bash
cd src/Jubilados.Infrastructure
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.0

cd ../Jubilados.Application
dotnet add package FluentValidation --version 11.9.0
# Pacotes Zeus/DFe.NET (escolha um dos abaixo):
dotnet add package NFe.Core      # DFe.NET
dotnet add package NFe.Classes
dotnet add package NFe.Servicos

cd ../Jubilados.API
dotnet add package Swashbuckle.AspNetCore --version 6.5.0
dotnet add package FluentValidation.AspNetCore --version 11.3.0
dotnet add package AspNetCore.HealthChecks.NpgSql
```

---

## 🌐 Endpoints da API

| Método | Endpoint | Descrição |
|---|---|---|
| `POST` | `/api/nfe/emitir` | Emite uma NFe na SEFAZ |
| `GET` | `/api/nfe/{id}` | Consulta detalhes de uma NFe |
| `POST` | `/api/nfe/consultar` | Consulta NFe por chave/ID |
| `GET` | `/api/nfe/entrada?empresaId=&ultimoNSU=` | Consulta NFes de entrada |
| `POST` | `/api/nfe/manifestar` | Manifesta o destinatário |
| `GET` | `/api/empresa` | Lista empresas |
| `POST` | `/api/empresa` | Cadastra empresa |
| `PUT` | `/api/empresa/{id}/certificado` | Atualiza certificado digital |
| `GET` | `/health` | Health check |

---

## 📋 Exemplo: Emitir NFe

```json
POST /api/nfe/emitir
{
  "empresaId": "uuid-da-empresa",
  "clienteId": "uuid-do-cliente",
  "naturezaOperacao": "Venda de Produto",
  "serie": "1",
  "valorDesconto": 0,
  "itens": [
    {
      "produtoId": "uuid-do-produto",
      "quantidade": 2,
      "valorUnitario": 150.00,
      "valorDesconto": 0
    }
  ]
}
```

### Resposta:
```json
{
  "sucesso": true,
  "cStat": "100",
  "xMotivo": "Autorizado o uso da NF-e",
  "notaFiscalId": "uuid",
  "chaveAcesso": "35241204...",
  "protocolo": "135241234567890"
}
```

---

## 📋 Exemplo: Manifestar NFe

```json
POST /api/nfe/manifestar
{
  "notaFiscalId": "uuid-da-nota",
  "empresaId": "uuid-da-empresa",
  "tipoManifestacao": "ConfirmacaoOperacao"
}
```

Tipos válidos: `CienciaOperacao` | `ConfirmacaoOperacao` | `Desconhecimento` | `OperacaoNaoRealizada`

---

## 🔐 Certificado Digital

O certificado A1 (.pfx) fica armazenado no banco como base64, por empresa (multi-tenant):

```json
PUT /api/empresa/{id}/certificado
{
  "base64": "MIIKHAIBAzCCCdQGCSqGSIb3DQEHAaCC...",
  "senha": "sua_senha_pfx",
  "validade": "2026-12-31T00:00:00Z"
}
```

---

## 🐳 Variáveis Docker

| Variável | Descrição | Padrão |
|---|---|---|
| `DB_PASSWORD` | Senha PostgreSQL Supabase | — |
| `NFE_AMBIENTE` | `1`=Produção `2`=Homologação | `2` |
| `NFE_CODIGO_UF` | UF do emitente (SP=35) | `35` |

---

## 📊 Supabase

- **Projeto:** `uguqpkwaowvnefsorqoa`
- **URL:** https://uguqpkwaowvnefsorqoa.supabase.co
- **SQL Editor:** https://supabase.com/dashboard/project/uguqpkwaowvnefsorqoa/sql
- **Tabelas criadas:** `empresas`, `clientes`, `produtos`, `notas_fiscais`, `nota_itens`
