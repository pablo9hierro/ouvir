-- Migration 008: autenticação local e reset de senha

CREATE TABLE IF NOT EXISTS usuarios (
    id         UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    email      VARCHAR(255) NOT NULL UNIQUE,
    senha_hash TEXT         NOT NULL,
    criado_em  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS password_reset_tokens (
    id          UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    usuario_id  UUID         NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    token       VARCHAR(64)  NOT NULL UNIQUE,
    expires_at  TIMESTAMPTZ  NOT NULL,
    used_at     TIMESTAMPTZ  NULL,
    criado_em   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_prt_token ON password_reset_tokens (token);
