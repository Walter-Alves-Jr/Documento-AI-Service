using System.Text.RegularExpressions;
using DocumentAIService.Models;

namespace DocumentAIService.Services;

public interface IDocumentAnalysisService
{
    Task<ValidationResponse> ValidateDocumentAsync(string tipoDocumento, byte[] imageData);
}

public class DocumentAnalysisService : IDocumentAnalysisService
{
    private readonly IOcrService _ocrService;
    private readonly IDirecaoDefensivaConfigService _ddConfigService;
    private readonly ILogger<DocumentAnalysisService> _logger;

    public DocumentAnalysisService(
        IOcrService ocrService,
        IDirecaoDefensivaConfigService ddConfigService,
        ILogger<DocumentAnalysisService> logger)
    {
        _ocrService = ocrService;
        _ddConfigService = ddConfigService;
        _logger = logger;
    }

    public async Task<ValidationResponse> ValidateDocumentAsync(string tipoDocumento, byte[] imageData)
    {
        var response = new ValidationResponse();
        double score = 0;

        try
        {
            var (extractedText, ocrConfidence) = await _ocrService.ExtractTextAsync(imageData);

            if (string.IsNullOrEmpty(extractedText))
            {
                response.Status = "REPROVADO";
                response.Confianca = 0;
                response.Motivos.Add("✗ Falha ao extrair texto do documento - OCR não conseguiu ler");
                return response;
            }

            if (ocrConfidence < 0.5)
            {
                response.Status = "ANÁLISE MANUAL";
                response.Confianca = 30;
                response.Motivos.Add($"⚠ Confiança do OCR muito baixa: {ocrConfidence:P}");
                response.Motivos.Add("⚠ Qualidade da imagem inadequada para validação automática");
                return response;
            }

            score += 30;
            response.Motivos.Add($"✓ OCR realizado com sucesso (confiança: {ocrConfidence:P})");

            // Rotear para validação específica por tipo de documento
            switch (tipoDocumento.ToUpper())
            {
                case "ASO":
                    score = await ValidateAso(extractedText, ocrConfidence, score, response);
                    break;
                case "DIRECAO_DEFENSIVA":
                    score = await ValidateDirecaoDefensiva(extractedText, ocrConfidence, score, response);
                    break;
                default:
                    score = await ValidateGenericDocument(extractedText, tipoDocumento, ocrConfidence, score, response);
                    break;
            }

            response.Confianca = Math.Min(100, Math.Max(0, score));

            // Determinar status final
            if (response.Status == string.Empty)
            {
                if (response.Confianca >= 85)
                {
                    response.Status = "APROVADO";
                    response.Motivos.Add("✓ Documento aprovado automaticamente");
                }
                else if (response.Confianca >= 50)
                {
                    response.Status = "ANÁLISE MANUAL";
                    response.Motivos.Add("⚠ Documento requer análise manual");
                }
                else
                {
                    response.Status = "REPROVADO";
                    response.Motivos.Add("✗ Documento reprovado - confiança insuficiente");
                }
            }

            _logger.LogInformation($"Validação concluída. Tipo: {tipoDocumento}, Status: {response.Status}, Confiança: {response.Confianca:F2}%");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Erro na validação: {ex.Message}");
            response.Status = "REPROVADO";
            response.Confianca = 0;
            response.Motivos.Add($"✗ Erro ao processar documento: {ex.Message}");
        }

        return response;
    }

    // ─── VALIDAÇÃO ASO ────────────────────────────────────────────────────────
    private Task<double> ValidateAso(string text, double ocrConfidence, double score, ValidationResponse response)
    {
        var dados = new DadosExtraidos();
        dados.TextoExtraido = text.Length > 500 ? text.Substring(0, 500) + "..." : text;
        response.DadosExtraidos = dados;

        // 1. Verificar se é realmente um ASO
        bool isAso = Regex.IsMatch(text, @"A\.S\.O\.|Atestado\s+de\s+Sa[uú]de\s+Ocupacional|PCMSO|Programa\s+Controle\s+M[eé]dico", RegexOptions.IgnoreCase);
        if (isAso)
        {
            score += 10;
            response.Motivos.Add("✓ Documento identificado como ASO");
        }
        else
        {
            response.Motivos.Add("⚠ Documento não identificado claramente como ASO");
            score -= 5;
        }

        // 2. Extrair nome do trabalhador
        var nomePatterns = new[]
        {
            @"Nome:\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]+?)(?:\s+Fun|Func|Nasc|CPF|\n)",
            @"Nome\s*[:\-]\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,40})(?:\n|Func|Nasc|CPF)",
            @"certifica\s+que\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,50})\s*\n",
        };
        foreach (var p in nomePatterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var nome = m.Groups[1].Value.Trim();
                nome = Regex.Replace(nome, @"\s+", " ").Trim();
                if (nome.Length > 5 && nome.Split(' ').Length >= 2)
                {
                    dados.Nome = nome;
                    score += 15;
                    response.Motivos.Add($"✓ Nome encontrado: {nome}");
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(dados.Nome))
        {
            response.Motivos.Add("⚠ Nome do trabalhador não encontrado");
        }

        // 3. Extrair empresa
        var empresaMatch = Regex.Match(text, @"(?:RAZ[AÃ]O\s+SOCIAL|EMPRESA)[:\s]+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][^\n]{3,50})", RegexOptions.IgnoreCase);
        if (empresaMatch.Success)
        {
            dados.Empresa = empresaMatch.Groups[1].Value.Trim();
            response.Motivos.Add($"✓ Empresa: {dados.Empresa}");
        }

        // 4. Extrair resultado (Apto/Inapto)
        var aptoMatch = Regex.Match(text, @"\bApto\b|\bInapto\b|\bApto\s+com\s+restri", RegexOptions.IgnoreCase);
        if (aptoMatch.Success)
        {
            dados.Resultado = aptoMatch.Value.Trim();
            if (aptoMatch.Value.ToLower().StartsWith("apto") && !aptoMatch.Value.ToLower().Contains("inapto"))
            {
                score += 20;
                response.Motivos.Add($"✓ Resultado: {dados.Resultado}");
            }
            else
            {
                score -= 20;
                response.Motivos.Add($"✗ Resultado: {dados.Resultado} — trabalhador não está apto");
                response.Status = "REPROVADO";
            }
        }
        else
        {
            response.Motivos.Add("⚠ Resultado (Apto/Inapto) não encontrado");
            score -= 10;
        }

        // 5. Extrair data de realização do ASO
        // O ASO não tem "validade" explícita — a validade é calculada (geralmente 1 ano a partir da realização)
        // Padrões: "REALIZACAO DE AVALIACAO: dd/mm/aaaa" ou qualquer data no documento
        var dataPatterns = new[]
        {
            @"REALIZA[CÇ][AÃ]O\s+DE\s+AVALIA[CÇ][AÃ]O[:\s]+(\d{1,2}[/\-\.\s]\d{1,2}[/\-\.\s]\d{2,4})",
            @"Data\s+da\s+avalia[cç][aã]o[:\s]+(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})",
            @"Data[:\s]+(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})",
            @"Catanduvas.*?(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})",
            @"(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})",
        };

        string? dataRealizacao = null;
        foreach (var p in dataPatterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var candidate = m.Groups[m.Groups.Count - 1].Value.Trim();
                // Validar que é uma data plausível
                var parts2 = candidate.Split(new[] { '/', '-', '.' });
                if (parts2.Length == 3 && int.TryParse(parts2[2], out int yr) && yr >= 2015 && yr <= 2030)
                {
                    dataRealizacao = candidate;
                    break;
                }
            }
        }

        // Se não achou com padrão específico, pegar todas as datas e usar a mais recente
        if (dataRealizacao == null)
        {
            var allDates = Regex.Matches(text, @"\d{1,2}[/\-]\d{1,2}[/\-]\d{4}");
            DateTime? maisRecente = null;
            string? dataMaisRecente = null;
            foreach (Match dm in allDates)
            {
                var parts = dm.Value.Split(new[] { '/', '-' });
                if (parts.Length == 3 && int.TryParse(parts[2], out int year) && year >= 2015 && year <= 2030
                    && int.TryParse(parts[1], out int mes) && mes >= 1 && mes <= 12
                    && int.TryParse(parts[0], out int dia) && dia >= 1 && dia <= 31)
                {
                    try
                    {
                        var dt = new DateTime(year, mes, dia);
                        if (maisRecente == null || dt > maisRecente)
                        {
                            maisRecente = dt;
                            dataMaisRecente = dm.Value;
                        }
                    }
                    catch { }
                }
            }
            dataRealizacao = dataMaisRecente;
        }

        if (dataRealizacao != null)
        {
            dados.DataRealizacao = dataRealizacao;

            // Calcular validade: ASO tem validade de 1 ano
            try
            {
                var parts = dataRealizacao.Split(new[] { '/', '-', '.' });
                var dataAso = new DateTime(int.Parse(parts[2]), int.Parse(parts[1]), int.Parse(parts[0]));
                var validadeAso = dataAso.AddYears(1);
                dados.Validade = validadeAso.ToString("dd/MM/yyyy");

                if (validadeAso >= DateTime.Now)
                {
                    score += 25;
                    response.Motivos.Add($"✓ Data de realização: {dataRealizacao}");
                    response.Motivos.Add($"✓ Validade estimada (1 ano): {dados.Validade}");
                }
                else
                {
                    score -= 30;
                    response.Motivos.Add($"✗ ASO vencido! Realizado em: {dataRealizacao} — Validade expirou em: {dados.Validade}");
                    response.Status = "REPROVADO";
                }
            }
            catch
            {
                response.Motivos.Add($"⚠ Data de realização encontrada mas não foi possível calcular validade: {dataRealizacao}");
            }
        }
        else
        {
            // Data manuscrita/ilegível: se documento tem resultado Apto e médico, vai para ANÁLISE MANUAL
            response.Motivos.Add("⚠ Data de realização não legível pelo OCR (campo manuscrito/carimbado)");
            response.Motivos.Add("⚠ Verificar data de realização manualmente");
            // Penalidade menor — não reprovado automaticamente se demais dados OK
            score -= 10;
        }

        // 6. Extrair médico
        var medicoMatch = Regex.Match(text, @"Dr\.?\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][a-záéíóúâêôãõç\s]+?)(?:\n|Telefone|CRM)", RegexOptions.IgnoreCase);
        if (medicoMatch.Success)
        {
            dados.Medico = "Dr. " + medicoMatch.Groups[1].Value.Trim();
            response.Motivos.Add($"✓ Médico responsável: {dados.Medico}");
        }

        // 7. Extrair CPF/número
        var cpfMatch = Regex.Match(text, @"\b\d{3}[\.\s]\d{3}[\.\s]\d{3}[\-\s]\d{2}\b");
        if (cpfMatch.Success)
        {
            dados.NumeroDocumento = cpfMatch.Value;
            score += 10;
            response.Motivos.Add($"✓ CPF: {dados.NumeroDocumento}");
        }

        var qualidadeScore = (int)(ocrConfidence * 5);
        score += qualidadeScore;
        response.Motivos.Add($"✓ Qualidade da imagem: {ocrConfidence:P}");

        return Task.FromResult(score);
    }

    // ─── VALIDAÇÃO DIREÇÃO DEFENSIVA ──────────────────────────────────────────
    private Task<double> ValidateDirecaoDefensiva(string text, double ocrConfidence, double score, ValidationResponse response)
    {
        var dados = new DadosExtraidos();
        dados.TextoExtraido = text.Length > 500 ? text.Substring(0, 500) + "..." : text;
        response.DadosExtraidos = dados;

        var config = _ddConfigService.GetConfig();

        // 1. Verificar se é um certificado de Direção Defensiva
        bool isCertificado = Regex.IsMatch(text, @"CERTIFICADO|certifica\s+que|curso\s+de", RegexOptions.IgnoreCase);
        if (isCertificado)
        {
            score += 10;
            response.Motivos.Add("✓ Documento identificado como certificado de curso");
        }
        else
        {
            response.Motivos.Add("⚠ Documento não identificado claramente como certificado");
            score -= 5;
        }

        // 2. Extrair nome do aluno
        var nomePatterns = new[]
        {
            @"certifica\s+que\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60}?)\s*\n",
            @"Nome[:\s]+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60}?)(?:\n|CPF|RG)",
        };
        foreach (var p in nomePatterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var nome = m.Groups[1].Value.Trim();
                nome = Regex.Replace(nome, @"\s+", " ").Trim();
                if (nome.Length > 5 && nome.Split(' ').Length >= 2)
                {
                    dados.Nome = nome;
                    score += 15;
                    response.Motivos.Add($"✓ Nome encontrado: {nome}");
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(dados.Nome))
            response.Motivos.Add("⚠ Nome do aluno não encontrado");

        // 3. Extrair nome do curso
        var cursoMatch = Regex.Match(text, @"curso\s+de\s+([^\n,\.]{5,80})", RegexOptions.IgnoreCase);
        if (cursoMatch.Success)
        {
            dados.NomeCurso = cursoMatch.Groups[1].Value.Trim();
            response.Motivos.Add($"✓ Curso: {dados.NomeCurso}");

            // Verificar se o curso é aceito
            if (_ddConfigService.IsCursoAceito(dados.NomeCurso))
            {
                score += 10;
                response.Motivos.Add("✓ Tipo de curso aceito para credenciamento");
            }
            else
            {
                score -= 10;
                response.Motivos.Add($"✗ Curso '{dados.NomeCurso}' não está na lista de cursos aceitos");
            }
        }
        else
        {
            response.Motivos.Add("⚠ Nome do curso não encontrado");
        }

        // 4. Verificar escola aprovada
        dados.EscolaAprovada = _ddConfigService.IsEscolaAprovada(text);

        // Extrair nome da escola
        var escolaPatterns = new[]
        {
            @"ministrado\s+pela\s+(?:Unidade\s+)?([^\n]{5,60})",
            @"(?:SEST\s*SENAT|Servico\s+Nacional\s+de\s+Aprendizagem\s+do\s+Transporte)[^\n]*",
            @"(?:escola|instituicao|entidade)[:\s]+([^\n]{5,60})",
        };
        foreach (var p in escolaPatterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                dados.Escola = m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value)
                    ? m.Groups[1].Value.Trim()
                    : m.Value.Trim();
                break;
            }
        }

        if (dados.EscolaAprovada)
        {
            score += 20;
            response.Motivos.Add($"✓ Escola aprovada: {dados.Escola ?? "identificada"}");
        }
        else
        {
            score -= 15;
            var escolasLista = string.Join(", ", config.EscolasAprovadas.Take(5));
            response.Motivos.Add($"✗ Escola não está na lista de aprovadas. Escola identificada: {dados.Escola ?? "não encontrada"}");
            response.Motivos.Add($"  Escolas aceitas: {escolasLista}...");
        }

        // 5. Extrair carga horária
        var cargaPatterns = new[]
        {
            @"(\d+)\s*(?:horas?|h\.?)\s*(?:\/\s*aula)?",
            @"carga\s+hor[aá]ria[:\s]+(\d+)\s*h",
            @"dura[cç][aã]o[:\s]+(\d+)\s*h",
        };
        foreach (var p in cargaPatterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int horas) && horas > 0 && horas <= 200)
            {
                dados.CargaHorariaHoras = horas;
                dados.CargaHorariaAdequada = _ddConfigService.IsCargaHorariaAdequada(horas);

                if (dados.CargaHorariaAdequada)
                {
                    score += 10;
                    response.Motivos.Add($"✓ Carga horária: {horas}h (mínimo: {config.CargaHorariaMinimaHoras}h)");
                }
                else
                {
                    score -= 10;
                    response.Motivos.Add($"✗ Carga horária insuficiente: {horas}h (mínimo exigido: {config.CargaHorariaMinimaHoras}h)");
                }
                break;
            }
        }
        if (dados.CargaHorariaHoras == null)
        {
            response.Motivos.Add($"⚠ Carga horária não encontrada no documento (mínimo exigido: {config.CargaHorariaMinimaHoras}h)");
        }

        // 6. Extrair período do curso e calcular validade (geralmente 5 anos)
        var periodoMatch = Regex.Match(text,
            @"per[ií]odo\s+de\s+(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})\s+a\s+(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})",
            RegexOptions.IgnoreCase);

        if (periodoMatch.Success)
        {
            dados.DataInicioCurso = periodoMatch.Groups[1].Value;
            dados.DataFimCurso = periodoMatch.Groups[2].Value;
            response.Motivos.Add($"✓ Período do curso: {dados.DataInicioCurso} a {dados.DataFimCurso}");

            // Calcular validade: 5 anos a partir da data de conclusão
            try
            {
                var parts = dados.DataFimCurso.Split(new[] { '/', '-' });
                var dataConclusao = new DateTime(int.Parse(parts[2]), int.Parse(parts[1]), int.Parse(parts[0]));
                var validadeCert = dataConclusao.AddYears(5);
                dados.Validade = validadeCert.ToString("dd/MM/yyyy");

                if (validadeCert >= DateTime.Now)
                {
                    score += 15;
                    response.Motivos.Add($"✓ Certificado válido até: {dados.Validade} (5 anos da conclusão)");
                }
                else
                {
                    score -= 30;
                    response.Motivos.Add($"✗ Certificado vencido! Concluído em: {dados.DataFimCurso} — Expirou em: {dados.Validade}");
                    response.Status = "REPROVADO";
                }
            }
            catch
            {
                response.Motivos.Add("⚠ Não foi possível calcular validade do certificado");
            }
        }
        else
        {
            // Tentar pegar qualquer data e usar como data de emissão
            var allDates = Regex.Matches(text, @"\d{1,2}[/\-]\d{1,2}[/\-]\d{4}");
            if (allDates.Count > 0)
            {
                var lastDate = allDates[allDates.Count - 1].Value;
                dados.DataFimCurso = lastDate;
                try
                {
                    var parts = lastDate.Split(new[] { '/', '-' });
                    var dataEmissao = new DateTime(int.Parse(parts[2]), int.Parse(parts[1]), int.Parse(parts[0]));
                    var validadeCert = dataEmissao.AddYears(5);
                    dados.Validade = validadeCert.ToString("dd/MM/yyyy");

                    if (validadeCert >= DateTime.Now)
                    {
                        score += 10;
                        response.Motivos.Add($"✓ Data de emissão: {lastDate} — Válido até: {dados.Validade}");
                    }
                    else
                    {
                        score -= 30;
                        response.Motivos.Add($"✗ Certificado vencido! Emitido em: {lastDate} — Expirou em: {dados.Validade}");
                        response.Status = "REPROVADO";
                    }
                }
                catch { }
            }
            else
            {
                response.Motivos.Add("✗ Data do curso não encontrada");
                score -= 15;
            }
        }

        var qualidadeScore = (int)(ocrConfidence * 5);
        score += qualidadeScore;
        response.Motivos.Add($"✓ Qualidade da imagem: {ocrConfidence:P}");

        // Definir status se escola não aprovada ou curso não aceito
        if (!dados.EscolaAprovada && response.Status == string.Empty)
        {
            response.Status = "REPROVADO";
            response.Motivos.Add("✗ Reprovado: escola não está na lista de aprovadas");
        }

        return Task.FromResult(score);
    }

    // ─── VALIDAÇÃO GENÉRICA (CNH, etc.) ──────────────────────────────────────
    private Task<double> ValidateGenericDocument(string text, string tipoDocumento, double ocrConfidence, double score, ValidationResponse response)
    {
        var dados = ExtractDocumentData(text, tipoDocumento);
        response.DadosExtraidos = dados;
        response.DadosExtraidos.TextoExtraido = text.Length > 500 ? text.Substring(0, 500) + "..." : text;

        if (!string.IsNullOrEmpty(dados.Nome) && dados.Nome.Length > 5)
        {
            score += 25;
            response.Motivos.Add($"✓ Nome encontrado: {dados.Nome}");
        }
        else
        {
            response.Motivos.Add("✗ Nome não encontrado ou inválido");
            score -= 20;
        }

        if (!string.IsNullOrEmpty(dados.Validade))
        {
            if (IsDocumentValid(dados.Validade))
            {
                score += 25;
                response.Motivos.Add($"✓ Documento válido até: {dados.Validade}");
            }
            else
            {
                response.Motivos.Add($"✗ Documento expirado: {dados.Validade}");
                score -= 30;
            }
        }
        else
        {
            response.Motivos.Add("✗ Data de validade não encontrada");
            score -= 25;
        }

        if (!string.IsNullOrEmpty(dados.NumeroDocumento))
        {
            score += 15;
            response.Motivos.Add($"✓ Número do documento: {dados.NumeroDocumento}");
        }
        else
        {
            response.Motivos.Add("✗ Número do documento não encontrado");
            score -= 10;
        }

        var qualidadeScore = (int)(ocrConfidence * 5);
        score += qualidadeScore;
        response.Motivos.Add($"✓ Qualidade da imagem: {ocrConfidence:P}");

        // Para CNH, exige nome + validade para aprovar
        if (score >= 85 && !string.IsNullOrEmpty(dados.Nome) && !string.IsNullOrEmpty(dados.Validade))
        {
            response.Status = "APROVADO";
            response.Motivos.Add("✓ Documento aprovado automaticamente");
        }

        return Task.FromResult(score);
    }

    // ─── EXTRAÇÃO GENÉRICA ────────────────────────────────────────────────────
    private DadosExtraidos ExtractDocumentData(string text, string tipoDocumento)
    {
        var dados = new DadosExtraidos();

        var nomePatterns = new[]
        {
            @"NOME\s*\n\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]+?)(?:\n|$)",
            @"eo\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]+?)(?:\n|DOC|CPF)",
            @"([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]+\s+JUNIOR)(?:\n|'DOC)",
            @"([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]+\s+(?:ALVES|SILVA|SANTOS|OLIVEIRA|COSTA|MARTINS|SOUSA|FERREIRA))(?:\s+(?:JUNIOR|SENIOR))?(?:\n|'DOC)",
            @"^([A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]{10,}?)(?:\n|'DOC)",
        };

        foreach (var pattern in nomePatterns)
        {
            var nomeMatch = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (nomeMatch.Success)
            {
                var nome = nomeMatch.Groups[1].Value.Trim();
                nome = Regex.Replace(nome, @"^eo\s+", "", RegexOptions.IgnoreCase).Trim();
                nome = Regex.Replace(nome, @"[0-9\-\.]+", "").Trim();
                nome = Regex.Replace(nome, @"\s+", " ").Trim();

                var headerWords = @"^(REPUBLICA|FEDERATIVA|DO|BRASIL|MINISTERIO|TRANSPORTES|SECRETARIA|NACIONAL|TRANSITO|SENATRAN|CARTEIRA|HABILITACAO|MV|DORAN|PINS|ORL|IAL|PINST|PINAL)\s+";
                for (int i = 0; i < 5; i++)
                {
                    var cleaned = Regex.Replace(nome, headerWords, "", RegexOptions.IgnoreCase).Trim();
                    if (cleaned == nome) break;
                    nome = cleaned;
                }

                if (!string.IsNullOrEmpty(nome) && nome.Length > 5 && nome.Split(' ').Length >= 2)
                {
                    dados.Nome = nome;
                    break;
                }
            }
        }

        var validadePatterns = new[]
        {
            @"REGISTRO\s+VALIDADE\s+\d+\s+(\d{2}[/-]\d{2}[/-]\d{4})",
            @"VALIDADE\s+(\d{2}[/-]\d{2}[/-]\d{4})",
            @"Validade:\s*(\d{2}[/-]\d{2}[/-]\d{4})"
        };

        foreach (var pattern in validadePatterns)
        {
            var validadeMatch = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (validadeMatch.Success)
            {
                var data = validadeMatch.Groups[1].Value;
                if (IsValidDate(data))
                {
                    dados.Validade = data;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(dados.Validade))
        {
            var dateMatches = Regex.Matches(text, @"\d{2}[/-]\d{2}[/-]\d{4}");
            if (dateMatches.Count >= 2)
            {
                for (int i = 1; i < dateMatches.Count; i++)
                {
                    if (IsValidDate(dateMatches[i].Value))
                    {
                        dados.Validade = dateMatches[i].Value;
                        break;
                    }
                }
            }
        }

        var numeroMatch = Regex.Match(text, @"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b");
        if (numeroMatch.Success)
        {
            dados.NumeroDocumento = numeroMatch.Value;
        }
        else
        {
            var numeroMatch2 = Regex.Match(text, @"\b\d{8,12}\b");
            if (numeroMatch2.Success)
                dados.NumeroDocumento = numeroMatch2.Value;
        }

        return dados;
    }

    private bool IsValidDate(string data)
    {
        try
        {
            var parts = data.Split(new[] { '/', '-' });
            if (parts.Length != 3) return false;
            var day = int.Parse(parts[0]);
            var month = int.Parse(parts[1]);
            var year = int.Parse(parts[2]);
            if (day < 1 || day > 31 || month < 1 || month > 12 || year < 1900 || year > 2100)
                return false;
            var expiryDate = new DateTime(year, month, day);
            return expiryDate > DateTime.Now;
        }
        catch { return false; }
    }

    private bool IsDocumentValid(string dataValidade) => IsValidDate(dataValidade);
}
