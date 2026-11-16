using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using ComicOrganizer.App.Core;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

public class CoverDecodePerfTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly string _testImage;
    private readonly string _webpImage;
    private const int Iterations = 30;
    private const int Warmup = 5;
    private static readonly SKSamplingOptions ResizeSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    public CoverDecodePerfTest(ITestOutputHelper output)
    {
        _output = output;

        // 建立獨立的暫存資料夾
        _tempDir = Path.Combine(Path.GetTempPath(), "ComicOrganizerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _testImage = Path.Combine(_tempDir, "3.jpg");
        _webpImage = Path.Combine(_tempDir, "1.webp");

        // 動態生成 2000x3000 JPG 檔案
        using (var bmp = new Bitmap(2000, 3000))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Blue);
                g.DrawString("Test JPG", new Font("Arial", 50), Brushes.White, new PointF(100, 100));
            }
            bmp.Save(_testImage, System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        // 動態生成 2000x3000 WebP 檔案（透過 SkiaSharp）
        using (var skBmp = new SKBitmap(new SKImageInfo(2000, 3000, SKColorType.Bgra8888, SKAlphaType.Unpremul)))
        {
            skBmp.Erase(SKColors.DarkSlateBlue);
            using var skImg = SKImage.FromBitmap(skBmp);
            using var data = skImg.Encode(SKEncodedImageFormat.Webp, 85);
            using var fs = File.Create(_webpImage);
            data.SaveTo(fs);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Theory]
    [InlineData(170)]   // Small
    [InlineData(250)]   // Medium
    [InlineData(330)]   // Large
    public void Benchmark_DecodeAndResize(int targetWidth)
    {
        Assert.True(File.Exists(_testImage), $"Test image missing: {_testImage}");

        // Warmup（避免 JIT / disk cache 偏差）
        for (int i = 0; i < Warmup; i++)
        {
            GdiDecode(targetWidth).Dispose();
            SkiaFull(_testImage, targetWidth).Dispose();
            SkiaScaledCodec(_testImage, targetWidth).Dispose();
        }

        var gdi = MeasureMs(() => GdiDecode(targetWidth));
        var skFull = MeasureMs(() => SkiaFull(_testImage, targetWidth));
        var skScaled = MeasureMs(() => SkiaScaledCodec(_testImage, targetWidth));

        _output.WriteLine($"=== target width: {targetWidth} px ({Iterations} iter) ===");
        _output.WriteLine($"GDI+ full decode + resize       : {gdi,7:F2} ms");
        _output.WriteLine($"SkiaSharp full decode + resize  : {skFull,7:F2} ms  ({gdi / skFull:F2}x vs GDI+)");
        _output.WriteLine($"SkiaSharp + GetScaledDimensions : {skScaled,7:F2} ms  ({gdi / skScaled:F2}x vs GDI+)");
        _output.WriteLine("");
    }

    [Theory]
    [InlineData(170)]   // Small
    [InlineData(250)]   // Medium
    [InlineData(330)]   // Large
    public void Benchmark_WebP_DecodeAndResize(int targetWidth)
    {
        Assert.True(File.Exists(_webpImage), $"WebP test image missing: {_webpImage}");

        // Warmup
        for (int i = 0; i < Warmup; i++)
        {
            ReaderImageLoaderResize(_webpImage, targetWidth).Dispose();
            SkiaScaledCodec(_webpImage, targetWidth).Dispose();
        }

        var loader = MeasureMs(() => ReaderImageLoaderResize(_webpImage, targetWidth));
        var scaled = MeasureMs(() => SkiaScaledCodec(_webpImage, targetWidth));

        _output.WriteLine($"=== WebP target={targetWidth} px ({Iterations} iter) ===");
        _output.WriteLine($"ReaderImageLoader + GDI resize  : {loader,7:F2} ms");
        _output.WriteLine($"SkiaSharp + GetScaledDimensions : {scaled,7:F2} ms  ({loader / scaled:F2}x vs full)");
        _output.WriteLine("");
    }

    [Fact]
    public void Benchmark_IdentifyOnly()
    {
        Assert.True(File.Exists(_testImage));

        // Warmup
        for (int i = 0; i < Warmup; i++)
        {
            using var bm = Image.FromFile(_testImage);
            _ = bm.Width;
            using var codec = SKCodec.Create(_testImage);
            _ = codec?.Info.Width;
        }

        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            using var img = Image.FromFile(_testImage);
            _ = img.Width;
            _ = img.Height;
        }
        sw1.Stop();
        double gdi = sw1.Elapsed.TotalMilliseconds / Iterations;

        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            using var codec = SKCodec.Create(_testImage)!;
            var info = codec.Info;
            _ = info.Width;
            _ = info.Height;
        }
        sw2.Stop();
        double skIdentify = sw2.Elapsed.TotalMilliseconds / Iterations;

        _output.WriteLine("=== Identify (only read header) ===");
        _output.WriteLine($"GDI+ Image.FromFile + Width     : {gdi,7:F2} ms  (其實有完整解碼)");
        _output.WriteLine($"SkiaSharp SKCodec.Info          : {skIdentify,7:F2} ms");
        _output.WriteLine($"  speedup: {gdi / skIdentify:F2}x");
    }

    private double MeasureMs(Func<IDisposable> factory)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            using var _ = factory();
        }
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds / Iterations;
    }

    private Bitmap GdiDecode(int maxWidth)
    {
        using var img = Image.FromFile(_testImage);
        if (img.Width <= maxWidth) return new Bitmap(img);
        double ratio = (double)maxWidth / img.Width;
        int newHeight = (int)(img.Height * ratio);
        var res = new Bitmap(maxWidth, newHeight);
        using var g = Graphics.FromImage(res);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, 0, 0, maxWidth, newHeight);
        return res;
    }

    private static Bitmap ReaderImageLoaderResize(string path, int maxWidth)
    {
        using var img = ReaderImageLoader.DecodeWebpToBitmap(path);
        if (img.Width <= maxWidth) return new Bitmap(img);
        double ratio = (double)maxWidth / img.Width;
        int newHeight = (int)(img.Height * ratio);
        var res = new Bitmap(maxWidth, newHeight);
        using var g = Graphics.FromImage(res);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, 0, 0, maxWidth, newHeight);
        return res;
    }

    private static SKBitmap SkiaFull(string path, int maxWidth)
    {
        using var bmp = SKBitmap.Decode(path);
        if (bmp.Width <= maxWidth) return bmp.Copy();
        int newHeight = (int)(bmp.Height * ((double)maxWidth / bmp.Width));
        return bmp.Resize(new SKImageInfo(maxWidth, newHeight, bmp.ColorType, bmp.AlphaType), ResizeSampling);
    }

    private static SKBitmap SkiaScaledCodec(string path, int maxWidth)
    {
        using var codec = SKCodec.Create(path)!;
        var src = codec.Info;
        float scale = Math.Min(1f, (float)maxWidth / src.Width);
        var scaledSize = codec.GetScaledDimensions(scale);
        var decodeInfo = new SKImageInfo(scaledSize.Width, scaledSize.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var decoded = SKBitmap.Decode(codec, decodeInfo);
        if (decoded.Width == maxWidth) return decoded.Copy();
        int newHeight = (int)(decoded.Height * ((double)maxWidth / decoded.Width));
        return decoded.Resize(new SKImageInfo(maxWidth, newHeight, decoded.ColorType, decoded.AlphaType), ResizeSampling);
    }
}
