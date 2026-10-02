using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentAIService.Configuration;
using DocumentAIService.Models.V1;
using DocumentAIService.Persistence;
using DocumentAIService.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocumentAIService.Services.V1;

public interface IPersistedValidationService
{
    Task<PersistedValidationResponse> ValidateAsync(PersistedValidationRequest request, byte[] fileData, string mediaType, string clientId, string correlationId, CancellationToken cancellationToken);
    Task<PersistedValidationLookupResponse?> GetValidationAsync(string validationId, string clientId, CancellationToken cancellationToken);
    Task<ComplianceSummaryResponse?> GetComplianceAsync(ComplianceEntityType entityType, string externalId, string clientId, CancellationToken cancellationToken);
    Task<int> MarkExternalValidationsDueAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Orquestra o estado persistido de compliance. Nenhum arquivo, Base64 ou OCR é salvo;
/// somente metadados, hashes e snapshots minimizados cifrados são mantidos para reuso.
/// </summary>
public sealed class PersistedValidationService(
    DocumentValidationDbContext db,
    IDocumentValidationV1Service validationService,
    IProtectedDataCodec protectedData,
    IOptions<DvsOptions> options,
    ILogger<PersistedValidationService> logger) : IPersistedValidationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PersistedValidationResponse> ValidateAsync(PersistedValidationRequest request, byte[] fileData, string mediaType, string clientId, string correlationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
            throw new ValidationInputException("ENTITY_ID_REQUIRED", "entityId é obrigatório para validação persistida.");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var idempotent = await db.DocumentValidations.AsNoTracking()
                .Include(x => x.Document)
                .FirstOrDefaultAsync(x => x.ClientId == clientId && x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (idempotent is not null)
            {
                var cached = RestoreResponse(idempotent);
                if (cached is not null)
                {
                    cached.CacheUsed = true;
                    cached.ProcessingTimeMs = 0;
                    logger.LogInformation("Resultado idempotente persistido retornado. ValidationId: {ValidationId}; Client: {Client}", cached.ValidationId, clientId);
                    return cached;
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var entity = await db.Entities
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.ClientId == clientId && x.Type == request.EntityType && x.ExternalId == request.EntityId, cancellationToken);

        if (entity is null)
        {
            entity = new ComplianceEntity
            {
                ClientId = clientId,
                Type = request.EntityType,
                ExternalId = request.EntityId.Trim(),
                Cpf = protectedData.Protect(request.Cpf),
                Plate = protectedData.Protect(request.Plate),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Entities.Add(entity);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.Cpf)) entity.Cpf = protectedData.Protect(request.Cpf);
            if (!string.IsNullOrWhiteSpace(request.Plate)) entity.Plate = protectedData.Protect(request.Plate);
            entity.UpdatedAt = now;
        }

        var fileHash = Hash(fileData);
        var cacheCandidate = entity.Documents.FirstOrDefault(x => x.DocumentType.Equals(request.DocumentType, StringComparison.OrdinalIgnoreCase) && x.FileHash == fileHash);
        if (cacheCandidate is not null && IsDocumentCurrent(cacheCandidate, now))
        {
            var cacheRecord = await db.DocumentValidations.AsNoTracking()
                .Where(x => x.DocumentId == cacheCandidate.Id)
                .OrderByDescending(x => x.ValidatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (cacheRecord is not null)
            {
                var cached = RestoreResponse(cacheRecord);
                if (cached is not null)
                {
                    cached.CacheUsed = true;
                    cached.ProcessingTimeMs = 0;
                    logger.LogInformation("Resultado de compliance reutilizado por hash. ValidationId: {ValidationId}; Entity: {EntityId}; DocumentType: {DocumentType}", cached.ValidationId, entity.Id, request.DocumentType);
                    return cached;
                }
            }
        }

        var effectiveContext = new Dictionary<string, string>(request.Context, StringComparer.OrdinalIgnoreCase);
        if (!effectiveContext.ContainsKey("vehiclePlate"))
        {
            var knownPlate = protectedData.Unprotect(entity.Plate);
            if (!string.IsNullOrWhiteSpace(knownPlate)) effectiveContext["vehiclePlate"] = knownPlate;
        }

        var baseRequest = new ValidationV1Request
        {
            DocumentType = request.DocumentType,
            Policy = request.Policy,
            File = request.File,
            Context = effectiveContext,
            IdempotencyKey = null
        };
        var internalResult = await validationService.ValidateAsync(baseRequest, fileData, mediaType, clientId, correlationId, cancellationToken);

        var existingDocument = cacheCandidate;
        var document = existingDocument ?? new ComplianceDocument
        {
            Entity = entity,
            DocumentType = internalResult.DocumentType,
            FileHash = fileHash,
            FileName = SanitizeFileName(request.FileName),
            MimeType = mediaType,
            CreatedAt = now
        };
        if (existingDocument is null) db.Documents.Add(document);

        document.DocumentType = internalResult.DocumentType;
        document.FileHash = fileHash;
        document.FileName = SanitizeFileName(request.FileName);
        document.MimeType = mediaType;
        document.DocumentExpiresAt = ParseDate(Field(internalResult, "expirationDate"));
        document.IssuedAt = ParseDate(Field(internalResult, "issueDate"));
        document.DocumentNumber = protectedData.Protect(Field(internalResult, "documentNumber"));
        document.LastValidatedAt = now;
        document.UpdatedAt = now;
        document.ExtractedDataSnapshot = protectedData.Protect(JsonSerializer.Serialize(internalResult.ExtractedFields, JsonOptions));

        var provider = await db.ExternalProviderConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Active && x.DocumentType == internalResult.DocumentType, cancellationToken);
        var (complianceStatus, reasonCode, externalRecord) = await EvaluateExternalAndCrossValidationAsync(internalResult, document, entity, provider, now, cancellationToken);
        document.Status = complianceStatus;
        if (externalRecord is not null)
        {
            db.ExternalValidations.Add(externalRecord);
            document.LastExternalValidatedAt = externalRecord.Status == ExternalValidationStatus.APPROVED ? now : null;
            document.NextValidationAt = externalRecord.ExpiresAt;
        }
        else
        {
            document.NextValidationAt = null;
        }

        var response = ToPersistedResponse(internalResult, entity.Id, document.Id, document, complianceStatus, reasonCode, provider, false);
        var snapshot = protectedData.Protect(JsonSerializer.Serialize(response, JsonOptions));
        var record = new DocumentValidationRecord
        {
            ValidationId = response.ValidationId,
            Document = document,
            ValidationType = response.Policy,
            Status = complianceStatus,
            Score = response.Score,
            Confidence = response.Confidence,
            Source = "ocr_internal",
            ValidatedAt = response.CompletedAt,
            ExpiresAt = document.DocumentExpiresAt,
            ResponseHash = Hash(snapshot ?? string.Empty),
            ResponseSnapshot = snapshot,
            ProcessingTimeMs = response.ProcessingTimeMs,
            CacheUsed = false,
            ExternalCall = response.ExternalCall,
            Provider = response.Provider,
            EstimatedCost = response.EstimatedCost,
            ClientId = clientId,
            IdempotencyKey = request.IdempotencyKey,
            CorrelationId = correlationId,
            CreatedAt = now
        };
        db.DocumentValidations.Add(record);
        db.ValidationEvents.Add(new ValidationEvent
        {
            EntityId = entity.Id,
            DocumentId = document.Id,
            DocumentValidationRecordId = record.Id,
            EventType = "DOCUMENT_VALIDATED",
            PreviousStatus = existingDocument?.Status,
            CurrentStatus = complianceStatus,
            ReasonCode = reasonCode,
            CorrelationId = correlationId,
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Compliance persistido. ValidationId: {ValidationId}; Entity: {EntityId}; Document: {DocumentId}; Status: {Status}; CacheUsed: false; ExternalCall: {ExternalCall}", response.ValidationId, entity.Id, document.Id, complianceStatus, response.ExternalCall);
        return response;
    }

    public async Task<PersistedValidationLookupResponse?> GetValidationAsync(string validationId, string clientId, CancellationToken cancellationToken)
    {
        return await db.DocumentValidations.AsNoTracking()
            .Where(x => x.ClientId == clientId && x.ValidationId == validationId)
            .Select(x => new PersistedValidationLookupResponse
            {
                ValidationId = x.ValidationId,
                EntityId = x.Document.EntityId,
                DocumentId = x.DocumentId,
                DocumentType = x.Document.DocumentType,
                Status = x.Status,
                Score = x.Score,
                Confidence = x.Confidence,
                Source = x.Source,
                ValidatedAt = x.ValidatedAt,
                ExpiresAt = x.ExpiresAt,
                CacheUsed = x.CacheUsed,
                ExternalCall = x.ExternalCall,
                Provider = x.Provider,
                EstimatedCost = x.EstimatedCost,
                ProcessingTimeMs = x.ProcessingTimeMs,
                CorrelationId = x.CorrelationId
            }).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ComplianceSummaryResponse?> GetComplianceAsync(ComplianceEntityType entityType, string externalId, string clientId, CancellationToken cancellationToken)
    {
        var entity = await db.Entities.AsNoTracking()
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.ClientId == clientId && x.Type == entityType && x.ExternalId == externalId, cancellationToken);
        if (entity is null) return null;

        var documentIds = entity.Documents.Select(x => x.Id).ToArray();
        var providers = await db.ExternalProviderConfigurations.AsNoTracking().Where(x => x.Active).ToListAsync(cancellationToken);
        var latestExternal = await db.ExternalValidations.AsNoTracking()
            .Where(x => documentIds.Contains(x.DocumentId))
            .GroupBy(x => x.DocumentId)
            .Select(x => x.OrderByDescending(v => v.RequestedAt).First())
            .ToListAsync(cancellationToken);

        var documents = entity.Documents.Select(document =>
        {
            var external = latestExternal.FirstOrDefault(x => x.DocumentId == document.Id);
            return new ComplianceDocumentResponse
            {
                DocumentId = document.Id,
                DocumentType = document.DocumentType,
                Status = document.Status,
                DocumentExpiresAt = document.DocumentExpiresAt,
                LastValidatedAt = document.LastValidatedAt,
                NextValidationAt = document.NextValidationAt,
                Provider = external?.Provider,
                ExternalValidationConfigured = providers.Any(x => x.DocumentType == document.DocumentType)
            };
        }).OrderBy(x => x.DocumentType).ToList();

        return new ComplianceSummaryResponse
        {
            EntityId = entity.Id,
            EntityType = entity.Type,
            ExternalId = entity.ExternalId,
            Status = AggregateStatus(documents.Select(x => x.Status)),
            Documents = documents
        };
    }

    public async Task<int> MarkExternalValidationsDueAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var threshold = now.AddDays(Math.Max(0, options.Value.Renewal.LeadDays));
        var documents = await db.Documents
            .Where(x => x.NextValidationAt != null && x.NextValidationAt <= threshold && x.Status != ComplianceStatus.REJECTED)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            var previous = document.Status;
            document.Status = ComplianceStatus.PENDING_VALIDATION;
            document.UpdatedAt = now;
            db.ValidationEvents.Add(new ValidationEvent
            {
                EntityId = document.EntityId,
                DocumentId = document.Id,
                EventType = "EXTERNAL_VALIDATION_DUE",
                PreviousStatus = previous,
                CurrentStatus = ComplianceStatus.PENDING_VALIDATION,
                ReasonCode = "EXTERNAL_VALIDATION_EXPIRED",
                CorrelationId = "renewal-scan",
                CreatedAt = now
            });
        }
        if (documents.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return documents.Count;
    }

    private async Task<(ComplianceStatus Status, string? ReasonCode, ExternalValidationRecord? Record)> EvaluateExternalAndCrossValidationAsync(ValidationV1Response result, ComplianceDocument document, ComplianceEntity entity, ExternalProviderConfiguration? provider, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var status = ToComplianceStatus(result.Status);
        string? reason = result.Status == ValidationDecisionStatus.MANUAL_REVIEW ? "MANUAL_REVIEW_REQUIRED" : null;
        ExternalValidationRecord? record = null;

        if (provider is not null)
        {
            // Nenhum cliente externo é inventado. Uma configuração ativa sem adaptador contratado
            // permanece pendente e registra a indisponibilidade sem chamada de rede.
            if (status != ComplianceStatus.REJECTED)
            {
                status = ComplianceStatus.PENDING_VALIDATION;
                reason = "EXTERNAL_PROVIDER_UNAVAILABLE";
            }
            record = new ExternalValidationRecord
            {
                Document = document,
                Provider = provider.Code,
                RequestHash = Hash($"{document.FileHash}:{provider.Code}"),
                Status = ExternalValidationStatus.UNAVAILABLE,
                RequestedAt = now,
                ExpiresAt = now.AddDays(Math.Max(1, provider.TtlDays)),
                ProcessingTimeMs = 0,
                EstimatedCost = 0,
                ReasonCode = reason
            };
        }
        else
        {
            // Nenhum provedor oficial/contratado: mantém OCR e validações internas.
            record = new ExternalValidationRecord
            {
                Document = document,
                Provider = "NOT_CONFIGURED",
                RequestHash = Hash(document.FileHash),
                Status = ExternalValidationStatus.NOT_AVAILABLE,
                RequestedAt = now,
                ProcessingTimeMs = 0,
                EstimatedCost = 0,
                ReasonCode = "EXTERNAL_VALIDATION_NOT_AVAILABLE"
            };
        }

        var plate = Field(result, "plate");
        if (!string.IsNullOrWhiteSpace(plate))
        {
            var mismatched = await HasPlateInconsistencyAsync(entity.Id, document.Id, plate, cancellationToken);
            if (mismatched)
            {
                status = options.Value.CrossValidation.InconsistencyOutcome.Equals("rejected", StringComparison.OrdinalIgnoreCase)
                    ? ComplianceStatus.REJECTED
                    : ComplianceStatus.PENDING_VALIDATION;
                reason = "CROSS_DOCUMENT_PLATE_MISMATCH";
            }
        }

        return (status, reason, record);
    }

    private async Task<bool> HasPlateInconsistencyAsync(Guid entityId, Guid currentDocumentId, string plate, CancellationToken cancellationToken)
    {
        var snapshots = await db.Documents.AsNoTracking()
            .Where(x => x.EntityId == entityId && x.Id != currentDocumentId && x.ExtractedDataSnapshot != null)
            .Select(x => x.ExtractedDataSnapshot!)
            .ToListAsync(cancellationToken);
        foreach (var encryptedSnapshot in snapshots)
        {
            var snapshot = protectedData.Unprotect(encryptedSnapshot);
            if (string.IsNullOrWhiteSpace(snapshot)) continue;
            try
            {
                var fields = JsonSerializer.Deserialize<List<ExtractedFieldResponse>>(snapshot, JsonOptions);
                var otherPlate = fields?.FirstOrDefault(x => x.Field.Equals("plate", StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(otherPlate) && !Normalize(otherPlate).Equals(Normalize(plate), StringComparison.Ordinal)) return true;
            }
            catch (JsonException) { /* snapshot inválido não causa reprovação */ }
        }
        return false;
    }

    private static PersistedValidationResponse ToPersistedResponse(ValidationV1Response result, Guid entityId, Guid documentId, ComplianceDocument document, ComplianceStatus status, string? reason, ExternalProviderConfiguration? provider, bool cacheUsed) => new()
    {
        ValidationId = result.ValidationId,
        Status = result.Status,
        Score = result.Score,
        Confidence = result.Confidence,
        DocumentType = result.DocumentType,
        DetectedDocumentType = result.DetectedDocumentType,
        Policy = result.Policy,
        PolicyVersion = result.PolicyVersion,
        RulesVersion = result.RulesVersion,
        ManualReviewRequired = result.ManualReviewRequired,
        Validations = result.Validations,
        Warnings = result.Warnings,
        ExtractedFields = result.ExtractedFields,
        CreatedAt = result.CreatedAt,
        CompletedAt = result.CompletedAt,
        ProcessingTimeMs = result.ProcessingTimeMs,
        CorrelationId = result.CorrelationId,
        CacheUsed = cacheUsed,
        ExternalCall = false,
        Provider = provider?.Code,
        EstimatedCost = 0,
        EntityId = entityId,
        DocumentId = documentId,
        ComplianceStatus = status,
        ComplianceReason = reason,
        DocumentExpiresAt = document.DocumentExpiresAt,
        LastValidatedAt = document.LastValidatedAt,
        NextValidationAt = document.NextValidationAt
    };

    private PersistedValidationResponse? RestoreResponse(DocumentValidationRecord record)
    {
        var snapshot = protectedData.Unprotect(record.ResponseSnapshot);
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        try { return JsonSerializer.Deserialize<PersistedValidationResponse>(snapshot, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static bool IsDocumentCurrent(ComplianceDocument document, DateTimeOffset now) =>
        (!document.DocumentExpiresAt.HasValue || document.DocumentExpiresAt >= now) &&
        (!document.NextValidationAt.HasValue || document.NextValidationAt > now);

    private static ComplianceStatus ToComplianceStatus(ValidationDecisionStatus status) => status switch
    {
        ValidationDecisionStatus.APPROVED => ComplianceStatus.APPROVED,
        ValidationDecisionStatus.REJECTED => ComplianceStatus.REJECTED,
        _ => ComplianceStatus.PENDING_VALIDATION
    };

    private static ComplianceStatus AggregateStatus(IEnumerable<ComplianceStatus> statuses)
    {
        var materialized = statuses.ToList();
        if (materialized.Any(x => x == ComplianceStatus.REJECTED)) return ComplianceStatus.REJECTED;
        if (materialized.Any(x => x == ComplianceStatus.PENDING_VALIDATION)) return ComplianceStatus.PENDING_VALIDATION;
        return materialized.Count > 0 ? ComplianceStatus.APPROVED : ComplianceStatus.PENDING_VALIDATION;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy" };
        return DateTime.TryParseExact(value, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
            : null;
    }

    private static string? Field(ValidationV1Response response, string field) => response.ExtractedFields.FirstOrDefault(x => x.Field.Equals(field, StringComparison.OrdinalIgnoreCase))?.Value;
    private static string SanitizeFileName(string? fileName) => string.IsNullOrWhiteSpace(fileName) ? "upload" : Path.GetFileName(fileName)[..Math.Min(255, Path.GetFileName(fileName).Length)];
    private static string Hash(byte[] input) => Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    private static string Hash(string input) => Hash(Encoding.UTF8.GetBytes(input));
    private static string Normalize(string value) => value.Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
}
