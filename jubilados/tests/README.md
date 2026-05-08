# Tests

Esta pasta fixa o comportamento atual dos contratos mais sensiveis do projeto.

- `Jubilados.UnitTests`: validacao e persistencia dos cadastros de produto, destinatario e fornecedor.
- `Jubilados.IntegrationTests`: contratos SOAP atuais, para evitar drift estrutural nas integracoes fiscais.
- `Jubilados.EndToEndTests`: smoke tests do host HTTP e das novas abas de cadastro.

Observacao: nao foram encontrados endpoints de webhook no codigo atual. A protecao de regressao cobre os contratos HTTP expostos pela UI e os envelopes SOAP que hoje estao em producao no projeto.

## Pre-producao: emissao automatizada ate cStat 100

Antes de publicar em producao, execute o smoke test abaixo para validar emissao NF-e em homologacao com retry automatico:

```powershell
pwsh ./tests/emitir-nfe-preprod.ps1 -ApiBase https://resolutoo.com -MaxTentativas 8 -IntervaloSegundos 8
```

Comportamento esperado:
- Sai com `exit code 0` apenas quando retorna `cStat=100`.
- Sai com `exit code 1` se nao atingir `cStat=100` dentro do limite de tentativas.