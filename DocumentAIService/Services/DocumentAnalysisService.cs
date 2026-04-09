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

    // Palavras de cabeçalho a remover de nomes extraídos
    private static readonly string[] HeaderWords = {
        "REPUBLICA", "FEDERATIVA", "DO", "BRASIL", "MINISTERIO", "DOS", "TRANSPORTES",
        "SECRETARIA", "NACIONAL", "DE", "TRANSITO", "SENATRAN", "SERPRO", "DETRAN",
        "CARTEIRA", "HABILITACAO", "DRIVER", "LICENSE", "PERMISO", "CONDUCCION",
        "CERTIFICADO", "CERTIFICAMOS", "QUE", "PARTICIPOU", "PROGRAMA", "CAPACITACAO",
        "DISTANCIA", "CONCLUINDO", "CURSO", "CONCLUIU", "MINISTRADO", "PELA",
        "ATESTADO", "SAUDE", "OCUPACIONAL", "ASO", "CLINICA", "MEDICINA", "TRABALHO",
        "MATO", "GROSSO", "PARANA", "SAO", "PAULO", "RIO", "JANEIRO", "MINAS", "GERAIS",
        "VALIDA", "VALIDO", "TERRITORIO", "TODO", "NACIONAL",
        "HABILITACAO", "PERMISSAO", "INFRAESTRUTURA", "DENATRAN",
    };

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

            if (string.IsNullOrWhiteSpace(extractedText) || extractedText.Trim().Length < 20)
            {
                response.Status = "REPROVADO";
                response.Confianca = 0;
                response.Motivos.Add("✗ Falha ao extrair texto do documento - OCR não conseguiu ler");
                return response;
            }

            score += 30;
            response.Motivos.Add($"✓ OCR realizado com sucesso (confiança: {ocrConfidence:P})");

            // Detectar tipo de documento pelo conteúdo
            var tipoDetectado = DetectDocumentType(extractedText, tipoDocumento);
            _logger.LogInformation($"Tipo solicitado: {tipoDocumento}, Tipo detectado: {tipoDetectado}");

            switch (tipoDetectado)
            {
                case "ASO":
                    score = await ValidateAso(extractedText, ocrConfidence, score, response);
                    break;
                case "DIRECAODEFENSIVA":
                    score = await ValidateDirecaoDefensiva(extractedText, ocrConfidence, score, response);
                    break;
                default:
                    score = await ValidateCnh(extractedText, tipoDocumento, ocrConfidence, score, response);
                    break;
            }

            response.Confianca = Math.Min(100, Math.Max(0, (int)score));

            if (string.IsNullOrEmpty(response.Status))
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

            _logger.LogInformation($"Validação: Tipo={tipoDocumento}, Status={response.Status}, Confiança={response.Confianca}%");
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

    // ─── DETECÇÃO AUTOMÁTICA DE TIPO ─────────────────────────────────────────
    private string DetectDocumentType(string text, string tipoSolicitado)
    {
        bool isAso = Regex.IsMatch(text,
            @"A\.S\.O\.|Atestado\s+de\s+Sa[uú]de\s+Ocupacional|PCMSO|Programa\s+Controle\s+M[eé]dico|ASO\s*[-–]\s*Atestado|CLINICA\s+DE\s+MEDICINA\s+DO\s+TRABALHO|Apto\s+para\s+Fun|CRM\s*[:\s]\s*\d|M[eé]dico\s+emitente|Médico\s+Encarregado|CEMETRA|CEVETRA|Capacidade\s+Laborativa",
            RegexOptions.IgnoreCase);

        bool isDirecao = Regex.IsMatch(text,
            @"CERTIFICAMOS\s+QUE|certifica\s+que|Dire[cç][aã]o\s+Defensiva|Direcao\s+Defensiva|SEST\s*SENAT|PRIME\s+CURSOS|Carga\s+hor[aá]ria|ABED|Ensino\s+a\s+Dist[aâ]ncia|Centro\s+de\s+Forma[cç][aã]o\s+de\s+Condutores|Hartmann|Inttergramed|Eco\s+Train|FABET|Champonalli|CERTO|CIT\s+Drive|Concordia\s+Treinamentos",
            RegexOptions.IgnoreCase);

        bool isCnh = Regex.IsMatch(text,
            @"CARTEIRA\s+NACIONAL\s+DE\s+HABILI|DRIVER\s+LICENSE|HABILITACAO|HABILITAÇÃO|SENATRAN|SERPRO|DETRAN|MINISTÉRIO\s+DOS\s+TRANSPORTES|MINISTÉRIO\s+DA\s+INFRAESTRUTURA|PERMISO\s+DE\s+CONDUCCION|RENACH|Renach",
            RegexOptions.IgnoreCase);

        if (isAso) return "ASO";
        if (isDirecao) return "DIRECAODEFENSIVA";
        if (isCnh) return "CNH";

        // Usar tipo solicitado como fallback
        var tipo = NormalizeForType(tipoSolicitado);
        if (tipo.Contains("ASO")) return "ASO";
        if (tipo.Contains("DIRECAO") || tipo.Contains("DEFENSIVA")) return "DIRECAODEFENSIVA";
        return "CNH";
    }

    private string NormalizeForType(string s) =>
        s.ToUpper().Replace(" ", "").Replace("_", "")
         .Replace("Ã", "A").Replace("Ç", "C").Replace("Â", "A")
         .Replace("É", "E").Replace("Ê", "E").Replace("Í", "I")
         .Replace("Ó", "O").Replace("Ô", "O").Replace("Ú", "U");

    // ─── VALIDAÇÃO CNH ────────────────────────────────────────────────────────
    private Task<double> ValidateCnh(string text, string tipoDocumento, double ocrConfidence, double score, ValidationResponse response)
    {
        var dados = new DadosExtraidos();
        dados.TextoExtraido = text.Length > 600 ? text.Substring(0, 600) + "..." : text;
        response.DadosExtraidos = dados;

        // 1. Identificar como CNH
        bool isCnh = Regex.IsMatch(text,
            @"CARTEIRA\s+NACIONAL\s+DE\s+HABILI|DRIVER\s+LICENSE|HABILITACAO|HABILITAÇÃO|SENATRAN|SERPRO|DETRAN|MINISTÉRIO\s+DOS\s+TRANSPORTES|MINISTÉRIO\s+DA\s+INFRAESTRUTURA|PERMISO\s+DE\s+CONDUCCION|RENACH|Renach",
            RegexOptions.IgnoreCase);
        if (isCnh)
        {
            score += 5;
            response.Motivos.Add("✓ Documento identificado como CNH");
        }
        else
        {
            response.Motivos.Add("⚠ Documento não identificado claramente como CNH");
        }

        // 2. Extrair nome
        dados.Nome = ExtractNameCnh(text);
        if (!string.IsNullOrEmpty(dados.Nome))
        {
            score += 25;
            response.Motivos.Add($"✓ Nome encontrado: {dados.Nome}");
        }
        else
        {
            response.Motivos.Add("✗ Nome não encontrado ou inválido");
            score -= 15;
        }

        // 3. Extrair validade
        dados.Validade = ExtractValidadeCnh(text);
        if (!string.IsNullOrEmpty(dados.Validade))
        {
            if (IsDateInFuture(dados.Validade))
            {
                score += 25;
                response.Motivos.Add($"✓ Documento válido até: {dados.Validade}");
            }
            else
            {
                score -= 30;
                response.Motivos.Add($"✗ CNH vencida! Validade: {dados.Validade}");
                response.Status = "REPROVADO";
            }
        }
        else
        {
            response.Motivos.Add("✗ Data de validade não encontrada");
            score -= 20;
        }

        // 4. Extrair número (CPF ou registro)
        dados.NumeroDocumento = ExtractDocumentNumber(text);
        if (!string.IsNullOrEmpty(dados.NumeroDocumento))
        {
            score += 10;
            response.Motivos.Add($"✓ Número do documento: {dados.NumeroDocumento}");
        }
        else
        {
            score -= 5;
            response.Motivos.Add("⚠ Número do documento não encontrado");
        }

        // 5. Qualidade da imagem
        var qualidadeScore = (int)(ocrConfidence * 5);
        score += qualidadeScore;
        response.Motivos.Add($"✓ Qualidade da imagem: {ocrConfidence:P}");

        // Para CNH, exige nome + validade para aprovar
        if (score >= 85 && !string.IsNullOrEmpty(dados.Nome) && !string.IsNullOrEmpty(dados.Validade) && IsDateInFuture(dados.Validade))
        {
            response.Status = "APROVADO";
            response.Motivos.Add("✓ CNH aprovada automaticamente");
        }

        return Task.FromResult(score);
    }

    // ─── EXTRAÇÃO DE NOME PARA CNH ────────────────────────────────────────────
    private string? ExtractNameCnh(string text)
    {
        // Estratégia 1: padrões estruturados da CNH digital
        var patterns = new[]
        {
            // "2 e 1 NOME E SOBRENOME\nNOME COMPLETO" — CNH digital padrão
            @"(?:2\s+e\s+1\s+)?NOME\s+E\s+SOBRENOME\s*\n\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇÀÜ][A-ZÁÉÍÓÚÂÊÔÃÕÇÀÜa-záéíóúâêôãõçàü\s]{5,60})",
            // "NOME\nNOME COMPLETO"
            @"^NOME\s*\n\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60})",
            // Após "DRIVER LICENSE" ou "HABILITAÇÃO"
            @"(?:DRIVER\s+LICENSE|HABILITAC[AÃ]O)[^\n]*\n+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]{8,50})\n",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (m.Success)
            {
                var nome = CleanName(m.Groups[1].Value);
                if (IsValidName(nome)) return nome;
            }
        }

        // Estratégia 2: localizar linha após "NOME" ou "NOME E SOBRENOME"
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (Regex.IsMatch(trimmed, @"^(NOME\s+E\s+SOBRENOME|NOME)\s*$", RegexOptions.IgnoreCase))
            {
                // Tentar as próximas 3 linhas
                for (int j = i + 1; j < Math.Min(i + 4, lines.Length); j++)
                {
                    var nextLine = lines[j].Trim();
                    if (nextLine.Length < 5) continue;
                    // Limpar ruído do OCR: remover caracteres especiais
                    var cleanedLine = Regex.Replace(nextLine, @"[^A-ZÁÉÍÓÚÂÊÔÃÕÇÀÜa-záéíóúâêôãõçàü\s]", " ");
                    cleanedLine = Regex.Replace(cleanedLine, @"\s+", " ").Trim();
                    if (cleanedLine.Length >= 6)
                    {
                        var nome = CleanName(cleanedLine);
                        if (IsValidName(nome)) return nome;
                    }
                }
            }
        }

        // Estratégia 2b: tentar MRZ (CNH-e digital — linha com SOBRENOME<<NOME)
        var mrzName = ExtractNameFromMrz(text);
        if (mrzName != null) return mrzName;

        // Estratégia 3: varrer linhas buscando padrão de nome (2-5 palavras maiúsculas sem números)
        var excludePatterns = new[]
        {
            @"CARTEIRA|HABILITAC|DRIVER|LICENSE|SERPRO|SENATRAN|DETRAN|MATO\s+GROSSO|MINAS\s+GERAIS|SAO\s+PAULO|VALIDA\s+EM|TERRITÓRIO|MINISTERIO|INFRAESTRUTURA|DENATRAN|NACIONAL|REPÚBLICA|FEDERATIVA|BRASIL|PERMISO|CONDUCCION|ASSINADO|DIGITALMENTE|CERTIFICADO|DOCUMENTO|MATO|GROSSO|PARANA|CONTRAN",
        };

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 8 || trimmed.Length > 60) continue;
            if (Regex.IsMatch(trimmed, @"\d")) continue;
            if (excludePatterns.Any(ep => Regex.IsMatch(trimmed, ep, RegexOptions.IgnoreCase))) continue;

            var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || words.Length > 6) continue;
            if (!words.All(w => w.Length >= 2 && Regex.IsMatch(w, @"^[A-ZÁÉÍÓÚÂÊÔÃÕÇÀÜ]", RegexOptions.IgnoreCase))) continue;

            var nome = CleanName(trimmed);
            if (IsValidName(nome)) return nome;
        }

        return null;
    }

    // ─── EXTRAÇÃO DE NOME DO MRZ (Machine Readable Zone) ───────────────────
    /// <summary>
    /// Extrai o nome do titular a partir do código MRZ da CNH-e.
    /// Formato MRZ linha 3: SOBRENOME<<NOME<NOME2<<<<<...
    /// </summary>
    private string? ExtractNameFromMrz(string text)
    {
        // Padrão MRZ linha 3: sequência de letras maiúsculas e "<"
        // Ex: LEONARDO<<VIEIRA<SILVA<<<<<<<<<<<<
        var mrzMatch = Regex.Match(text,
            @"([A-Z]{2,}<<[A-Z]{2,}(?:<[A-Z]{2,})*<*)",
            RegexOptions.Multiline);

        if (!mrzMatch.Success) return null;

        var mrzLine = mrzMatch.Value;
        // Separar sobrenome e nome pelo "<<"
        var parts = mrzLine.TrimEnd('<').Split("<<", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        // Montar nome completo: NOME SOBRENOME (ordem brasileira)
        var nomes = parts[0].Replace("<", " ").Trim(); // primeiro bloco = sobrenome
        var sobrenome = string.Join(" ", parts.Skip(1).Select(p => p.Replace("<", " ").Trim()));

        // Na MRZ brasileira: primeiro bloco = nome, segundo = sobrenome
        var nomeCompleto = $"{nomes} {sobrenome}".Trim();
        nomeCompleto = Regex.Replace(nomeCompleto, @"\s+", " ").Trim();

        if (IsValidName(nomeCompleto)) return nomeCompleto;
        return null;
    }

    // ─── EXTRAÇÃO DE VALIDADE PARA CNH ───────────────────────────────────────
    private string? ExtractValidadeCnh(string text)
    {
        var patterns = new[]
        {
            // "VALIDADE dd/mm/aaaa"
            @"VALIDADE\s+(\d{2}[/\-]\d{2}[/\-]\d{4})",
            @"Validade[:\s]+(\d{2}[/\-]\d{2}[/\-]\d{4})",
            // "DATA EMISSAO dd/mm/aaaa VALIDADE dd/mm/aaaa" — duas datas na mesma linha
            @"\d{2}[/\-]\d{2}[/\-]\d{4}\s+(\d{2}[/\-]\d{2}[/\-]\d{4})",
        };

        foreach (var p in patterns)
        {
            var matches = Regex.Matches(text, p, RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                var date = m.Groups[1].Value;
                if (TryParseDate(date, out DateTime dt) && dt.Year >= 2024 && dt.Year <= 2060)
                    return date;
            }
        }

        // Fallback MRZ: extrair validade da linha 2 do MRZ (formato AAMMDD na posição 8-13)
        // Exemplo: 9901085M3304204BRA — posições 8-13 = "330420" = 20/04/2033
        var mrzLine2 = Regex.Match(text, @"\d{7}[MF<](\d{6})\d");
        if (mrzLine2.Success)
        {
            var mrzDate = mrzLine2.Groups[1].Value; // AAMMDD
            if (mrzDate.Length == 6)
            {
                int yy = int.Parse(mrzDate.Substring(0, 2));
                int mm = int.Parse(mrzDate.Substring(2, 2));
                int dd = int.Parse(mrzDate.Substring(4, 2));
                // Para CNH, datas de validade são sempre futuras (século 2000)
                int yyyy = 2000 + yy;
                if (mm >= 1 && mm <= 12 && dd >= 1 && dd <= 31 && yyyy >= 2024 && yyyy <= 2060)
                {
                    var mrzDateStr = $"{dd:D2}/{mm:D2}/{yyyy}";
                    return mrzDateStr;
                }
            }
        }

        // Fallback: pegar a data mais futura (provável validade)
        var allDates = Regex.Matches(text, @"\b(\d{2}[/\-]\d{2}[/\-]\d{4})\b");
        DateTime? mostFuture = null;
        string? mostFutureStr = null;
        foreach (Match dm in allDates)
        {
            var dateStr = dm.Groups[1].Value;
            if (TryParseDate(dateStr, out DateTime dt) && dt.Year >= 2024)
            {
                if (mostFuture == null || dt > mostFuture)
                {
                    mostFuture = dt;
                    mostFutureStr = dateStr;
                }
            }
        }
        return mostFutureStr;
    }

    // ─── VALIDAÇÃO ASO ────────────────────────────────────────────────────────
    private Task<double> ValidateAso(string text, double ocrConfidence, double score, ValidationResponse response)
    {
        var dados = new DadosExtraidos();
        dados.TextoExtraido = text.Length > 600 ? text.Substring(0, 600) + "..." : text;
        response.DadosExtraidos = dados;

        // 1. Verificar se é realmente um ASO
        bool isAso = Regex.IsMatch(text,
            @"A\.S\.O\.|Atestado\s+de\s+Sa[uú]de\s+Ocupacional|PCMSO|Programa\s+Controle\s+M[eé]dico|ASO\s*[-–]\s*Atestado|CLINICA\s+DE\s+MEDICINA\s+DO\s+TRABALHO|M[eé]dico\s+emitente|Médico\s+Encarregado|CEMETRA|CEVETRA|Capacidade\s+Laborativa",
            RegexOptions.IgnoreCase);
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
        dados.Nome = ExtractNameAso(text);
        if (!string.IsNullOrEmpty(dados.Nome))
        {
            score += 15;
            response.Motivos.Add($"✓ Nome encontrado: {dados.Nome}");
        }
        else
        {
            response.Motivos.Add("⚠ Nome do trabalhador não encontrado");
        }

        // 3. Extrair empresa
        var empresaPatterns = new[]
        {
            @"(?:Empresa|EMPRESA)\s*:\s*([^\n\r]{3,60})",
            @"(?:RAZ[AÃ]O\s+SOCIAL)\s*:\s*([^\n\r]{3,60})",
            @"(?:TAC\s*[-–])\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ][^\n\r]{3,50})",
            @"Empregador\s*:\s*([^\n\r]{3,60})",
        };
        foreach (var p in empresaPatterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                dados.Empresa = m.Groups[1].Value.Trim();
                if (dados.Empresa.Length > 3)
                {
                    response.Motivos.Add($"✓ Empresa: {dados.Empresa}");
                    break;
                }
            }
        }

        // 4. Extrair resultado (Apto/Inapto)
        var aptoPatterns = new[]
        {
            @"Apto\s+para\s+(?:a\s+)?Fun[cç][aã]o\s+que\s+Exerce",
            @"Apto\s+para\s+(?:a\s+)?Fun[cç][aã]o\s+que\s+Exerceu",
            @"\bApto\b",
            @"\bInapto\b",
            @"Apto\s+com\s+restri",
            @"\bpto\b.*[Aa]pto",
        };
        bool aptoFound = false;
        foreach (var p in aptoPatterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                dados.Resultado = m.Value.Trim();
                aptoFound = true;
                bool isApto = !Regex.IsMatch(dados.Resultado, @"Inapto", RegexOptions.IgnoreCase);
                if (isApto)
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
                break;
            }
        }
        if (!aptoFound)
        {
            response.Motivos.Add("⚠ Resultado (Apto/Inapto) não encontrado");
            score -= 10;
        }

        // 5. Extrair data de realização do ASO
        string? dataRealizacao = ExtractDataRealizacaoAso(text);

        if (dataRealizacao != null)
        {
            dados.DataRealizacao = dataRealizacao;
            int validadeMeses = DetectValidadeAso(text);

            try
            {
                if (TryParseDate(dataRealizacao, out DateTime dataAso))
                {
                    var validadeAso = dataAso.AddMonths(validadeMeses);
                    dados.Validade = validadeAso.ToString("dd/MM/yyyy");

                    if (validadeAso >= DateTime.Now)
                    {
                        score += 25;
                        response.Motivos.Add($"✓ Data de realização: {dataRealizacao}");
                        response.Motivos.Add($"✓ Validade ({validadeMeses} meses): {dados.Validade}");
                    }
                    else
                    {
                        score -= 30;
                        response.Motivos.Add($"✗ ASO vencido! Realizado em: {dataRealizacao} — Validade expirou em: {dados.Validade}");
                        response.Status = "REPROVADO";
                    }
                }
            }
            catch
            {
                response.Motivos.Add($"⚠ Data encontrada mas não foi possível calcular validade: {dataRealizacao}");
            }
        }
        else
        {
            response.Motivos.Add("⚠ Data de realização não legível pelo OCR (campo manuscrito/carimbado)");
            response.Motivos.Add("⚠ Verificar data de realização manualmente");
            score -= 10;
        }

        // 6. Extrair médico
        var medicoPatterns = new[]
        {
            @"(?:Dr\.?|Dra\.?)\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][a-záéíóúâêôãõç\s]+?)(?:\n|Telefone|CRM|$)",
            @"M[eé]dico\s+emitente\s*:\s*([^\n\r\-\(]{5,60}?)(?:\s*[-\(]|\n|$)",
            @"CRM[:\s]+[\d\-/\w]+\s*\n\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ][a-záéíóúâêôãõç\s]{5,50}?)(?:\n|$)",
            @"([A-ZÁÉÍÓÚÂÊÔÃÕÇ][a-záéíóúâêôãõç\s]{5,50}?)\s*\n\s*CRM",
        };
        foreach (var p in medicoPatterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var medico = m.Groups[1].Value.Trim();
                if (medico.Length > 3 && IsValidName(medico))
                {
                    dados.Medico = medico.StartsWith("Dr") ? medico : "Dr. " + medico;
                    response.Motivos.Add($"✓ Médico responsável: {dados.Medico}");
                    break;
                }
            }
        }

        // 7. Extrair CPF
        var cpfMatch = Regex.Match(text, @"\b(\d{3}[\.\s]\d{3}[\.\s]\d{3}[\-\s]\d{2})\b");
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

    private string? ExtractNameAso(string text)
    {
        var patterns = new[]
        {
            // "Nome: NOME COMPLETO" — padrão mais comum
            @"(?:^|\n)\s*Nome\s*[:\-\|]\s*\|?\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60}?)(?:\s*CPF|\s*Idade|\s*Nasc|\s*Fun|\s*\n|\s*\|)",
            // Tolerante a ruído: "Nome: JLuzcaRLOS..." → limpar e normalizar
            @"(?:^|\n)\s*[Nn]ome\s*[:\-\|]\s*([^\n\r]{5,60}?)(?:\s*CPF|\s*Idade|\s*Nasc|\s*Fun|\s*\n|\s*\||\s*$)",
            // "Funcionário: NOME COMPLETO"
            @"Funcion[aá]rio\s*:\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60}?)(?:\s*\(|\s*\n)",
            // "Nome:\nNOME COMPLETO"
            @"Nome\s*:\s*\n\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60})",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (m.Success)
            {
                var raw = m.Groups[1].Value;
                // Limpar ruído do OCR: remover caracteres não-alfanuméricos exceto espaço
                var cleaned = Regex.Replace(raw, @"[^A-ZÁÉÍÓÚÂÊÔÃÕÇÀÜa-záéíóúâêôãõçàü\s]", " ");
                cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
                var nome = CleanName(cleaned);
                if (IsValidName(nome)) return nome;
            }
        }
        return null;
    }

    private string? ExtractDataRealizacaoAso(string text)
    {
        var patterns = new[]
        {
            // "Data dd/mm/aaaa" — padrão mais comum no final do ASO
            @"(?:^|\n)\s*Data\s+(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})",
            @"Data[:\s]+(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})",
            // "REALIZACAO DE AVALIACAO: dd/mm/aaaa"
            @"REALIZA[CÇ][AÃ]O\s+DE\s+AVALIA[CÇ][AÃ]O[:\s]+(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})",
            // "Avaliacao Clinica ... dd/mm/aaaa"
            @"Avalia[cç][aã]o\s+Cl[ií]nica[^\n]*(\d{2}[/\-]\d{2}[/\-]\d{4})",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (m.Success)
            {
                var dateStr = m.Groups[1].Value.Trim();
                if (TryParseDate(dateStr, out DateTime dt) && dt.Year >= 2015 && dt <= DateTime.Now.AddDays(1))
                    return dateStr;
            }
        }

        // Fallback: pegar a data mais recente (data de realização)
        var allDates = Regex.Matches(text, @"\b(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})\b");
        DateTime? maisRecente = null;
        string? dataMaisRecente = null;
        foreach (Match dm in allDates)
        {
            var dateStr = dm.Groups[1].Value;
            if (TryParseDate(dateStr, out DateTime dt) && dt.Year >= 2015 && dt <= DateTime.Now.AddDays(1))
            {
                if (maisRecente == null || dt > maisRecente)
                {
                    maisRecente = dt;
                    dataMaisRecente = dateStr;
                }
            }
        }
        return dataMaisRecente;
    }

    private int DetectValidadeAso(string text)
    {
        if (Regex.IsMatch(text, @"(?:\[x\]|\[X\]|☑|✓|✔|@)\s*6\s*[Mm]eses?|6\s*[Mm]eses?\s*(?:\[x\]|\[X\]|☑)", RegexOptions.IgnoreCase))
            return 6;
        if (Regex.IsMatch(text, @"(?:\[x\]|\[X\]|☑|✓|✔|@)\s*2\s*\(?[Dd]ois?\)?|2\s*[Aa]nos?\s*(?:\[x\]|\[X\]|☑)", RegexOptions.IgnoreCase))
            return 24;
        if (Regex.IsMatch(text, @"(?:\[x\]|\[X\]|☑|✓|✔|@)\s*1\s*\(?[Uu]m\)?|1\s*\(?[Uu]m\)?\s*[Aa]no", RegexOptions.IgnoreCase))
            return 12;
        return 12; // Default: 1 ano
    }

    // ─── VALIDAÇÃO DIREÇÃO DEFENSIVA ──────────────────────────────────────────
    private Task<double> ValidateDirecaoDefensiva(string text, double ocrConfidence, double score, ValidationResponse response)
    {
        var dados = new DadosExtraidos();
        dados.TextoExtraido = text.Length > 600 ? text.Substring(0, 600) + "..." : text;
        response.DadosExtraidos = dados;

        var config = _ddConfigService.GetConfig();

        // 1. Verificar se é um certificado de Direção Defensiva
        bool isCertificado = Regex.IsMatch(text,
            @"CERTIFICADO|certifica\s+que|Dire[cç][aã]o\s+Defensiva|Direcao\s+Defensiva|CERTIFICAMOS\s+QUE|Centro\s+de\s+Forma[cç][aã]o\s+de\s+Condutores",
            RegexOptions.IgnoreCase);
        if (isCertificado)
        {
            score += 10;
            response.Motivos.Add("✓ Documento identificado como certificado de Direção Defensiva");
        }
        else
        {
            response.Motivos.Add("⚠ Documento não identificado claramente como certificado");
            score -= 5;
        }

        // 2. Extrair nome do aluno
        dados.Nome = ExtractNameDirecaoDefensiva(text);
        if (!string.IsNullOrEmpty(dados.Nome))
        {
            score += 15;
            response.Motivos.Add($"✓ Nome encontrado: {dados.Nome}");
        }
        else
        {
            response.Motivos.Add("⚠ Nome do aluno não encontrado");
        }

        // 3. Extrair nome do curso
        var cursoPatterns = new[]
        {
            @"(?:Concluiu|concluiu|Concluindo|concluindo)\s+o\s+[Cc]urso\s+de\s+([^\n,\.]{5,80})",
            @"[Cc]urso\s+de\s+([^\n,\.]{5,80})",
            @"Dire[cç][aã]o\s+Defensiva(?:\s*[-–]\s*([^\n,\.]{3,60}))?",
        };
        foreach (var p in cursoPatterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                dados.NomeCurso = m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value)
                    ? ("Direção Defensiva - " + m.Groups[1].Value.Trim()).TrimEnd('-', ' ')
                    : "Direção Defensiva";
                dados.NomeCurso = dados.NomeCurso.Trim();
                response.Motivos.Add($"✓ Curso: {dados.NomeCurso}");

                if (_ddConfigService.IsCursoAceito(dados.NomeCurso))
                {
                    score += 10;
                    response.Motivos.Add("✓ Tipo de curso aceito para credenciamento");
                }
                else
                {
                    score -= 5;
                    response.Motivos.Add($"⚠ Curso '{dados.NomeCurso}' não está na lista de cursos aceitos — verificar manualmente");
                }
                break;
            }
        }

        // 4. Verificar escola aprovada (lista BRF)
        dados.EscolaAprovada = _ddConfigService.IsEscolaAprovada(text);
        dados.Escola = ExtractEscolaDirecaoDefensiva(text);
        bool isSestSenat = _ddConfigService.IsEscolaSestSenat(text);

        if (dados.EscolaAprovada)
        {
            score += 20;
            response.Motivos.Add($"✓ Escola homologada BRF: {dados.Escola ?? "identificada"}");
        }
        else
        {
            score -= 15;
            response.Motivos.Add($"✗ Escola não está na lista de homologadas BRF. Identificada: {dados.Escola ?? "não encontrada"}");
            response.Motivos.Add($"  Escolas aceitas: Hartmann, Inttergramed, Eco Trainning, SEST SENAT, Concórdia, FABET, Champonalli, CERTO, CIT Drive");
        }

        // 5. Extrair carga horária — com regra diferenciada por escola
        int cargaMinima = _ddConfigService.GetCargaHorariaMinimaParaEscola(text);
        dados.CargaHorariaMinimaExigida = cargaMinima;
        int? cargaHoras = ExtractCargaHoraria(text);

        if (cargaHoras.HasValue)
        {
            dados.CargaHorariaHoras = cargaHoras.Value;
            dados.CargaHorariaAdequada = _ddConfigService.IsCargaHorariaAdequada(cargaHoras.Value, text);

            if (dados.CargaHorariaAdequada)
            {
                score += 10;
                string regraEscola = isSestSenat ? "SEST SENAT: mín. 4h" : "demais escolas: mín. 8h";
                response.Motivos.Add($"✓ Carga horária: {cargaHoras.Value}h (regra BRF — {regraEscola})");
            }
            else
            {
                score -= 10;
                string regraEscola = isSestSenat ? "SEST SENAT: mín. 4h" : "demais escolas: mín. 8h";
                response.Motivos.Add($"✗ Carga horária insuficiente: {cargaHoras.Value}h (regra BRF — {regraEscola})");
            }
        }
        else
        {
            string regraEscola = isSestSenat ? "SEST SENAT: mín. 4h" : "demais escolas: mín. 8h";
            response.Motivos.Add($"⚠ Carga horária não encontrada (regra BRF — {regraEscola})");
        }

        // 6. Extrair data de conclusão e calcular validade (5 anos)
        string? dataConclusao = ExtractDataConclusaoDirecaoDefensiva(text);

        if (dataConclusao != null)
        {
            dados.DataFimCurso = dataConclusao;
            try
            {
                if (TryParseDate(dataConclusao, out DateTime dtConclusao))
                {
                    var validadeCert = dtConclusao.AddYears(5);
                    dados.Validade = validadeCert.ToString("dd/MM/yyyy");

                    if (validadeCert >= DateTime.Now)
                    {
                        score += 15;
                        response.Motivos.Add($"✓ Concluído em: {dataConclusao} — Válido até: {dados.Validade} (5 anos)");
                    }
                    else
                    {
                        score -= 30;
                        response.Motivos.Add($"✗ Certificado vencido! Concluído em: {dataConclusao} — Expirou em: {dados.Validade}");
                        response.Status = "REPROVADO";
                    }
                }
            }
            catch
            {
                response.Motivos.Add("⚠ Não foi possível calcular validade do certificado");
            }
        }
        else
        {
            response.Motivos.Add("✗ Data de conclusão do curso não encontrada");
            score -= 15;
        }

        var qualidadeScore = (int)(ocrConfidence * 5);
        score += qualidadeScore;
        response.Motivos.Add($"✓ Qualidade da imagem: {ocrConfidence:P}");

        // Escola não aprovada = reprovado
        if (!dados.EscolaAprovada && string.IsNullOrEmpty(response.Status))
        {
            response.Status = "REPROVADO";
            response.Motivos.Add("✗ Reprovado: escola não está na lista de homologadas BRF");
        }

        return Task.FromResult(score);
    }

    private string? ExtractNameDirecaoDefensiva(string text)
    {
        var patterns = new[]
        {
            // "CERTIFICAMOS QUE\nNOME" — padrão mais comum
            @"CERTIFICAMOS\s+QUE\s*\n\s*([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60}?)\s*\n",
            // "certifica que NOME concluiu"
            @"certifica\s+que\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60}?)\s*(?:concluiu|participou|\n)",
            // "CERTIFICAMOS QUE NOME concluiu"
            @"CERTIFICAMOS\s+QUE\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60}?)\s*(?:concluiu|participou|\n)",
            // "Certificamos que NOME CPF:"
            @"[Cc]ertificamos\s+que\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][A-ZÁÉÍÓÚÂÊÔÃÕÇa-záéíóúâêôãõç\s]{5,60}?)\s+CPF",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (m.Success)
            {
                var nome = CleanName(m.Groups[1].Value);
                if (IsValidName(nome)) return nome;
            }
        }
        return null;
    }

    private string? ExtractEscolaDirecaoDefensiva(string text)
    {
        var patterns = new[]
        {
            @"ministrado\s+pela\s+(?:Unidade\s+)?([^\n]{5,80})",
            @"(SEST\s*SENAT|Servico\s+Nacional\s+de\s+Aprendizagem\s+do\s+Transporte)",
            @"(PRIME\s+CURSOS[^\n]*)",
            @"(Hartmann[^\n]*)",
            @"(Inttergramed[^\n]*)",
            @"(Eco\s+Train[^\n]*)",
            @"(FABET[^\n]*)",
            @"(Champonalli[^\n]*)",
            @"(CERTO[^\n]*)",
            @"(CIT\s+Drive[^\n]*)",
            @"(Concordia\s+Treinamentos[^\n]*)",
            @"(UNIGIO|WOLI|SENAI|SENAC)[^\n]*",
            @"(?:escola|instituicao|entidade|empresa)[:\s]+([^\n]{5,60})",
            @"Centro\s+de\s+Forma[cç][aã]o\s+de\s+Condutores\s*\n\s*([^\n]{5,60})",
        };
        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
                return m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value)
                    ? m.Groups[1].Value.Trim()
                    : m.Value.Trim();
        }
        return null;
    }

    private int? ExtractCargaHoraria(string text)
    {
        var patterns = new[]
        {
            @"[Cc]arga\s+hor[aá]ria[:\s]+(\d+)\s*h",
            @"CARGA\s+HORARIA\s+TOTAL\s*\n\s*(\d+)",
            @"(\d+)\s*(?:horas?)\b",
            @"\b(\d{1,3}):00:00\b",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int horas) && horas > 0 && horas <= 500)
                return horas;
        }
        return null;
    }

    private string? ExtractDataConclusaoDirecaoDefensiva(string text)
    {
        var patterns = new[]
        {
            @"FINALIZADO\s+EM[:\s]*\n?\s*(\d{2}[/\-]\d{2}[/\-]\d{4})",
            // "período de dd/mm/aaaa à dd/mm/aaaa" — pegar a data final
            @"per[ií]odo\s+de\s+[\d/\-]+\s+[aà]\s+(\d{2}[/\-]\d{2}[/\-]\d{4})",
            @"[Mm][eê]s\s+e\s+ano\s+da\s+conclus[aã]o[:\s]+(\w+\s+de\s+\d{4})",
            @"conclus[aã]o[:\s\n]+(\d{1,2}[/\-]\d{1,2}[/\-]\d{4})",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var dateStr = m.Groups[1].Value.Trim();
                if (Regex.IsMatch(dateStr, @"\w+\s+de\s+\d{4}"))
                    return ConvertMonthYearToDate(dateStr);
                if (TryParseDate(dateStr, out DateTime dt) && dt.Year >= 2010)
                    return dateStr;
            }
        }

        // Fallback: pegar a data mais recente no documento
        var allDates = Regex.Matches(text, @"\b(\d{2}[/\-]\d{2}[/\-]\d{4})\b");
        DateTime? maisRecente = null;
        string? dataMaisRecente = null;
        foreach (Match dm in allDates)
        {
            var dateStr = dm.Groups[1].Value;
            if (TryParseDate(dateStr, out DateTime dt) && dt.Year >= 2010)
            {
                if (maisRecente == null || dt > maisRecente)
                {
                    maisRecente = dt;
                    dataMaisRecente = dateStr;
                }
            }
        }
        return dataMaisRecente;
    }

    private string? ConvertMonthYearToDate(string monthYear)
    {
        var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            {"janeiro",1},{"fevereiro",2},{"março",3},{"marco",3},{"abril",4},
            {"maio",5},{"junho",6},{"julho",7},{"agosto",8},{"setembro",9},
            {"outubro",10},{"novembro",11},{"dezembro",12}
        };
        var m = Regex.Match(monthYear, @"(\w+)\s+de\s+(\d{4})", RegexOptions.IgnoreCase);
        if (m.Success && months.TryGetValue(m.Groups[1].Value, out int mes))
        {
            int ano = int.Parse(m.Groups[2].Value);
            int ultimoDia = DateTime.DaysInMonth(ano, mes);
            return $"{ultimoDia:D2}/{mes:D2}/{ano}";
        }
        return null;
    }

    // ─── UTILITÁRIOS ─────────────────────────────────────────────────────────
    private string CleanName(string raw)
    {
        var nome = raw.Trim();
        nome = Regex.Replace(nome, @"\s+", " ").Trim();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var word in HeaderWords)
            {
                var before = nome;
                nome = Regex.Replace(nome, $@"^{Regex.Escape(word)}\s+", "", RegexOptions.IgnoreCase).Trim();
                if (nome != before) changed = true;
            }
        }
        nome = Regex.Replace(nome, @"[0-9\.\-\/\\@#$%&*()_+=\[\]{}|<>?!:;,""']", "").Trim();
        nome = Regex.Replace(nome, @"\s+", " ").Trim();
        return nome;
    }

    private bool IsValidName(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return false;
        if (nome.Length < 6 || nome.Length > 80) return false;
        var words = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return false;
        foreach (var word in HeaderWords)
        {
            if (string.Equals(nome.Trim(), word, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return Regex.IsMatch(nome, @"[A-Za-záéíóúâêôãõç]");
    }

    private string? ExtractDocumentNumber(string text)
    {
        // CPF: 000.000.000-00
        var cpf = Regex.Match(text, @"\b(\d{3}[\.\s]\d{3}[\.\s]\d{3}[\-\s]\d{2})\b");
        if (cpf.Success) return cpf.Groups[1].Value;

        // Registro CNH: 8-12 dígitos
        var reg = Regex.Match(text, @"\b(\d{8,12})\b");
        if (reg.Success) return reg.Groups[1].Value;

        return null;
    }

    private bool TryParseDate(string dateStr, out DateTime result)
    {
        result = default;
        try
        {
            var parts = dateStr.Split(new[] { '/', '-', '.' });
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], out int day)) return false;
            if (!int.TryParse(parts[1], out int month)) return false;
            if (!int.TryParse(parts[2], out int year)) return false;
            if (day < 1 || day > 31 || month < 1 || month > 12 || year < 1900 || year > 2100) return false;
            result = new DateTime(year, month, day);
            return true;
        }
        catch { return false; }
    }

    private bool IsDateInFuture(string dateStr)
    {
        if (TryParseDate(dateStr, out DateTime dt))
            return dt >= DateTime.Now;
        return false;
    }
}
