-- Migration 003: Adiciona campo modelo (55=NF-e, 65=NFC-e) a notas_fiscais
-- e atualiza a constraint UNIQUE para incluir modelo.

-- 1. Adiciona coluna modelo
ALTER TABLE notas_fiscais
    ADD COLUMN IF NOT EXISTS modelo CHAR(2) NOT NULL DEFAULT '55';

-- 2. Remove constraint UNIQUE antiga
ALTER TABLE notas_fiscais
    DROP CONSTRAINT IF EXISTS notas_fiscais_empresa_id_numero_serie_key;

-- 3. Cria nova constraint UNIQUE incluindo modelo
ALTER TABLE notas_fiscais
    ADD CONSTRAINT notas_fiscais_empresa_id_numero_serie_modelo_key
    UNIQUE (empresa_id, numero, serie, modelo);
