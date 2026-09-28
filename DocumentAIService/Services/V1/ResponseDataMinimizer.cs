using System.Text.RegularExpressions;
using DocumentAIService.Models.Catalog;
using DocumentAIService.Models.V1;

namespace DocumentAIService.Services.V1;

public interface IResponseDataMinimizer
{
    List<ExtractedFieldResponse> Build(DocumentTypeDefinition documentType, NormalizedExtractionResult extraction);
}

public sealed class ResponseDataMinimizer : IResponseDataMinimizer
{
    public List<ExtractedFieldResponse> Build(DocumentTypeDefinition documentType, NormalizedExtractionResult extraction)
    {
        var output = new List<ExtractedFieldResponse>();
        foreach (var definition in documentType.Fields)
        {
            if (!extraction.Fields.TryGetValue(definition.Code, out var extracted) || string.IsNullOrWhiteSpace(extracted.Value)) continue;
            if (definition.ResponseMode.Equals("redact", StringComparison.OrdinalIgnoreCase)) continue;

            var shouldMask = definition.ResponseMode.Equals("mask", StringComparison.OrdinalIgnoreCase);
            output.Add(new ExtractedFieldResponse
            {
                Field = definition.Code,
                Value = shouldMask ? Mask(definition.Code, extracted.Value) : extracted.Value,
                Confidence = Math.Round(extracted.Confidence, 2),
                Masked = shouldMask
            });
        }
        return output;
    }

    private static string Mask(string field, string value)
    {
        if (field.Equals("holderName", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Length <= 1 ? "*" : part[0] + new string('*', Math.Min(part.Length - 1, 3))));
        }

        if (field.Contains("number", StringComparison.OrdinalIgnoreCase) || field.Contains("cpf", StringComparison.OrdinalIgnoreCase))
        {
            var digits = Regex.Replace(value, "\\D", string.Empty);
            return digits.Length <= 4 ? "****" : $"***{digits[^4..]}";
        }

        return "***";
    }
}
