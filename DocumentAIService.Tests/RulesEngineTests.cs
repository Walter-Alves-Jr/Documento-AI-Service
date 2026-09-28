using System.Text.Json;
using DocumentAIService.Models.Catalog;
using DocumentAIService.Models.V1;
using DocumentAIService.Services.V1;

namespace DocumentAIService.Tests;

public sealed class RulesEngineTests
{
    private readonly RulesEngine _engine = new();

    [Fact]
    public void Approves_when_type_and_expiration_are_valid()
    {
        var policy = Policy(
            Rule("DOCUMENT_TYPE", "document_type", expected: Json("\"CNH\""), unknown: "reject"),
            Rule("EXPIRATION", "date_not_expired", field: "expirationDate"));
        var extraction = Extraction("CNH", ("expirationDate", DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")));

        var result = _engine.Evaluate(policy, extraction, EmptyContext());

        Assert.Equal(ValidationDecisionStatus.APPROVED, result.Status);
        Assert.Equal(100, result.Score);
        Assert.All(result.Evaluations, evaluation => Assert.Equal(RuleEvaluationStatus.PASS, evaluation.Status));
    }

    [Fact]
    public void Rejects_when_expiration_has_passed()
    {
        var policy = Policy(Rule("EXPIRATION", "date_not_expired", field: "expirationDate", severity: "reject"));
        var extraction = Extraction("CNH", ("expirationDate", DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd")));

        var result = _engine.Evaluate(policy, extraction, EmptyContext());

        Assert.Equal(ValidationDecisionStatus.REJECTED, result.Status);
        Assert.Equal(RuleEvaluationStatus.FAIL, Assert.Single(result.Evaluations).Status);
        Assert.Equal("EXPIRED", result.Evaluations[0].Code);
    }

    [Fact]
    public void Routes_to_manual_review_when_critical_field_is_missing()
    {
        var policy = Policy(Rule("FITNESS", "equals", field: "fitnessResult", expected: Json("\"APTO\""), unknown: "manual_review"));

        var result = _engine.Evaluate(policy, Extraction("ASO"), EmptyContext());

        Assert.Equal(ValidationDecisionStatus.MANUAL_REVIEW, result.Status);
        Assert.True(result.ManualReviewRequired);
        Assert.Equal(RuleEvaluationStatus.MANUAL_REVIEW, Assert.Single(result.Evaluations).Status);
    }

    [Fact]
    public void Applies_reference_minimum_for_sest_senat()
    {
        var parameters = Json("""
        { "defaultMinimum": 8, "references": [ { "field": "issuer", "contains": "SEST SENAT", "minimum": 4 } ] }
        """);
        var policy = Policy(Rule("COURSE_HOURS", "minimum_by_reference", field: "courseHours", parameters: parameters));
        var extraction = Extraction("DIRECAO_DEFENSIVA", ("issuer", "SEST SENAT"), ("courseHours", "4"));

        var result = _engine.Evaluate(policy, extraction, EmptyContext());

        Assert.Equal(ValidationDecisionStatus.APPROVED, result.Status);
        Assert.Equal("MINIMUM_MET", Assert.Single(result.Evaluations).Code);
    }

    [Fact]
    public void Uses_manual_review_for_low_ocr_confidence()
    {
        var policy = Policy(Rule("DOCUMENT_TYPE", "document_type", expected: Json("\"CNH\""), unknown: "reject"));
        policy.MinimumConfidence = 0.80;
        var extraction = Extraction("CNH", confidence: 0.50);

        var result = _engine.Evaluate(policy, extraction, EmptyContext());

        Assert.Equal(ValidationDecisionStatus.MANUAL_REVIEW, result.Status);
        Assert.Contains(result.Warnings, x => x.Code == "LOW_CONFIDENCE");
    }

    private static ValidationPolicyDefinition Policy(params RuleDefinition[] rules) => new()
    {
        Code = "TEST_POLICY", Name = "Test policy", DocumentType = "CNH", Version = "1.0", MinimumConfidence = 0.1, Rules = rules.ToList()
    };

    private static RuleDefinition Rule(string code, string type, string? field = null, JsonElement? expected = null, JsonElement? parameters = null, string severity = "reject", string unknown = "manual_review") => new()
    {
        Code = code, Name = code, RuleType = type, Field = field, ExpectedValue = expected, Parameters = parameters, Severity = severity, UnknownOutcome = unknown
    };

    private static NormalizedExtractionResult Extraction(string detectedType, params (string Field, string Value)[] fields) => Extraction(detectedType, 0.90, fields);

    private static NormalizedExtractionResult Extraction(string detectedType, double confidence = 0.90, params (string Field, string Value)[] fields)
    {
        var result = new NormalizedExtractionResult { DetectedDocumentType = detectedType, OcrConfidence = confidence };
        foreach (var (field, value) in fields)
            result.Fields[field] = new ExtractedFieldValue { Value = value, Confidence = confidence };
        return result;
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static IReadOnlyDictionary<string, string> EmptyContext() => new Dictionary<string, string>();
}
