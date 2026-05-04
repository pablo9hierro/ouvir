# Tests

Esta pasta fixa o comportamento atual dos contratos mais sensiveis do projeto.

- `Jubilados.UnitTests`: validacao e persistencia dos cadastros de produto, destinatario e fornecedor.
- `Jubilados.IntegrationTests`: contratos SOAP atuais, para evitar drift estrutural nas integracoes fiscais.
- `Jubilados.EndToEndTests`: smoke tests do host HTTP e das novas abas de cadastro.

Observacao: nao foram encontrados endpoints de webhook no codigo atual. A protecao de regressao cobre os contratos HTTP expostos pela UI e os envelopes SOAP que hoje estao em producao no projeto.