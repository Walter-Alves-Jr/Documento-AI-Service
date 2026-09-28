using System.Globalization;
using System.Text.Json;
using DocumentAIService.Models.Catalog;
using DocumentAIService.Models.V1;

namespace DocumentAIService.Services.V1;

public interface IRulesEngine
{
    RulesEngineResult Evaluate(ValidationPolicyDefinition policy, NormalizedExtractionResult extraction, IReadOnlyDictionary<string, string> context);
}

public sealed class RulesEngineResult
{
    public int Score { get; init; }
    public ValidationDecisionStatus Status { get; init; }
    public bool ManualReviewRequired { get; init; }
    public List<RuleEvaluation> Evaluations { get; init; } = [];
    public List<ValidationWarning> Warnings { get; init; } = [];
}

public sealed class RulesEngine : IRulesEngine
{
    public RulesEngineResult Evaluate(ValidationPolicyDefinition policy, NormalizedExtractionResult extraction, IReadOnlyDictionary<string, string> context)
    {
        var evaluations = new List<RuleEvaluation>();
        var warnings = new List<ValidationWarning>(extraction.Warnings);

        if (extraction.OcrConfidence < policy.MinimumConfidence)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "LOW_CONFIDENCE",
                Message = "A confiança da extração ficou abaixo do mínimo configurado pela política."
            });
        }

        foreach (var rule in policy.Rules)
        {
            var outcome = EvaluateRule(rule, extraction, context);
            evaluations.Add(ToEvaluation(rule, outcome));
        }

        var applicable = evaluations.Where(x => x.Status is RuleEvaluationStatus.PASS or RuleEvaluationStatus.FAIL or RuleEvaluationStatus.MANUAL_REVIEW).ToList();
        var passed = applicable.Count(x => x.Status == RuleEvaluationStatus.PASS);
        var score = applicable.Count == 0 ? 0 : (int)Math.Round(100d * passed / applicable.Count, MidpointRounding.AwayFromZero);
        var rejected = evaluations.Any(x => x.Status == RuleEvaluationStatus.FAIL);
        var manual = !rejected && (evaluations.Any(x => x.Status == RuleEvaluationStatus.MANUAL_REVIEW) || extraction.OcrConfidence < policy.MinimumConfidence);

        return new RulesEngineResult
        {
            Score = score,
            Status = rejected ? ValidationDecisionStatus.REJECTED : manual ? ValidationDecisionStatus.MANUAL_REVIEW : ValidationDecisionStatus.APPROVED,
            ManualReviewRequired = manual,
            Evaluations = evaluations,
            Warnings = warnings
        };
    }

    private static RuleOutcome EvaluateRule(RuleDefinition rule, NormalizedExtractionResult extraction, IReadOnlyDictionary<string, string> context)
    {
        ExtractedFieldValue? fieldValue = null;
        if (!string.IsNullOrWhiteSpace(rule.Field))
            extraction.Fields.TryGetValue(rule.Field, out fieldValue);
        var actual = fieldValue?.Value;

        return rule.RuleType.ToLowerInvariant() switch
        {
            "document_type" => Compare(Normalize(extraction.DetectedDocumentType), Normalize(ExpectedString(rule)), "DOCUMENT_TYPE_MISMATCH", "O tipo identificado não atende à política."),
            "exists" => string.IsNullOrWhiteSpace(actual) ? Unknown("FIELD_NOT_FOUND", "O campo obrigatório não foi identificado.") : Pass("FIELD_PRESENT", "Campo obrigatório identificado."),
            "equals" => string.IsNullOrWhiteSpace(actual) ? Unknown("FIELD_NOT_FOUND", "O campo necessário não foi identificado.") : Compare(Normalize(actual), Normalize(ExpectedString(rule)), "VALUE_MISMATCH", "O valor extraído não atende à regra."),
            "in" => string.IsNullOrWhiteSpace(actual) ? Unknown("FIELD_NOT_FOUND", "O campo necessário não foi identificado.") : In(actual, ExpectedStrings(rule)),
            "contains_any" => string.IsNullOrWhiteSpace(actual) ? Unknown("FIELD_NOT_FOUND", "O campo necessário não foi identificado.") : ContainsAny(actual, ExpectedStrings(rule)),
            "date_not_expired" => DateNotExpired(actual),
            "number_gte" => NumberGte(actual, ExpectedDecimal(rule)),
            "boolean_true" => string.IsNullOrWhiteSpace(actual) ? Unknown("FIELD_NOT_FOUND", "O campo necessário não foi identificado.") : bool.TryParse(actual, out var value) && value ? Pass("BOOLEAN_TRUE", "Condição booleana atendida.") : Fail("BOOLEAN_FALSE", "Condição obrigatória não foi atendida."),
            "matches_context" => MatchesContext(actual, rule.ContextKey, context),
            "minimum_by_reference" => MinimumByReference(actual, rule.Parameters, extraction),
            _ => Unknown("UNSUPPORTED_RULE", "O tipo de regra configurado ainda não é suportado.")
        };
    }

    private static RuleEvaluation ToEvaluation(RuleDefinition rule, RuleOutcome outcome)
    {
        if (outcome.State == RuleState.Pass)
            return new RuleEvaluation { Rule = rule.Code, Code = outcome.Code, Message = outcome.Message, Status = RuleEvaluationStatus.PASS };

        if (outcome.State == RuleState.Unknown)
        {
            var unknown = rule.UnknownOutcome.ToLowerInvariant();
            return new RuleEvaluation
            {
                Rule = rule.Code,
                Code = outcome.Code,
                Message = outcome.Message,
                Status = unknown switch
                {
                    "reject" => RuleEvaluationStatus.FAIL,
                    "warning" => RuleEvaluationStatus.WARNING,
                    _ => RuleEvaluationStatus.MANUAL_REVIEW
                }
            };
        }

        return new RuleEvaluation
        {
            Rule = rule.Code,
            Code = outcome.Code,
            Message = outcome.Message,
            Status = rule.Severity.ToLowerInvariant() switch
            {
                "manual" => RuleEvaluationStatus.MANUAL_REVIEW,
                "warning" => RuleEvaluationStatus.WARNING,
                _ => RuleEvaluationStatus.FAIL
            }
        };
    }

    private static RuleOutcome DateNotExpired(string? actual)
    {
        if (string.IsNullOrWhiteSpace(actual)) return Unknown("EXPIRATION_NOT_FOUND", "A data de validade não foi identificada.");
        var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy" };
        if (!DateTime.TryParseExact(actual, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return Unknown("EXPIRATION_INVALID", "A data de validade extraída não é válida.");
        return date.Date >= DateTime.UtcNow.Date
            ? Pass("NOT_EXPIRED", "O documento está vigente.")
            : Fail("EXPIRED", "O documento está vencido.");
    }

    private static RuleOutcome NumberGte(string? actual, decimal? expected)
    {
        if (string.IsNullOrWhiteSpace(actual) || expected is null) return Unknown("NUMBER_NOT_FOUND", "O valor numérico necessário não foi identificado.");
        return decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && number >= expected
            ? Pass("MINIMUM_MET", "O valor mínimo configurado foi atendido.")
            : Fail("MINIMUM_NOT_MET", "O valor mínimo configurado não foi atendido.");
    }

    private static RuleOutcome MatchesContext(string? actual, string? key, IReadOnlyDictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(key) || !context.TryGetValue(key, out var expected) || string.IsNullOrWhiteSpace(expected))
            return Unknown("CONTEXT_OR_FIELD_MISSING", "O contexto ou campo necessário para esta regra não foi informado.");
        return Compare(Normalize(actual), Normalize(expected), "CONTEXT_MISMATCH", "O valor extraído não corresponde ao contexto informado.");
    }

    private static RuleOutcome MinimumByReference(string? actual, JsonElement? parameters, NormalizedExtractionResult extraction)
    {
        if (string.IsNullOrWhiteSpace(actual) || !decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return Unknown("NUMBER_NOT_FOUND", "A carga horária necessária não foi identificada.");
        if (parameters is null || parameters.Value.ValueKind != JsonValueKind.Object || !parameters.Value.TryGetProperty("defaultMinimum", out var defaultMinimum))
            return Unknown("RULE_CONFIGURATION_INVALID", "A regra não possui mínimo configurado.");

        var minimum = defaultMinimum.GetDecimal();
        if (parameters.Value.TryGetProperty("references", out var references) && references.ValueKind == JsonValueKind.Array)
        {
            foreach (var reference in references.EnumerateArray())
            {
                if (!reference.TryGetProperty("field", out var fieldElement) || !reference.TryGetProperty("contains", out var containsElement) || !reference.TryGetProperty("minimum", out var minimumElement)) continue;
                var field = fieldElement.GetString();
                var contains = containsElement.GetString();
                if (field is not null && contains is not null && extraction.Fields.TryGetValue(field, out var referenceValue) && referenceValue.Value?.Contains(contains, StringComparison.OrdinalIgnoreCase) == true)
                {
                    minimum = minimumElement.GetDecimal();
                    break;
                }
            }
        }

        return number >= minimum
            ? Pass("MINIMUM_MET", "A carga horária atende ao mínimo configurado.")
            : Fail("MINIMUM_NOT_MET", "A carga horária não atende ao mínimo configurado.");
    }

    private static RuleOutcome In(string actual, IEnumerable<string> expected)
    {
        return expected.Any(x => Normalize(x) == Normalize(actual))
            ? Pass("VALUE_ALLOWED", "O valor extraído está entre os valores permitidos.")
            : Fail("VALUE_NOT_ALLOWED", "O valor extraído não está entre os valores permitidos.");
    }

    private static RuleOutcome ContainsAny(string actual, IEnumerable<string> expected)
    {
        return expected.Any(x => actual.Contains(x, StringComparison.OrdinalIgnoreCase))
            ? Pass("VALUE_ALLOWED", "O valor extraído atende à regra.")
            : Fail("VALUE_NOT_ALLOWED", "O valor extraído não atende à regra.");
    }

    private static RuleOutcome Compare(string actual, string expected, string code, string failureMessage) =>
        actual == expected ? Pass("VALUE_MATCH", "A regra foi atendida.") : Fail(code, failureMessage);

    private static RuleOutcome Pass(string code, string message) => new(RuleState.Pass, code, message);
    private static RuleOutcome Fail(string code, string message) => new(RuleState.Fail, code, message);
    private static RuleOutcome Unknown(string code, string message) => new(RuleState.Unknown, code, message);

    private static string ExpectedString(RuleDefinition rule) =>
        rule.ExpectedValue is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? string.Empty : string.Empty;

    private static IEnumerable<string> ExpectedStrings(RuleDefinition rule)
    {
        if (rule.ExpectedValue is not { } value) return [];
        return value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString() ?? string.Empty],
            JsonValueKind.Array => value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToList(),
            _ => []
        };
    }

    private static decimal? ExpectedDecimal(RuleDefinition rule) =>
        rule.ExpectedValue is { } value && value.TryGetDecimal(out var decimalValue) ? decimalValue : null;

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);

    private enum RuleState { Pass, Fail, Unknown }
    private sealed record RuleOutcome(RuleState State, string Code, string Message);
}
