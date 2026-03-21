using DocumentAIService.Models;
using DocumentAIService.Services;
using Microsoft.AspNetCore.Mvc;

namespace DocumentAIService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ValidationController : ControllerBase
    {
        private readonly IDocumentAnalysisService _analysisService;
        private readonly IPdfConverterService _pdfConverterService;
        private readonly ILogger<ValidationController> _logger;

        public ValidationController(IDocumentAnalysisService analysisService, IPdfConverterService pdfConverterService, ILogger<ValidationController> logger)
        {
            _analysisService = analysisService;
            _pdfConverterService = pdfConverterService;
            _logger = logger;
        }

    [HttpPost]
    public async Task<ActionResult<ValidationResponse>> ValidateDocument([FromBody] ValidationRequest request)
    {
        if (string.IsNullOrEmpty(request.TipoDocumento) || string.IsNullOrEmpty(request.Base64Arquivo))
        {
            return BadRequest(new { error = "tipoDocumento e base64Arquivo são obrigatórios" });
        }

        try
            {
                // Decodificar base64
                byte[] fileData = Convert.FromBase64String(request.Base64Arquivo);

                // Validar tamanho (máximo 5MB)
                if (fileData.Length > 5 * 1024 * 1024)
                {
                    return BadRequest(new { error = "Arquivo muito grande (máximo 5MB)" });
                }

                // Se for PDF, converter para imagem
                byte[] imageData = fileData;
                if (IsPdf(fileData))
                {
                    _logger.LogInformation("PDF detected, converting to image");
                    imageData = await _pdfConverterService.ConvertPdfToImageAsync(fileData);
                }

                // Processar validação
                var response = await _analysisService.ValidateDocumentAsync(request.TipoDocumento, imageData);

            _logger.LogInformation($"Validation request processed. Type: {request.TipoDocumento}, Status: {response.Status}");

            return Ok(response);
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "base64Arquivo inválido" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Validation error: {ex.Message}");
            return StatusCode(500, new { error = "Erro ao processar documento", details = ex.Message });
        }
    }

    [HttpGet("health")]
        public ActionResult<object> Health()
        {
            return Ok(new { status = "OK", timestamp = DateTime.UtcNow });
        }

        private bool IsPdf(byte[] data)
        {
            // Verificar assinatura PDF (primeiros 4 bytes: %PDF)
            return data.Length >= 4 && 
                   data[0] == 0x25 && data[1] == 0x50 && 
                   data[2] == 0x44 && data[3] == 0x46;
        }
    }
