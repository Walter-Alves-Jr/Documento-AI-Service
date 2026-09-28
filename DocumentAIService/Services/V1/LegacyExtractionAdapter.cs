using DocumentAIService.Models.V1;
using DocumentAIService.Models.Catalog;
using System.Text.RegularExpressions;

namespace DocumentAIService.Services.V1;

public interface IExtractionAdapter
{
    Task<NormalizedExtractionResult> ExtractAsync(DocumentTypeDefinition documentType, byte[] fileData, CancellationToken cancellationToken);
}

/// <summary>
/// Adaptador de transição: reaproveita a extração já calibrada sem usar seu status legado
/// como decisão da API v1. A decisão v1 pertence exclusivamente à política e ao Rules Engine.
/// </summary>
public sealed class LegacyExtractionAdapter(IDocumentAnalysisService legacyAnalysis, IOcrService ocrService, ILogger<LegacyExtractionAdapter> logger) : IExtractionAdapter
{
    public async Task<NormalizedExtractionResult> ExtractAsync(DocumentTypeDefinition documentType, byte[] fileData, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestedDocumentType = documentType.Code;
        if (!IsLegacyType(requestedDocumentType))
            return await ExtractUsingSchemaAsync(documentType, fileData, cancellationToken);

        var legacy = await legacyAnalysis.ValidateDocumentAsync(requestedDocumentType, fileData);
        var data = legacy.DadosExtraidos;
        var result = new NormalizedExtractionResult
        {
            DetectedDocumentType = NormalizeDetectedType(data.TipoDocumentoDetectado, requestedDocumentType, legacy.Motivos),
            OcrConfidence = data.ConfiancaOcr > 0 ? data.ConfiancaOcr : 0,
        };

        Add(result, "holderName", data.Nome, result.OcrConfidence);
        Add(result, "documentNumber", data.NumeroDocumento, result.OcrConfidence);
        Add(result, "expirationDate", data.Validade, result.OcrConfidence);
        Add(result, "fitnessResult", data.Resultado?.ToUpperInvariant(), result.OcrConfidence);
        Add(result, "employer", data.Empresa, result.OcrConfidence);
        Add(result, "issueDate", data.DataRealizacao, result.OcrConfidence);
        Add(result, "doctorName", data.Medico, result.OcrConfidence);
        Add(result, "courseName", data.NomeCurso, result.OcrConfidence);
        Add(result, "issuer", data.Escola, result.OcrConfidence);
        Add(result, "courseHours", data.CargaHorariaHoras?.ToString(), result.OcrConfidence);
        Add(result, "completionDate", data.DataFimCurso, result.OcrConfidence);

        if (result.DetectedDocumentType.Equals("DIRECAO_DEFENSIVA", StringComparison.OrdinalIgnoreCase))
            Add(result, "issuerApproved", data.EscolaAprovada.ToString().ToLowerInvariant(), result.OcrConfidence);

        if (result.OcrConfidence <= 0)
            result.Warnings.Add(new ValidationWarning { Code = "LOW_CONFIDENCE", Message = "Não foi possível obter texto OCR confiável." });
        if (!result.DetectedDocumentType.Equals(Normalize(requestedDocumentType), StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add(new ValidationWarning { Code = "DOCUMENT_TYPE_MISMATCH", Message = "O tipo identificado não corresponde ao tipo solicitado." });

        logger.LogInformation("Extração normalizada concluída. RequestedType: {RequestedType}; DetectedType: {DetectedType}; Fields: {FieldCount}", requestedDocumentType, result.DetectedDocumentType, result.Fields.Count);
        return result;
    }

    private async Task<NormalizedExtractionResult> ExtractUsingSchemaAsync(DocumentTypeDefinition documentType, byte[] fileData, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (text, confidence) = await ocrService.ExtractTextAsync(fileData);
        var result = new NormalizedExtractionResult { OcrConfidence = confidence };

        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 20)
        {
            result.DetectedDocumentType = "UNKNOWN";
            result.Warnings.Add(new ValidationWarning { Code = "LOW_CONFIDENCE", Message = "Não foi possível extrair texto suficiente do documento." });
            return result;
        }

        var typeMatches = documentType.IdentificationPatterns.Count > 0 && documentType.IdentificationPatterns.Any(pattern => Matches(text, pattern));
        result.DetectedDocumentType = typeMatches ? documentType.Code : "UNKNOWN";
        if (!typeMatches)
            result.Warnings.Add(new ValidationWarning { Code = "DOCUMENT_TYPE_UNCONFIRMED", Message = "Os marcadores configurados do tipo de documento não foram encontrados." });

        foreach (var field in documentType.Fields)
        {
            foreach (var pattern in field.ExtractionPatterns)
            {
                var match = SafeMatch(text, pattern);
                if (!match.Success) continue;
                var value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                if (string.IsNullOrWhiteSpace(value)) continue;
                result.Fields[field.Code] = new ExtractedFieldValue { Value = value.Trim(), Confidence = confidence, Source = "schema_regex" };
                break;
            }
        }

        logger.LogInformation("Extração configurável concluída. RequestedType: {RequestedType}; DetectedType: {DetectedType}; Fields: {FieldCount}", documentType.Code, result.DetectedDocumentType, result.Fields.Count);
        return result;
    }

    private static bool Matches(string text, string pattern) => SafeMatch(text, pattern).Success;

    private static Match SafeMatch(string text, string pattern)
    {
        try { return Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromMilliseconds(250)); }
        catch (ArgumentException) { return Match.Empty; }
        catch (RegexMatchTimeoutException) { return Match.Empty; }
    }

    private static bool IsLegacyType(string type) =>
        Normalize(type) is "CNH" or "ASO" or "DIRECAO_DEFENSIVA";

    private static void Add(NormalizedExtractionResult result, string field, string? value, double confidence)
    {
        if (!string.IsNullOrWhiteSpace(value))
            result.Fields[field] = new ExtractedFieldValue { Value = value.Trim(), Confidence = confidence };
    }

    private static string NormalizeDetectedType(string? detected, string requested, IEnumerable<string> reasons)
    {
        if (reasons.Any(x => x.Contains("não é uma CNH", StringComparison.OrdinalIgnoreCase) || x.Contains("não é uma fatura", StringComparison.OrdinalIgnoreCase)))
            return "UNKNOWN";
        return string.IsNullOrWhiteSpace(detected) ? Normalize(requested) : Normalize(detected);
    }

    private static string Normalize(string value)
    {
        var normalized = value.ToUpperInvariant().Replace(" ", string.Empty).Replace("_", "").Replace("Ç", "C").Replace("Ã", "A").Replace("Á", "A").Replace("É", "E").Replace("Ê", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ô", "O").Replace("Ú", "U");
        return normalized switch
        {
            "DIRECAODEFENSIVA" => "DIRECAO_DEFENSIVA",
            _ => normalized
        };
    }
}
