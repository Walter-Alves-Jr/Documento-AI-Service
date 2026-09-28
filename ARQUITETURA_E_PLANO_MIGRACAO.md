# Document Validation Service — Arquitetura Atual e Plano de Evolução

> **Status:** proposta executada incrementalmente nesta versão.  
> **Escopo:** transformar o protótipo de validação de CNH, ASO e Direção Defensiva em um serviço reutilizável, multi-documento e multi-política, sem acoplar o núcleo a Vapora, B3agro, Trizy, BRF ou a qualquer decisão operacional de cliente.

## 1. Diagnóstico da arquitetura atual

### Componentes encontrados

| Camada | Implementação atual | Responsabilidade atual |
|---|---|---|
| API | `ValidationController` | Recebe Base64, limita tamanho, converte PDF e chama análise. Também expõe configuração BRF. |
| Orquestração | `DocumentAnalysisService` | Mistura identificação de tipo, extração por regex/MRZ, regras BRF, score e decisão final. |
| OCR | `OcrService` | Tesseract local com PSM 3/6, rotação, escala, contraste e fallback em inglês. |
| PDF | `PdfConverterService` | Converte a primeira página de PDF em PNG com `pdftoppm`. |
| Configuração BRF | `DirecaoDefensivaConfigService` | Persiste escolas, cursos e cargas horárias em JSON local. |
| UI de teste | `wwwroot/index.html` | Faz upload, envia Base64 e mostra resposta completa. |

### Fluxo atual

```text
POST /api/validation
  -> valida Base64 e limite de tamanho
  -> PDF? converte primeira página em imagem
  -> OCR local (Tesseract)
  -> identifica CNH / ASO / Direção Defensiva
  -> extrai campos com regex/MRZ
  -> aplica regras específicas do tipo e BRF
  -> calcula uma única métrica chamada confiança
  -> APROVADO / REPROVADO / ANÁLISE MANUAL
```

### Pontos fortes preservados

- OCR Tesseract é local e já possui estratégias úteis para documentos rotacionados, CNH-e/MRZ e imagens pequenas.
- Conversão PDF e descarte de temporários já existem.
- As regras calibradas de CNH, ASO e Direção Defensiva constituem uma regressão funcional importante.
- A interface de teste local continua útil para calibração.

### Acoplamentos e riscos identificados

1. `DocumentAnalysisService` concentra **extração, regra e decisão**; isto dificulta reutilização para novos documentos e políticas.
2. A regra BRF fica parcialmente em código e parcialmente em arquivo local, sem versão de política.
3. A mesma métrica representa qualidade de OCR e validade de negócio, causando ambiguidade.
4. O endpoint legado devolve CPF/número, texto OCR e outros dados além do necessário.
5. CORS amplo, endpoints administrativos sem autenticação e erros detalhados não são adequados a produção.
6. A integração de navegador existente tem URL de ambiente temporário e não deve embutir credenciais de produção.
7. Não havia validação por assinatura do arquivo, rate limit, correlation ID, auditoria minimizada ou retenção definida.

## 2. Arquitetura de destino — primeira etapa

```text
Sistema consumidor (Vapora, YMS, TMS, portal ou integração servidor-a-servidor)
  -> API v1 autenticada
  -> validação segura do arquivo (tamanho e assinatura)
  -> OCR / pré-processamento existente
  -> adaptador de extração por tipo
  -> ExtractionResult normalizado
  -> Rules Engine genérico
  -> Validation Policy versionada
  -> resposta minimizada e explicável
  -> auditoria de metadados sem imagem, OCR ou identificadores pessoais
```

### Separação de responsabilidades

| Componente | Pergunta que responde | Não faz |
|---|---|---|
| OCR / pré-processamento | “Qual texto é legível?” | Aprovar documentos. |
| Adaptador de extração | “Quais campos existem e com qual confiança?” | Aplicar regra operacional do consumidor. |
| Rules Engine | “Os campos atendem a cada regra?” | Conhecer terminal, agenda ou fluxo do cliente. |
| Validation Policy | “Quais regras e versões se aplicam a este tipo?” | Executar OCR. |
| Consumidor | “O que fazer com o resultado?” | Delegar decisão documental ao front-end. |

### Compatibilidade incremental

O endpoint legado `POST /api/validation` permanece durante a transição. A API nova entra em `POST /api/v1/validation` e usa um **adaptador de compatibilidade** para aproveitar a extração já calibrada, enquanto a decisão final da v1 é produzida exclusivamente pelo Rules Engine e pela política solicitada.

Assim, não há reescrita do OCR nem remoção imediata das regras existentes. A próxima etapa poderá substituir o adaptador por extratores independentes sem mudar o contrato da API v1 nem as políticas.

## 3. Modelo de configuração

A primeira etapa usa catálogo JSON versionado, isolado por interfaces de repositório. Em produção, o arquivo deve estar em volume persistente ou ser substituído por uma implementação de banco de dados; o núcleo não depende de detalhes do armazenamento.

### DocumentType

```json
{
  "code": "CNH",
  "name": "Carteira Nacional de Habilitação",
  "active": true,
  "version": "1.0",
  "fields": [
    { "code": "holderName", "dataType": "string", "required": true, "sensitivity": "personal" },
    { "code": "expirationDate", "dataType": "date", "required": true, "sensitivity": "personal" }
  ]
}
```

### ValidationPolicy

```json
{
  "code": "CNH_DEFAULT",
  "documentType": "CNH",
  "version": "1.0",
  "minimumConfidence": 0.5,
  "rules": [
    { "code": "DOCUMENT_TYPE", "ruleType": "document_type", "severity": "reject" },
    { "code": "EXPIRATION", "ruleType": "date_not_expired", "field": "expirationDate", "severity": "reject", "unknownOutcome": "manual_review" }
  ]
}
```

As regras são declarativas. O motor inicial suporta: `document_type`, `exists`, `equals`, `in`, `contains_any`, `date_not_expired`, `number_gte`, `boolean_true`, `matches_context` e `minimum_by_reference`.

`minimum_by_reference` permite regras como Direção Defensiva: carga mínima 4 h para textos de SEST SENAT e 8 h como padrão, configurada em política — não em controller ou em regra específica de cliente.

## 4. Contrato v1

```json
POST /api/v1/validation
X-API-Key: <chave configurada no servidor>
X-Correlation-Id: <opcional>

{
  "documentType": "CNH",
  "policy": "CNH_DEFAULT",
  "context": {
    "driverId": "12345",
    "operationType": "INBOUND"
  },
  "file": "<base64>"
}
```

A resposta usa `APPROVED`, `REJECTED` ou `MANUAL_REVIEW`, separa `score` de `confidence`, informa tipo/política/versão/regras e retorna somente campos permitidos e minimizados. Não retorna imagem, Base64 ou OCR bruto.

## 5. Segurança e LGPD por desenho

- Chave de API obrigatória para a v1; papel `admin` separado para catálogo.
- CORS por allowlist de configuração, sem `AllowAnyOrigin`.
- Limite de corpo e arquivo, assinatura binária de PDF/JPEG/PNG e rejeição de tipos desconhecidos.
- Rate limiting por chave/IP, correlation ID e resposta de erro genérica.
- Logs e auditoria sem Base64, OCR, CPF, CNH, nome ou conteúdo de documento.
- Arquivos temporários com nomes aleatórios e limpeza em `finally`.
- Auditoria guarda somente metadados minimizados: id técnico, versões, status, score, confiança, tempo e correlation ID.
- Política de retenção e substituição do repositório de arquivo por banco/armazenamento gerenciado são requisitos de implantação, não uma suposição implícita.
- O serviço não usa documentos enviados para treinamento.

> ASO pode conter dados de saúde, classificados como dados pessoais sensíveis pela LGPD. Antes de produção, o controlador deve definir base legal, aviso de privacidade, retenção, processo de atendimento ao titular, contrato com operadores/suboperadores e fluxo de incidentes.

## 6. Plano de migração executado

1. Preservar OCR, conversão de PDF e endpoint legado.
2. Adicionar contratos, catálogo de tipos, políticas e motor de regras versionados.
3. Mapear CNH, ASO e Direção Defensiva para políticas equivalentes (`CNH_DEFAULT`, `ASO_DEFAULT`, `DIRECAO_DEFENSIVA_DEFAULT`).
4. Adicionar API v1, administração protegida e resposta com minimização de dados.
5. Aplicar autenticação por API key, CORS restritivo, rate limiting, correlation ID, inspeção de assinatura e auditoria mínima.
6. Criar testes unitários de regras e segurança de arquivo, além de testes de regressão do contrato legado quando houver conjunto de documentos autorizado para teste.
7. Documentar instalação, configuração, integração genérica e integração ilustrativa com Vapora.

## 7. Limites desta etapa

- O processamento é síncrono. O `validationId`, auditoria e contratos foram estruturados para permitir fila e `202 Accepted` futuramente.
- Catálogo JSON é adequado para desenvolvimento/piloto; produção concorrente requer banco ou serviço de configuração transacional.
- A v1 não verifica autenticidade junto a órgãos emissores. Ela avalia evidência extraída e regras configuradas.
- Configurar uma política não cria automaticamente um extrator de alto desempenho para documentos inéditos. Caso não haja adaptador/extrator, o resultado é `MANUAL_REVIEW`, sem aprovação indevida.
