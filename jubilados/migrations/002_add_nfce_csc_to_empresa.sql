-- Migration 002: Adiciona campos CSC (NFC-e) à tabela empresas
ALTER TABLE empresas
    ADD COLUMN IF NOT EXISTS nfce_csc_id    VARCHAR(10)  NULL,
    ADD COLUMN IF NOT EXISTS nfce_csc_token VARCHAR(64)  NULL;
