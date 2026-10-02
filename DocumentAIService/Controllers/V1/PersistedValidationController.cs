using System.Security.Claims;
using DocumentAIService.Models.V1;
using DocumentAIService.Persistence;
using DocumentAIService.Security;
using DocumentAIService.Services.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocumentAIService.Controllers.V1;

[ApiController]
[Authorize(Policy = "Validator")]
public sealed class PersistedValidationController(IFileInspector fileInspector, IPersistedValidationService validationService) : ControllerBase
{
    [HttpPost("api/v1/validations")]
    [EnableRateLimiting("validation")]
    [ProducesResponseType<PersistedValidationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PersistedValidationResponse>> Validate([FromBody] PersistedValidationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId) || request.EntityId.Length > 160)
            return BadRequest(Problem(title: "entityId é obrigatório e deve ter até 160 caracteres.", statusCode: StatusCodes.Status400BadRequest));
        if (string.IsNullOrWhiteSpace(request.DocumentType) || string.IsNullOrWhiteSpace(request.Policy) || string.IsNullOrWhiteSpace(request.File))
            return BadRequest(Problem(title: "documentType, policy e file são obrigatórios.", statusCode: StatusCodes.Status400BadRequest));
        if (!IsValidIdempotencyKey(request.IdempotencyKey))
            return BadRequest(Problem(title: "idempotencyKey inválida.", statusCode: StatusCodes.Status400BadRequest));

        byte[] fileData;
        try { fileData = Convert.FromBase64String(request.File); }
        catch (FormatException) { return BadRequest(Problem(title: "Arquivo Base64 inválido.", statusCode: StatusCodes.Status400BadRequest)); }

        var inspection = fileInspector.Inspect(fileData);
        if (!inspection.IsValid) return BadRequest(ProblemWithCode(inspection.ErrorMessage!, inspection.ErrorCode!));

        try
        {
            var clientId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            return Ok(await validationService.ValidateAsync(request, fileData, inspection.MediaType!, clientId, HttpContext.TraceIdentifier, cancellationToken));
        }
        catch (ValidationInputException ex) { return BadRequest(ProblemWithCode(ex.Message, ex.Code)); }
    }

    [HttpGet("api/v1/validations/{validationId}")]
    public async Task<ActionResult<PersistedValidationLookupResponse>> GetValidation(string validationId, CancellationToken cancellationToken)
    {
        var clientId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var record = await validationService.GetValidationAsync(validationId, clientId, cancellationToken);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpGet("api/v1/compliance/{entityType}/{entityId}")]
    public async Task<ActionResult<ComplianceSummaryResponse>> GetCompliance(string entityType, string entityId, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ComplianceEntityType>(entityType, true, out var parsedType))
            return BadRequest(Problem(title: "entityType deve ser DRIVER, VEHICLE, EQUIPMENT ou CARRIER.", statusCode: StatusCodes.Status400BadRequest));
        if (string.IsNullOrWhiteSpace(entityId) || entityId.Length > 160)
            return BadRequest(Problem(title: "entityId inválido.", statusCode: StatusCodes.Status400BadRequest));

        var clientId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var compliance = await validationService.GetComplianceAsync(parsedType, entityId, clientId, cancellationToken);
        return compliance is null ? NotFound() : Ok(compliance);
    }

    private static bool IsValidIdempotencyKey(string? key) => string.IsNullOrWhiteSpace(key) || (key.Length <= 128 && System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z0-9_.:-]+$"));

    private static ProblemDetails ProblemWithCode(string title, string code)
    {
        var details = new ProblemDetails { Title = title, Status = StatusCodes.Status400BadRequest };
        details.Extensions["code"] = code;
        return details;
    }
}
