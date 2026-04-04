-- ══════════════════════════════════════════════════════════════════════════════
-- JUBILADOS SaaS NFe — Migration inicial do banco de dados
-- Execute este SQL no Supabase Studio:
--   https://supabase.com/dashboard/project/uguqpkwaowvnefsorqoa/sql
-- ══════════════════════════════════════════════════════════════════════════════

-- Habilitar extensão UUID
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ── EMPRESAS ──────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS empresas (
    id                   UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    cnpj                 VARCHAR(18)  NOT NULL UNIQUE,
    razao_social         VARCHAR(150) NOT NULL,
    nome_fantasia        VARCHAR(100),
    inscricao_estadual   VARCHAR(20),
    logradouro           VARCHAR(100),
    numero               VARCHAR(10),
    complemento          VARCHAR(60),
    bairro               VARCHAR(60),
    municipio            VARCHAR(60),
    uf                   CHAR(2),
    cep                  VARCHAR(9),
    telefone             VARCHAR(15),
    email                VARCHAR(100),
    certificado_base64   TEXT,
    certificado_senha    VARCHAR(256),
    certificado_validade TIMESTAMPTZ,
    criado_em            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    atualizado_em        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ── CLIENTES ─────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS clientes (
    id                   UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id           UUID         NOT NULL REFERENCES empresas(id) ON DELETE CASCADE,
    nome                 VARCHAR(150) NOT NULL,
    cpf_cnpj             VARCHAR(18)  NOT NULL,
    inscricao_estadual   VARCHAR(20),
    email                VARCHAR(100),
    telefone             VARCHAR(15),
    logradouro           VARCHAR(100),
    numero               VARCHAR(10),
    complemento          VARCHAR(60),
    bairro               VARCHAR(60),
    municipio            VARCHAR(60),
    codigo_municipio     VARCHAR(7),
    uf                   CHAR(2),
    cep                  VARCHAR(9),
    pais                 VARCHAR(60)  DEFAULT 'Brasil',
    codigo_pais          VARCHAR(4)   DEFAULT '1058',
    criado_em            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    atualizado_em        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_clientes_empresa_id ON clientes(empresa_id);

-- ── PRODUTOS ──────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS produtos (
    id              UUID           PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id      UUID           NOT NULL REFERENCES empresas(id) ON DELETE CASCADE,
    nome            VARCHAR(120)   NOT NULL,
    descricao       VARCHAR(500),
    ncm             VARCHAR(8)     NOT NULL,
    cfop            VARCHAR(4)     NOT NULL,
    cst             VARCHAR(3)     NOT NULL,
    csosn           VARCHAR(3),
    cest            VARCHAR(7),
    unidade         VARCHAR(6)     DEFAULT 'UN',
    preco           NUMERIC(18,2)  NOT NULL,
    aliquota_icms   NUMERIC(5,2)   DEFAULT 0,
    aliquota_ipi    NUMERIC(5,2)   DEFAULT 0,
    aliquota_pis    NUMERIC(5,2)   DEFAULT 0,
    aliquota_cofins NUMERIC(5,2)   DEFAULT 0,
    ativo           BOOLEAN        DEFAULT TRUE,
    criado_em       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    atualizado_em   TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_produtos_empresa_id ON produtos(empresa_id);

-- ── NOTAS FISCAIS ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS notas_fiscais (
    id                  UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id          UUID          NOT NULL REFERENCES empresas(id) ON DELETE RESTRICT,
    cliente_id          UUID          NOT NULL REFERENCES clientes(id) ON DELETE RESTRICT,
    numero              INTEGER       NOT NULL,
    serie               VARCHAR(3)    NOT NULL,
    chave_acesso        VARCHAR(44),
    status              INTEGER       NOT NULL DEFAULT 0,
    tipo_operacao       CHAR(1)       DEFAULT '1',
    natureza_operacao   VARCHAR(60)   DEFAULT 'Venda de Produto',
    valor_produtos      NUMERIC(18,2) DEFAULT 0,
    valor_desconto      NUMERIC(18,2) DEFAULT 0,
    valor_frete         NUMERIC(18,2) DEFAULT 0,
    valor_seguro        NUMERIC(18,2) DEFAULT 0,
    valor_outros        NUMERIC(18,2) DEFAULT 0,
    valor_icms          NUMERIC(18,2) DEFAULT 0,
    valor_ipi           NUMERIC(18,2) DEFAULT 0,
    valor_pis           NUMERIC(18,2) DEFAULT 0,
    valor_cofins        NUMERIC(18,2) DEFAULT 0,
    valor_total         NUMERIC(18,2) DEFAULT 0,
    xml_envio           TEXT,
    xml_retorno         TEXT,
    protocolo           VARCHAR(20),
    cstat               VARCHAR(3),
    xmotivo             VARCHAR(255),
    nsu                 BIGINT        DEFAULT 0,
    manifestada         BOOLEAN       DEFAULT FALSE,
    tipo_manifestacao   INTEGER,
    emitida_em          TIMESTAMPTZ  DEFAULT NOW(),
    autorizada_em       TIMESTAMPTZ,
    criado_em           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    atualizado_em       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (empresa_id, numero, serie)
);
CREATE INDEX IF NOT EXISTS idx_nf_empresa_id   ON notas_fiscais(empresa_id);
CREATE INDEX IF NOT EXISTS idx_nf_chave_acesso ON notas_fiscais(chave_acesso);
CREATE INDEX IF NOT EXISTS idx_nf_status       ON notas_fiscais(status);

-- ── ITENS DA NOTA ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS nota_itens (
    id              UUID           PRIMARY KEY DEFAULT gen_random_uuid(),
    nota_fiscal_id  UUID           NOT NULL REFERENCES notas_fiscais(id) ON DELETE CASCADE,
    produto_id      UUID           NOT NULL REFERENCES produtos(id) ON DELETE RESTRICT,
    numero_item     INTEGER        NOT NULL,
    quantidade      NUMERIC(15,4)  NOT NULL,
    unidade         VARCHAR(6)     DEFAULT 'UN',
    valor_unitario  NUMERIC(21,10) NOT NULL,
    valor_desconto  NUMERIC(18,2)  DEFAULT 0,
    valor_total     NUMERIC(18,2)  NOT NULL,
    base_icms       NUMERIC(18,2)  DEFAULT 0,
    aliquota_icms   NUMERIC(5,2)   DEFAULT 0,
    valor_icms      NUMERIC(18,2)  DEFAULT 0,
    aliquota_ipi    NUMERIC(5,2)   DEFAULT 0,
    valor_ipi       NUMERIC(18,2)  DEFAULT 0,
    aliquota_pis    NUMERIC(5,2)   DEFAULT 0,
    valor_pis       NUMERIC(18,2)  DEFAULT 0,
    aliquota_cofins NUMERIC(5,2)   DEFAULT 0,
    valor_cofins    NUMERIC(18,2)  DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_ni_nota_fiscal_id ON nota_itens(nota_fiscal_id);

-- ── Trigger: atualiza atualizado_em automaticamente ──────────────────────────
CREATE OR REPLACE FUNCTION trigger_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.atualizado_em = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DO $$
DECLARE tbl TEXT;
BEGIN
  FOREACH tbl IN ARRAY ARRAY['empresas','clientes','produtos','notas_fiscais']
  LOOP
    EXECUTE format(
      'DROP TRIGGER IF EXISTS set_updated_at ON %I;
       CREATE TRIGGER set_updated_at BEFORE UPDATE ON %I
       FOR EACH ROW EXECUTE FUNCTION trigger_set_updated_at()', tbl, tbl);
  END LOOP;
END $$;

-- ══════════════════════════════════════════════════════════════════════════════
-- FIM DA MIGRATION — Execute no Supabase SQL Editor e o banco estará pronto.
-- ══════════════════════════════════════════════════════════════════════════════
