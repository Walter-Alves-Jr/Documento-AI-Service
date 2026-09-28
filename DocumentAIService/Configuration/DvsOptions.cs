namespace DocumentAIService.Configuration;

public sealed class DvsOptions
{
    public const string SectionName = "DocumentValidation";
    public SecurityOptions Security { get; set; } = new();
    public CorsOptions Cors { get; set; } = new();
    public FileValidationOptions Files { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
    public CatalogOptions Catalog { get; set; } = new();
    public AuditOptions Audit { get; set; } = new();
}

public sealed class SecurityOptions
{
    public List<ApiKeyOptions> ApiKeys { get; set; } = [];
}

public sealed class ApiKeyOptions
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Role { get; set; } = "validator";
    public bool Active { get; set; } = true;
}

public sealed class CorsOptions
{
    public List<string> AllowedOrigins { get; set; } = [];
}

public sealed class FileValidationOptions
{
    public int MaxBytes { get; set; } = 10 * 1024 * 1024;
    public List<string> AllowedTypes { get; set; } = ["application/pdf", "image/jpeg", "image/png"];
}

public sealed class RateLimitOptions
{
    public int PermitLimit { get; set; } = 30;
    public int WindowSeconds { get; set; } = 60;
}

public sealed class CatalogOptions
{
    public string FilePath { get; set; } = "configuration/validation-catalog.json";
}

public sealed class AuditOptions
{
    public int RetentionDays { get; set; } = 30;
}
