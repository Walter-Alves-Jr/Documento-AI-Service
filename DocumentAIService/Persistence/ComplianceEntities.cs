namespace DocumentAIService.Persistence;

public enum ComplianceEntityType
{
    DRIVER,
    VEHICLE,
    EQUIPMENT,
    CARRIER
}

public enum ComplianceStatus
{
    APPROVED,
    REJECTED,
    PENDING_VALIDATION
}

public enum ExternalValidationStatus
{
    NOT_AVAILABLE,
    APPROVED,
    REJECTED,
    UNAVAILABLE,
    ERROR
}

/// <summary>
/// Dono operacional de documentos. ClientId preserva isolamento por credencial consumidora.
/// CPF e placa são protegidos antes da persistência.
/// </summary>
public sealed class ComplianceEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClientId { get; set; } = string.Empty;
    public ComplianceEntityType Type { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? Cpf { get; set; }
    public string? Plate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ComplianceDocument> Documents { get; set; } = [];
}

/// <summary>
/// Metadados do documento. Arquivo, Base64 e OCR bruto não são persistidos.
/// Snapshots criptografados guardam somente dados minimizados necessários para cache e consistência.
/// </summary>
public sealed class ComplianceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    public ComplianceEntity Entity { get; set; } = null!;
    public string DocumentType { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? DocumentExpiresAt { get; set; }
    public ComplianceStatus Status { get; set; } = ComplianceStatus.PENDING_VALIDATION;
    public DateTimeOffset? LastValidatedAt { get; set; }
    public DateTimeOffset? LastExternalValidatedAt { get; set; }
    public DateTimeOffset? NextValidationAt { get; set; }
    public string? ExtractedDataSnapshot { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<DocumentValidationRecord> Validations { get; set; } = [];
    public List<ExternalValidationRecord> ExternalValidations { get; set; } = [];
    public List<ValidationEvent> Events { get; set; } = [];
}

public sealed class DocumentValidationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ValidationId { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public ComplianceDocument Document { get; set; } = null!;
    public string ValidationType { get; set; } = string.Empty;
    public ComplianceStatus Status { get; set; }
    public int Score { get; set; }
    public double Confidence { get; set; }
    public string Source { get; set; } = "internal";
    public DateTimeOffset ValidatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string ResponseHash { get; set; } = string.Empty;
    public string? ResponseSnapshot { get; set; }
    public long ProcessingTimeMs { get; set; }
    public bool CacheUsed { get; set; }
    public bool ExternalCall { get; set; }
    public string? Provider { get; set; }
    public decimal EstimatedCost { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ExternalValidationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public ComplianceDocument Document { get; set; } = null!;
    public string Provider { get; set; } = "NOT_CONFIGURED";
    public string RequestHash { get; set; } = string.Empty;
    public ExternalValidationStatus Status { get; set; }
    public string? Response { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public long ProcessingTimeMs { get; set; }
    public decimal EstimatedCost { get; set; }
    public string? ReasonCode { get; set; }
}

public sealed class ValidationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? DocumentValidationRecordId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public ComplianceStatus? PreviousStatus { get; set; }
    public ComplianceStatus CurrentStatus { get; set; }
    public string? ReasonCode { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Configuração futura de fornecedor. SecretReference contém somente o nome/referência
/// de um segredo externo; URL, token ou chave não são embutidos no código.
/// </summary>
public sealed class ExternalProviderConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public string AuthenticationType { get; set; } = "none";
    public string? SecretReference { get; set; }
    public string HeadersJson { get; set; } = "{}";
    public string ParametersJson { get; set; } = "{}";
    public int TimeoutSeconds { get; set; } = 15;
    public int TtlDays { get; set; } = 15;
    public decimal EstimatedCost { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
