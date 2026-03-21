using System.Diagnostics;

namespace DocumentAIService.Services;

public interface IPdfConverterService
{
    Task<byte[]> ConvertPdfToImageAsync(byte[] pdfData);
}

public class PdfConverterService : IPdfConverterService
{
    private readonly ILogger<PdfConverterService> _logger;

    public PdfConverterService(ILogger<PdfConverterService> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> ConvertPdfToImageAsync(byte[] pdfData)
    {
        try
        {
            // Salvar PDF temporário
            var tempPdfPath = Path.Combine(Path.GetTempPath(), $"pdf_{Guid.NewGuid()}.pdf");
            await File.WriteAllBytesAsync(tempPdfPath, pdfData);
            _logger.LogInformation($"Temporary PDF saved: {tempPdfPath}");

            try
            {
                // Converter PDF para PNG usando pdftoppm
                var tempImagePath = Path.Combine(Path.GetTempPath(), $"pdf_image_{Guid.NewGuid()}");
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = "pdftoppm",
                    Arguments = $"\"{tempPdfPath}\" \"{tempImagePath}\" -png -f 1 -l 1",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _logger.LogInformation($"Running pdftoppm: {processInfo.FileName} {processInfo.Arguments}");

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    _logger.LogError("Failed to start pdftoppm process");
                    return pdfData; // Retornar PDF original se falhar
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogError($"pdftoppm exited with code {process.ExitCode}");
                    _logger.LogError($"Error output: {error}");
                    return pdfData;
                }

                // Ler arquivo de imagem gerado
                var imageFile = $"{tempImagePath}-1.png";
                if (File.Exists(imageFile))
                {
                    var imageData = await File.ReadAllBytesAsync(imageFile);
                    _logger.LogInformation($"PDF converted to image. Size: {imageData.Length} bytes");
                    
                    // Limpar arquivo
                    try { File.Delete(imageFile); } catch { }
                    
                    return imageData;
                }
                else
                {
                    _logger.LogWarning("PDF conversion image file not found");
                    return pdfData;
                }
            }
            finally
            {
                // Limpar arquivo PDF
                try
                {
                    if (File.Exists(tempPdfPath))
                    {
                        File.Delete(tempPdfPath);
                        _logger.LogInformation("Temporary PDF deleted");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to delete temporary PDF: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"PDF conversion error: {ex.Message}");
            return pdfData;
        }
    }
}
