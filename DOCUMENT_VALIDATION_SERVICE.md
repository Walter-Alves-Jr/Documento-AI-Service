# Document Validation Service (DVS) — v1

O **Document Validation Service** é uma API genérica para extrair evidências de documentos e avaliá-las contra políticas versionadas. Ele não decide se um motorista entra em terminal, se um agendamento é liberado, nem se uma operação de carga pode ocorrer. Essas decisões permanecem no sistema consumidor.

> **Resultado do DVS:** “O documento solicitado foi identificado, os campos configurados foram extraídos, a política X versão Y foi avaliada e o resultado foi `APPROVED`, `REJECTED` ou `MANUAL_REVIEW`.”

## Sumário

- [Arquitetura](#arquitetura)
- [Fluxo da validação](#fluxo-da-validação)
- [Contrato HTTP](#contrato-http)
- [Tipos, campos, políticas e regras](#tipos-campos-políticas-e-regras)
- [Como cadastrar um novo tipo ou política](#como-cadastrar-um-novo-tipo-ou-política)
- [Segurança e LGPD](#segurança-e-lgpd)
- [Execução e implantação](#execução-e-implantação)
- [Integração ilustrativa com Vapora](#integração-ilustrativa-com-vapora)
- [Observabilidade, resiliência e limites](#observabilidade-resiliência-e-limites)
- [Testes](#testes)

## Arquitetura

```text
Consumidor (YMS, TMS, terminal, portal ou integração servidor-a-servidor)
   |
   | documentType + policy + context + arquivo + API key
   v
Document Validation API v1
   |
   +-- valida autenticação, rate limit, tamanho e assinatura do arquivo
   +-- PDF: converte a primeira página em imagem temporária
   +-- OCR local Tesseract e pré-processamento de imagem
   +-- adaptador de extração normalizada
   |     +-- CNH / ASO / Direção Defensiva: extratores calibrados legados
   |     +-- novos tipos: padrões do catálogo configurável
   +-- Rules Engine declarativo
   +-- política versionada
   +-- minimização de dados e resposta explicável
   +-- auditoria somente de metadados
   v
Resultado estruturado para o consumidor
```

### Componentes

| Componente | Responsabilidade |
|---|---|
| `OcrService` | OCR local Tesseract, PSM 3/6, rotações, escala, contraste e fallback em inglês. |
| `PdfConverterService` | Conversão temporária da primeira página de PDF para PNG via `pdftoppm`. |
| `LegacyExtractionAdapter` | Preserva a extração calibrada de CNH, ASO e Direção Defensiva; para demais tipos usa o esquema de padrões do catálogo. |
| `ValidationCatalogService` | Carrega e atualiza o catálogo versionado de tipos e políticas. A implementação inicial usa JSON com escrita atômica. |
| `RulesEngine` | Avalia regras declarativas; não contém decisões de domínio de consumidor. |
| `DocumentValidationV1Service` | Orquestra extração, regras, minimização, auditoria e idempotência. |
| `ResponseDataMinimizer` | Omite OCR bruto e aplica mascaramento configurável. |
| `ValidationAuditStore` | Mantém metadados técnicos, sem arquivo, Base64, OCR ou campos pessoais. |

### Compatibilidade

O endpoint anterior `POST /api/validation` foi preservado para transição, mas agora exige API key e valida assinatura do arquivo. O contrato recomendado é **`POST /api/v1/validation`**.

As políticas de equivalência existentes são:

| Tipo | Política | Versão | Regras principais |
|---|---|---:|---|
| CNH | `CNH_DEFAULT` | 1.0 | Tipo CNH, nome identificado, validade vigente. |
| ASO | `ASO_DEFAULT` | 1.0 | Tipo ASO, nome, resultado APTO, validade vigente. |
| Direção Defensiva | `DIRECAO_DEFENSIVA_DEFAULT` | 1.0 | Tipo, nome, instituição homologada, carga horária e validade. |
| CNH | `CNH_TRANSPORTE_PRODUTO_PERIGOSO` | 1.0 | Tipo, validade e categoria C/D/E quando a categoria for extraída. |
| CIPP | `CIPP_DEFAULT` | 1.0 | Exemplo configurável: tipo, validade e placa igual ao contexto. |

A regra migrada de Direção Defensiva mantém **4 h para SEST SENAT** e **8 h para os demais emissores**, declarada como `minimum_by_reference` na política.

## Fluxo da validação

1. O consumidor envia o arquivo e informa `documentType`, `policy` e `context` opcional.
2. A API autentica a chave, aplica rate limit e cria/propaga o `X-Correlation-Id`.
3. Base64 é decodificado; o serviço aceita somente PDF, JPEG ou PNG com assinatura binária válida, até o limite configurado.
4. PDF é convertido para imagem temporária. Os arquivos temporários recebem nomes aleatórios e são excluídos ao término normal do processamento.
5. Para CNH, ASO e Direção Defensiva, o adaptador reutiliza a extração calibrada. Para tipos novos, o OCR é aplicado e os padrões configurados identificam o tipo e extraem campos.
6. O Rules Engine avalia cada regra da política e calcula `score` com base nas regras, independentemente de `confidence` do OCR.
7. O serviço produz `APPROVED`, `REJECTED` ou `MANUAL_REVIEW`, registra uma auditoria mínima e retorna campos autorizados/mascarados.

### Decisão

| Condição | Resultado |
|---|---|
| Ao menos uma regra de rejeição falha | `REJECTED` |
| Não há reprovação, mas há campo crítico ausente, regra manual ou OCR abaixo do mínimo | `MANUAL_REVIEW` |
| Todas as regras aplicáveis passam e OCR atende ao mínimo | `APPROVED` |

`confidence` representa a qualidade da leitura/extracão (0 a 1); `score` é o percentual de regras aprovadas (0 a 100). Uma leitura boa não aprova documento vencido, e uma leitura ruim não transforma automaticamente o documento em inválido.

## Contrato HTTP

### Autenticação

A API exige chave em `X-API-Key`. Há dois papéis:

| Papel | Permissões |
|---|---|
| `validator` | Validar e consultar auditoria da própria chave. |
| `admin` | Tudo de `validator` e manutenção de catálogo/políticas. |

**Não exponha uma API key administrativa em navegador.** Para aplicações web em produção, use um backend-for-frontend ou um gateway do consumidor para chamar o DVS.

### Validar documento

```http
POST /api/v1/validation
X-API-Key: <chave-de-validação>
X-Correlation-Id: <opcional>
Content-Type: application/json
```

```json
{
  "documentType": "CNH",
  "policy": "CNH_DEFAULT",
  "idempotencyKey": "yms-upload-7b2c0d46",
  "context": {
    "driverId": "12345",
    "operationType": "INBOUND"
  },
  "file": "<arquivo-em-base64>"
}
```

`context` é um dicionário genérico de texto. O DVS não contém propriedades nem regras próprias do Vapora, Trizy ou qualquer sistema consumidor. Uma política pode referenciar uma chave de contexto, por exemplo `vehiclePlate`.

**Resposta reduzida:**

```json
{
  "validationId": "VAL-20260928-6EE900FA1A",
  "status": "APPROVED",
  "score": 100,
  "confidence": 0.85,
  "documentType": "CNH",
  "detectedDocumentType": "CNH",
  "policy": "CNH_DEFAULT",
  "policyVersion": "1.0",
  "rulesVersion": "CNH_DEFAULT:1.0",
  "manualReviewRequired": false,
  "validations": [
    { "rule": "DOCUMENT_TYPE", "status": "PASS", "code": "VALUE_MATCH", "message": "A regra foi atendida." },
    { "rule": "EXPIRATION", "status": "PASS", "code": "NOT_EXPIRED", "message": "O documento está vigente." }
  ],
  "warnings": [],
  "extractedFields": [
    { "field": "holderName", "value": "J*** D* S***", "confidence": 0.85, "masked": true },
    { "field": "documentNumber", "value": "***8909", "confidence": 0.85, "masked": true },
    { "field": "expirationDate", "value": "31/12/2030", "confidence": 0.85, "masked": false }
  ],
  "processingTimeMs": 549,
  "correlationId": "request-correlation-id"
}
```

A resposta **não contém** imagem, Base64, OCR bruto, CPF completo ou número completo de CNH. Campos podem ser `show`, `mask` ou `redact` na definição do tipo.

### Consultar resultado técnico

```http
GET /api/v1/validation/{validationId}
X-API-Key: <mesma-chave-cliente>
```

Devolve a auditoria mínima da validação para a mesma chave cliente. A implementação inicial é em memória e deve ser substituída por persistência transacional para uso distribuído/produção.

### Catálogo administrativo

```http
GET /api/v1/admin/catalog/document-types
GET /api/v1/admin/catalog/policies?documentType=CNH
PUT /api/v1/admin/catalog/document-types/{code}
PUT /api/v1/admin/catalog/policies/{code}
X-API-Key: <chave-administrativa>
```

Os endpoints administrativos são protegidos por papel `admin`.

### Respostas de erro

| Status | Significado |
|---:|---|
| 400 | Request inválido, Base64 malformado, política/tipo incompatível ou assinatura de arquivo não aceita. |
| 401 | Chave ausente ou inválida. |
| 403 | Chave autenticada sem autorização para o recurso. |
| 429 | Limite de chamadas excedido. |
| 500 | Falha técnica sem dados internos; use `correlationId` para investigação. |

## Tipos, campos, políticas e regras

O catálogo está em `DocumentAIService/configuration/validation-catalog.json`. Essa implementação é intencionalmente simples para a primeira etapa e está atrás de uma interface para futura troca por banco de dados.

### Definição de tipo

```json
{
  "code": "CIPP",
  "name": "Certificado de Inspeção",
  "active": true,
  "version": "1.0",
  "identificationPatterns": ["CERTIFICADO\\s+DE\\s+INSPECAO"],
  "fields": [
    {
      "code": "plate",
      "name": "Placa",
      "dataType": "string",
      "required": true,
      "sensitivity": "personal",
      "responseMode": "show",
      "extractionPatterns": ["PLACA\\s*[:#]?\\s*([A-Z]{3}[- ]?\\d[A-Z0-9]\\d{2})"]
    }
  ]
}
```

Padrões têm limite de tamanho e timeout de regex para reduzir risco de expressões custosas. Para um documento complexo, crie um adaptador/extrator especializado, mantendo o mesmo resultado normalizado; não mova regras de política para o controller.

### Regras disponíveis

| `ruleType` | Função |
|---|---|
| `document_type` | Confere o tipo identificado contra o esperado. |
| `exists` | Confere se o campo foi extraído. |
| `equals` | Compara o campo com valor esperado. |
| `in` | Confere se o campo está em uma lista permitida. |
| `contains_any` | Confere presença de valor permitido no campo. |
| `date_not_expired` | Reprova data de validade anterior a hoje. |
| `number_gte` | Confere um valor numérico mínimo. |
| `boolean_true` | Exige campo booleano verdadeiro. |
| `matches_context` | Compara campo extraído com uma chave de contexto. |
| `minimum_by_reference` | Aplica mínimo padrão ou mínimo diferenciado baseado em outro campo. |

`severity` define o efeito de uma falha: `reject`, `manual` ou `warning`. `unknownOutcome` define o efeito quando não existe evidência suficiente: `reject`, `manual_review` ou `warning`.

## Como cadastrar um novo tipo ou política

1. Cadastre o `DocumentType` com campos, sensibilidade, modo de resposta e marcadores de identificação/extração.
2. Cadastre uma `ValidationPolicy` apontando para o `documentType`, com versão e regras declarativas.
3. Envie um documento sintético/autorizado com a política e valide as regras antes de ativar o uso operacional.
4. Versione uma política ao mudar seus critérios. Não altere silenciosamente uma política já usada em decisões auditáveis.
5. Quando OCR genérico não for suficiente, implemente somente um novo adaptador de extração; preserve o Rules Engine, o contrato e a política.

Exemplo de política CIPP que usa contexto genérico:

```json
{
  "code": "CIPP_DEFAULT",
  "documentType": "CIPP",
  "version": "1.0",
  "active": true,
  "minimumConfidence": 0.5,
  "rules": [
    { "code": "DOCUMENT_TYPE", "ruleType": "document_type", "expectedValue": "CIPP", "severity": "reject", "unknownOutcome": "reject" },
    { "code": "EXPIRATION", "ruleType": "date_not_expired", "field": "expirationDate", "severity": "reject", "unknownOutcome": "manual_review" },
    { "code": "VEHICLE_PLATE", "ruleType": "matches_context", "field": "plate", "contextKey": "vehiclePlate", "severity": "manual", "unknownOutcome": "manual_review" }
  ]
}
```

Nenhum controller novo é necessário para esse cadastro.

## Segurança e LGPD

> **Avaliação operacional, não parecer jurídico.** ASO pode conter dado pessoal sensível de saúde. A entrada em produção depende de definição de controlador, base legal, aviso de privacidade, retenção, atendimento de titulares e contratos com operadores/suboperadores.

### Controles implementados

- Autenticação por API key e autorização por papel.
- CORS com allowlist configurável; não há `AllowAnyOrigin`.
- Rate limit configurável por chave/IP.
- Limite de corpo/arquivo e detecção por assinatura binária de PDF, JPEG e PNG.
- Validação Base64, erros genéricos e `X-Correlation-Id`.
- Endpoints administrativos protegidos.
- Logs sem Base64, imagem, OCR, CPF, CNH, nome ou outros campos de documento.
- Arquivos temporários de PDF/OCR removidos pelo fluxo de processamento.
- Resposta v1 minimizada, com mascaramento/omissão configurável.
- Auditoria sem arquivo e sem campos pessoais.
- Idempotência por cliente/chave, armazenando somente resposta minimizada.
- O serviço não envia documentos a modelos externos nem os utiliza para treinamento.

### Itens obrigatórios antes de produção

1. Usar chaves longas/rotacionáveis em secret manager, nunca em repositório, HTML, extensão ou cliente de navegador.
2. Trocar `InMemoryValidationAuditStore` e `InMemoryIdempotencyStore` por banco/Redis com criptografia, controle de acesso, retenção e exclusão verificável.
3. Armazenar catálogo administrativo em banco/configuração persistente com histórico e aprovação; filesystem do container é inadequado para governança de produção.
4. Colocar API atrás de TLS, WAF/gateway, monitoramento e política de incidentes.
5. Definir DPA/contrato com hospedagem e qualquer futuro fornecedor de IA externa como suboperador, quando aplicável.
6. Implantar aviso de privacidade, canal de direitos do titular e processo de revisão humana/contestação para `MANUAL_REVIEW` e decisões relevantes.
7. Fazer teste de segurança, teste de carga e RIPD/avaliação de impacto proporcional ao volume, ao ASO e à decisão automatizada.
8. Remover de documentação pública resultados associados a documentos reais; usar somente dados sintéticos ou anonimizados.

## Execução e implantação

### Pré-requisitos

- .NET SDK 8
- Tesseract com idiomas `por` e `eng`
- Poppler (`pdftoppm`) para PDF

Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y tesseract-ocr tesseract-ocr-por tesseract-ocr-eng poppler-utils libgdiplus
```

### Configurar desenvolvimento local

Defina uma chave **somente local**, fora do repositório:

```bash
export DocumentValidation__Security__ApiKeys__0__Id=local-validator
export DocumentValidation__Security__ApiKeys__0__Key='troque-por-uma-chave-local-longa'
export DocumentValidation__Security__ApiKeys__0__Role=admin
export DocumentValidation__Cors__AllowedOrigins__0=http://localhost:5000
```

Compile e inicie:

```bash
cd DocumentAIService
dotnet restore
dotnet build
dotnet run --urls http://0.0.0.0:5000
```

Verificação:

```bash
curl http://localhost:5000/api/validation/health
```

### cURL v1

```bash
BASE64=$(base64 -w0 documento.png)
curl -sS -X POST http://localhost:5000/api/v1/validation \
  -H 'Content-Type: application/json' \
  -H "X-API-Key: $DOCUMENT_VALIDATION_API_KEY" \
  -H 'X-Correlation-Id: yms-req-001' \
  -d "{\"documentType\":\"CNH\",\"policy\":\"CNH_DEFAULT\",\"idempotencyKey\":\"yms-doc-001\",\"file\":\"$BASE64\"}"
```

### Docker/Railway

O `Dockerfile` existente publica .NET 8 com Tesseract e Poppler. No ambiente de hospedagem, configure por secret/environment variable:

```text
DocumentValidation__Security__ApiKeys__0__Id=consumer-prod
DocumentValidation__Security__ApiKeys__0__Key=<segredo-gerenciado>
DocumentValidation__Security__ApiKeys__0__Role=validator
DocumentValidation__Cors__AllowedOrigins__0=https://app.exemplo.com
DocumentValidation__RateLimit__PermitLimit=30
DocumentValidation__RateLimit__WindowSeconds=60
DocumentValidation__Audit__RetentionDays=30
```

Para catálogo alterável em produção, use volume persistente com backup/controle de mudança ou, preferencialmente, implemente repositório de banco de dados. Em hosts com filesystem efêmero, alterações administrativas em JSON não sobrevivem a deploy/restart.

## Integração ilustrativa com Vapora

Vapora é somente um consumidor do DVS. A integração deve ocorrer no backend do Vapora ou por gateway confiável, não por chave embutida no front-end.

```text
Vapora envia documento + policy + contexto
  -> DVS retorna validationId e resultado documental
  -> Vapora grava somente os metadados necessários
  -> Vapora aplica sua própria regra operacional de elegibilidade
```

Exemplo de payload para operação de inflamáveis:

```json
{
  "documentType": "CIPP",
  "policy": "CIPP_DEFAULT",
  "idempotencyKey": "vapora-document-9fcb7e",
  "context": {
    "driverId": "driver-12345",
    "operationType": "INBOUND",
    "cargoType": "INFLAMMABLE",
    "riskClass": "3",
    "vehicleType": "TANQUE",
    "vehiclePlate": "ABC-1D23",
    "site": "TERMINAL_A"
  },
  "file": "<base64>"
}
```

Vapora pode tratar `MANUAL_REVIEW` como `PENDING_VALIDATION`; não deve convertê-lo automaticamente em rejeição. A eventual elegibilidade de agenda, portaria ou carga continua sendo decisão do Vapora.

## Observabilidade, resiliência e limites

- `validationId`: identifica tecnicamente uma validação.
- `X-Correlation-Id`: liga logs, erro e chamada do consumidor.
- Métricas disponíveis na resposta: `processingTimeMs`, status, score e confidence.
- Logs registram somente tipo, política, status, tempos, client ID técnico e correlation ID.
- `idempotencyKey`: evita reprocessamento acidental para mesma chave cliente durante a retenção em memória.
- O modelo é síncrono nesta etapa. O contrato e o `validationId` permitem evolução para fila, `202 Accepted` e `GET /api/v1/validation/{id}` sem mudar a semântica do consumidor.

Limites atuais:

- Até 10 MB de arquivo original.
- PDF: somente a primeira página é convertida/analisada.
- Autenticidade documental não é confirmada em bases governamentais; o serviço avalia a evidência OCR e as regras configuradas.
- Para tipos inteiramente novos, a qualidade depende dos padrões configurados ou de um adaptador de extração dedicado.

## Testes

```bash
dotnet test DocumentAIService.Tests/DocumentAIService.Tests.csproj
```

A suíte cobre:

- aprovação, reprovação e revisão manual do Rules Engine;
- data vencida, confiança baixa e carga horária diferenciada por referência;
- inspeção de assinatura de arquivo;
- mascaramento e omissão de dados;
- idempotência por cliente/chave;
- extração configurável de um tipo novo sem controller adicional.

Também foram executadas regressões HTTP com documentos sintéticos para CNH, ASO, Direção Defensiva e CIPP configurável.
