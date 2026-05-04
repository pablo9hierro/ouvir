-- Migration 009: criação da tabela fornecedores

CREATE TABLE IF NOT EXISTS fornecedores (
    id                 UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id         UUID         NOT NULL REFERENCES empresas(id) ON DELETE CASCADE,
    nome               VARCHAR(150) NOT NULL,
    cpf_cnpj           VARCHAR(18)  NOT NULL,
    inscricao_estadual VARCHAR(20)  NULL,
    email              VARCHAR(100) NULL,
    telefone           VARCHAR(15)  NULL,
    logradouro         VARCHAR(100) NULL,
    numero             VARCHAR(10)  NULL,
    complemento        VARCHAR(60)  NULL,
    bairro             VARCHAR(60)  NULL,
    municipio          VARCHAR(60)  NULL,
    codigo_municipio   VARCHAR(7)   NULL,
    uf                 VARCHAR(2)   NULL,
    cep                VARCHAR(9)   NULL,
    pais               VARCHAR(60)  NOT NULL DEFAULT 'Brasil',
    codigo_pais        VARCHAR(4)   NOT NULL DEFAULT '1058',
    criado_em          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    atualizado_em      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_fornecedores_empresa_id ON fornecedores (empresa_id);
CREATE INDEX IF NOT EXISTS idx_fornecedores_cpf_cnpj ON fornecedores (cpf_cnpj);
