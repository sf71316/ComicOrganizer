using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.Plugin.PaddleOcr;

/// <summary>
/// WinForms <see cref="IOcrService"/> 轉接器，內部直接委派給 <see cref="PaddleSkiaOcrService"/>。
/// </summary>
public class PaddleOcrService : IOcrService, ISkiaOcrService, IDisposable
{
    private readonly PaddleSkiaOcrService _core;

    public PaddleOcrService(string? inferenceDir = null)
    {
        _core = new PaddleSkiaOcrService(inferenceDir);
    }

    public void Dispose() => _core.Dispose();

    public bool IsLanguageSupported(string langCode) => _core.IsLanguageSupported(langCode);

    public List<string> GetSupportedLanguages() => _core.GetSupportedLanguages();

    public async Task<List<OcrBlock>> RecognizeAsync(Image image, string langCode = "ja-JP")
    {
        byte[] imgBytes;
        using (var ms = new MemoryStream())
        {
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            imgBytes = ms.ToArray();
        }
        return await _core.RecognizeEncodedAsync(imgBytes, langCode).ConfigureAwait(false);
    }

    public Task<List<OcrBlock>> RecognizeEncodedAsync(byte[] encodedImageBytes, string langCode = "ja-JP")
        => _core.RecognizeEncodedAsync(encodedImageBytes, langCode);

    public List<OcrBlock> MergeBlocks(List<OcrBlock> blocks, bool isVerticalFlow = true, float thresholdMultiplier = 1.0f)
        => _core.MergeBlocks(blocks, isVerticalFlow, thresholdMultiplier);

    public static List<List<OcrBlock>> ClusterChildrenByGap(List<OcrBlock>? children, bool isVerticalFlow, float gapRatio)
        => PaddleSkiaOcrService.ClusterChildrenByGap(children, isVerticalFlow, gapRatio);
}
