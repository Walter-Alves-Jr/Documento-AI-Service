# Guia de Desenvolvimento — Document Validation Service v1

## Estrutura

```text
DocumentAIService/
  Controllers/
    ValidationController.cs            # contrato legado, autenticado para transição
    V1/DocumentValidationV1Controller  # contrato recomendado
    V1/CatalogAdminController          # catálogo administrativo
  Services/
    OcrService.cs                      # Tesseract e pré-processamento
    PdfConverterService.cs             # primeira página de PDF
    DocumentAnalysisService.cs         # extratores legados calibrados
    V1/LegacyExtractionAdapter.cs      # normalização/extração configurável
    V1/RulesEngine.cs                  # regras declarativas
    V1/ValidationCatalogService.cs     # catálogo JSON/versionamento
    V1/DocumentValidationV1Service.cs  # orquestração
  Models/Catalog/                      # tipos, campos, políticas e regras
  Models/V1/                           # contratos v1
  Security/                            # API key, file inspection, middleware
  configuration/validation-catalog.json
```

## Princípios de alteração

1. Não misture extração com decisão de negócio do consumidor.
2. Não adicione regra de política em controller.
3. Primeiro altere o catálogo; crie código apenas quando a extração exigir técnica dedicada.
4. Crie uma versão nova de política quando critérios mudarem.
5. Não devolver OCR bruto nem dado pessoal completo pela API v1.
6. Não adicione documentos reais, Base64, chaves ou resultados identificáveis ao Git.

## Extração

CNH, ASO e Direção Defensiva seguem pelo `DocumentAnalysisService` por compatibilidade e calibração. O adaptador v1 normaliza seus campos, mas ignora o status legado para tomar a decisão: a decisão v1 sempre vem de `RulesEngine` + `ValidationPolicyDefinition`.

Para tipo novo, `LegacyExtractionAdapter` usa `IdentificationPatterns` e `ExtractionPatterns` definidos no catálogo. O motor aplica timeout de regex e o catálogo recusa padrões inválidos/longos. A qualidade desse caminho deve ser calibrada antes de uso operacional.

## Políticas

`ValidationPolicyDefinition` possui `code`, `documentType`, `version`, `minimumConfidence`, estado e `rules`. Os rule types iniciais estão descritos em [DOCUMENT_VALIDATION_SERVICE.md](DOCUMENT_VALIDATION_SERVICE.md). O tipo `minimum_by_reference` implementa, por configuração, a regra de 4h para SEST SENAT e 8h padrão.

## Segurança no desenvolvimento

- Use API key por variável de ambiente, não por arquivo versionado.
- O painel local armazena a chave apenas em `sessionStorage`; é ferramenta de teste, não interface administrativa de produção.
- Erros são correlacionados por `X-Correlation-Id`.
- Configure explicitamente CORS no ambiente necessário.
- Use dados sintéticos em testes repetíveis.

## Testar alterações

```bash
dotnet build DocumentAIService/DocumentAIService.csproj
dotnet test DocumentAIService.Tests/DocumentAIService.Tests.csproj
```

Adicione teste de regra sempre que incluir rule type; adicione teste de extração para novos padrões/adaptadores; execute regressão com documentos autorizados fora do Git para CNH, ASO e Direção Defensiva.

## Limitações atuais

- Processamento síncrono e somente primeira página de PDF.
- Auditoria/idempotência em memória; produção requer persistência apropriada.
- Catálogo em JSON é estágio inicial; produção distribuída requer banco/controle de mudança.
- OCR não atesta autenticidade em fontes emissoras.
