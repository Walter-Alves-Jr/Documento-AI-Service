# Document AI Service — Guia de Integração B3agro/Trizy

## Visão Geral

Este guia descreve como implantar o **Document AI Service** na tela de **Cadastro de Motorista** do sistema B3agro/Trizy, permitindo que os documentos enviados (CNH, ASO e Direção Defensiva) sejam analisados automaticamente e as datas de validade preenchidas sem intervenção manual.

---

## Como Funciona

Ao fazer o upload de um documento na aba **DOCUMENTOS**, o script intercepta o arquivo, envia para o Document AI Service via API REST, e:

1. **Extrai automaticamente** a data de validade do documento via OCR
2. **Preenche o campo de validade** correspondente na tela
3. **Exibe um painel de resultado** com status, confiança e dados extraídos
4. **Adiciona um badge colorido** no label do campo indicando o resultado

| Campo no Sistema | Tipo de Documento | Campo Preenchido |
|---|---|---|
| Cópia da CNH | CNH | Validade (`#ValidadeCnh`) |
| Direção Defensiva | DIRECAO_DEFENSIVA | Vencimento Direção (`#DataVencimentoMOPP`) |
| Cópia ASO | ASO | Validade ASO (`#DataVencimentoPID`) |

---

## Opções de Implantação

### Opção 1 — Console do Navegador (Teste Imediato)

1. Abra a tela do motorista: `brf-homolog.yms.trizy.com.br/Motorista/Create/{id}`
2. Clique na aba **DOCUMENTOS**
3. Pressione **F12** para abrir o DevTools
4. Clique na aba **Console**
5. Cole o conteúdo do arquivo `document-ai-trizy.js` e pressione **Enter**
6. O banner **🤖 Document AI ativo** aparecerá no canto inferior esquerdo

### Opção 2 — Bookmarklet (Uso Diário sem Extensão)

1. Copie o conteúdo do arquivo `bookmarklet.txt`
2. No navegador, crie um novo favorito/bookmark
3. No campo **URL/Endereço**, cole o conteúdo copiado
4. Nomeie como "Document AI - Trizy"
5. Para ativar: abra a tela do motorista e clique no bookmark

### Opção 3 — Extensão Chrome (Ativação Automática) ⭐ Recomendado

Crie uma extensão Chrome que injeta o script automaticamente na URL do Trizy:

**Estrutura de arquivos:**
```
document-ai-extension/
  manifest.json
  content.js
```

**manifest.json:**
```json
{
  "manifest_version": 3,
  "name": "Document AI - B3agro Trizy",
  "version": "1.0",
  "description": "Validação automática de documentos de motoristas",
  "content_scripts": [
    {
      "matches": ["*://brf-homolog.yms.trizy.com.br/Motorista/*",
                  "*://*.yms.trizy.com.br/Motorista/*"],
      "js": ["content.js"],
      "run_at": "document_idle"
    }
  ]
}
```

**content.js:** (copiar o conteúdo de `document-ai-trizy.js`)

**Para instalar:**
1. Abra `chrome://extensions/`
2. Ative o **Modo do desenvolvedor**
3. Clique em **Carregar sem compactação**
4. Selecione a pasta `document-ai-extension`

### Opção 4 — Injeção pelo Backend (Produção)

Solicitar à equipe de desenvolvimento do Trizy para adicionar o script diretamente na view Razor/HTML da página `Motorista/Create`:

```html
<!-- Adicionar antes do </body> na view Motorista/Create -->
<script src="/scripts/document-ai-trizy.js"></script>
```

---

## Configuração da URL da API

O script usa a URL do Document AI Service configurada na variável `API_URL`. Para produção, altere para a URL do servidor definitivo:

```javascript
const API_URL = 'https://SEU-SERVIDOR/api/validation';
```

---

## Critérios de Validação

| Status | Condição |
|---|---|
| ✅ **APROVADO** | Score ≥ 85% + Nome + Validade encontrados |
| ⚠️ **ANÁLISE MANUAL** | 50% ≤ Score < 85% |
| ❌ **REPROVADO** | Score < 50% ou OCR falhou |

---

## Requisitos do Servidor

- .NET 8.0 Runtime
- Tesseract OCR instalado (`tesseract-ocr`)
- Poppler Utils (`poppler-utils`) para conversão de PDF
- Porta 5000 acessível

---

## Suporte

Para dúvidas ou problemas, consulte o arquivo `README.md` do projeto ou entre em contato com a equipe de desenvolvimento.
