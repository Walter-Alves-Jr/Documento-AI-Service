using System.Text.Json;
using DocumentAIService.Configuration;
using DocumentAIService.Models.Catalog;
using Microsoft.Extensions.Options;

namespace DocumentAIService.Services.V1;

public interface IValidationCatalogService
{
    IReadOnlyList<DocumentTypeDefinition> GetDocumentTypes(bool activeOnly = true);
    IReadOnlyList<ValidationPolicyDefinition> GetPolicies(string? documentType = null, bool activeOnly = true);
    DocumentTypeDefinition? GetDocumentType(string code);
    ValidationPolicyDefinition? GetPolicy(string code);
    void UpsertDocumentType(DocumentTypeDefinition documentType);
    void UpsertPolicy(ValidationPolicyDefinition policy);
}

public sealed class ValidationCatalogService : IValidationCatalogService
{
    private readonly ILogger<ValidationCatalogService> _logger;
    private readonly string _catalogPath;
    private readonly ReaderWriterLockSlim _lock = new();
    private ValidationCatalog _catalog;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public ValidationCatalogService(IHostEnvironment environment, IOptions<DvsOptions> options, ILogger<ValidationCatalogService> logger)
    {
        _logger = logger;
        _catalogPath = Path.Combine(environment.ContentRootPath, options.Value.Catalog.FilePath);
        _catalog = LoadCatalog();
    }

    public IReadOnlyList<DocumentTypeDefinition> GetDocumentTypes(bool activeOnly = true)
    {
        _lock.EnterReadLock();
        try
        {
            return _catalog.DocumentTypes
                .Where(x => !activeOnly || x.Active)
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Select(Clone).ToList();
        }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyList<ValidationPolicyDefinition> GetPolicies(string? documentType = null, bool activeOnly = true)
    {
        _lock.EnterReadLock();
        try
        {
            return _catalog.Policies
                .Where(x => (!activeOnly || x.Active) && (string.IsNullOrWhiteSpace(documentType) || x.DocumentType.Equals(documentType, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Select(Clone).ToList();
        }
        finally { _lock.ExitReadLock(); }
    }

    public DocumentTypeDefinition? GetDocumentType(string code) =>
        GetDocumentTypes(false).FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public ValidationPolicyDefinition? GetPolicy(string code) =>
        GetPolicies(null, false).FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public void UpsertDocumentType(DocumentTypeDefinition documentType)
    {
        ValidateCode(documentType.Code, "documentType");
        if (string.IsNullOrWhiteSpace(documentType.Name)) throw new ArgumentException("name é obrigatório.");
        ValidatePatterns(documentType.IdentificationPatterns, "identificationPatterns");
        foreach (var field in documentType.Fields) ValidatePatterns(field.ExtractionPatterns, $"fields.{field.Code}.extractionPatterns");
        _lock.EnterWriteLock();
        try
        {
            var index = _catalog.DocumentTypes.FindIndex(x => x.Code.Equals(documentType.Code, StringComparison.OrdinalIgnoreCase));
            documentType.Code = documentType.Code.Trim().ToUpperInvariant();
            documentType.Id = string.IsNullOrWhiteSpace(documentType.Id) ? Guid.NewGuid().ToString("N") : documentType.Id;
            documentType.Version = string.IsNullOrWhiteSpace(documentType.Version) ? "1.0" : documentType.Version;
            if (index >= 0) _catalog.DocumentTypes[index] = documentType; else _catalog.DocumentTypes.Add(documentType);
            Persist();
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void UpsertPolicy(ValidationPolicyDefinition policy)
    {
        ValidateCode(policy.Code, "policy");
        ValidateCode(policy.DocumentType, "documentType");
        if (string.IsNullOrWhiteSpace(policy.Name)) throw new ArgumentException("name é obrigatório.");
        if (!_catalog.DocumentTypes.Any(x => x.Code.Equals(policy.DocumentType, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("O tipo de documento informado não existe no catálogo.");
        if (policy.Rules.Count == 0) throw new ArgumentException("A política deve ter ao menos uma regra.");

        _lock.EnterWriteLock();
        try
        {
            var index = _catalog.Policies.FindIndex(x => x.Code.Equals(policy.Code, StringComparison.OrdinalIgnoreCase));
            policy.Code = policy.Code.Trim().ToUpperInvariant();
            policy.DocumentType = policy.DocumentType.Trim().ToUpperInvariant();
            policy.Id = string.IsNullOrWhiteSpace(policy.Id) ? Guid.NewGuid().ToString("N") : policy.Id;
            policy.Version = string.IsNullOrWhiteSpace(policy.Version) ? "1.0" : policy.Version;
            if (index >= 0) _catalog.Policies[index] = policy; else _catalog.Policies.Add(policy);
            Persist();
        }
        finally { _lock.ExitWriteLock(); }
    }

    private ValidationCatalog LoadCatalog()
    {
        if (!File.Exists(_catalogPath))
            throw new InvalidOperationException($"Catálogo de validação não encontrado: {_catalogPath}");
        var json = File.ReadAllText(_catalogPath);
        return JsonSerializer.Deserialize<ValidationCatalog>(json, JsonOptions) ?? throw new InvalidOperationException("Catálogo de validação inválido.");
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
        var tempPath = _catalogPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_catalog, JsonOptions));
        File.Move(tempPath, _catalogPath, true);
        _logger.LogInformation("Catálogo de validação atualizado. DocumentTypes: {DocumentTypes}; Policies: {Policies}", _catalog.DocumentTypes.Count, _catalog.Policies.Count);
    }

    private static void ValidateCode(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_\\-]{2,80}$"))
            throw new ArgumentException($"{name} deve usar 2 a 80 caracteres alfanuméricos, hífen ou sublinhado.");
    }

    private static void ValidatePatterns(IEnumerable<string> patterns, string name)
    {
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 500) throw new ArgumentException($"{name} possui um padrão vazio ou maior que 500 caracteres.");
            try { _ = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)); }
            catch (ArgumentException) { throw new ArgumentException($"{name} possui expressão regular inválida."); }
        }
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;
}
