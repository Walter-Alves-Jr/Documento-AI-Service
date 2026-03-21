namespace DocumentAIService.Models;

public class ValidationRequest
{
    public string TipoDocumento { get; set; } = string.Empty;
    public string Base64Arquivo { get; set; } = string.Empty;
}
