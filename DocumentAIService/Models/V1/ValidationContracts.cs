using System.Text.Json.Serialization;

namespace DocumentAIService.Models.V1;

public class ValidationV1Request
{
    public string DocumentType { get; set; } = string.Empty;
    public string Policy { get; set; } = string.Empty;
    public Dictionary<string, string> Context { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string File { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
}

public class ValidationV1Response
{
    public string ValidationId { get; set; } = string.Empty;
    public ValidationDecisionStatus Status { get; set; }
    public int Score { get; set; }
    public double Confidence { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DetectedDocumentType { get; set; } = "UNKNOWN";
    public string Policy { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string RulesVersion { get; set; } = string.Empty;
    public bool ManualReviewRequired { get; set; }
    public List<RuleEvaluation> Validations { get; set; } = [];
    public List<ValidationWarning> Warnings { get; set; } = [];
    public List<ExtractedFieldResponse> ExtractedFields { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public long ProcessingTimeMs { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public bool CacheUsed { get; set; }
    public bool ExternalCall { get; set; }
    public string? Provider { get; set; }
    public decimal EstimatedCost { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidationDecisionStatus
{
    APPROVED,
    REJECTED,
    MANUAL_REVIEW
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleEvaluationStatus
{
    PASS,
    FAIL,
    MANUAL_REVIEW,
    WARNING,
    NOT_APPLICABLE
}

public sealed class RuleEvaluation
{
    public string Rule { get; set; } = string.Empty;
    public RuleEvaluationStatus Status { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class ValidationWarning
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class ExtractedFieldResponse
{
    public string Field { get; set; } = string.Empty;
    public string? Value { get; set; }
    public double Confidence { get; set; }
    public bool Masked { get; set; }
}

public sealed class ExtractedFieldValue
{
    public string? Value { get; set; }
    public double Confidence { get; set; }
    public string Source { get; set; } = "ocr";
}

public sealed class NormalizedExtractionResult
{
    public string DetectedDocumentType { get; set; } = "UNKNOWN";
    public double OcrConfidence { get; set; }
    public Dictionary<string, ExtractedFieldValue> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ValidationWarning> Warnings { get; set; } = [];
}

public sealed class ValidationAuditRecord
{
    public string ValidationId { get; set; } = string.Empty;
    [JsonIgnore]
    public string ClientId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string DetectedDocumentType { get; set; } = "UNKNOWN";
    public string Policy { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public ValidationDecisionStatus Status { get; set; }
    public int Score { get; set; }
    public double Confidence { get; set; }
    public bool ManualReviewRequired { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public long ProcessingTimeMs { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public List<RuleEvaluation> Validations { get; set; } = [];
}
