using System.Diagnostics;
using DocumentAIService.Models.V1;
using DocumentAIService.Security;

namespace DocumentAIService.Services.V1;

public interface IDocumentValidationV1Service
{
    Task<ValidationV1Response> ValidateAsync(ValidationV1Request request, byte[] fileData, string mediaType, string clientId, string correlationId, CancellationToken cancellationToken);
}

public sealed class DocumentValidationV1Service(
    IValidationCatalogService catalog,
    IExtractionAdapter extractor,
    IRulesEngine rulesEngine,
    IResponseDataMinimizer minimizer,
    IPdfConverterService pdfConverter,
    IValidationAuditStore auditStore,
    IIdempotencyStore idempotencyStore,
    ILogger<DocumentValidationV1Service> logger) : IDocumentValidationV1Service
{
    public async Task<ValidationV1Response> ValidateAsync(ValidationV1Request request, byte[] fileData, string mediaType, string clientId, string correlationId, CancellationToken cancellationToken)
    {
        var documentType = catalog.GetDocumentType(request.DocumentType);
        if (documentType is null || !documentType.Active)
            throw new ValidationInputException("DOCUMENT_TYPE_NOT_FOUND", "O tipo de documento solicitado não está ativo no catálogo.");

        var policy = catalog.GetPolicy(request.Policy);
        if (policy is null || !policy.Active)
            throw new ValidationInputException("POLICY_NOT_FOUND", "A política solicitada não está ativa no catálogo.");
        if (!policy.DocumentType.Equals(documentType.Code, StringComparison.OrdinalIgnoreCase))
            throw new ValidationInputException("POLICY_DOCUMENT_TYPE_MISMATCH", "A política não é aplicável ao tipo de documento informado.");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) && idempotencyStore.TryGet(clientId, request.IdempotencyKey, out var existing))
        {
            logger.LogInformation("Resposta idempotente retornada. ValidationId: {ValidationId}; Client: {Client}", existing.ValidationId, clientId);
            return existing;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var contentForOcr = mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            ? await pdfConverter.ConvertPdfToImageAsync(fileData)
            : fileData;

        var extraction = await extractor.ExtractAsync(documentType, contentForOcr, cancellationToken);
        var engine = rulesEngine.Evaluate(policy, extraction, request.Context);
        timer.Stop();

        var response = new ValidationV1Response
        {
            ValidationId = $"VAL-{startedAt:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            Status = engine.Status,
            Score = engine.Score,
            Confidence = Math.Round(extraction.OcrConfidence, 2),
            DocumentType = documentType.Code,
            DetectedDocumentType = extraction.DetectedDocumentType,
            Policy = policy.Code,
            PolicyVersion = policy.Version,
            RulesVersion = $"{policy.Code}:{policy.Version}",
            ManualReviewRequired = engine.ManualReviewRequired,
            Validations = engine.Evaluations,
            Warnings = engine.Warnings,
            ExtractedFields = minimizer.Build(documentType, extraction),
            CreatedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = timer.ElapsedMilliseconds,
            CorrelationId = correlationId
        };

        auditStore.Save(new ValidationAuditRecord
        {
            ValidationId = response.ValidationId,
            ClientId = clientId,
            DocumentType = response.DocumentType,
            DetectedDocumentType = response.DetectedDocumentType,
            Policy = response.Policy,
            PolicyVersion = response.PolicyVersion,
            Status = response.Status,
            Score = response.Score,
            Confidence = response.Confidence,
            ManualReviewRequired = response.ManualReviewRequired,
            CreatedAt = response.CreatedAt,
            CompletedAt = response.CompletedAt,
            ProcessingTimeMs = response.ProcessingTimeMs,
            CorrelationId = correlationId,
            Validations = response.Validations
        });

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            idempotencyStore.Save(clientId, request.IdempotencyKey, response);

        logger.LogInformation("Validação v1 concluída. ValidationId: {ValidationId}; Client: {Client}; DocumentType: {DocumentType}; Policy: {Policy}; Status: {Status}; ProcessingTimeMs: {ProcessingTimeMs}", response.ValidationId, clientId, response.DocumentType, response.Policy, response.Status, response.ProcessingTimeMs);
        return response;
    }
}

public sealed class ValidationInputException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
