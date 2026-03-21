namespace DocumentAIService.Models;

public class ValidationResponse
{
    public string Status { get; set; } = string.Empty;
    public double Confianca { get; set; }
    public DadosExtraidos DadosExtraidos { get; set; } = new();
    public List<string> Motivos { get; set; } = new();
}

public class DadosExtraidos
{
    public string? Nome { get; set; }
    public string? Validade { get; set; }
    public string? NumeroDocumento { get; set; }
    public string? TextoExtraido { get; set; }
}
