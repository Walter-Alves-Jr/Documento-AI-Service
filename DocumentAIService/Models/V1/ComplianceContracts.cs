using System.Text.Json.Serialization;
using DocumentAIService.Persistence;

namespace DocumentAIService.Models.V1;

public sealed class PersistedValidationRequest : ValidationV1Request
{
    public ComplianceEntityType EntityType { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string? Cpf { get; set; }
    public string? Plate { get; set; }
    public string? FileName { get; set; }
}

public sealed class PersistedValidationResponse : ValidationV1Response
{
    public Guid EntityId { get; set; }
    public Guid DocumentId { get; set; }
    public ComplianceStatus ComplianceStatus { get; set; }
    public string? ComplianceReason { get; set; }
    public DateTimeOffset? DocumentExpiresAt { get; set; }
    public DateTimeOffset? LastValidatedAt { get; set; }
    public DateTimeOffset? NextValidationAt { get; set; }
}

public sealed class ComplianceSummaryResponse
{
    public Guid EntityId { get; set; }
    public ComplianceEntityType EntityType { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public ComplianceStatus Status { get; set; }
    public IReadOnlyList<ComplianceDocumentResponse> Documents { get; set; } = [];
}

public sealed class ComplianceDocumentResponse
{
    public Guid DocumentId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public ComplianceStatus Status { get; set; }
    public DateTimeOffset? DocumentExpiresAt { get; set; }
    public DateTimeOffset? LastValidatedAt { get; set; }
    public DateTimeOffset? NextValidationAt { get; set; }
    public string? Provider { get; set; }
    public bool ExternalValidationConfigured { get; set; }
}

public sealed class PersistedValidationLookupResponse
{
    public string ValidationId { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public ComplianceStatus Status { get; set; }
    public int Score { get; set; }
    public double Confidence { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset ValidatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool CacheUsed { get; set; }
    public bool ExternalCall { get; set; }
    public string? Provider { get; set; }
    public decimal EstimatedCost { get; set; }
    public long ProcessingTimeMs { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class ExternalProviderConfigurationRequest
{
    public string Code { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public string AuthenticationType { get; set; } = "none";
    public string? SecretReference { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int TimeoutSeconds { get; set; } = 15;
    public int TtlDays { get; set; } = 15;
    public decimal EstimatedCost { get; set; }
    public bool Active { get; set; }
}

public sealed class ExternalProviderConfigurationResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string AuthenticationType { get; set; } = string.Empty;
    public string? SecretReference { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int TimeoutSeconds { get; set; }
    public int TtlDays { get; set; }
    public decimal EstimatedCost { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
