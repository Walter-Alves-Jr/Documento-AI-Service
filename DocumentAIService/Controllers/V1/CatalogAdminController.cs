using DocumentAIService.Models.Catalog;
using DocumentAIService.Services.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentAIService.Controllers.V1;

[ApiController]
[Route("api/v1/admin/catalog")]
[Authorize(Policy = "Admin")]
public sealed class CatalogAdminController(IValidationCatalogService catalog) : ControllerBase
{
    [HttpGet("document-types")]
    public ActionResult<IReadOnlyList<DocumentTypeDefinition>> ListDocumentTypes() => Ok(catalog.GetDocumentTypes(false));

    [HttpGet("document-types/{code}")]
    public ActionResult<DocumentTypeDefinition> GetDocumentType(string code)
    {
        var value = catalog.GetDocumentType(code);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPut("document-types/{code}")]
    public ActionResult<DocumentTypeDefinition> UpsertDocumentType(string code, [FromBody] DocumentTypeDefinition documentType)
    {
        if (!code.Equals(documentType.Code, StringComparison.OrdinalIgnoreCase)) return BadRequest(Problem(title: "O código da rota deve ser igual ao código do corpo.", statusCode: 400));
        try
        {
            catalog.UpsertDocumentType(documentType);
            return Ok(catalog.GetDocumentType(documentType.Code));
        }
        catch (ArgumentException ex) { return BadRequest(Problem(title: ex.Message, statusCode: 400)); }
    }

    [HttpGet("policies")]
    public ActionResult<IReadOnlyList<ValidationPolicyDefinition>> ListPolicies([FromQuery] string? documentType) => Ok(catalog.GetPolicies(documentType, false));

    [HttpGet("policies/{code}")]
    public ActionResult<ValidationPolicyDefinition> GetPolicy(string code)
    {
        var value = catalog.GetPolicy(code);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPut("policies/{code}")]
    public ActionResult<ValidationPolicyDefinition> UpsertPolicy(string code, [FromBody] ValidationPolicyDefinition policy)
    {
        if (!code.Equals(policy.Code, StringComparison.OrdinalIgnoreCase)) return BadRequest(Problem(title: "O código da rota deve ser igual ao código do corpo.", statusCode: 400));
        try
        {
            catalog.UpsertPolicy(policy);
            return Ok(catalog.GetPolicy(policy.Code));
        }
        catch (ArgumentException ex) { return BadRequest(Problem(title: ex.Message, statusCode: 400)); }
    }
}
