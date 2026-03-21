using DocumentAIService.Models;
using System.Text.Json;

namespace DocumentAIService.Services;

public interface IDirecaoDefensivaConfigService
{
    DirecaoDefensivaConfig GetConfig();
    void UpdateConfig(DirecaoDefensivaConfig config);
    bool IsEscolaAprovada(string textoDocumento);
    bool IsCargaHorariaAdequada(int horas);
    bool IsCursoAceito(string nomeCurso);
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

    public bool IsEscolaAprovada(string textoDocumento)
    {
        if (string.IsNullOrEmpty(textoDocumento)) return false;
        var textoLower = textoDocumento.ToLower()
            .Replace("ç", "c").Replace("ã", "a").Replace("á", "a")
            .Replace("é", "e").Replace("ê", "e").Replace("í", "i")
            .Replace("ó", "o").Replace("ô", "o").Replace("ú", "u");

        foreach (var escola in _config.EscolasAprovadas)
        {
            var escolaLower = escola.ToLower()
                .Replace("ç", "c").Replace("ã", "a").Replace("á", "a")
                .Replace("é", "e").Replace("ê", "e").Replace("í", "i")
                .Replace("ó", "o").Replace("ô", "o").Replace("ú", "u");

            if (textoLower.Contains(escolaLower))
            {
                _logger.LogInformation($"Escola aprovada encontrada: {escola}");
                return true;
            }
        }
        return false;
    }

    public bool IsCargaHorariaAdequada(int horas)
    {
        return horas >= _config.CargaHorariaMinimaHoras;
    }

    public bool IsCursoAceito(string nomeCurso)
    {
        if (string.IsNullOrEmpty(nomeCurso)) return false;
        var cursoLower = nomeCurso.ToLower()
            .Replace("ç", "c").Replace("ã", "a").Replace("á", "a")
            .Replace("é", "e").Replace("ê", "e").Replace("í", "i")
            .Replace("ó", "o").Replace("ô", "o").Replace("ú", "u");

        foreach (var curso in _config.CursosAceitos)
        {
            var cursoAceitoLower = curso.ToLower()
                .Replace("ç", "c").Replace("ã", "a").Replace("á", "a")
                .Replace("é", "e").Replace("ê", "e").Replace("í", "i")
                .Replace("ó", "o").Replace("ô", "o").Replace("ú", "u");

            if (cursoLower.Contains(cursoAceitoLower) || cursoAceitoLower.Contains(cursoLower))
            {
                return true;
            }
        }
        return false;
    }
}
