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

    // Campos específicos para Direção Defensiva
    public string? NomeCurso { get; set; }
    public string? Escola { get; set; }
    public int? CargaHorariaHoras { get; set; }
    public string? DataInicioCurso { get; set; }
    public string? DataFimCurso { get; set; }
    public bool EscolaAprovada { get; set; } = false;
    public bool CargaHorariaAdequada { get; set; } = false;

    // Campos específicos para ASO
    public string? DataRealizacao { get; set; }
    public string? Resultado { get; set; }  // Apto / Inapto / Apto com restrições
    public string? Medico { get; set; }
    public string? Empresa { get; set; }
}

// Configuração de escolas aprovadas e carga horária mínima
public class DirecaoDefensivaConfig
{
    public List<string> EscolasAprovadas { get; set; } = new()
    {
        "SEST SENAT",
        "SESTSENAT",
        "SEST/SENAT",
        "Servico Nacional de Aprendizagem do Transporte",
        "SENAC",
        "SENAI",
        "CFC",
        "Centro de Formacao de Condutores",
        "Autoescola",
        "Auto Escola"
    };

    public int CargaHorariaMinimaHoras { get; set; } = 8;

    public List<string> CursosAceitos { get; set; } = new()
    {
        "Direcao Preventiva",
        "Direcao Defensiva",
        "Atualizacao de Direcao",
        "Seguranca no Transito",
        "Transporte de Cargas",
        "Motorista Profissional"
    };
}
