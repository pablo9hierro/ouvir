-- Migration 006: Corrige coluna modelo em notas_fiscais — garante NOT NULL com default '55'
-- A migration 003 usou IF NOT EXISTS e foi no-op para bancos que já tinham a coluna sem constraint.

-- 1. Preenche registros antigos que ficaram com modelo NULL
UPDATE notas_fiscais SET modelo = '55' WHERE modelo IS NULL;

-- 2. Garante que a coluna tem valor padrão
ALTER TABLE notas_fiscais ALTER COLUMN modelo SET DEFAULT '55';

-- 3. Aplica NOT NULL (seguro agora que não há NULLs)
ALTER TABLE notas_fiscais ALTER COLUMN modelo SET NOT NULL;
