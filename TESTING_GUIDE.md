# Guia de Testes — Document Validation Service v1

A referência de contrato e arquitetura é [DOCUMENT_VALIDATION_SERVICE.md](DOCUMENT_VALIDATION_SERVICE.md). Este guia usa somente documentos sintéticos/autorizados e não deve receber arquivos reais em repositório público.

## Pré-requisitos

```bash
sudo apt-get install -y tesseract-ocr tesseract-ocr-por tesseract-ocr-eng poppler-utils libgdiplus
export DocumentValidation__Security__ApiKeys__0__Id=local-admin
export DocumentValidation__Security__ApiKeys__0__Key='chave-local-de-teste'
export DocumentValidation__Security__ApiKeys__0__Role=admin
```

Inicie a API:

```bash
cd DocumentAIService
dotnet run --urls http://0.0.0.0:5000
```

## Testes automatizados

```bash
dotnet test DocumentAIService.Tests/DocumentAIService.Tests.csproj
```

Cobertura atual:

| Área | Casos |
|---|---|
| Rules Engine | Aprovação, documento vencido, campo ausente/manual review, baixa confiança, mínimo SEST SENAT. |
| Segurança de arquivo | PDF reconhecido por assinatura; tipo desconhecido rejeitado. |
| Privacidade | Nome/documento mascarados; campo redigido omitido. |
| Extensibilidade | CIPP extraído por padrões configurados sem controller novo. |
| Resiliência | Chave idempotente separada por cliente. |
| Compliance persistido | Reuso por SHA-256, alteração de arquivo, estado por entidade, renovação elegível e inconsistência de placa. |

## Smoke tests HTTP

```bash
curl -sS http://localhost:5000/api/validation/health
```

### Autenticação obrigatória

```bash
curl -i -X POST http://localhost:5000/api/v1/validation \
  -H 'content-type: application/json' \
  -d '{"documentType":"CNH","policy":"CNH_DEFAULT","file":"JVBERg=="}'
```

**Esperado:** `401 Unauthorized`.

### Assinatura inválida

```bash
curl -sS -X POST http://localhost:5000/api/v1/validation \
  -H 'content-type: application/json' \
  -H "x-api-key: $DOCUMENT_VALIDATION_API_KEY" \
  -d '{"documentType":"CNH","policy":"CNH_DEFAULT","file":"aW52YWxpZA=="}'
```

**Esperado:** `400` com código `UNSUPPORTED_FILE_TYPE`.

### Política CNH

```bash
BASE64=$(base64 -w0 cnh-sintetica.png)
curl -sS -X POST http://localhost:5000/api/v1/validation \
  -H 'content-type: application/json' \
  -H "x-api-key: $DOCUMENT_VALIDATION_API_KEY" \
  -H 'x-correlation-id: regression-cnh-001' \
  -d "{\"documentType\":\"CNH\",\"policy\":\"CNH_DEFAULT\",\"idempotencyKey\":\"cnh-sintetica-001\",\"file\":\"$BASE64\"}"
```

**Esperado:** campos e regras sem OCR bruto, número de documento mascarado e `status` coerente com a evidência sintética.

### Idempotência

Repita a chamada anterior com a mesma `idempotencyKey` e a mesma chave cliente. **Esperado:** mesmo `validationId`, sem novo processamento enquanto o armazenamento de idempotência mantiver o item.

### Administração

```bash
curl -sS http://localhost:5000/api/v1/admin/catalog/policies \
  -H "x-api-key: $DOCUMENT_VALIDATION_ADMIN_KEY"
```

**Esperado:** `200` apenas para chave com papel `admin`; `403` para chave validator.

### Compliance persistido

Com PostgreSQL disponível, envie uma requisição para `POST /api/v1/validations` com `entityType`, `entityId`, `documentType`, política e arquivo. Repita com o mesmo arquivo: **esperado:** `cacheUsed: true` e mesmo `validationId`. Altere qualquer byte do arquivo: **esperado:** novo processamento e novo documento lógico.

Consulte `GET /api/v1/compliance/{entityType}/{entityId}`: **esperado:** resposta de banco sem OCR ou chamada externa. Consulte [COMPLIANCE_PERSISTENCE.md](COMPLIANCE_PERSISTENCE.md) para exemplos completos.

## Regressão funcional manual

Antes de liberar uma mudança de extrator, teste o conjunto autorizado de documentos de calibração fora do repositório e registre somente metadados/anotações anonimizadas:

| Documento | Cenário | Resultado esperado |
|---|---|---|
| CNH válida | Nome e validade legíveis | `APPROVED` ou revisão por baixa evidência. |
| CNH vencida | Data passada | `REJECTED`. |
| ASO APTO vigente | Resultado e validade legíveis | `APPROVED`. |
| ASO INAPTO | Resultado incompatível | `REJECTED`. |
| ASO incompleto | Data/resultado ausente | `MANUAL_REVIEW`. |
| Direção Defensiva SEST SENAT 4 h | Escola homologada + 4 h | `APPROVED` quando demais campos válidos. |
| Direção Defensiva outra homologada 4 h | Mínimo padrão 8 h | `REJECTED`. |
| Instituição não homologada | Emissor fora da política | `REJECTED`. |
| Tipo configurável (CIPP) | Placa igual ao contexto e validade futura | `APPROVED`. |

## Checklist de segurança

- [ ] Nenhuma chave API está em arquivos, logs, browser extension ou repositório.
- [ ] CORS permite somente origens necessárias.
- [ ] Endpoint v1 e administração retornam `401/403` conforme esperado.
- [ ] Tipos fora de PDF/JPEG/PNG são recusados mesmo que tenham extensão enganosa.
- [ ] Resposta v1 não contém Base64, OCR bruto, CPF ou CNH completos.
- [ ] Logs de aplicação não contêm documento, OCR ou identificadores pessoais.
- [ ] Fluxos `MANUAL_REVIEW` chegam à revisão humana e não são tratados como reprovação automática.
