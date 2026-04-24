-- Migration 007: Expand cnae column from VARCHAR(7) to VARCHAR(10)
-- Allows formatted CNAE values like "4744-0/01" (9 chars)
ALTER TABLE empresas ALTER COLUMN cnae TYPE VARCHAR(10);
