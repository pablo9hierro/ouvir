# Bootstrap no Supabase (projeto migkkrwzykpztrakbfij)

Execute no SQL Editor do Supabase, em ordem:

1. 001_create_jubilados_tables.sql
2. 002_add_nfce_csc_to_empresa.sql
3. 003_add_modelo_nfce.sql
4. 004_auth_usuario_empresa.sql
5. 005_empresa_dados_fiscais.sql
6. 006_fix_modelo_notnull.sql
7. 007_expand_cnae_column.sql
8. 008_auth_local_password_reset.sql
9. 009_create_fornecedores.sql
10. 010_seed_orlando_tenant.sql

Observações:
- As migrations 008-010 foram adicionadas para alinhar o SQL com o startup atual da API.
- A seed Orlando é idempotente (usa ON CONFLICT DO NOTHING).
- Após rodar as migrations no projeto novo, a API pode apontar para o pooler de produção com as variáveis de ambiente definidas.
