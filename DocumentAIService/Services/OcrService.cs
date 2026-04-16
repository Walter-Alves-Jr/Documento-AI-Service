using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace DocumentAIService.Services;

public interface IOcrService
{
    Task<(string text, double confidence)> ExtractTextAsync(byte[] imageData);
}

public class OcrService : IOcrService
{
    private readonly ILogger<OcrService> _logger;

    public OcrService(ILogger<OcrService> logger)
    {
        _logger = logger;
    }

    public async Task<(string text, double confidence)> ExtractTextAsync(byte[] imageData)
    {
        try
        {
            // Estratégia 1: tentar PSM=6 e PSM=3 e combinar os resultados
            // PSM=6 é melhor para layouts uniformes (ASO, certificados)
            // PSM=3 é melhor para layouts complexos (CNH, MRZ)
            var t6 = await TryOcr(imageData, "por", 6);
            var t3 = await TryOcr(imageData, "por", 3);

            bool t6Good = IsGoodText(t6) && !IsInvertedText(t6);
            bool t3Good = IsGoodText(t3) && !IsInvertedText(t3);

            if (t6Good && t3Good)
            {
                // Combinar: usar PSM=6 como base e adicionar conteúdo único do PSM=3
                // (especialmente útil para MRZ que PSM=3 lê melhor)
                var combined = t6 + "\n" + t3;
                return (combined, 0.85);
            }
            if (t6Good) return (t6, 0.85);
            if (t3Good) return (t3, 0.85);

            // Fallback: continuar com estratégias de rotação

            // Estratégia 3: rotação 90° (documentos deitados)
            var rotated90 = await TransformImage(imageData, scale: 1.0f, contrast: 1.0f, rotate: 90);
            if (rotated90 != null)
            {
                var tR90 = await TryOcr(rotated90, "por", 3);
                if (IsGoodText(tR90) && !IsInvertedText(tR90)) return (tR90, 0.80);
            }

            // Estratégia 4: rotação 270° (documentos deitados ao contrário)
            var rotated270 = await TransformImage(imageData, scale: 1.0f, contrast: 1.0f, rotate: 270);
            if (rotated270 != null)
            {
                var t4 = await TryOcr(rotated270, "por", 3);
                if (IsGoodText(t4) && !IsInvertedText(t4)) return (t4, 0.80);
            }

            // Estratégia 5: rotação 180° (PDFs invertidos de cabeça para baixo)
            var rotated180 = await TransformImage(imageData, scale: 1.0f, contrast: 1.0f, rotate: 180);
            if (rotated180 != null)
            {
                var t5 = await TryOcr(rotated180, "por", 3);
                if (IsGoodText(t5) && !IsInvertedText(t5)) return (t5, 0.80);
            }

            // Estratégia 6: escala 2x + contraste (CNH física, documentos pequenos)
            var scaled = await TransformImage(imageData, scale: 2.0f, contrast: 1.8f, rotate: 0);
            if (scaled != null)
            {
                var tS6 = await TryOcr(scaled, "por", 6);
                if (IsGoodText(tS6) && !IsInvertedText(tS6)) return (tS6, 0.80);

                var tS3 = await TryOcr(scaled, "por", 3);
                if (IsGoodText(tS3) && !IsInvertedText(tS3)) return (tS3, 0.80);
            }

            // Estratégia 7: escala 2x + contraste + rotação 180°
            var scaledRot = await TransformImage(imageData, scale: 2.0f, contrast: 1.8f, rotate: 180);
            if (scaledRot != null)
            {
                var t7 = await TryOcr(scaledRot, "por", 3);
                if (IsGoodText(t7) && !IsInvertedText(t7)) return (t7, 0.75);
            }

            // Estratégia 8: escala 2x + contraste + rotação 90°
            var scaledRot90 = await TransformImage(imageData, scale: 2.0f, contrast: 1.8f, rotate: 90);
            if (scaledRot90 != null)
            {
                var t8 = await TryOcr(scaledRot90, "por", 3);
                if (IsGoodText(t8) && !IsInvertedText(t8)) return (t8, 0.75);
            }

            // Estratégia 9: escala 2x + contraste + rotação 270°
            var scaledRot270 = await TransformImage(imageData, scale: 2.0f, contrast: 1.8f, rotate: 270);
            if (scaledRot270 != null)
            {
                var t9 = await TryOcr(scaledRot270, "por", 3);
                if (IsGoodText(t9) && !IsInvertedText(t9)) return (t9, 0.75);
            }

            // Estratégia 10: inglês como fallback (CNH com MRZ)
            var t10 = await TryOcr(imageData, "eng", 3);
            if (IsGoodText(t10)) return (t10, 0.65);

            // Último recurso: retornar o melhor texto disponível mesmo que invertido
            if (IsGoodText(t6)) return (t6, 0.50);
            if (IsGoodText(t3)) return (t3, 0.50);

            _logger.LogWarning("All OCR attempts failed");
            return (string.Empty, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError($"OCR error: {ex.GetType().Name}: {ex.Message}");
            return (string.Empty, 0);
        }
    }

    /// <summary>
    /// Detecta se o texto está invertido (rotacionado 180°) verificando padrões típicos de texto invertido.
    /// Texto invertido em português tem muitas sequências de letras em ordem reversa.
    /// </summary>
    private bool IsInvertedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Palavras comuns em português que, se aparecerem invertidas, indicam rotação
        var invertedIndicators = new[]
        {
            "euejues", "epiossedy", "ejebuesop", "ojnuysu", "seroJ",
            "olutnoc", "oãçazilitu", "oãçazilaer", "oãçacifitreC",
            "adedilaV", "oãçazilaV", "amaN", "emoN"
        };

        var lowerText = text.ToLower();
        int invertedCount = invertedIndicators.Count(ind => lowerText.Contains(ind.ToLower()));
        return invertedCount >= 2;
    }

    private async Task<string> TryOcr(byte[] imageData, string lang, int psm)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid()}.png");
        var outBase = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid()}");
        try
        {
            await File.WriteAllBytesAsync(tmp, imageData);
            var psi = new ProcessStartInfo
            {
                FileName = "tesseract",
                Arguments = $"\"{tmp}\" \"{outBase}\" -l {lang} --psm {psm}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return string.Empty;
            await proc.WaitForExitAsync();
            var txtFile = outBase + ".txt";
            if (File.Exists(txtFile))
                return await File.ReadAllTextAsync(txtFile);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"TryOcr ({lang}, psm={psm}) error: {ex.Message}");
            return string.Empty;
        }
        finally
        {
            TryDelete(tmp);
            TryDelete(outBase + ".txt");
        }
    }

    private bool IsGoodText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var clean = text.Trim();
        if (clean.Length < 30) return false;
        int alnum = clean.Count(c => char.IsLetterOrDigit(c));
        return (double)alnum / clean.Length > 0.25;
    }

    private async Task<byte[]?> TransformImage(byte[] imageData, float scale, float contrast, int rotate)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imageData);
            image.Mutate(x =>
            {
                if (rotate != 0) x.Rotate(rotate);
                if (scale != 1.0f)
                {
                    int newW = (int)(image.Width * scale);
                    int newH = (int)(image.Height * scale);
                    x.Resize(newW, newH);
                }
                if (contrast != 1.0f)
                {
                    x.Grayscale();
                    x.Contrast(contrast);
                }
            });
            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"TransformImage failed: {ex.Message}");
            return null;
        }
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
