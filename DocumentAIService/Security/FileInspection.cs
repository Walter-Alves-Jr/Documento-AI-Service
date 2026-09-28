using DocumentAIService.Configuration;
using Microsoft.Extensions.Options;

namespace DocumentAIService.Security;

public interface IFileInspector
{
    FileInspectionResult Inspect(byte[] data);
}

public sealed record FileInspectionResult(bool IsValid, string? MediaType, string? ErrorCode, string? ErrorMessage)
{
    public static FileInspectionResult Valid(string mediaType) => new(true, mediaType, null, null);
    public static FileInspectionResult Invalid(string code, string message) => new(false, null, code, message);
}

public sealed class FileInspector(IOptions<DvsOptions> options) : IFileInspector
{
    private readonly FileValidationOptions _options = options.Value.Files;

    public FileInspectionResult Inspect(byte[] data)
    {
        if (data.Length == 0)
            return FileInspectionResult.Invalid("EMPTY_FILE", "Arquivo vazio.");
        if (data.Length > _options.MaxBytes)
            return FileInspectionResult.Invalid("FILE_TOO_LARGE", $"Arquivo excede o limite de {_options.MaxBytes / (1024 * 1024)} MB.");

        string? mediaType = DetectMediaType(data);
        if (mediaType is null || !_options.AllowedTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
            return FileInspectionResult.Invalid("UNSUPPORTED_FILE_TYPE", "Formato não suportado. Envie PDF, JPEG ou PNG válidos.");

        return FileInspectionResult.Valid(mediaType);
    }

    private static string? DetectMediaType(byte[] data)
    {
        if (data.Length >= 4 && data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46)
            return "application/pdf";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return "image/png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";
        return null;
    }
}
