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
    private readonly IDirecaoDefensivaConfigService _ddConfigService;
    private readonly ILogger<ValidationController> _logger;

    public ValidationController(
        IDocumentAnalysisService analysisService,
        IPdfConverterService pdfConverterService,
        IDirecaoDefensivaConfigService ddConfigService,
        ILogger<ValidationController> logger)
    {
        _analysisService = analysisService;
        _pdfConverterService = pdfConverterService;
        _ddConfigService = ddConfigService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<ValidationResponse>> ValidateDocument([FromBody] ValidationRequest request)
    {
        if (string.IsNullOrEmpty(request.TipoDocumento) || string.IsNullOrEmpty(request.Base64Arquivo))
            return BadRequest(new { error = "tipoDocumento e base64Arquivo são obrigatórios" });

        try
        {
            byte[] fileData = Convert.FromBase64String(request.Base64Arquivo);

            if (fileData.Length > 10 * 1024 * 1024)
                return BadRequest(new { error = "Arquivo muito grande (máximo 10MB)" });

            byte[] imageData = fileData;
            if (IsPdf(fileData))
            {
                _logger.LogInformation("PDF detectado, convertendo para imagem");
                imageData = await _pdfConverterService.ConvertPdfToImageAsync(fileData);
            }

            var response = await _analysisService.ValidateDocumentAsync(request.TipoDocumento, imageData);
            _logger.LogInformation($"Validação concluída. Tipo: {request.TipoDocumento}, Status: {response.Status}");
            return Ok(response);
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "base64Arquivo inválido" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Erro na validação: {ex.Message}");
            return StatusCode(500, new { error = "Erro ao processar documento", details = ex.Message });
        }
    }

    [HttpGet("health")]
    public ActionResult<object> Health()
    {
        return Ok(new
        {
            status = "OK",
            timestamp = DateTime.UtcNow,
            version = "2.0.0"
        });
    }

    // ─── ENDPOINTS DE CONFIGURAÇÃO DE DIREÇÃO DEFENSIVA ──────────────────────

    [HttpGet("config/direcao-defensiva")]
    public ActionResult<DirecaoDefensivaConfig> GetDirecaoDefensivaConfig()
    {
        return Ok(_ddConfigService.GetConfig());
    }

    [HttpPut("config/direcao-defensiva")]
    public ActionResult UpdateDirecaoDefensivaConfig([FromBody] DirecaoDefensivaConfig config)
    {
        try
        {
            _ddConfigService.UpdateConfig(config);
            return Ok(new { message = "Configuração atualizada com sucesso", config });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Erro ao salvar configuração", details = ex.Message });
        }
    }

    [HttpPost("config/direcao-defensiva/escolas")]
    public ActionResult AddEscolaAprovada([FromBody] string escola)
    {
        var config = _ddConfigService.GetConfig();
        if (!config.EscolasAprovadas.Contains(escola, StringComparer.OrdinalIgnoreCase))
        {
            config.EscolasAprovadas.Add(escola);
            _ddConfigService.UpdateConfig(config);
            return Ok(new { message = $"Escola '{escola}' adicionada com sucesso", total = config.EscolasAprovadas.Count });
        }
        return Conflict(new { message = $"Escola '{escola}' já está na lista" });
    }

    [HttpDelete("config/direcao-defensiva/escolas/{escola}")]
    public ActionResult RemoveEscolaAprovada(string escola)
    {
        var config = _ddConfigService.GetConfig();
        var removed = config.EscolasAprovadas.RemoveAll(e => e.Equals(escola, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            _ddConfigService.UpdateConfig(config);
            return Ok(new { message = $"Escola '{escola}' removida com sucesso" });
        }
        return NotFound(new { message = $"Escola '{escola}' não encontrada na lista" });
    }

    [HttpPut("config/direcao-defensiva/carga-horaria/{horas}")]
    public ActionResult UpdateCargaHorariaMinima(int horas)
    {
        if (horas < 1 || horas > 200)
            return BadRequest(new { error = "Carga horária deve ser entre 1 e 200 horas" });

        var config = _ddConfigService.GetConfig();
        config.CargaHorariaMinimaHoras = horas;
        _ddConfigService.UpdateConfig(config);
        return Ok(new { message = $"Carga horária mínima atualizada para {horas}h" });
    }

    private bool IsPdf(byte[] data)
    {
        return data.Length >= 4 &&
               data[0] == 0x25 && data[1] == 0x50 &&
               data[2] == 0x44 && data[3] == 0x46;
    }
}
