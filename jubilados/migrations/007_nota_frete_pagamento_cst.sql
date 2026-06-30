-- Migration 007: Tipo de frete, forma de pagamento e CST/CSOSN por item

-- Campos de transporte e pagamento na nota fiscal
ALTER TABLE notas_fiscais ADD COLUMN IF NOT EXISTS modal_frete     VARCHAR(1) NOT NULL DEFAULT '9';
ALTER TABLE notas_fiscais ADD COLUMN IF NOT EXISTS forma_pagamento VARCHAR(2) NOT NULL DEFAULT '01';

-- CST/CSOSN efetivo registrado por item (o que foi usado na emissão)
ALTER TABLE nota_itens ADD COLUMN IF NOT EXISTS cst   VARCHAR(3);
ALTER TABLE nota_itens ADD COLUMN IF NOT EXISTS csosn VARCHAR(3);
