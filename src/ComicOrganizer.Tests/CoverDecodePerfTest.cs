using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
                g.Clear(System.Drawing.Color.Blue);
                g.DrawString("Test JPG", new Font("Arial", 50), Brushes.White, new System.Drawing.PointF(100, 100));
            }
            bmp.Save(_testImage, System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        // 動態生成 2000x3000 WebP 檔案
        using (var img = new SixLabors.ImageSharp.Image<Bgra32>(2000, 3000))
        {
            img.SaveAsWebp(_webpImage);
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
            ImageSharpFull(targetWidth).Dispose();
            ImageSharpTargetSize(targetWidth).Dispose();
        }

        var gdi = MeasureMs(() => GdiDecode(targetWidth));
        var isFull = MeasureMs(() => ImageSharpFull(targetWidth));
        var isDct = MeasureMs(() => ImageSharpTargetSize(targetWidth));

        _output.WriteLine($"=== target width: {targetWidth} px ({Iterations} iter) ===");
        _output.WriteLine($"GDI+ full decode + resize       : {gdi,7:F2} ms");
        _output.WriteLine($"ImageSharp full decode + resize : {isFull,7:F2} ms  ({gdi / isFull:F2}x vs GDI+)");
        _output.WriteLine($"ImageSharp + TargetSize (DCT)   : {isDct,7:F2} ms  ({gdi / isDct:F2}x vs GDI+)");
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
            ImageSharpWebpFull(_webpImage, targetWidth).Dispose();
            ImageSharpWebpTargetSize(_webpImage, targetWidth).Dispose();
        }

        var full = MeasureMs(() => ImageSharpWebpFull(_webpImage, targetWidth));
        var dct = MeasureMs(() => ImageSharpWebpTargetSize(_webpImage, targetWidth));

        _output.WriteLine($"=== WebP target={targetWidth} px ({Iterations} iter) ===");
        _output.WriteLine($"ImageSharp full decode + resize : {full,7:F2} ms");
        _output.WriteLine($"ImageSharp + TargetSize         : {dct,7:F2} ms  ({full / dct:F2}x vs full)");
        _output.WriteLine("");
    }

    private SixLabors.ImageSharp.Image<Bgra32> ImageSharpWebpFull(string path, int maxWidth)
    {
        var img = SixLabors.ImageSharp.Image.Load<Bgra32>(path);
        img.Mutate(x => x.Resize(maxWidth, 0));
        return img;
    }

    private SixLabors.ImageSharp.Image<Bgra32> ImageSharpWebpTargetSize(string path, int maxWidth)
    {
        var options = new DecoderOptions
        {
            TargetSize = new SixLabors.ImageSharp.Size(maxWidth, 0)
        };
        var img = SixLabors.ImageSharp.Image.Load<Bgra32>(options, path);
        if (img.Width != maxWidth)
        {
            img.Mutate(x => x.Resize(maxWidth, 0));
        }
        return img;
    }

    [Fact]
    public void Benchmark_IdentifyOnly()
    {
        Assert.True(File.Exists(_testImage));

        // Warmup
        for (int i = 0; i < Warmup; i++)
        {
            using var bm = System.Drawing.Image.FromFile(_testImage);
            _ = bm.Width;
            SixLabors.ImageSharp.Image.Identify(_testImage);
        }

        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            using var img = System.Drawing.Image.FromFile(_testImage);
            _ = img.Width;
            _ = img.Height;
        }
        sw1.Stop();
        double gdi = sw1.Elapsed.TotalMilliseconds / Iterations;

        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            var info = SixLabors.ImageSharp.Image.Identify(_testImage);
            _ = info.Width;
            _ = info.Height;
        }
        sw2.Stop();
        double isIdentify = sw2.Elapsed.TotalMilliseconds / Iterations;

        _output.WriteLine($"=== Identify (only read header) ===");
        _output.WriteLine($"GDI+ Image.FromFile + Width     : {gdi,7:F2} ms  (其實有完整解碼)");
        _output.WriteLine($"ImageSharp.Image.Identify       : {isIdentify,7:F2} ms");
        _output.WriteLine($"  speedup: {gdi / isIdentify:F2}x");
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

    // ====== 策略 1：GDI+ ======
    private Bitmap GdiDecode(int maxWidth)
    {
        using var img = System.Drawing.Image.FromFile(_testImage);
        if (img.Width <= maxWidth) return new Bitmap(img);
        double ratio = (double)maxWidth / img.Width;
        int newHeight = (int)(img.Height * ratio);
        var res = new Bitmap(maxWidth, newHeight);
        using var g = Graphics.FromImage(res);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, 0, 0, maxWidth, newHeight);
        return res;
    }

    // ====== 策略 2：ImageSharp 全解碼 + resize ======
    private SixLabors.ImageSharp.Image<Rgba32> ImageSharpFull(int maxWidth)
    {
        var img = SixLabors.ImageSharp.Image.Load<Rgba32>(_testImage);
        img.Mutate(x => x.Resize(maxWidth, 0));
        return img;
    }

    // ====== 策略 3：ImageSharp + TargetSize ======
    private SixLabors.ImageSharp.Image<Rgba32> ImageSharpTargetSize(int maxWidth)
    {
        var options = new DecoderOptions
        {
            TargetSize = new SixLabors.ImageSharp.Size(maxWidth, 0)
        };
        var img = SixLabors.ImageSharp.Image.Load<Rgba32>(options, _testImage);
        if (img.Width != maxWidth)
        {
            img.Mutate(x => x.Resize(maxWidth, 0));
        }
        return img;
    }
}
