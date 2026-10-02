# Revisão técnica — commit `2ee6977`

**Repositório:** `Walter-Alves-Jr/Documento-AI-Service`  
**Escopo auditado:** persistência de compliance, PostgreSQL, endpoints v1, autenticação, worker, Docker e preparação para Orbit/B3.  
**Correções resultantes:** commit `4803daf` (`fix: reforçar idempotência e isolamento de compliance`).

> Esta revisão não criou provider externo, fila, stack alternativa ou funcionalidade de negócio nova. As alterações abaixo corrigem riscos de produção encontrados no commit auditado.

## CRÍTICO

### Serviço público Railway indisponível

A URL pública anteriormente registrada, `https://documento-ai-service-production.up.railway.app/health`, respondeu em **02/10/2026** com:

```text
HTTP 404
x-railway-fallback: true
{"status":"error","code":404,"message":"Application not found"}
```

Isso bloqueia integração real com Orbit/B3 enquanto não houver ambiente ativo. Não é possível corrigir apenas com código: é necessário reativar/provisionar o serviço, configurar PostgreSQL 17, variáveis de ambiente, volume para chaves de Data Protection e domínio/health check no provedor.

## ALTO — corrigidos

### 1. Idempotency-Key reutilizada para request diferente

**Achado no `2ee6977`:** a busca usava somente `(clientId, idempotencyKey)`. Uma chave usada com arquivo, entidade ou política diferente poderia retornar resultado anterior incorreto.

**Correção:** cada validação agora persiste `RequestHash`, derivado de entidade, tipo, política, CPF/placa, contexto ordenado e SHA-256 do arquivo. A mesma chave com hash diferente retorna `409 Conflict` e código `IDEMPOTENCY_KEY_REUSED`.

### 2. Corrida entre réplicas para mesmo documento

**Achado no `2ee6977`:** o índice de `(entityId, documentType, fileHash)` não era único, permitindo duas validações concorrentes criarem compliance duplicado.

**Correção:**

- índice agora é único;
- a persistência trata `DbUpdateException`, relê exclusivamente o resultado vencedor do mesmo tenant e o devolve como cache;
- migration incremental `HardenComplianceAudit` preserva o histórico da migration já publicada.

### 3. Worker repetia eventos para documentos já pendentes

**Achado no `2ee6977`:** o worker selecionava qualquer status exceto `REJECTED`; após marcar `PENDING_VALIDATION`, voltaria a marcar e criar eventos idênticos a cada ciclo.

**Correção:** ele seleciona apenas `APPROVED` dentro da janela `nextValidationAt`. Após a transição para pendente, o registro deixa de ser elegível.

### 4. Chaves de Data Protection efêmeras em Production

**Achado no `2ee6977`:** quando `DataProtection:KeysDirectory` fosse omitido, a aplicação poderia usar chaves efêmeras e tornar snapshots cifrados inacessíveis após reinício.

**Correção:** fora de `Development`, a aplicação falha no startup se não houver `DataProtection:KeysDirectory`. Docker Compose já monta volume para o diretório de chaves.

### 5. Identidade vazia de API Key

**Achado no `2ee6977`:** uma configuração inválida com `Id` vazio poderia produzir o mesmo `clientId` para credenciais mal configuradas.

**Correção:** a autenticação agora rejeita API keys ativas sem `Id` e sem valor de chave.

### 6. Credenciais em parâmetros de provider

**Achado no `2ee6977`:** só headers sensíveis eram recusados; parâmetros podiam receber `token`, `secret`, `password` ou similares e ser persistidos.

**Correção:** a validação cobre headers **e** parâmetros. Credenciais devem usar exclusivamente `secretReference` para um cofre/variável externa.

## MÉDIO

1. **Worker em múltiplas réplicas:** a correção elimina repetição de evento depois da mudança de estado, e a unicidade protege documento/idempotência. Ainda assim, a operação recomendada é uma única instância de worker ou um mecanismo de lease distribuído já padronizado pelo ecossistema antes de ativá-lo em réplicas múltiplas.
2. **Migrations no startup:** `Database:MigrateOnStartup=true` segue o padrão existente, mas em produção com múltiplas réplicas é preferível aplicar migrations no pipeline e iniciar a aplicação com `Database:MigrateOnStartup=false`.
3. **Provider futuro:** `ExternalValidationRecord.Response` está sem uso hoje. Quando um adaptador oficial for criado, qualquer conteúdo de resposta deverá ser minimizado e protegido pelo mesmo codec antes de ser persistido.
4. **Configuração administrativa:** o catálogo continua em JSON e a configuração de provider é global de administrador. A integração Orbit/B3 deve decidir formalmente se providers/políticas são globais ou exigem escopo por organização.
5. **Pré-flight da migration de hardening:** ambientes que já tenham dados precisam verificar duplicidades de `(EntityId, DocumentType, FileHash)` antes de aplicar a constraint única.

## BAIXO

- `AllowedHosts` está com `*`; restringir por hostname no ambiente público é recomendável.
- O Dockerfile ainda executa como usuário padrão do container; uma imagem futura pode executar como usuário não privilegiado.
- O Docker Compose foi revisado estruturalmente, mas não foi executado neste sandbox porque Docker não está instalado.

## OK

### Multi-tenancy

- `clientId` não existe no contrato público persistido.
- O controller obtém `clientId` exclusivamente do `ClaimTypes.NameIdentifier` emitido pela API key autenticada.
- Criação de `entities` persiste esse `clientId`.
- Busca de entidade, idempotência, `GET /api/v1/validations/{id}` e `GET /api/v1/compliance/{entityType}/{entityId}` aplicam filtro por `clientId`.
- Teste integrado confirmou: tenant A recebeu `200` para sua entidade e tenant B recebeu `404` para o mesmo `entityId`.

### Idempotência e cache

- Mesma chave + mesmo request retorna o mesmo `validationId` e `cacheUsed: true`.
- Mesma chave + request diferente retorna `409`.
- Mesmo hash é reutilizado apenas dentro da mesma entidade, tipo documental e tenant, respeitando validade documental e TTL externo.
- Mesmo hash em entidades diferentes gera novo documento/validação; não compartilha compliance.
- Duas requisições simultâneas sem chave de idempotência para o mesmo documento retornaram `200`; uma teve cache e ambas retornaram o mesmo documento/validation ID. O banco manteve **um** documento.

### Compliance e status

- `GET /api/v1/compliance/...` faz somente leituras `AsNoTracking` no PostgreSQL; não chama OCR, Rules Engine ou provider.
- `APPROVED` é preservado quando as regras internas aprovam e não existe pendência externa.
- `REJECTED` interno não é rebaixado para pendente por provider indisponível.
- `MANUAL_REVIEW` é convertido para `PENDING_VALIDATION`.
- Provider configurado sem adaptador permanece `PENDING_VALIDATION`, sem chamada de rede e com `UNAVAILABLE` auditado.
- TTL externo elegível é movido a `PENDING_VALIDATION` pelo worker.
- Divergência de placa CRLV/CIPP gera `CROSS_DOCUMENT_PLATE_MISMATCH` e fica pendente na configuração padrão; só rejeita se a configuração explicitamente determinar `rejected`.
- Alteração de bytes cria novo SHA-256 e novo processamento.

### Segurança e administração

- Não há API key, senha ou token versionado em `appsettings`, `.env.example` ou código.
- Campos pessoais persistidos e snapshots minimizados usam Data Protection.
- Logs revisados registram IDs técnicos, status, tipo, duração e correlation ID; não registram Base64, OCR bruto, arquivo ou segredo.
- Todos os endpoints administrativos (`/api/v1/admin/catalog`, `/api/v1/admin/external-providers` e configuração legada) usam política `Admin`.
- Endpoints de validação/compliance usam política `Validator` (aceita `validator` e `admin`).

### PostgreSQL e migrations

- PostgreSQL 17 é o padrão do Compose; EF Core 8 + Npgsql 8 são compatíveis com .NET 8.
- Migration base `InitialCompliancePersistence` foi preservada.
- Migration incremental [HardenComplianceAudit](DocumentAIService/Persistence/Migrations/20261002222456_HardenComplianceAudit.cs) adiciona `RequestHash`, FK de eventos para entidade/validação e unicidade de hash documental por entidade/tipo.
- Teste em PostgreSQL limpo aplicou primeiro a migration base e depois a incremental sem erro.
- FKs validadas: documento→entidade, validação→documento, validação externa→documento, evento→documento, evento→entidade e evento→validação.
- Índices validados: `clientId/type/externalId`, `entityId/documentType/fileHash`, `status/nextValidationAt`, `clientId/validationId`, `clientId/idempotencyKey` e `documentId/validatedAt`.

## Testes executados

| Verificação | Resultado |
|---|---|
| `dotnet build` | Sucesso, sem warnings. |
| `dotnet test` | **22 aprovados**, 0 falhas. |
| Modelo EF | Sem mudanças pendentes após migration. |
| Migration idempotente | Script gerado com as duas migrations. |
| PostgreSQL real | Migration inicial + hardening aplicadas com sucesso. |
| Constraints reais | Unicidade de entidade, documento/hash e idempotência confirmadas. |
| HTTP idempotência | `200`, `200` com cache, `409` para payload diferente. |
| HTTP multi-tenant | `200` para tenant proprietário e `404` para outro tenant. |
| Concorrência PostgreSQL | Duas requisições simultâneas retornaram um único documento/resultado vencedor. |
| Guarda Data Protection | Startup Production sem diretório persistente falhou como esperado. |

## Arquivos alterados pela correção

- `DocumentAIService/Services/V1/PersistedValidationService.cs`
- `DocumentAIService/Controllers/V1/PersistedValidationController.cs`
- `DocumentAIService/Security/ApiKeyAuthenticationHandler.cs`
- `DocumentAIService/Controllers/V1/ExternalProviderAdminController.cs`
- `DocumentAIService/Persistence/ComplianceEntities.cs`
- `DocumentAIService/Persistence/Configurations/ComplianceConfigurations.cs`
- `DocumentAIService/Persistence/Migrations/20261002222456_HardenComplianceAudit.cs`
- `DocumentAIService/Persistence/Migrations/20261002222456_HardenComplianceAudit.Designer.cs`
- `DocumentAIService/Persistence/Migrations/DocumentValidationDbContextModelSnapshot.cs`
- `DocumentAIService/Program.cs`
- `DocumentAIService.Tests/PersistedValidationServiceTests.cs`
- `COMPLIANCE_PERSISTENCE.md`, `README.md`, `QUICK_START.md` e `DOCUMENT_VALIDATION_SERVICE.md`

## Recomendação para Orbit/B3

A implementação está tecnicamente consistente no código para avançar ao desenho de integração, **mas não para operação pública enquanto o ambiente Railway permanecer indisponível**. Antes de integrar Orbit/B3, provisionar PostgreSQL 17 e Data Protection persistente, aplicar as migrations, configurar API keys/segredos no ambiente e restaurar um health check público saudável.
