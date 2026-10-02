# Início rápido — Document Validation Service v1

## 1. Dependências

```bash
sudo apt-get update
sudo apt-get install -y tesseract-ocr tesseract-ocr-por tesseract-ocr-eng poppler-utils libgdiplus
```

A API usa PostgreSQL para estado de compliance e migrations. SQLite não é utilizado.

## 2. Caminho recomendado: Docker Compose

```bash
cp .env.example .env
# Edite POSTGRES_PASSWORD e DVS_API_KEY com valores locais fortes.
docker compose up --build
```

- API: `http://localhost:8080`
- Painel local: `http://localhost:8080/`
- Swagger: `http://localhost:8080/swagger`
- Health PostgreSQL: `http://localhost:8080/health`

## 3. Executar a API fora do container

Inicie PostgreSQL 17 primeiro, por exemplo somente o serviço de banco do compose:

```bash
cp .env.example .env
docker compose up -d postgres
```

Em outro terminal:

```bash
export DocumentValidation__Security__ApiKeys__0__Id=local-admin
export DocumentValidation__Security__ApiKeys__0__Key='troque-por-uma-chave-local-longa'
export DocumentValidation__Security__ApiKeys__0__Role=admin
export DocumentValidation__Cors__AllowedOrigins__0=http://localhost:5000
export ConnectionStrings__DocumentValidation='Host=localhost;Port=5435;Database=document_validation;Username=document_validation;Password=<POSTGRES_PASSWORD>'
export DOCUMENT_VALIDATION_API_KEY="$DocumentValidation__Security__ApiKeys__0__Key"

cd DocumentAIService
dotnet restore
dotnet run --urls http://0.0.0.0:5000
```

## 4. Validar documento sem estado

```bash
BASE64=$(base64 -w0 documento.png)
curl -sS -X POST http://localhost:5000/api/v1/validation \
  -H 'content-type: application/json' \
  -H "x-api-key: $DOCUMENT_VALIDATION_API_KEY" \
  -d "{\"documentType\":\"CNH\",\"policy\":\"CNH_DEFAULT\",\"file\":\"$BASE64\"}"
```

## 5. Validar e persistir compliance

```bash
BASE64=$(base64 -w0 cipp.png)
curl -sS -X POST http://localhost:5000/api/v1/validations \
  -H 'content-type: application/json' \
  -H "x-api-key: $DOCUMENT_VALIDATION_API_KEY" \
  -d "{\"entityType\":\"VEHICLE\",\"entityId\":\"vehicle-123\",\"plate\":\"ABC-1D23\",\"documentType\":\"CIPP\",\"policy\":\"CIPP_DEFAULT\",\"idempotencyKey\":\"vehicle-123-cipp-1\",\"file\":\"$BASE64\"}"
```

Consulte posteriormente sem reprocessamento:

```bash
curl -sS http://localhost:5000/api/v1/compliance/VEHICLE/vehicle-123 \
  -H "x-api-key: $DOCUMENT_VALIDATION_API_KEY"
```

## 6. Testar

```bash
dotnet test ../DocumentAIService.Tests/DocumentAIService.Tests.csproj
```

Consulte [DOCUMENT_VALIDATION_SERVICE.md](DOCUMENT_VALIDATION_SERVICE.md) para a arquitetura v1 e [COMPLIANCE_PERSISTENCE.md](COMPLIANCE_PERSISTENCE.md) para PostgreSQL, migrations, cache, compliance e providers futuros.
