# Document Validation Service

API .NET 8 para **extração documental via OCR local** e **validação por políticas versionadas**. A plataforma preserva as validações atuais de CNH, ASO e Direção Defensiva e introduz uma arquitetura extensível para CIPP, CIV, MOPP, certificados e futuros documentos logísticos.

> O DVS avalia um documento contra uma política. O sistema consumidor — Vapora, YMS, TMS, portal ou outro — continua responsável pela decisão operacional de acesso, agenda, carga ou credenciamento.

## O que mudou na v1

- API `POST /api/v1/validation` com `documentType`, `policy`, `context`, arquivo Base64 e `idempotencyKey` opcional.
- Separação entre **OCR/extração**, **Rules Engine** e **política de validação**.
- Catálogo versionado de tipos e políticas em JSON, protegido por endpoints administrativos.
- Migração por compatibilidade de CNH, ASO e Direção Defensiva para `CNH_DEFAULT`, `ASO_DEFAULT` e `DIRECAO_DEFENSIVA_DEFAULT`.
- Exemplo funcional de CIPP parametrizável sem novo controller.
- `score` das regras separado de `confidence` de OCR.
- Respostas minimizadas: não retornam OCR bruto, imagem, Base64, CPF completo nem número completo de documento.
- Autenticação por API key, papéis `validator`/`admin`, CORS restritivo, rate limiting, validação de assinatura de arquivo, correlation ID e auditoria mínima.

## Documentação

A referência completa de arquitetura, API, regras, configuração, LGPD, segurança, execução, deploy e integração está em [DOCUMENT_VALIDATION_SERVICE.md](DOCUMENT_VALIDATION_SERVICE.md).

O diagnóstico do protótipo anterior e o plano de migração incremental estão em [ARQUITETURA_E_PLANO_MIGRACAO.md](ARQUITETURA_E_PLANO_MIGRACAO.md).

## Início rápido

```bash
sudo apt-get update
sudo apt-get install -y tesseract-ocr tesseract-ocr-por tesseract-ocr-eng poppler-utils libgdiplus

export DocumentValidation__Security__ApiKeys__0__Id=local-validator
export DocumentValidation__Security__ApiKeys__0__Key='troque-por-uma-chave-local-longa'
export DocumentValidation__Security__ApiKeys__0__Role=admin
export DocumentValidation__Cors__AllowedOrigins__0=http://localhost:5000

cd DocumentAIService
dotnet restore
dotnet run --urls http://0.0.0.0:5000
```

```bash
BASE64=$(base64 -w0 documento.png)
curl -sS -X POST http://localhost:5000/api/v1/validation \
  -H 'Content-Type: application/json' \
  -H "X-API-Key: $DOCUMENT_VALIDATION_API_KEY" \
  -d "{\"documentType\":\"CNH\",\"policy\":\"CNH_DEFAULT\",\"file\":\"$BASE64\"}"
```

## Testes

```bash
dotnet test DocumentAIService.Tests/DocumentAIService.Tests.csproj
```

## Segurança e privacidade

O serviço pode processar dados pessoais e, no caso de ASO, dados pessoais sensíveis. Antes de produção, configure segredos em cofre apropriado, persistência governada, retenção, TLS, monitoramento, aviso de privacidade e processo de atendimento a titulares. Consulte o guia completo para os controles já implementados e pendências de produção.
