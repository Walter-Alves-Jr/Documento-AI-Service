namespace DocumentAIService.Models.Catalog;

public sealed class DocumentTypeDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string Version { get; set; } = "1.0";
    public List<string> IdentificationPatterns { get; set; } = [];
    public List<FieldDefinition> Fields { get; set; } = [];
}

public sealed class FieldDefinition
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "string";
    public bool Required { get; set; }
    public string Sensitivity { get; set; } = "personal";
    public string ResponseMode { get; set; } = "mask";
    public List<string> ExtractionPatterns { get; set; } = [];
}

public sealed class ValidationPolicyDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public bool Active { get; set; } = true;
    public double MinimumConfidence { get; set; } = 0.5;
    public List<RuleDefinition> Rules { get; set; } = [];
}

public sealed class RuleDefinition
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string? ContextKey { get; set; }
    public string Severity { get; set; } = "reject";
    public string UnknownOutcome { get; set; } = "manual_review";
    public System.Text.Json.JsonElement? ExpectedValue { get; set; }
    public System.Text.Json.JsonElement? Parameters { get; set; }
}

public sealed class ValidationCatalog
{
    public string CatalogVersion { get; set; } = "1.0";
    public List<DocumentTypeDefinition> DocumentTypes { get; set; } = [];
    public List<ValidationPolicyDefinition> Policies { get; set; } = [];
}
