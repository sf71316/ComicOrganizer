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
}
