using DocumentAIService.Models;
using DocumentAIService.Models.Catalog;
using DocumentAIService.Services;
using DocumentAIService.Services.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentAIService.Tests;

public sealed class ConfigurableExtractionTests
{
    [Fact]
    public async Task Extracts_a_new_document_type_from_catalog_patterns_without_controller_changes()
    {
        var documentType = new DocumentTypeDefinition
        {
            Code = "CIPP",
            Name = "Certificado de Inspeção",
            IdentificationPatterns = ["CERTIFICADO\\s+DE\\s+INSPECAO"],
            Fields =
            [
                new FieldDefinition { Code = "plate", ExtractionPatterns = ["PLACA\\s*[:#]?\\s*([A-Z]{3}[- ]?\\d[A-Z0-9]\\d{2})"] },
                new FieldDefinition { Code = "expirationDate", ExtractionPatterns = ["VALIDADE\\s*[:#]?\\s*(\\d{2}/\\d{2}/\\d{4})"] }
            ]
        };
        var adapter = new LegacyExtractionAdapter(new NeverCalledLegacyAnalysis(), new FixedOcr("CERTIFICADO DE INSPECAO\nPLACA: ABC-1D23\nVALIDADE: 31/12/2030"), NullLogger<LegacyExtractionAdapter>.Instance);

        var result = await adapter.ExtractAsync(documentType, [0x89, 0x50], CancellationToken.None);

        Assert.Equal("CIPP", result.DetectedDocumentType);
        Assert.Equal("ABC-1D23", result.Fields["plate"].Value);
        Assert.Equal("31/12/2030", result.Fields["expirationDate"].Value);
    }

    private sealed class FixedOcr(string text) : IOcrService
    {
        public Task<(string text, double confidence)> ExtractTextAsync(byte[] imageData) => Task.FromResult((text, 0.92));
    }

    private sealed class NeverCalledLegacyAnalysis : IDocumentAnalysisService
    {
        public Task<ValidationResponse> ValidateDocumentAsync(string tipoDocumento, byte[] imageData) => throw new InvalidOperationException("O adaptador legado não deveria ser usado para CIPP.");
    }
}
