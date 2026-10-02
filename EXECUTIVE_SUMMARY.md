# Sumário Executivo — Document Validation Service

## Visão

O Document Validation Service evoluiu de um protótipo focado em CNH, ASO e Direção Defensiva para uma plataforma de validação documental **multi-tipo, multi-política e reutilizável** para operações logísticas.

A plataforma não determina se um motorista entra no terminal, se uma agenda é liberada ou se veículo pode carregar. Ela devolve um resultado documental explicável para o sistema consumidor aplicar sua regra operacional.

## Entrega atual

| Capacidade | Situação |
|---|---|
| OCR local Tesseract, PDF e pré-processamento | Preservado |
| CNH, ASO e Direção Defensiva | Migrados por políticas de compatibilidade |
| API genérica v1 | Implementada |
| Tipos/campos/políticas/regras versionados | Implementados em catálogo configurável |
| CIPP configurável sem novo controller | Validado com teste sintético |
| Score separado da confidence OCR | Implementado |
| Autenticação, papéis, CORS, rate limit e inspeção de arquivo | Implementados |
| Resposta minimizada e auditoria sem documento | Implementadas |
| Persistência de compliance PostgreSQL, migrations e administração de provider | Implementadas sem integração externa fictícia |

## Benefícios

- Reduz o acoplamento entre validação documental e sistemas consumidores.
- Permite diferentes políticas para o mesmo documento, por operação/cliente/site.
- Mantém rastreabilidade por `validationId`, versão de política, regras e correlation ID.
- Evita que OCR de baixa qualidade seja confundido com invalidade documental.
- Reduz exposição de dados por mascaramento e omissão de OCR/imagem na resposta v1.

## Próximas decisões de produto/infraestrutura

1. Definir controlador, bases legais, retenção, encarregado e fluxo de revisão humana.
2. Migrar o catálogo administrativo JSON para persistência governada com trilha de aprovação.
3. Implantar gateway, secret manager, TLS, WAF e monitoramento.
4. Estabelecer processo de aprovação/versionamento de políticas.
5. Calibrar novos documentos com conjunto autorizado e métricas de qualidade.
6. Implementar adaptadores de provedores oficiais/contratados e, só então, habilitar renovação externa em produção.

Consulte [DOCUMENT_VALIDATION_SERVICE.md](DOCUMENT_VALIDATION_SERVICE.md) para arquitetura, contrato e controles de produção.
