using ComicOrganizer.App.Core;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

public class ReaderImageEnhancerTests
{
    private static SKImage CreateSyntheticMangaPage()
    {
        using var bmp = new SKBitmap(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);

        // 泛黃老舊掃描紙張背景 RGB(240, 235, 222)
        canvas.Clear(new SKColor(240, 235, 222));

        // 灰霧不夠黑的漫畫墨線區塊 RGB(48, 48, 48)
        using var inkPaint = new SKPaint { Color = new SKColor(48, 48, 48), IsAntialias = false };
        canvas.DrawRect(new SKRect(8, 8, 16, 24), inkPaint);

        // 低飽和度彩色插圖區塊 RGB(190, 120, 110)
        using var colorPaint = new SKPaint { Color = new SKColor(190, 120, 110), IsAntialias = false };
        canvas.DrawRect(new SKRect(18, 8, 28, 24), colorPaint);

        canvas.Flush();
        return SKImage.FromBitmap(bmp);
    }

    [Fact]
    public void VintageBleach_BleachesYellowedPaper_AndCrushesGrayInk()
    {
        using var source = CreateSyntheticMangaPage();
        var options = ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.VintageBleach);
        Assert.True(options.HasActiveFilter);

        using var enhanced = ReaderImageEnhancer.RenderEnhancedImage(source, options);
        using var bmp = SKBitmap.FromImage(enhanced);

        // 背景 (2, 2) 原為泛黃 (240, 235, 222)，經老舊掃描漂白後應接近純白 (>= 252)
        var bg = bmp.GetPixel(2, 2);
        Assert.True(bg.Red >= 252 && bg.Green >= 252 && bg.Blue >= 252, $"Expected bleached white paper, got ({bg.Red},{bg.Green},{bg.Blue})");

        // 墨線中心 (12, 16) 原為灰黑 (48, 48, 48)，經黑點壓實與對比強化後應比原本更黑
        var ink = bmp.GetPixel(12, 16);
        Assert.True(ink.Red < 30 && ink.Green < 30 && ink.Blue < 30, $"Expected darker ink, got ({ink.Red},{ink.Green},{ink.Blue})");
    }

    [Fact]
    public void MangaCrisp_SharpensEdges_AndClearsLightHaze()
    {
        using var source = CreateSyntheticMangaPage();
        var options = ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.MangaCrisp);

        using var enhanced = ReaderImageEnhancer.RenderEnhancedImage(source, options);
        using var bmp = SKBitmap.FromImage(enhanced);

        var bg = bmp.GetPixel(2, 2);
        Assert.True(bg.Blue > 222, $"Expected cleaner background than 222, got {bg.Blue}");

        var ink = bmp.GetPixel(12, 16);
        Assert.True(ink.Red < 48, $"Expected darker ink than 48, got {ink.Red}");
    }

    [Fact]
    public void ColorVivid_IncreasesColorSaturation()
    {
        using var source = CreateSyntheticMangaPage();
        var options = ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.ColorVivid);

        using var enhanced = ReaderImageEnhancer.RenderEnhancedImage(source, options);
        using var bmp = SKBitmap.FromImage(enhanced);

        // 原彩漫區塊 (22, 16) 為 (190, 120, 110)，R-B 差距為 80；增艷後 R-B 差距應顯著擴大
        var vivid = bmp.GetPixel(22, 16);
        int origSpread = 190 - 110;
        int newSpread = vivid.Red - vivid.Blue;
        Assert.True(newSpread > origSpread, $"Expected saturation spread > {origSpread}, got {newSpread} ({vivid.Red},{vivid.Green},{vivid.Blue})");
    }

    [Fact]
    public void NightEyeCare_DimsHarshWhite_AndAppliesWarmPaperTint()
    {
        using var source = CreateSyntheticMangaPage();
        var options = ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.NightEyeCare);

        using var enhanced = ReaderImageEnhancer.RenderEnhancedImage(source, options);
        using var bmp = SKBitmap.FromImage(enhanced);

        // 背景應降亮 (< 220) 且呈暖色溫 (R > G > B)
        var bg = bmp.GetPixel(2, 2);
        Assert.True(bg.Red < 225, $"Expected dimmed red channel < 225, got {bg.Red}");
        Assert.True(bg.Red > bg.Blue, $"Expected warm tint (Red > Blue), got R={bg.Red}, B={bg.Blue}");
    }

    [Fact]
    public async Task BuiltInAnimeLineSuperResPlugin_EnhancesAndUpscalesSmallMangaPage()
    {
        using var source = CreateSyntheticMangaPage();
        var plugins = ImageEnhancerPluginManager.GetPlugins();
        Assert.NotEmpty(plugins);

        var builtIn = plugins.First(p => p.Id == "builtin-anime4k-superres");
        Assert.True(builtIn.IsAvailable);

        using var superRes = await builtIn.EnhancePageAsync(source);
        Assert.NotNull(superRes);
        // 32x32 小圖經 1.5x 升頻後應為 48x48
        Assert.Equal(48, superRes!.Width);
        Assert.Equal(48, superRes.Height);
    }

    [Fact]
    public void ContinuousLayout_SupportsStandardGap_AndGaplessKoreanWebtoonMode()
    {
        var layout = new ContinuousLayout(3, 800);
        layout.SetMeasured(0, 800, 1200);
        layout.SetMeasured(1, 800, 1200);
        layout.SetMeasured(2, 800, 1200);

        // 標準頁距 8px
        layout.Gap = 8;
        Assert.Equal(0, layout.GetPageY(0));
        Assert.Equal(1208, layout.GetPageY(1));
        Assert.Equal(2416, layout.GetPageY(2));
        Assert.Equal(3616, layout.TotalHeight);

        // 韓漫無縫 0px
        layout.Gap = 0;
        Assert.Equal(0, layout.GetPageY(0));
        Assert.Equal(1200, layout.GetPageY(1));
        Assert.Equal(2400, layout.GetPageY(2));
        Assert.Equal(3600, layout.TotalHeight);
    }

    [Fact]
    public void ReaderSourceFactory_OpensDirectory_AndSingleImageFileWithInitialPageIndex()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ComicOrganizer_FactoryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var img = CreateSyntheticMangaPage();
            using var data = img.Encode(SKEncodedImageFormat.Png, 90);
            var p1 = Path.Combine(tempDir, "001.png");
            var p2 = Path.Combine(tempDir, "002.png");
            File.WriteAllBytes(p1, data.ToArray());
            File.WriteAllBytes(p2, data.ToArray());

            using var dirSource = ReaderSourceFactory.Open(tempDir, out int dirIdx);
            Assert.Equal(2, dirSource.PageCount);
            Assert.Equal(0, dirIdx);

            using var fileSource = ReaderSourceFactory.Open(p2, out int fileIdx);
            Assert.Equal(2, fileSource.PageCount);
            Assert.Equal(1, fileIdx);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
