# Prompt para Geração de Copy da Landing Page — Resolutoo / Jubilados NF-e

## Contexto Geral

Você é um copywriter especialista em SaaS B2B para pequenas e médias empresas no Brasil.
Preciso que você gere os textos (copy) de uma landing page completa para um sistema chamado **Resolutoo** (marca comercial) ou **Jubilados NF-e** (nome técnico).

O sistema é uma **plataforma SaaS de gestão fiscal e emissão de documentos eletrônicos** voltada para estabelecimentos comerciais brasileiros: conveniências, lojas de material de construção, mercados, distribuidores, prestadores de serviço, etc.

---

## Identidade da Marca

- **Nome do produto:** Resolutoo
- **Tagline sugerida:** "Sua loja emite nota, recebe nota de entrada e gerencia documentos fiscais — tudo num só lugar."
- **Tom de voz:** Direto, confiante, sem juridiquês. Fala com o dono de loja, não com o contador.
- **Público-alvo primário:** Proprietários e gerentes de pequenos comércios (conveniência, construção, mercearia, distribuidora, prestadores de serviço autônomo/MEI).
- **Diferenciais:** Simples de usar (sem contador para as operações do dia a dia), integrado à SEFAZ, sem precisar de ERP caro.

---

## Funcionalidades do Sistema (use para construir os blocos de serviços)

### 1. Emissão de NF-e (Nota Fiscal Eletrônica — modelo 55)
- Emite NF-e diretamente na SEFAZ com certificado digital A1
- Fluxo guiado em 4 passos: Empresa → Destinatário → Produto → Emitir
- Suporte a múltiplos itens por nota, frete, desconto, informações complementares
- Destinatário CPF, CNPJ ou Consumidor Anônimo
- Contingência automática SVC-AN quando SEFAZ principal está fora do ar
- Download do XML e DANFE em PDF

### 2. Emissão de NFC-e (Cupom Fiscal Eletrônico — modelo 65)
- Emite cupom fiscal para venda ao consumidor final no balcão
- Integração com CSC (Código de Segurança do Contribuinte)
- Geração do QR Code para o consumidor consultar
- Ideal para conveniências, mercadinhos e lojas de varejo

### 3. Consulta SEFAZ
- Verifica o status de qualquer NF-e pelo número de chave de acesso (44 dígitos)
- Confirma se a nota está autorizada, cancelada ou rejeitada na base da SEFAZ

### 4. Cancelamento de NF-e
- Cancela notas autorizadas dentro do prazo legal (24h)
- Envia evento de cancelamento homologado pela SEFAZ

### 5. Inutilização de Numeração
- Inutiliza séries e números de NF-e que nunca foram emitidos
- Evita inconsistências na sequência de numeração

### 6. Manifestação do Destinatário
- Manifesta NF-e de entrada como:
  - Ciência da Operação
  - Confirmação da Operação
  - Desconhecimento
  - Operação Não Realizada
- Necessário para empresas que recebem mercadorias de fornecedores com NF-e

### 7. Notas de Entrada (DF-e / NSU)
- Consulta e importa NF-e de entrada emitidas contra o CNPJ da empresa
- Importação de XML de NF-e de terceiros para registro no sistema

### 8. Nuvem Fiscal / Histórico de Notas
- Painel com todas as NF-e emitidas pela empresa
- Filtragem por status (Autorizada, Cancelada, Rejeitada)
- Download individual do XML ou DANFE de qualquer nota
- Cancelamento de notas diretamente no histórico

### 9. Carta de Correção Eletrônica (CC-e)
- Envia CC-e para corrigir informações de NF-e já autorizadas (exceto dados que alteram valor ou destinatário)

### 10. Status do Serviço SEFAZ
- Consulta em tempo real se o serviço da SEFAZ está operacional
- Evita tentar emitir nota quando o sistema fiscal está fora do ar

### 11. NFS-e (Nota Fiscal de Serviço Eletrônica)
- Emite NFS-e via webservice municipal (padrão ABRASF 2.04)
- Para prestadores de serviço com inscrição municipal
- Cálculo automático de ISS
- Informação de tomador (CPF/CNPJ opcional)

### 12. Cadastro de Empresa, Produtos e Clientes
- Cadastro completo de dados fiscais da empresa (CNPJ, IE, endereço, regime tributário)
- Upload e gestão do certificado digital A1 (.pfx)
- Cadastro de produtos com NCM, CFOP, CST/CSOSN, unidade e preço
- Cadastro de clientes (PF e PJ) com histórico

---

## Estrutura de Seções que Preciso para a Landing Page

Gere copy para cada seção abaixo. Para cada seção, entregue:
- **Título principal** (headline)
- **Subtítulo** (máximo 2 linhas)
- **Corpo do texto** (parágrafos curtos, linguagem simples)
- **CTA (call to action)** quando aplicável

### Seções:

1. **Hero (topo da página)**
   - Título impactante que resume o produto em 1 frase
   - Subtítulo explicando o problema que resolve
   - CTA principal: "Começar Grátis" ou similar
   - CTA secundário: "Ver Demonstração"

2. **Barra de prova social** (logos ou números)
   - Ex: "Mais de X empresas emitem nota com o Resolutoo" (use números fictícios razoáveis se necessário)
   - Segmentos atendidos: conveniências, materiais de construção, mercadinhos, prestadores de serviço

3. **Bloco "O problema que resolvemos"**
   - Dor do dono de loja sem sistema fiscal integrado
   - 3 a 4 bullets descrevendo a dor

4. **Bloco de Funcionalidades / Serviços** (6 cards)
   - Selecione as 6 funcionalidades mais relevantes para o público-alvo
   - Cada card: ícone sugerido + título + 1 frase de descrição

5. **Seção "Como funciona" (3 passos)**
   - Passo 1: Cadastre sua empresa e envie o certificado digital
   - Passo 2: Cadastre produtos e clientes
   - Passo 3: Emita notas, gerencie documentos de entrada e cancele ou corrija com 1 clique

6. **Bloco de segmentos atendidos**
   - Conveniência / Minimercado
   - Loja de Material de Construção
   - Distribuidora / Atacado
   - Prestador de Serviço / MEI

7. **Seção de preços / planos** (3 planos sugeridos)
   - Plano Básico: emissão de NF-e e NFC-e
   - Plano Profissional: + NFS-e, CC-e, manifestação do destinatário, notas de entrada
   - Plano Empresarial: múltiplas empresas, suporte prioritário
   - (use valores fictícios ou coloque "sob consulta")

8. **Bloco de depoimentos** (3 depoimentos fictícios mas realistas)
   - Dono de conveniência
   - Dono de loja de material de construção
   - Prestador de serviço autônomo

9. **FAQ (5 perguntas)**
   - Precisa de contador para usar?
   - O certificado digital precisa ser de qual tipo?
   - Funciona para Simples Nacional?
   - Os dados ficam seguros?
   - Tem suporte técnico?

10. **Rodapé / CTA Final**
    - Frase de encerramento motivacional
    - CTA: "Comece agora — é grátis por 30 dias"

---

## Restrições e Diretrizes

- **Não use jargões fiscais sem explicar**: se mencionar "NF-e", explique que é "nota fiscal eletrônica"
- **Linguagem inclusiva e direta**: fale "você" não "o empresário"
- **Foco no benefício, não na feature técnica**: ao invés de "emite XML assinado SHA-1", diga "sua nota chega na SEFAZ em segundos"
- **Urgência real**: emitir nota fiscal errada tem multa; nosso sistema valida antes de enviar
- **Não prometa o que o sistema não faz**: sem integração com maquininha de cartão, sem módulo de estoque avançado (ainda), sem geração de SPED ou obrigações acessórias tributárias
- **Mentions de compliance**: SEFAZ, Receita Federal, Simples Nacional — mostra credibilidade

---

## Entregável Esperado

Um documento com todos os textos organizados por seção, prontos para colar nos componentes do frontend da landing page. Inclua variações de headline quando pertinente (A/B test).
