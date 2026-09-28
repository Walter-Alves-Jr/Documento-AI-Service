# Implantação — Document Validation Service v1

## Princípios de implantação

O DVS processa documentos e pode receber dados pessoais sensíveis em ASO. Produção requer TLS, secret manager, persistência governada, restrição de rede, observabilidade e política de retenção. O `Dockerfile` atual fornece .NET 8, Tesseract e Poppler; ele não substitui esses controles de infraestrutura.

## Variáveis de ambiente obrigatórias

Nunca grave chaves no repositório, `appsettings.json`, interface web ou extensão de navegador.

```text
DocumentValidation__Security__ApiKeys__0__Id=consumer-prod
DocumentValidation__Security__ApiKeys__0__Key=<segredo-de-alta-entropia>
DocumentValidation__Security__ApiKeys__0__Role=validator

DocumentValidation__Security__ApiKeys__1__Id=platform-admin
DocumentValidation__Security__ApiKeys__1__Key=<segredo-administrativo-diferente>
DocumentValidation__Security__ApiKeys__1__Role=admin

DocumentValidation__Cors__AllowedOrigins__0=https://app.exemplo.com
DocumentValidation__RateLimit__PermitLimit=30
DocumentValidation__RateLimit__WindowSeconds=60
DocumentValidation__Audit__RetentionDays=30
```

Use uma chave por consumidor e faça rotação. A chave `admin` deve ser exclusiva de automações/operadores de plataforma e não deve ser distribuída a consumidores.

## Docker

```bash
docker build -t document-validation-service:latest .
docker run --rm -p 8080:8080 \
  -e DocumentValidation__Security__ApiKeys__0__Id=local-admin \
  -e DocumentValidation__Security__ApiKeys__0__Key="$DVS_API_KEY" \
  -e DocumentValidation__Security__ApiKeys__0__Role=admin \
  -e DocumentValidation__Cors__AllowedOrigins__0=https://app.exemplo.com \
  document-validation-service:latest
```

Verifique:

```bash
curl -fsS http://localhost:8080/api/validation/health
```

## Railway / serviço de containers

1. Conecte o repositório e use o `Dockerfile` da raiz.
2. Defina os secrets acima no painel do provedor; não use valores de exemplo.
3. Mantenha o health check em `/api/validation/health`.
4. Use domínio HTTPS próprio e configure somente as origens necessárias em CORS.
5. Posicione a API atrás de gateway/WAF quando houver exposição externa.
6. Restrinja o endpoint administrativo à rede de operação ou gateway de administração, além do papel `admin`.

> O filesystem do container pode ser efêmero. Nesta primeira etapa, o catálogo configurável é JSON e alterações administrativas precisam de volume persistente com backup ou, preferencialmente, de uma implementação de banco de dados antes de produção distribuída.

## Persistência e retenção

A implementação atual de auditoria e idempotência é em memória. Antes de múltiplas réplicas, restart seguro, busca histórica ou SLA de auditoria, substitua-a por serviços persistentes com:

- criptografia em repouso;
- controle de acesso por tenant/cliente;
- TTL/retention configurável e exclusão verificável;
- backup e testes de restauração;
- trilha de alterações de políticas;
- observabilidade sem dados pessoais.

Não persista arquivo ou OCR completo por padrão. Caso se torne necessário armazenar documentos, trate-o como funcionalidade explícita com justificativa, prazo, acesso e descarte definidos.

## Checklist de go-live

- [ ] API keys em secret manager e rotação definida.
- [ ] TLS fim a fim e cabeçalhos/proxy confiáveis configurados.
- [ ] CORS em allowlist, sem curingas.
- [ ] Rate limiting e limites de upload testados.
- [ ] Catálogo e auditoria em armazenamento persistente governado.
- [ ] Política de retenção, privacidade e atendimento a titulares definida.
- [ ] Contratos com operadores/suboperadores e plano de incidente definidos.
- [ ] Testes de regressão com documentos autorizados concluídos.
- [ ] Fluxo de revisão humana implantado para `MANUAL_REVIEW`.
- [ ] Monitoramento de erros, status, duração, 401/403/429 e revisões manuais ativo.

## Rollback

Versione imagem/container, catálogo e políticas. Se uma política nova causar regressão, reative a versão anterior no catálogo e faça rollback da imagem. Não edite uma política usada em decisões sem criar uma nova versão e registrar a mudança.
