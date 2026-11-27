using ComicOrganizer.App.Core;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

public class SkiaTranslationRendererTests
{
    [Fact]
    public void EmbeddedTranslationTypeface_AndChineseWordDict_LoadFromCore()
    {
        var typeface = SkiaTranslationRenderer.TranslationTypeface;
        Assert.NotNull(typeface);
        Assert.True(ChineseWordDict.Shared.Count > 0, "Expected embedded zh_tw_words dictionary to load from Core assembly.");
    }

    [Fact]
    public void RenderTranslatedPage_MasksAndDrawsText_WithZeroNightEyeCareColorMismatch()
    {
        // 建立一張白紙漫畫對話框（黑框包白底，內部有「日本語」模擬像素筆畫）
        using var bmp = new SKBitmap(220, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(new SKColor(232, 232, 232));
            using var whiteFill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
            using var blackBorder = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
            using var origText = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };

            var bubbleRect = new SKRect(30, 30, 190, 290);
            canvas.DrawOval(bubbleRect, whiteFill);
            canvas.DrawOval(bubbleRect, blackBorder);
            // 繪製兩道模擬日文直書細筆畫（保留周圍白色氣泡留白，符合真實氣泡內部特徵）
            canvas.DrawRect(new SKRect(96, 102, 102, 208), origText);
            canvas.DrawRect(new SKRect(118, 106, 124, 204), origText);
            canvas.Flush();
        }

        // 先驗證 BubbleEnvelope.Detect(SKBitmap) 能準確抓出白底氣泡
        var textRegion = new SKRectI(85, 90, 135, 220);
        var env = BubbleEnvelope.Detect(bmp, textRegion);
        Assert.True(env.Valid);
        Assert.False(env.DarkBubble);

        using var sourceImg = SKImage.FromBitmap(bmp);
        var block = new OcrBlock
        {
            X = 85,
            Y = 90,
            Width = 50,
            Height = 130,
            OriginalText = "テスト会話…！",
            Text = "這是繁體中文對話測試…！"
        };

        using var translatedImg = SkiaTranslationRenderer.RenderTranslatedPage(sourceImg, new[] { block });
        Assert.NotNull(translatedImg);
        Assert.Equal(220, translatedImg.Width);
        Assert.Equal(320, translatedImg.Height);

        // 再套用夜間護眼降亮濾鏡（驗證氣泡內遮罩白底與原白氣泡未遮罩區擁有完全一致的暖暗紙色）
        var nightOpts = ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.NightEyeCare);
        using var nightImg = ReaderImageEnhancer.RenderEnhancedImage(translatedImg, nightOpts);
        using var nightBmp = SKBitmap.FromImage(nightImg);

        // 氣泡上緣未塗白純白區 (110, 48) 與遮罩空白角落 (88, 92) 比較色差應 <= 2
        var unmaskedWhite = nightBmp.GetPixel(110, 48);
        var maskedCorner = nightBmp.GetPixel(88, 92);
        Assert.True(Math.Abs(unmaskedWhite.Red - maskedCorner.Red) <= 2);
        Assert.True(Math.Abs(unmaskedWhite.Green - maskedCorner.Green) <= 2);
        Assert.True(Math.Abs(unmaskedWhite.Blue - maskedCorner.Blue) <= 2);
    }

    [Fact]
    public void SkiaOcrPluginManager_ListsAvailableEnginesWithoutThrowing()
    {
        var engines = SkiaOcrPluginManager.GetAvailableEngines();
        Assert.NotNull(engines);
        Assert.True(engines.Count >= 2);
    }

    [Fact]
    public void SkiaTextEraser_DiffusionFillAndTranslatedScope_PreservesBubbleTintAndUntranslatedSfx()
    {
        // 建立一張淡藍漸層氣泡底（非純白）＋黑色對話文字＋氣泡外紅色手繪狀聲詞筆畫
        var bubbleColor = new SKColor(210, 232, 248);
        var sfxColor = new SKColor(220, 40, 40);
        using var bmp = new SKBitmap(220, 260, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(bubbleColor);
            using var stroke = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            using var sfxPaint = new SKPaint { Color = sfxColor, Style = SKPaintStyle.Fill };
            // 對話區塊內原文 (70..76, 60..140)
            canvas.DrawRect(new SKRect(70, 60, 76, 140), stroke);
            // 氣泡外手繪狀聲詞 (165..175, 50..120)
            canvas.DrawRect(new SKRect(165, 50, 175, 120), sfxPaint);
            canvas.Flush();
        }

        // 模擬偵測器把整頁字畫（對話 + 狀聲詞）都標成 1
        var rawMask = new byte[220 * 260];
        for (int y = 60; y < 140; y++)
        {
            for (int x = 70; x < 76; x++)
            {
                rawMask[y * 220 + x] = 1;
            }
        }
        for (int y = 50; y < 120; y++)
        {
            for (int x = 165; x < 175; x++)
            {
                rawMask[y * 220 + x] = 1;
            }
        }

        var dialogBlock = new OcrBlock
        {
            X = 55,
            Y = 48,
            Width = 40,
            Height = 105,
            OriginalText = "テスト",
            Text = "你好"
        };
        var sfxBlock = new OcrBlock
        {
            X = 160,
            Y = 45,
            Width = 22,
            Height = 80,
            OriginalText = "ドキッ",
            Text = "" // LLM 刻意濾掉狀聲詞
        };

        var scoped = SkiaTextEraser.RestrictMaskToTranslatedBlocks(rawMask, 220, 260, new[] { dialogBlock, sfxBlock });
        Assert.Equal(1, scoped[100 * 220 + 73]);
        Assert.Equal(0, scoped[80 * 220 + 170]); // 未翻譯狀聲詞不被納入抹除遮罩

        using var sourceImg = SKImage.FromBitmap(bmp);
        using var erasedBmp = SkiaTextEraser.Erase(sourceImg, scoped)!;
        // 原本黑色筆畫處被淡藍背景色擴散填滿（而非硬塗純白 255,255,255）
        var filledPixel = erasedBmp.GetPixel(73, 100);
        Assert.InRange(filledPixel.Red, 204, 216);
        Assert.InRange(filledPixel.Green, 226, 238);
        Assert.InRange(filledPixel.Blue, 242, 254);

        // 狀聲詞像素完全保留原紅色
        var preservedSfx = erasedBmp.GetPixel(170, 80);
        Assert.Equal(sfxColor, preservedSfx);

        // 透過 RenderTranslatedPage(..., rawMask) 整合執行後，狀聲詞依然完好且區塊角落維持原背景色
        using var rendered = SkiaTranslationRenderer.RenderTranslatedPage(sourceImg, new[] { dialogBlock, sfxBlock }, rawMask);
        using var renderedBmp = SKBitmap.FromImage(rendered);
        Assert.Equal(sfxColor, renderedBmp.GetPixel(170, 80));
        var edgeBg = renderedBmp.GetPixel(56, 49);
        Assert.InRange(edgeBg.Blue, 242, 254);
    }

    [Fact]
    public void SkiaComicTextDetector_MissingModel_DegradesGracefullyWithoutThrowing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ComicOrg_CTD_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var missingPath = Path.Combine(tempDir, "inference", "missing.onnx");
            using var detector = new SkiaComicTextDetector(modelPath: missingPath);
            Assert.False(detector.IsAvailable);

            using var bmp = new SKBitmap(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var img = SKImage.FromBitmap(bmp);
            Assert.Null(detector.DetectTextMask(img));

            // 檢查 DataDirectory 解析回退：若主目錄沒有但 DataDirectory\inference 有，可正確定位
            Assert.False(ModelDownloader.IsComicTextDetectorAvailable(tempDir));
            var inferDir = Path.Combine(tempDir, "inference");
            Directory.CreateDirectory(inferDir);
            var dummyModel = Path.Combine(inferDir, ModelDownloader.ComicTextDetectorFileName);
            File.WriteAllBytes(dummyModel, new byte[] { 1, 2, 3, 4 });
            var resolved = ModelDownloader.ResolveComicTextDetectorPath(tempDir);
            Assert.True(File.Exists(resolved));
            Assert.False(ModelDownloader.IsComicTextDetectorAvailable(tempDir)); // 檔案過小視為未完整就緒
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
