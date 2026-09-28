using System.Collections.Concurrent;
using DocumentAIService.Configuration;
using DocumentAIService.Models.V1;
using Microsoft.Extensions.Options;

namespace DocumentAIService.Services.V1;

public interface IValidationAuditStore
{
    void Save(ValidationAuditRecord record);
    ValidationAuditRecord? Get(string validationId);
}

public interface IIdempotencyStore
{
    bool TryGet(string clientId, string key, out ValidationV1Response response);
    void Save(string clientId, string key, ValidationV1Response response);
}

/// <summary>
/// Armazena somente metadados de auditoria. Não recebe imagem, Base64, OCR bruto ou campos pessoais.
/// Substituir por repositório transacional em produção distribuída.
/// </summary>
public sealed class InMemoryValidationAuditStore(IOptions<DvsOptions> options) : IValidationAuditStore
{
    private readonly ConcurrentDictionary<string, ValidationAuditRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _retention = TimeSpan.FromDays(Math.Max(1, options.Value.Audit.RetentionDays));

    public void Save(ValidationAuditRecord record)
    {
        PurgeExpired();
        _records[record.ValidationId] = record;
    }

    public ValidationAuditRecord? Get(string validationId)
    {
        PurgeExpired();
        return _records.TryGetValue(validationId, out var record) ? record : null;
    }

    private void PurgeExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - _retention;
        foreach (var item in _records.Where(x => x.Value.CreatedAt < cutoff))
            _records.TryRemove(item.Key, out _);
    }
}

/// <summary>
/// Evita reprocessamento por repetição de uma mesma chamada no mesmo cliente durante a janela de retenção.
/// Não armazena arquivo nem OCR; a resposta já contém somente campos minimizados.
/// </summary>
public sealed class InMemoryIdempotencyStore(IOptions<DvsOptions> options) : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset CreatedAt, ValidationV1Response Response)> _items = new(StringComparer.Ordinal);
    private readonly TimeSpan _retention = TimeSpan.FromDays(Math.Max(1, options.Value.Audit.RetentionDays));

    public bool TryGet(string clientId, string key, out ValidationV1Response response)
    {
        PurgeExpired();
        if (_items.TryGetValue(BuildKey(clientId, key), out var item))
        {
            response = item.Response;
            return true;
        }
        response = null!;
        return false;
    }

    public void Save(string clientId, string key, ValidationV1Response response)
    {
        PurgeExpired();
        _items.TryAdd(BuildKey(clientId, key), (DateTimeOffset.UtcNow, response));
    }

    private void PurgeExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - _retention;
        foreach (var item in _items.Where(x => x.Value.CreatedAt < cutoff)) _items.TryRemove(item.Key, out _);
    }

    private static string BuildKey(string clientId, string key) => $"{clientId}:{key}";
}
