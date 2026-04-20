-- Migration 004: Autenticação Supabase — tabela usuario_empresa
-- Liga o supabase_user_id (auth.users) à empresa do sistema

CREATE TABLE IF NOT EXISTS usuario_empresa (
    id                   UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    supabase_user_id     UUID         NOT NULL UNIQUE,
    empresa_id           UUID         REFERENCES empresas(id) ON DELETE SET NULL,
    onboarding_concluido BOOLEAN      NOT NULL DEFAULT false,
    criado_em            TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_usuario_empresa_supabase_user_id
    ON usuario_empresa (supabase_user_id);
