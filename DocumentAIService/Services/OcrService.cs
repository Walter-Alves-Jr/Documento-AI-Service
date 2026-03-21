using System.Diagnostics;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace DocumentAIService.Services;

public interface IOcrService
{
    Task<(string Text, double Confidence)> ExtractTextAsync(byte[] imageData);
}

public class OcrService : IOcrService
{
    private readonly ILogger<OcrService> _logger;

    public OcrService(ILogger<OcrService> logger)
    {
        _logger = logger;
    }

    public async Task<(string Text, double Confidence)> ExtractTextAsync(byte[] imageData)
    {
        try
        {
            // Validar imagem
            var imageQuality = await ValidateImageQuality(imageData);
            _logger.LogInformation($"Image quality score: {imageQuality:F2}");
            
            if (imageQuality < 0.2)
            {
                _logger.LogWarning("Image quality too low");
                return (string.Empty, 0);
            }

            // Usar imagem original sem processamento para manter qualidade
            _logger.LogInformation("Using original image for OCR");

            // Salvar imagem temporária
            var tempImagePath = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid()}.png");
            await File.WriteAllBytesAsync(tempImagePath, imageData);
            _logger.LogInformation($"Temporary image saved: {tempImagePath}");

            try
            {
                // Chamar Tesseract via linha de comando
                var text = await RunTesseractAsync(tempImagePath);
                
                if (!string.IsNullOrEmpty(text) && text.Length > 10)
                {
                    _logger.LogInformation($"OCR completed successfully. Text length: {text.Length}");
                    return (text, 0.85); // Confiança padrão
                }
                else
                {
                    _logger.LogWarning($"OCR result too short. Length: {text?.Length ?? 0}");
                }
            }
            finally
            {
                // Limpar arquivo temporário
                try
                {
                    if (File.Exists(tempImagePath))
                    {
                        File.Delete(tempImagePath);
                        _logger.LogInformation("Temporary image deleted");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to delete temporary file: {ex.Message}");
                }
            }

            // Se Tesseract falhar, retornar vazio
            _logger.LogWarning("OCR extraction failed");
            return (string.Empty, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError($"OCR error: {ex.GetType().Name}: {ex.Message}");
            return (string.Empty, 0);
        }
    }

    private async Task<string> RunTesseractAsync(string imagePath)
    {
        try
        {
            var outputPath = Path.Combine(Path.GetTempPath(), $"ocr_output_{Guid.NewGuid()}");
            
            var processInfo = new ProcessStartInfo
            {
                FileName = "tesseract",
                Arguments = $"\"{imagePath}\" \"{outputPath}\" -l eng",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _logger.LogInformation($"Running Tesseract: {processInfo.FileName} {processInfo.Arguments}");

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start Tesseract process");
                return string.Empty;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError($"Tesseract exited with code {process.ExitCode}");
                _logger.LogError($"Error output: {error}");
            }

            // Ler arquivo de saída
            var textFile = $"{outputPath}.txt";
            if (File.Exists(textFile))
            {
                var text = await File.ReadAllTextAsync(textFile);
                
                // Limpar arquivo
                try { File.Delete(textFile); } catch { }
                
                return text;
            }

            _logger.LogWarning("Tesseract output file not found");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Tesseract execution error: {ex.Message}");
            return string.Empty;
        }
    }

    private async Task<double> ValidateImageQuality(byte[] imageData)
    {
        try
        {
            using var image = Image.Load(imageData);
            
            _logger.LogInformation($"Image dimensions: {image.Width}x{image.Height}");
            
            // Validar dimensões mínimas
            if (image.Width < 200 || image.Height < 200)
            {
                _logger.LogWarning($"Image too small: {image.Width}x{image.Height}");
                return 0.2;
            }

            // Validar proporção (documento pode ter várias proporções)
            var aspectRatio = (double)image.Width / image.Height;
            _logger.LogInformation($"Image aspect ratio: {aspectRatio:F2}");
            
            if (aspectRatio < 0.3 || aspectRatio > 3.0)
            {
                _logger.LogWarning($"Invalid aspect ratio: {aspectRatio:F2}");
                return 0.3;
            }

            // Calcular contraste aproximado
            var contrast = await CalculateContrast(image);
            _logger.LogInformation($"Image contrast: {contrast:F2}");

            // Retornar qualidade baseada em contraste
            return Math.Min(1.0, contrast);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Image validation error: {ex.Message}");
            return 0;
        }
    }

    private Task<double> CalculateContrast(Image image)
    {
        try
        {
            // Usar uma abordagem simplificada
            var aspectRatio = (double)image.Width / image.Height;
            var sizeScore = Math.Min(1.0, (image.Width * image.Height) / 1000000.0);
            var ratioScore = 1.0 - Math.Abs(aspectRatio - 1.4) / 2.5; // Mais flexível
            
            var contrast = (sizeScore + ratioScore) / 2.0;
            return Task.FromResult(Math.Min(1.0, Math.Max(0.3, contrast)));
        }
        catch
        {
            return Task.FromResult(0.5);
        }
    }


}
