using DocumentAIService.Configuration;
using DocumentAIService.Models.Catalog;
using DocumentAIService.Models.V1;
using DocumentAIService.Security;
using DocumentAIService.Services.V1;
using Microsoft.Extensions.Options;

namespace DocumentAIService.Tests;

public sealed class SecurityAndMinimizationTests
{
    [Fact]
    public void Rejects_unknown_file_signature()
    {
        var inspector = new FileInspector(Options.Create(new DvsOptions()));

        var result = inspector.Inspect([0x01, 0x02, 0x03, 0x04]);

        Assert.False(result.IsValid);
        Assert.Equal("UNSUPPORTED_FILE_TYPE", result.ErrorCode);
    }

    [Fact]
    public void Recognizes_pdf_by_binary_signature()
    {
        var inspector = new FileInspector(Options.Create(new DvsOptions()));

        var result = inspector.Inspect([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37]);

        Assert.True(result.IsValid);
        Assert.Equal("application/pdf", result.MediaType);
    }

    [Fact]
    public void Masks_personal_fields_and_omits_redacted_fields()
    {
        var documentType = new DocumentTypeDefinition
        {
            Fields =
            [
                new FieldDefinition { Code = "holderName", ResponseMode = "mask" },
                new FieldDefinition { Code = "documentNumber", ResponseMode = "mask" },
                new FieldDefinition { Code = "doctorName", ResponseMode = "redact" },
                new FieldDefinition { Code = "expirationDate", ResponseMode = "show" }
            ]
        };
        var extraction = new NormalizedExtractionResult { OcrConfidence = 0.90 };
        extraction.Fields["holderName"] = new ExtractedFieldValue { Value = "JOAO DA SILVA", Confidence = 0.90 };
        extraction.Fields["documentNumber"] = new ExtractedFieldValue { Value = "123.456.789-09", Confidence = 0.90 };
        extraction.Fields["doctorName"] = new ExtractedFieldValue { Value = "Dra. Ana", Confidence = 0.90 };
        extraction.Fields["expirationDate"] = new ExtractedFieldValue { Value = "2028-01-01", Confidence = 0.90 };

        var result = new ResponseDataMinimizer().Build(documentType, extraction);

        Assert.Equal("J*** D* S***", result.Single(x => x.Field == "holderName").Value);
        Assert.Equal("***8909", result.Single(x => x.Field == "documentNumber").Value);
        Assert.DoesNotContain(result, x => x.Field == "doctorName");
        Assert.Equal("2028-01-01", result.Single(x => x.Field == "expirationDate").Value);
    }

    [Fact]
    public void Stores_idempotent_response_per_client_and_key()
    {
        var store = new InMemoryIdempotencyStore(Options.Create(new DvsOptions()));
        var response = new ValidationV1Response { ValidationId = "VAL-TEST", Status = ValidationDecisionStatus.APPROVED };

        store.Save("client-a", "request-1", response);

        Assert.True(store.TryGet("client-a", "request-1", out var cached));
        Assert.Equal("VAL-TEST", cached.ValidationId);
        Assert.False(store.TryGet("client-b", "request-1", out _));
    }
}
