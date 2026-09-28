using System.Security.Claims;
using DocumentAIService.Models.V1;
using DocumentAIService.Security;
using DocumentAIService.Services.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocumentAIService.Controllers.V1;

[ApiController]
[Route("api/v1/validation")]
[Authorize(Policy = "Validator")]
public sealed class DocumentValidationV1Controller(IFileInspector fileInspector, IDocumentValidationV1Service validationService, IValidationAuditStore auditStore) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("validation")]
    [ProducesResponseType<ValidationV1Response>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ValidationV1Response>> Validate([FromBody] ValidationV1Request request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentType) || string.IsNullOrWhiteSpace(request.Policy) || string.IsNullOrWhiteSpace(request.File))
            return BadRequest(Problem(title: "documentType, policy e file são obrigatórios.", statusCode: StatusCodes.Status400BadRequest));
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) && (request.IdempotencyKey.Length > 128 || !System.Text.RegularExpressions.Regex.IsMatch(request.IdempotencyKey, "^[A-Za-z0-9_.:-]+$")))
            return BadRequest(Problem(title: "idempotencyKey inválida.", statusCode: StatusCodes.Status400BadRequest));

        byte[] fileData;
        try { fileData = Convert.FromBase64String(request.File); }
        catch (FormatException) { return BadRequest(Problem(title: "Arquivo Base64 inválido.", statusCode: StatusCodes.Status400BadRequest)); }

        var inspection = fileInspector.Inspect(fileData);
        if (!inspection.IsValid)
            return BadRequest(ProblemWithCode(inspection.ErrorMessage!, inspection.ErrorCode!));

        try
        {
            var clientId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            var result = await validationService.ValidateAsync(request, fileData, inspection.MediaType!, clientId, HttpContext.TraceIdentifier, cancellationToken);
            return Ok(result);
        }
        catch (ValidationInputException ex)
        {
            return BadRequest(ProblemWithCode(ex.Message, ex.Code));
        }
    }

    [HttpGet("{validationId}")]
    public ActionResult<ValidationAuditRecord> Get(string validationId)
    {
        var record = auditStore.Get(validationId);
        if (record is null) return NotFound();
        var clientId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.Equals(record.ClientId, clientId, StringComparison.Ordinal)) return Forbid();
        return Ok(record);
    }

    private static ProblemDetails ProblemWithCode(string title, string code)
    {
        var details = new ProblemDetails { Title = title, Status = StatusCodes.Status400BadRequest };
        details.Extensions["code"] = code;
        return details;
    }
}
