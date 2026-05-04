-- Migration 010: seed Orlando (tenant, produto, cliente, fornecedor)
-- Idempotente: pode ser executada mais de uma vez.

INSERT INTO empresas (
    id, cnpj, razao_social, nome_fantasia, inscricao_estadual,
    logradouro, numero, bairro, municipio, uf, cep, telefone, email,
    criado_em, atualizado_em
)
VALUES (
    'dd104b57-010a-4458-8699-d63807e205d3',
    '21362844000152',
    'Orlando Lucindo de Pontes Neto Materiais de Construcao',
    'Azulzao da Construcao',
    '162421303',
    'R OTAVIANO D MONTEIRO P FILHO',
    '19',
    'FUNCIONARIOS',
    'JOAO PESSOA',
    'PB',
    '58078340',
    '83998347220',
    '',
    NOW(), NOW()
)
ON CONFLICT (cnpj) DO NOTHING;

INSERT INTO produtos (
    id, empresa_id, nome, ncm, cfop, cst, unidade, preco,
    aliquota_icms, aliquota_pis, aliquota_cofins, ativo, criado_em, atualizado_em
)
VALUES (
    '9ae15d08-81e9-4767-911e-8faf96ad2821',
    'dd104b57-010a-4458-8699-d63807e205d3',
    'Produto Teste Homologacao',
    '84713012',
    '5102',
    '00',
    'UN',
    100.00,
    0.00,
    0.65,
    3.00,
    TRUE,
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

INSERT INTO clientes (
    id, empresa_id, nome, cpf_cnpj, inscricao_estadual,
    logradouro, numero, bairro, municipio, uf, cep, criado_em, atualizado_em
)
VALUES (
    '0865f76e-bff7-48ef-8b02-f24b6a468e3d',
    'dd104b57-010a-4458-8699-d63807e205d3',
    'WURTH DO BRASIL PECAS DE FIXACAO LTDA',
    '43648971003170',
    '161585078',
    'ROD BR-230',
    'SN',
    'CRISTO REDENTOR',
    'JOAO PESSOA',
    'PB',
    '58071680',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

INSERT INTO fornecedores (
    id, empresa_id, nome, cpf_cnpj, inscricao_estadual,
    municipio, uf, pais, codigo_pais, criado_em, atualizado_em
)
VALUES (
    '4f9a7a8c-20d7-4dbf-88f7-14ce4afd0ff0',
    'dd104b57-010a-4458-8699-d63807e205d3',
    'Fornecedor Seed Orlando',
    '00000000000191',
    'ISENTO',
    'JOAO PESSOA',
    'PB',
    'Brasil',
    '1058',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;
