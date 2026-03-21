# Guia de Testes - Document AI Service

## Visão Geral

Este guia fornece instruções detalhadas para testar o protótipo funcional do Document AI Service.

## Acesso à Aplicação

### URL Pública
```
https://5000-iomtdl8xard50urtz3x4o-2fab9d3c.us2.manus.computer
```

### URL Local
```
http://localhost:5000
```

## Testes da Interface Web

### Teste 1: Upload e Validação de Documento

**Objetivo**: Validar o fluxo completo de upload e análise

**Passos**:
1. Acessar http://localhost:5000
2. Selecionar "CNH - Carteira Nacional de Habilitação" no dropdown
3. Fazer upload de uma imagem de CNH válida
4. Clicar em "Validar Documento"
5. Verificar resultado

**Resultado Esperado**:
- Status: APROVADO (se documento válido)
- Confiança: ≥ 85%
- Dados extraídos: Nome, Validade, Número do documento
- Motivos: Lista de critérios atendidos

### Teste 2: Documento Expirado

**Objetivo**: Validar tratamento de documentos expirados

**Passos**:
1. Fazer upload de documento com data de validade expirada
2. Clicar em "Validar Documento"

**Resultado Esperado**:
- Status: ANÁLISE MANUAL ou REPROVADO
- Confiança: 50-84% ou < 50%
- Motivo: "✗ Documento expirado: DD/MM/YYYY"

### Teste 3: Imagem de Baixa Qualidade

**Objetivo**: Validar tratamento de imagens ruins

**Passos**:
1. Fazer upload de imagem borrada, pixelada ou com pouco contraste
2. Clicar em "Validar Documento"

**Resultado Esperado**:
- Status: ANÁLISE MANUAL
- Confiança: 50-84%
- Motivo: Qualidade da imagem baixa

### Teste 4: Arquivo Inválido

**Objetivo**: Validar rejeição de arquivos não suportados

**Passos**:
1. Tentar fazer upload de arquivo .txt ou .doc
2. Verificar mensagem de erro

**Resultado Esperado**:
- Erro: "Formatos suportados: JPG, PNG, PDF"

### Teste 5: Arquivo Muito Grande

**Objetivo**: Validar limite de tamanho

**Passos**:
1. Tentar fazer upload de arquivo > 5MB
2. Verificar mensagem de erro

**Resultado Esperado**:
- Erro: "Arquivo muito grande (máximo 5MB)"

## Testes da API REST

### Teste 1: Health Check

**Requisição**:
```bash
curl http://localhost:5000/api/validation/health
```

**Resposta Esperada** (200 OK):
```json
{
  "status": "OK",
  "timestamp": "2026-03-21T05:27:00.1742807Z"
}
```

### Teste 2: Validação com Documento Válido

**Requisição**:
```bash
BASE64=$(base64 -w 0 documento_valido.png)
curl -X POST http://localhost:5000/api/validation \
  -H "Content-Type: application/json" \
  -d "{
    \"tipoDocumento\": \"CNH\",
    \"base64Arquivo\": \"$BASE64\"
  }"
```

**Resposta Esperada** (200 OK):
```json
{
  "status": "APROVADO",
  "confianca": 98,
  "dadosExtraidos": {
    "nome": "JOÃO DA SILVA SANTOS",
    "validade": "15/05/2030",
    "numeroDocumento": "123.456.789-00",
    "textoExtraido": "..."
  },
  "motivos": [
    "✓ OCR realizado com sucesso",
    "✓ Nome encontrado: JOÃO DA SILVA SANTOS",
    "✓ Documento válido até: 15/05/2030",
    "✓ Número do documento: 123.456.789-00",
    "✓ Qualidade da imagem: 85.00 %",
    "✓ Documento aprovado automaticamente"
  ]
}
```

### Teste 3: Validação com Base64 Inválido

**Requisição**:
```bash
curl -X POST http://localhost:5000/api/validation \
  -H "Content-Type: application/json" \
  -d "{
    \"tipoDocumento\": \"CNH\",
    \"base64Arquivo\": \"INVALIDO!!!\"
  }"
```

**Resposta Esperada** (400 Bad Request):
```json
{
  "error": "base64Arquivo inválido"
}
```

### Teste 4: Validação com Parâmetros Faltando

**Requisição**:
```bash
curl -X POST http://localhost:5000/api/validation \
  -H "Content-Type: application/json" \
  -d "{
    \"tipoDocumento\": \"CNH\"
  }"
```

**Resposta Esperada** (400 Bad Request):
```json
{
  "error": "tipoDocumento e base64Arquivo são obrigatórios"
}
```

### Teste 5: Arquivo Muito Grande

**Requisição**:
```bash
# Criar arquivo > 5MB
dd if=/dev/zero bs=1M count=6 | base64 > large_file.txt

curl -X POST http://localhost:5000/api/validation \
  -H "Content-Type: application/json" \
  -d "{
    \"tipoDocumento\": \"CNH\",
    \"base64Arquivo\": \"$(cat large_file.txt)\"
  }"
```

**Resposta Esperada** (400 Bad Request):
```json
{
  "error": "Arquivo muito grande (máximo 5MB)"
}
```

## Testes de Performance

### Teste 1: Tempo de Resposta

**Objetivo**: Medir tempo de processamento

**Requisição**:
```bash
time curl -X POST http://localhost:5000/api/validation \
  -H "Content-Type: application/json" \
  -d "{
    \"tipoDocumento\": \"CNH\",
    \"base64Arquivo\": \"$(base64 -w 0 documento.png)\"
  }"
```

**Resultado Esperado**:
- Tempo total: 1-3 segundos
- Tempo de resposta: < 100ms após processamento

### Teste 2: Requisições Simultâneas

**Objetivo**: Validar comportamento sob carga

**Script**:
```bash
#!/bin/bash
BASE64=$(base64 -w 0 documento.png)

for i in {1..10}; do
  curl -X POST http://localhost:5000/api/validation \
    -H "Content-Type: application/json" \
    -d "{
      \"tipoDocumento\": \"CNH\",
      \"base64Arquivo\": \"$BASE64\"
    }" &
done

wait
```

**Resultado Esperado**:
- Todas as requisições completam com sucesso
- Sem erros de timeout
- Respostas consistentes

## Testes de Diferentes Tipos de Documento

### CNH (Carteira Nacional de Habilitação)

**Dados esperados**:
- Nome do titular
- CPF
- Categoria de habilitação
- Data de validade

**Teste**:
```bash
curl -X POST http://localhost:5000/api/validation \
  -H "Content-Type: application/json" \
  -d "{
    \"tipoDocumento\": \"CNH\",
    \"base64Arquivo\": \"$(base64 -w 0 cnh.png)\"
  }"
```

### RG (Registro Geral)

**Dados esperados**:
- Nome completo
- Número do RG
- Data de emissão
- Data de validade

**Teste**:
```bash
curl -X POST http://localhost:5000/api/validation \
  -H "Content-Type: application/json" \
  -d "{
    \"tipoDocumento\": \"RG\",
    \"base64Arquivo\": \"$(base64 -w 0 rg.png)\"
  }"
```

### CPF (Cadastro de Pessoa Física)

**Dados esperados**:
- Nome
- Número do CPF
- Data de nascimento

**Teste**:
```bash
curl -X POST http://localhost:5000/api/validation \
  -H "Content-Type: application/json" \
  -d "{
    \"tipoDocumento\": \"CPF\",
    \"base64Arquivo\": \"$(base64 -w 0 cpf.png)\"
  }"
```

## Testes de Casos Extremos

### Teste 1: Imagem Muito Pequena

**Objetivo**: Validar redimensionamento automático

**Passos**:
1. Criar imagem 100x100px
2. Fazer upload
3. Verificar se é redimensionada e processada

**Resultado Esperado**:
- Imagem é redimensionada para 200x200px
- Processamento continua normalmente

### Teste 2: Imagem Invertida

**Objetivo**: Validar robustez do OCR

**Passos**:
1. Inverter cores de documento válido
2. Fazer upload
3. Verificar resultado

**Resultado Esperado**:
- OCR tenta processar
- Resultado pode ser ANÁLISE MANUAL

### Teste 3: Documento Parcialmente Visível

**Objetivo**: Validar tratamento de documentos incompletos

**Passos**:
1. Fazer upload de documento cortado
2. Verificar resultado

**Resultado Esperado**:
- Status: ANÁLISE MANUAL
- Motivo: Dados incompletos

## Checklist de Testes

- [ ] Interface web carrega corretamente
- [ ] Upload de arquivo funciona
- [ ] Validação retorna resultado esperado
- [ ] Barra de confiança exibe corretamente
- [ ] Dados extraídos são precisos
- [ ] Motivos da decisão são claros
- [ ] API responde com status correto
- [ ] Erros são tratados adequadamente
- [ ] Requisições simultâneas funcionam
- [ ] Performance está dentro do esperado
- [ ] Diferentes tipos de documento funcionam
- [ ] Casos extremos são tratados
- [ ] Logs são gerados corretamente
- [ ] Health check funciona

## Relatório de Testes

### Template

```
Data: DD/MM/YYYY
Testador: [Nome]
Versão: 1.0.0

## Testes Executados

| Teste | Status | Observações |
|-------|--------|-------------|
| Health Check | ✓ PASSOU | Resposta em < 100ms |
| Upload de Documento | ✓ PASSOU | Arquivo de 2MB processado |
| Validação de CNH | ✓ PASSOU | Status APROVADO com 98% confiança |
| ... | ... | ... |

## Problemas Encontrados

- [ ] Nenhum problema encontrado
- [ ] Problemas encontrados:
  1. [Descrição do problema]
  2. [Descrição do problema]

## Recomendações

- [Recomendação 1]
- [Recomendação 2]

## Conclusão

[Resumo geral dos testes]
```

## Próximos Passos

1. Documentar resultados dos testes
2. Corrigir problemas encontrados
3. Realizar testes de carga mais intensivos
4. Implementar melhorias sugeridas
5. Preparar para produção

---

**Versão**: 1.0.0  
**Data**: Março 2026
