using System.Text.Json;
using DocumentAIService.Models.V1;
using DocumentAIService.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentAIService.Controllers.V1;

[ApiController]
[Route("api/v1/admin/external-providers")]
[Authorize(Policy = "Admin")]
public sealed class ExternalProviderAdminController(DocumentValidationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExternalProviderConfigurationResponse>>> List([FromQuery] string? documentType, CancellationToken cancellationToken)
    {
        var query = db.ExternalProviderConfigurations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(documentType)) query = query.Where(x => x.DocumentType == documentType);
        var values = await query.OrderBy(x => x.DocumentType).ThenBy(x => x.Code).ToListAsync(cancellationToken);
        return Ok(values.Select(ToResponse).ToList());
    }

    [HttpGet("{code}/{documentType}")]
    public async Task<ActionResult<ExternalProviderConfigurationResponse>> Get(string code, string documentType, CancellationToken cancellationToken)
    {
        var value = await db.ExternalProviderConfigurations.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code && x.DocumentType == documentType, cancellationToken);
        return value is null ? NotFound() : Ok(ToResponse(value));
    }

    [HttpPut("{code}/{documentType}")]
    public async Task<ActionResult<ExternalProviderConfigurationResponse>> Upsert(string code, string documentType, [FromBody] ExternalProviderConfigurationRequest request, CancellationToken cancellationToken)
    {
        if (!code.Equals(request.Code, StringComparison.OrdinalIgnoreCase) || !documentType.Equals(request.DocumentType, StringComparison.OrdinalIgnoreCase))
            return BadRequest(Problem(title: "Code e documentType da rota devem corresponder ao corpo.", statusCode: StatusCodes.Status400BadRequest));
        if (!IsCode(code) || !IsCode(documentType)) return BadRequest(Problem(title: "Code e documentType devem conter somente letras, números, hífen ou sublinhado.", statusCode: StatusCodes.Status400BadRequest));
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("https" or "http")) return BadRequest(Problem(title: "Endpoint absoluto HTTP/HTTPS é obrigatório.", statusCode: StatusCodes.Status400BadRequest));
        if (!new[] { "GET", "POST", "PUT" }.Contains(request.HttpMethod, StringComparer.OrdinalIgnoreCase)) return BadRequest(Problem(title: "httpMethod deve ser GET, POST ou PUT.", statusCode: StatusCodes.Status400BadRequest));
        if (request.TimeoutSeconds is < 1 or > 120 || request.TtlDays is < 1 or > 3650 || request.EstimatedCost < 0) return BadRequest(Problem(title: "Timeout, TTL ou custo estimado inválidos.", statusCode: StatusCodes.Status400BadRequest));
        if (request.Headers.Keys.Concat(request.Parameters.Keys).Any(IsSensitiveConfigurationKey)) return BadRequest(Problem(title: "Headers e parâmetros não podem conter credenciais. Use secretReference para apontar a um segredo gerenciado.", statusCode: StatusCodes.Status400BadRequest));
        if (!string.IsNullOrWhiteSpace(request.SecretReference) && !System.Text.RegularExpressions.Regex.IsMatch(request.SecretReference, "^[A-Za-z0-9_.:-]{1,200}$")) return BadRequest(Problem(title: "secretReference inválida.", statusCode: StatusCodes.Status400BadRequest));

        var now = DateTimeOffset.UtcNow;
        var value = await db.ExternalProviderConfigurations.FirstOrDefaultAsync(x => x.Code == code && x.DocumentType == documentType, cancellationToken);
        if (value is null)
        {
            value = new ExternalProviderConfiguration { Code = code.ToUpperInvariant(), DocumentType = documentType.ToUpperInvariant(), CreatedAt = now };
            db.ExternalProviderConfigurations.Add(value);
        }

        value.Endpoint = endpoint.ToString();
        value.HttpMethod = request.HttpMethod.ToUpperInvariant();
        value.AuthenticationType = request.AuthenticationType.Trim().ToLowerInvariant();
        value.SecretReference = request.SecretReference?.Trim();
        value.HeadersJson = JsonSerializer.Serialize(request.Headers);
        value.ParametersJson = JsonSerializer.Serialize(request.Parameters);
        value.TimeoutSeconds = request.TimeoutSeconds;
        value.TtlDays = request.TtlDays;
        value.EstimatedCost = request.EstimatedCost;
        value.Active = request.Active;
        value.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(value));
    }

    private static bool IsCode(string value) => value.Length is > 1 and <= 100 && System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_-]+$");
    private static bool IsSensitiveConfigurationKey(string name) =>
        name.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("key", StringComparison.OrdinalIgnoreCase);

    private static ExternalProviderConfigurationResponse ToResponse(ExternalProviderConfiguration value) => new()
    {
        Id = value.Id,
        Code = value.Code,
        DocumentType = value.DocumentType,
        Endpoint = value.Endpoint,
        HttpMethod = value.HttpMethod,
        AuthenticationType = value.AuthenticationType,
        SecretReference = value.SecretReference,
        Headers = Deserialize(value.HeadersJson),
        Parameters = Deserialize(value.ParametersJson),
        TimeoutSeconds = value.TimeoutSeconds,
        TtlDays = value.TtlDays,
        EstimatedCost = value.EstimatedCost,
        Active = value.Active,
        UpdatedAt = value.UpdatedAt
    };

    private static Dictionary<string, string> Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }
}
