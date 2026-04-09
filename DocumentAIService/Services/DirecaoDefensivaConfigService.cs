using DocumentAIService.Models;
using System.Text.Json;

namespace DocumentAIService.Services;

public interface IDirecaoDefensivaConfigService
{
    DirecaoDefensivaConfig GetConfig();
    void UpdateConfig(DirecaoDefensivaConfig config);
    bool IsEscolaAprovada(string textoDocumento);
    bool IsCargaHorariaAdequada(int horas, string textoDocumento);
    bool IsCursoAceito(string nomeCurso);
    bool IsEscolaSestSenat(string textoDocumento);
    int GetCargaHorariaMinimaParaEscola(string textoDocumento);
    bool IsMotoristasInstrutorHomologado(string textoDocumento);
}

public class DirecaoDefensivaConfigService : IDirecaoDefensivaConfigService
{
    private DirecaoDefensivaConfig _config;
    private readonly ILogger<DirecaoDefensivaConfigService> _logger;
    private readonly string _configFilePath;

    public DirecaoDefensivaConfigService(ILogger<DirecaoDefensivaConfigService> logger)
    {
        _logger = logger;
        _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "direcao_defensiva_config.json");
        _config = LoadConfig();
    }

    private DirecaoDefensivaConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<DirecaoDefensivaConfig>(json);
                if (config != null)
                {
                    _logger.LogInformation("Configuração de Direção Defensiva carregada do arquivo.");
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Erro ao carregar configuração: {ex.Message}. Usando configuração padrão.");
        }

        return new DirecaoDefensivaConfig();
    }

    public DirecaoDefensivaConfig GetConfig() => _config;

    public void UpdateConfig(DirecaoDefensivaConfig config)
    {
        _config = config;
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
            _logger.LogInformation("Configuração de Direção Defensiva salva.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Erro ao salvar configuração: {ex.Message}");
        }
    }

    private string NormalizeText(string text)
    {
        return text.ToLower()
            .Replace("ç", "c").Replace("ã", "a").Replace("á", "a")
            .Replace("é", "e").Replace("ê", "e").Replace("í", "i")
            .Replace("ó", "o").Replace("ô", "o").Replace("ú", "u")
            .Replace("à", "a").Replace("â", "a").Replace("î", "i")
            .Replace("û", "u").Replace("ü", "u");
    }

    public bool IsEscolaSestSenat(string textoDocumento)
    {
        if (string.IsNullOrEmpty(textoDocumento)) return false;
        var textoNorm = NormalizeText(textoDocumento);
        return textoNorm.Contains("sest senat") || textoNorm.Contains("sestsenat") ||
               textoNorm.Contains("sest/senat") ||
               textoNorm.Contains("servico nacional de aprendizagem do transporte");
    }

    public int GetCargaHorariaMinimaParaEscola(string textoDocumento)
    {
        // SEST SENAT: mínimo 4 horas (regra BRF)
        if (IsEscolaSestSenat(textoDocumento))
            return _config.CargaHorariaMinimaHorasSestSenat;

        // Demais escolas: mínimo 8 horas (regra BRF)
        return _config.CargaHorariaMinimaHoras;
    }

    public bool IsEscolaAprovada(string textoDocumento)
    {
        if (string.IsNullOrEmpty(textoDocumento)) return false;
        var textoNorm = NormalizeText(textoDocumento);

        foreach (var escola in _config.EscolasAprovadas)
        {
            var escolaNorm = NormalizeText(escola);
            if (textoNorm.Contains(escolaNorm))
            {
                _logger.LogInformation($"Escola aprovada encontrada: {escola}");
                return true;
            }
        }
        // Verificar também motoristas instrutores homologados
        return IsMotoristasInstrutorHomologado(textoDocumento);
    }

    public bool IsMotoristasInstrutorHomologado(string textoDocumento)
    {
        if (string.IsNullOrEmpty(textoDocumento)) return false;
        var textoNorm = NormalizeText(textoDocumento);

        foreach (var instrutor in _config.MotoristasInstrutoresHomologados)
        {
            var instrutorNorm = NormalizeText(instrutor);
            var partes = instrutorNorm.Split(' ');
            if (partes.Length >= 2)
            {
                bool encontrado = textoNorm.Contains(instrutorNorm);
                if (!encontrado && partes.Length >= 3)
                {
                    var parcial = partes[0] + " " + partes[partes.Length - 1];
                    encontrado = textoNorm.Contains(parcial);
                }
                if (encontrado)
                {
                    _logger.LogInformation($"Motorista instrutor homologado encontrado: {instrutor}");
                    return true;
                }
            }
        }
        return false;
    }

    public bool IsCargaHorariaAdequada(int horas, string textoDocumento)
    {
        int minimo = GetCargaHorariaMinimaParaEscola(textoDocumento);
        return horas >= minimo;
    }

    public bool IsCursoAceito(string nomeCurso)
    {
        if (string.IsNullOrEmpty(nomeCurso)) return false;
        var cursoNorm = NormalizeText(nomeCurso);

        foreach (var curso in _config.CursosAceitos)
        {
            var cursoAceitoNorm = NormalizeText(curso);
            if (cursoNorm.Contains(cursoAceitoNorm) || cursoAceitoNorm.Contains(cursoNorm))
                return true;
        }
        return false;
    }
}
