-- Origem da mercadoria (0-8, grupo ICMS obrigatorio no XML) -- confirmado
-- ausente na entidade Produto na auditoria da integracao com o Resolutoo.
ALTER TABLE produtos ADD COLUMN IF NOT EXISTS origem CHAR(1) NOT NULL DEFAULT '0';
COMMENT ON COLUMN produtos.origem IS 'Origem da mercadoria ICMS: 0=Nacional, 1-8=conforme tabela oficial (importado, etc)';
