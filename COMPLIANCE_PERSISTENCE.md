# Persistência de Compliance — Documento-AI-Service

## Decisão arquitetural

A persistência segue o padrão já adotado no ecossistema **Vapora Scheduling** e **Optivus Experience**:

| Aspecto | Implementação |
|---|---|
| Banco | PostgreSQL 17 |
| ORM | EF Core 8 + `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Migrations | EF Core, versionadas em `DocumentAIService/Persistence/Migrations` |
| Inicialização | `Database.MigrateAsync()` controlado por `Database:MigrateOnStartup` |
| Configuração | `ConnectionStrings:DocumentValidation` e variáveis de ambiente `ConnectionStrings__DocumentValidation` |
| Health | `GET /health`, incluindo `DbContextCheck` PostgreSQL |
| Logs | JSON console, correlation ID, sem Base64, OCR, arquivo ou dados pessoais em log |
| Renovação | `BackgroundService` opcional, desabilitado por padrão, sem fila nova |

SQLite **não** é utilizado. O serviço permanece em .NET 8 e não cria cliente ou chamada externa fictícia.

## Modelo persistido

### `entities`

Representa a entidade operacional dona de documentos:

| Campo | Descrição |
|---|---|
| `id` | UUID técnico. |
| `client_id` | Isolamento por consumidor autenticado. |
| `type` | `DRIVER`, `VEHICLE`, `EQUIPMENT` ou `CARRIER`. |
| `external_id` | Identificador externo do consumidor. |
| `cpf`, `plate` | Opcional; protegidos por Data Protection antes de persistir. |
| `created_at`, `updated_at` | Auditoria técnica. |

A unicidade é `(client_id, type, external_id)`.

### `documents`

Mantém metadados de um documento e o estado atual de conformidade.

| Campo | Descrição |
|---|---|
| `entity_id`, `document_type` | Associação e tipo catalogado. |
| `file_hash` | SHA-256 do arquivo original; não é guardado o arquivo. |
| `file_name`, `mime_type` | Metadados normalizados. |
| `document_number` | Opcional e protegido. |
| `issued_at`, `document_expires_at` | Validade do documento, independente de fonte externa. |
| `status` | `APPROVED`, `REJECTED` ou `PENDING_VALIDATION`. |
| `last_validated_at` | Última avaliação interna concluída. |
| `last_external_validated_at` | Última validação externa aprovada, quando existir adaptador oficial. |
| `next_validation_at` | Próximo vencimento de validade externa. |
| `extracted_data_snapshot` | Snapshot **minimizado e cifrado** da resposta, usado para cache e consistência. |

O índice `(entity_id, document_type, file_hash)` permite localizar reuso por hash. O índice `(status, next_validation_at)` permite localizar somente documentos elegíveis à renovação.

### `document_validations`

Histórico de cada processamento que efetivamente ocorreu:

- identificador público `validation_id`;
- política/`validation_type`, status, score e confidence;
- hash da requisição, hash da resposta e snapshot minimizado cifrado;
- tempo de processamento, `cache_used`, `external_call`, provider e custo estimado;
- cliente, `idempotency_key` e correlation ID.

Há unicidade por `(client_id, idempotency_key)` e `(client_id, validation_id)`. A mesma chave de idempotência só pode retornar o resultado anterior quando o hash canônico da requisição — entidade, tipo, política, contexto e arquivo — também for idêntico; reutilização para payload diferente é recusada.

### `external_validations`

Audita uma tentativa/estado de fonte externa, quando aplicável:

- `provider`, hash da requisição, status, data, TTL, duração e custo;
- resposta opcional cifrada/minimizada para futuro adaptador;
- códigos como `EXTERNAL_VALIDATION_NOT_AVAILABLE` e `EXTERNAL_PROVIDER_UNAVAILABLE`.

Nesta versão não existe cliente ANTT, Infosimples ou qualquer integração simulada. Quando não houver adaptador oficial/contratado, o status registrado é `NOT_AVAILABLE` e a validação interna continua.

### `validation_events`

Trilha de mudanças de status, incluindo validação concluída e vencimento de TTL externo.

### `external_provider_configurations`

API administrativa persistida para tela futura. Guarda somente configuração não secreta:

- código, tipo de documento, endpoint, método, modo de autenticação;
- headers e parâmetros não sensíveis;
- referência de segredo (`secretReference`), nunca token/senha/chave;
- timeout, TTL, custo estimado e estado ativo.

Headers e parâmetros com identificadores de credencial (`Authorization`, `token`, `api-key`, `secret`, `password`, `credential` ou `key`) são recusados. Credenciais devem permanecer em secret manager/variáveis de ambiente e ser apontadas por `secretReference`.

## Fluxo de validação persistida

```text
POST /api/v1/validations
  -> autenticação, rate limit, Base64, tamanho e assinatura
  -> entidade por client + type + externalId
  -> SHA-256
  -> reuso se entity + documentType + hash iguais e documento/TTL ainda válidos
  -> OCR + extração + Rules Engine somente quando não houver reuso
  -> validade documental
  -> registro external NOT_AVAILABLE ou UNAVAILABLE (sem chamada fictícia)
  -> validação cruzada de placa, quando houver evidência
  -> documento, histórico, evento e snapshot minimizado cifrado
  -> resposta com cacheUsed, externalCall, provider e estimatedCost
```

### Cache de conformidade

O reuso não é cache efêmero. Ele é o **estado persistido de compliance** e exige:

1. mesmo cliente e entidade;
2. mesmo tipo documental;
3. mesmo SHA-256 de arquivo;
4. documento ainda não vencido;
5. TTL externo ainda válido, quando houver.

Uma alteração de bytes gera novo hash e novo processamento. A consulta `GET /api/v1/compliance/...` nunca dispara OCR, regra ou chamada externa.

### Status

| Situação | Status persistido |
|---|---|
| Regras internas aprovadas e sem pendência externa | `APPROVED` |
| Regra interna de rejeição falha | `REJECTED` |
| Revisão manual, TTL expirado, fornecedor indisponível ou inconsistência configurada | `PENDING_VALIDATION` |

Inconsistência de placa entre documentos gera `CROSS_DOCUMENT_PLATE_MISMATCH`. O comportamento padrão é `PENDING_VALIDATION`; pode ser configurado para rejeição com `DocumentValidation:CrossValidation:InconsistencyOutcome=rejected`.

## Endpoints

### Validar e persistir

```http
POST /api/v1/validations
X-API-Key: <chave-validator>
Content-Type: application/json
```

```json
{
  "entityType": "VEHICLE",
  "entityId": "vehicle-123",
  "plate": "ABC-1D23",
  "documentType": "CIPP",
  "policy": "CIPP_DEFAULT",
  "idempotencyKey": "vapora-cipp-vehicle-123-v1",
  "context": {
    "operationType": "INBOUND"
  },
  "fileName": "cipp.pdf",
  "file": "<base64>"
}
```

Exemplo resumido de resposta:

```json
{
  "validationId": "VAL-20261002-ABC1234567",
  "status": "APPROVED",
  "complianceStatus": "APPROVED",
  "cacheUsed": false,
  "externalCall": false,
  "provider": null,
  "estimatedCost": 0,
  "documentExpiresAt": "2030-12-31T00:00:00+00:00",
  "nextValidationAt": null
}
```

### Consultar uma validação

```http
GET /api/v1/validations/{validationId}
X-API-Key: <mesma-chave-cliente>
```

### Consultar compliance rapidamente

```http
GET /api/v1/compliance/VEHICLE/vehicle-123
X-API-Key: <mesma-chave-cliente>
```

A resposta é somente leitura de PostgreSQL e apresenta documentos, status, validade, próxima validação e provider. Não reprocessa arquivo.

### Administrar provider futuro

```http
PUT /api/v1/admin/external-providers/ANTT/CIPP
X-API-Key: <chave-admin>
Content-Type: application/json
```

```json
{
  "code": "ANTT",
  "documentType": "CIPP",
  "endpoint": "https://api.exemplo.gov.br/cipp",
  "httpMethod": "GET",
  "authenticationType": "bearer",
  "secretReference": "ANTT_CIPP_TOKEN",
  "headers": { "Accept": "application/json" },
  "parameters": { "version": "v1" },
  "timeoutSeconds": 15,
  "ttlDays": 15,
  "estimatedCost": 0,
  "active": false
}
```

> Salvar configuração **não ativa uma chamada de rede**. Um adaptador oficial deve ser implementado, testado e registrado antes de tornar o provider ativo para produção.

## Migrations

Defina a string de conexão e execute:

```bash
export ConnectionStrings__DocumentValidation='Host=localhost;Port=5432;Database=document_validation;Username=document_validation;Password=...'
export DOTNET_ROOT="$HOME/.dotnet"
$HOME/.dotnet/tools/dotnet-ef database update \
  --project DocumentAIService/DocumentAIService.csproj \
  --startup-project DocumentAIService/DocumentAIService.csproj \
  --context DocumentValidationDbContext
```

Criar uma migration futura:

```bash
$HOME/.dotnet/tools/dotnet-ef migrations add NomeDaMudanca \
  --project DocumentAIService/DocumentAIService.csproj \
  --startup-project DocumentAIService/DocumentAIService.csproj \
  --output-dir Persistence/Migrations \
  --context DocumentValidationDbContext
```

Em produção, `Database:MigrateOnStartup=true` aplica migrations no startup, seguindo o padrão do ecossistema. Para change management estrito, aplique a migration no pipeline e defina essa chave como `false` nos pods/containers da aplicação.

## Docker local

```bash
cp .env.example .env
# Edite POSTGRES_PASSWORD e DVS_API_KEY.
docker compose up --build
```

- API: `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`
- Health PostgreSQL: `http://localhost:8080/health`
- PostgreSQL: porta local `5435`

## Renovação externa

`DocumentValidation:Renewal:Enabled` fica `false` por padrão. Quando ativado, `ExternalValidationRenewalWorker` consulta somente o índice de `next_validation_at` dentro de `LeadDays` e move os registros elegíveis para `PENDING_VALIDATION` com evento `EXTERNAL_VALIDATION_EXPIRED`.

Ele não consulta todos os veículos e não chama fonte externa sem adaptador oficialmente implementado. Em ambiente com múltiplas réplicas, execute-o como instância única/worker dedicado antes de habilitá-lo.

## Tipos catalogados

Além de CNH, ASO, Direção Defensiva e CIPP, o catálogo inicial inclui MOPP, CRLV, CIV, CTPP, RNTRC e `TRAINING_CERTIFICATE`. Esses tipos são configuráveis e devem ser calibrados/validados com documentos autorizados antes de ativação operacional.

## Segurança e privacidade

- Não são persistidos arquivo, Base64 ou OCR bruto.
- Hash SHA-256 identifica o arquivo sem armazenar seu conteúdo.
- Snapshots minimizados e campos pessoais persistidos são protegidos por Data Protection.
- Defina `DataProtection:KeysDirectory` em volume persistente ou use um repositório de chaves centralizado adequado ao ambiente. Fora de `Development`, a aplicação não inicia sem essa configuração, evitando cifragem efêmera inacessível após restart.
- `clientId` da API Key segmenta entidades, validações e consultas.
- As respostas continuam minimizadas/mascaradas; dados decifrados não são retornados pelo endpoint de compliance.
- ASO demanda governança LGPD adicional, incluindo base legal, retenção, revisão humana e controle de acesso.
