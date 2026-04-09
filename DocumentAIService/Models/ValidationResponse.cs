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
    public int? CargaHorariaMinimaExigida { get; set; }
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
    // ─── ESCOLAS HOMOLOGADAS BRF (lista oficial — imagem WhatsApp 02/04/2026) ───
    public List<string> EscolasAprovadas { get; set; } = new()
    {
        "Hartmann",
        "Hartmann treinamentos",
        "Inttergramed",
        "Eco Trainning",
        "Eco Training",
        "SEST SENAT",
        "SESTSENAT",
        "SEST/SENAT",
        "Servico Nacional de Aprendizagem do Transporte",
        "Serviço Nacional de Aprendizagem do Transporte",
        "Concordia Treinamentos",
        "Concórdia Treinamentos",
        "Concordia",
        "FABET",
        "Champonalli",
        "Champonalli Centro de Formacao de Condutores",
        "Centro de Formacao de Condutores",
        "Centro de Formação de Condutores",
        "CERTO",
        "Centro de Referencia em Treinamento Operacional",
        "Centro de Referência em Treinamento Operacional",
        "CIT Drive",
        "Consultoria Integrada ao Transportador",
        // Variações comuns de OCR
        "SEST",
        "SENAT"
    };

    // ─── MOTORISTAS INSTRUTORES HOMOLOGADOS BRF ───────────────────────────────
    // Certificados emitidos por estes instrutores também são válidos (carga mín. 8h)
    public List<string> MotoristasInstrutoresHomologados { get; set; } = new()
    {
        "Jussy Ane Ferreira da Silva",
        "Gustavo Myller Novati",
        "Everton da Silva Barboza",
        "Raul Pereira Goetten",
        "Emilio Domingues Neto",
        "Claudemir Ravali",
        "Gilmar Alves dos Santos",
        "Eliana Lima dos Santos",
        "Messias Emilio dos Santos",
        "Sergio Ferreira Lima",
        "Adelar Steffler",
        "Marcio Jose da Silva Canto",
        "Julia Lagos Miguel",
        "Argeu Pusini",
    };

    // Carga horária mínima padrão (para escolas que não são SEST SENAT)
    public int CargaHorariaMinimaHoras { get; set; } = 8;

    // Carga horária mínima específica para SEST SENAT (4 horas conforme regra BRF)
    public int CargaHorariaMinimaHorasSestSenat { get; set; } = 4;

    public List<string> CursosAceitos { get; set; } = new()
    {
        "Direcao Preventiva",
        "Direcao Defensiva",
        "Atualizacao de Direcao",
        "Seguranca no Transito",
        "Transporte de Cargas",
        "Motorista Profissional",
        "Atualização de Direção",
        "Direção Preventiva",
        "Direção Defensiva",
        "Operacoes Logisticas",
        "Operações Logísticas",
    };
}
