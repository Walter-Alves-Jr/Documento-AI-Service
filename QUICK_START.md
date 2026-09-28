# Início rápido — Document Validation Service v1

## 1. Dependências

```bash
sudo apt-get update
sudo apt-get install -y tesseract-ocr tesseract-ocr-por tesseract-ocr-eng poppler-utils libgdiplus
```

## 2. Chave local de desenvolvimento

```bash
export DocumentValidation__Security__ApiKeys__0__Id=local-admin
export DocumentValidation__Security__ApiKeys__0__Key='troque-por-uma-chave-local-longa'
export DocumentValidation__Security__ApiKeys__0__Role=admin
export DocumentValidation__Cors__AllowedOrigins__0=http://localhost:5000
export DOCUMENT_VALIDATION_API_KEY="$DocumentValidation__Security__ApiKeys__0__Key"
```

## 3. Executar

```bash
cd DocumentAIService
dotnet restore
dotnet run --urls http://0.0.0.0:5000
```

- Health: `http://localhost:5000/api/validation/health`
- Painel de teste local: `http://localhost:5000/` — solicita a chave apenas na sessão do navegador.
- API recomendada: `POST /api/v1/validation`

## 4. Validar

```bash
BASE64=$(base64 -w0 documento.png)
curl -sS -X POST http://localhost:5000/api/v1/validation \
  -H 'content-type: application/json' \
  -H "x-api-key: $DOCUMENT_VALIDATION_API_KEY" \
  -d "{\"documentType\":\"CNH\",\"policy\":\"CNH_DEFAULT\",\"file\":\"$BASE64\"}"
```

A resposta tem `status` (`APPROVED`, `REJECTED` ou `MANUAL_REVIEW`), `score` de regras, `confidence` de OCR, política/versão, regras explicáveis e campos minimizados.

## 5. Testar

```bash
dotnet test ../DocumentAIService.Tests/DocumentAIService.Tests.csproj
```

Consulte [DOCUMENT_VALIDATION_SERVICE.md](DOCUMENT_VALIDATION_SERVICE.md) para catálogo, novas políticas, segurança, LGPD e deploy.
