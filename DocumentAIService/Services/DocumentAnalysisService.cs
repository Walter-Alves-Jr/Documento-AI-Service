using System.Text.RegularExpressions;
using DocumentAIService.Models;

namespace DocumentAIService.Services;

public interface IDocumentAnalysisService
{
    Task<ValidationResponse> ValidateDocumentAsync(string tipoDocumento, byte[] imageData);
}

public class DocumentAnalysisService : IDocumentAnalysisService
{
    private readonly IOcrService _ocrService;
    private readonly ILogger<DocumentAnalysisService> _logger;

    public DocumentAnalysisService(IOcrService ocrService, ILogger<DocumentAnalysisService> logger)
    {
        _ocrService = ocrService;
        _logger = logger;
    }

    public async Task<ValidationResponse> ValidateDocumentAsync(string tipoDocumento, byte[] imageData)
    {
        var response = new ValidationResponse();
        double score = 0;

        try
        {
            var (extractedText, ocrConfidence) = await _ocrService.ExtractTextAsync(imageData);

            if (string.IsNullOrEmpty(extractedText))
            {
                response.Status = "REPROVADO";
                response.Confianca = 0;
                response.Motivos.Add("✗ Falha ao extrair texto do documento - OCR não conseguiu ler");
                return response;
            }

            if (ocrConfidence < 0.5)
            {
                response.Status = "ANÁLISE MANUAL";
                response.Confianca = 30;
                response.Motivos.Add($"⚠ Confiança do OCR muito baixa: {ocrConfidence:P}");
                response.Motivos.Add("⚠ Qualidade da imagem inadequada para validação automática");
                return response;
            }

            score += 30;
            response.Motivos.Add($"✓ OCR realizado com sucesso (confiança: {ocrConfidence:P})");

            var dadosExtraidos = ExtractDocumentData(extractedText, tipoDocumento);
            response.DadosExtraidos = dadosExtraidos;
            response.DadosExtraidos.TextoExtraido = extractedText;

            if (!string.IsNullOrEmpty(dadosExtraidos.Nome) && dadosExtraidos.Nome.Length > 5)
            {
                score += 25;
                response.Motivos.Add($"✓ Nome encontrado: {dadosExtraidos.Nome}");
            }
            else
            {
                response.Motivos.Add("✗ Nome não encontrado ou inválido");
                score -= 20;
            }

            if (!string.IsNullOrEmpty(dadosExtraidos.Validade))
            {
                if (IsDocumentValid(dadosExtraidos.Validade))
                {
                    score += 25;
                    response.Motivos.Add($"✓ Documento válido até: {dadosExtraidos.Validade}");
                }
                else
                {
                    response.Motivos.Add($"✗ Documento expirado: {dadosExtraidos.Validade}");
                    score -= 30;
                }
            }
            else
            {
                response.Motivos.Add("✗ Data de validade não encontrada");
                score -= 25;
            }

            if (!string.IsNullOrEmpty(dadosExtraidos.NumeroDocumento))
            {
                score += 15;
                response.Motivos.Add($"✓ Número do documento: {dadosExtraidos.NumeroDocumento}");
            }
            else
            {
                response.Motivos.Add("✗ Número do documento não encontrado");
                score -= 10;
            }

            var qualidadeScore = (int)(ocrConfidence * 5);
            score += qualidadeScore;
            response.Motivos.Add($"✓ Qualidade da imagem: {ocrConfidence:P}");

            response.Confianca = Math.Min(100, Math.Max(0, score));

            if (response.Confianca >= 85 && !string.IsNullOrEmpty(dadosExtraidos.Nome) && !string.IsNullOrEmpty(dadosExtraidos.Validade))
            {
                response.Status = "APROVADO";
                response.Motivos.Add("✓ Documento aprovado automaticamente");
            }
            else if (response.Confianca >= 50)
            {
                response.Status = "ANÁLISE MANUAL";
                response.Motivos.Add("⚠ Documento requer análise manual");
            }
            else
            {
                response.Status = "REPROVADO";
                response.Motivos.Add("✗ Documento reprovado - confiança insuficiente");
            }

            _logger.LogInformation($"Document validation completed. Type: {tipoDocumento}, Status: {response.Status}, Confidence: {response.Confianca:F2}%");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Validation error: {ex.Message}");
            response.Status = "REPROVADO";
            response.Confianca = 0;
            response.Motivos.Add($"✗ Erro ao processar documento: {ex.Message}");
        }

        return response;
    }

    private DadosExtraidos ExtractDocumentData(string text, string tipoDocumento)
    {
        var dados = new DadosExtraidos();

        // Extrair nome - múltiplos padrões para capturar diferentes formatos
        var nomePatterns = new[]
        {
            @"NOME\s*\n\s*eo\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]+?)(?:\n|$)",
            @"NOME\s*\n\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]+?)(?:\n|$)",
            @"eo\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]+?)(?:\n|DOC|CPF|'DOC)",
            @"([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]+\s+JUNIOR)(?:\n|'DOC)",
            @"([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]+\s+(?:ALVES|SILVA|SANTOS|OLIVEIRA|COSTA|MARTINS|SOUSA|FERREIRA))(?:\s+(?:JUNIOR|SENIOR))?(?:\n|'DOC)",
            @"^([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]{10,}?)(?:\n|'DOC)",
        };

        foreach (var pattern in nomePatterns)
        {
            var nomeMatch = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (nomeMatch.Success)
            {
                var nome = nomeMatch.Groups[1].Value.Trim();
                nome = Regex.Replace(nome, @"^eo\s+", "", RegexOptions.IgnoreCase).Trim();
                nome = Regex.Replace(nome, @"[0-9\-\.]+", "").Trim();
                nome = Regex.Replace(nome, @"\s+", " ").Trim();
                
                // Remover palavras de cabeçalho (pode ter múltiplas)
                var headerWords = @"^(REPUBLICA|FEDERATIVA|DO|BRASIL|MINISTERIO|TRANSPORTES|SECRETARIA|NACIONAL|TRANSITO|SENATRAN|CARTEIRA|HABILITACAO|MV|DORAN|PINS|ORL|IAL|PINST|PINAL)\s+";
                for (int i = 0; i < 5; i++)
                {
                    var cleaned = Regex.Replace(nome, headerWords, "", RegexOptions.IgnoreCase).Trim();
                    if (cleaned == nome) break; // Parou de encontrar
                    nome = cleaned;
                }
                
                if (!string.IsNullOrEmpty(nome) && nome.Length > 5 && nome.Split(' ').Length >= 2)
                {
                    dados.Nome = nome;
                    _logger.LogInformation($"Nome extraído: {nome}");
                    break;
                }
            }
        }

        // Extrair data de validade
        var validadePatterns = new[]
        {
            @"REGISTRO\s+VALIDADE\s+\d+\s+(\d{2}[/-]\d{2}[/-]\d{4})",
            @"VALIDADE\s+(\d{2}[/-]\d{2}[/-]\d{4})",
            @"Validade:\s*(\d{2}[/-]\d{2}[/-]\d{4})"
        };

        foreach (var pattern in validadePatterns)
        {
            var validadeMatch = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (validadeMatch.Success)
            {
                var data = validadeMatch.Groups[1].Value;
                if (IsValidDate(data))
                {
                    dados.Validade = data;
                    _logger.LogInformation($"Validade extraída: {data}");
                    break;
                }
            }
        }

        // Se não encontrou, procurar segunda data (primeira é nascimento)
        if (string.IsNullOrEmpty(dados.Validade))
        {
            var dateMatches = Regex.Matches(text, @"\d{2}[/-]\d{2}[/-]\d{4}");
            if (dateMatches.Count >= 2)
            {
                for (int i = 1; i < dateMatches.Count; i++)
                {
                    if (IsValidDate(dateMatches[i].Value))
                    {
                        dados.Validade = dateMatches[i].Value;
                        _logger.LogInformation($"Validade extraída (segunda data): {dateMatches[i].Value}");
                        break;
                    }
                }
            }
        }

        // Extrair número do documento
        var numeroMatch = Regex.Match(text, @"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b");
        if (numeroMatch.Success)
        {
            dados.NumeroDocumento = numeroMatch.Value;
        }
        else
        {
            var numeroMatch2 = Regex.Match(text, @"\b\d{8,12}\b");
            if (numeroMatch2.Success)
            {
                dados.NumeroDocumento = numeroMatch2.Value;
            }
        }

        return dados;
    }

    private bool IsValidDate(string data)
    {
        try
        {
            var parts = data.Split(new[] { '/', '-' });
            if (parts.Length != 3) return false;

            var day = int.Parse(parts[0]);
            var month = int.Parse(parts[1]);
            var year = int.Parse(parts[2]);

            if (day < 1 || day > 31 || month < 1 || month > 12 || year < 1900 || year > 2100)
                return false;

            var expiryDate = new DateTime(year, month, day);
            return expiryDate > DateTime.Now;
        }
        catch
        {
            return false;
        }
    }

    private bool IsDocumentValid(string dataValidade)
    {
        return IsValidDate(dataValidade);
    }
}
