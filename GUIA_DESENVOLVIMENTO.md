# Guia de Desenvolvimento — Document AI Service v2.1

Este documento detalha a arquitetura, regras de negócio, ambiente de desenvolvimento e processos de calibração do **Document AI Service**, com foco nas regras específicas de credenciamento da **BRF**.

---

## 1. Visão Geral da Arquitetura

O sistema é uma API REST construída em **ASP.NET Core 8.0**, focada em validação automática de documentos (CNH, ASO e Certificados de Direção Defensiva) utilizando Inteligência Artificial (OCR) e expressões regulares.

### Componentes Principais

- **Controllers (`ValidationController`)**: Ponto de entrada da API. Recebe requisições POST com arquivos em Base64 e gerencia os endpoints de configuração.
- **DocumentAnalysisService**: O coração do sistema. Contém a lógica de negócio, extração de dados e validação de regras específicas (MRZ, validade, carga horária).
- **OcrService**: Serviço de abstração para o Tesseract OCR. Implementa 10 estratégias de extração (rotações 0°, 90°, 180°, 270°, escala 2×, binarização) para garantir a leitura mesmo em documentos digitalizados com baixa qualidade.
- **PdfConverterService**: Serviço utilitário que converte arquivos PDF em imagens PNG (via `pdftoppm`) antes de enviar ao OCR.
- **DirecaoDefensivaConfigService**: Gerencia a persistência das regras dinâmicas (escolas homologadas, cursos aceitos, carga horária) em um arquivo JSON local.

---

## 2. Regras de Negócio (Calibração BRF)

A versão 2.1 foi rigorosamente calibrada para atender aos requisitos de credenciamento de motoristas da BRF.

### 2.1. Escolas Homologadas (Direção Defensiva)

O sistema aceita **apenas** certificados emitidos pelas seguintes instituições homologadas:

- Hartmann
- Inttergramed
- Eco Trainning
- SEST SENAT
- Concórdia Treinamentos
- FABET
- Champonalli (Centro de Formação de Condutores)
- CERTO (Centro de Ref. em Treinamento Op.)
- CIT Drive (Consultoria Integrada ao Transportador)

> **Importante:** Instituições genéricas como "UNIGIO", "SENAI", "SENAC" ou plataformas EAD não listadas acima são reprovadas automaticamente.

### 2.2. Regra de Carga Horária Diferenciada

A BRF estabelece uma regra específica de carga horária mínima para os cursos de Direção Defensiva:

- **SEST SENAT**: Carga horária mínima exigida é de **4 horas**.
- **Demais Escolas Homologadas**: Carga horária mínima exigida é de **8 horas**.

Esta regra está codificada no método `IsCargaHorariaValida` do `DocumentAnalysisService`.

### 2.3. Extração MRZ (CNH-e Digital)

Para CNHs digitais (CNH-e), o OCR tradicional muitas vezes falha ao ler o texto impresso devido a artefatos visuais. A versão 2.1 introduz a extração de dados via **MRZ (Machine Readable Zone)**:

- **Nome do Condutor**: Extraído da linha 3 do MRZ (ex: `LEONARDO<<VIEIRA<SILVA`).
- **Data de Validade**: Extraída da linha 2 do MRZ, que contém a data no formato `AAMMDD` (ex: `9901085M3304204` indica validade em `20/04/2033`).

---

## 3. Ambiente de Desenvolvimento Local

### 3.1. Pré-requisitos

Para rodar o projeto localmente (Linux/Ubuntu), você precisará instalar as dependências de sistema:

```bash
sudo apt update
sudo apt install -y tesseract-ocr tesseract-ocr-por poppler-utils libgdiplus
```

- `tesseract-ocr` e `tesseract-ocr-por`: Motor de OCR e pacote de idioma Português.
- `poppler-utils`: Fornece o comando `pdftoppm` usado para converter PDFs em imagens.
- `libgdiplus`: Dependência do .NET para manipulação de imagens (System.Drawing).

### 3.2. Compilando e Rodando

O projeto utiliza o SDK do .NET 8.0.

```bash
cd DocumentAIService
dotnet build
dotnet run --urls "http://0.0.0.0:5000"
```

A API estará disponível em `http://localhost:5000/api/validation`.
O front-end de testes locais estará disponível na raiz `http://localhost:5000/`.

---

## 4. Front-end de Testes Locais

O projeto inclui um front-end Single Page Application (SPA) embutido no `wwwroot/index.html`. Ele é servido automaticamente pelo Kestrel e serve como uma ferramenta de diagnóstico e calibração para desenvolvedores.

### Recursos do Front-end:
- **Upload Drag & Drop**: Suporte a PDF e imagens com conversão automática para Base64.
- **Visualização de Confiança**: Barra de progresso mostrando o nível de confiança do OCR.
- **Painel de Configuração BRF**: Interface gráfica para adicionar/remover escolas homologadas e ajustar a carga horária padrão.
- **Status da API**: Monitoramento em tempo real do health check do servidor.

---

## 5. Estratégias de Depuração (Debugging)

Quando um documento falha na validação, o problema geralmente está na qualidade da imagem ou no padrão Regex.

### Script de Extração de Texto Bruto
Para ver exatamente o que o OCR está lendo, crie um script Python simples que chame o OCR diretamente ou adicione um log temporário no `OcrService.cs`:

```csharp
// No OcrService.cs, dentro de ProcessImage()
Console.WriteLine("--- TEXTO OCR ---");
Console.WriteLine(text);
Console.WriteLine("-----------------");
```

### Problemas Comuns:
1. **Documento Rotacionado**: O OCR nativo falha se o texto estiver de lado. A v2.1 tenta 4 rotações (0, 90, 180, 270). Se falhar, a imagem original tem ruído excessivo.
2. **Datas Manuscritas**: ASOs frequentemente possuem datas carimbadas ou escritas à mão. O Tesseract tem baixa precisão para caligrafia. Nesses casos, o sistema retorna `ANÁLISE MANUAL`.
3. **Falso Positivo de Escola**: O regex de extração de escola pode capturar pedaços de texto aleatórios. A lista de escolas homologadas atua como um filtro final de segurança.
