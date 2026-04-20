-- Migration 005: Dados fiscais da empresa (onboarding wizard)
-- Amplia a tabela empresas com campos de regime tributário, ramo, ICMS-ST, etc.

ALTER TABLE empresas ADD COLUMN IF NOT EXISTS ramo                           VARCHAR(50);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS regime_tributario              VARCHAR(30);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS crt                            INTEGER     NOT NULL DEFAULT 1;
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS cnae                           VARCHAR(7);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS inscricao_municipal            VARCHAR(30);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS emite_nfse                     BOOLEAN     NOT NULL DEFAULT false;
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS inscrito_suframa               BOOLEAN     NOT NULL DEFAULT false;
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS csosn_padrao                   VARCHAR(3)  DEFAULT '102';
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS cst_icms_padrao                VARCHAR(3);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS cst_pis_padrao                 VARCHAR(2);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS cst_cofins_padrao              VARCHAR(2);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS aliquota_pis                   DECIMAL(5,2) DEFAULT 0.65;
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS aliquota_cofins                DECIMAL(5,2) DEFAULT 3.00;
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS aliquota_iss                   DECIMAL(5,2) DEFAULT 5.00;
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS opera_como_substituto_tributario BOOLEAN   DEFAULT false;
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS mva_padrao                     DECIMAL(5,2);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS possui_st_bebidas              BOOLEAN     DEFAULT false;
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS contador_nome                  VARCHAR(100);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS contador_crc                   VARCHAR(20);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS contador_email                 VARCHAR(100);
ALTER TABLE empresas ADD COLUMN IF NOT EXISTS faixa_simples                  VARCHAR(10);

-- Campo de estoque para Bloco H do SPED
ALTER TABLE produtos ADD COLUMN IF NOT EXISTS quantidade_estoque             DECIMAL(15,4) DEFAULT 0;
ALTER TABLE produtos ADD COLUMN IF NOT EXISTS ean                            VARCHAR(14);
