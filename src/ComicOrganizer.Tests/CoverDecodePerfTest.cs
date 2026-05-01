using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

public class CoverDecodePerfTest
{
    private readonly ITestOutputHelper _output;
    private const string TestImage = @"D:\Project\ComicOrganize\TestData\Library\3.jpg";
    private const int Iterations = 30;
    private const int Warmup = 5;

    public CoverDecodePerfTest(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(170)]   // Small
    [InlineData(250)]   // Medium
    [InlineData(330)]   // Large
    public void Benchmark_DecodeAndResize(int targetWidth)
    {
        Assert.True(File.Exists(TestImage), $"Test image missing: {TestImage}");

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

    [Fact]
    public void Benchmark_IdentifyOnly()
    {
        Assert.True(File.Exists(TestImage));

        // Warmup
        for (int i = 0; i < Warmup; i++)
        {
            using var bm = System.Drawing.Image.FromFile(TestImage);
            _ = bm.Width;
            SixLabors.ImageSharp.Image.Identify(TestImage);
        }

        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            using var img = System.Drawing.Image.FromFile(TestImage);
            _ = img.Width;
            _ = img.Height;
        }
        sw1.Stop();
        double gdi = sw1.Elapsed.TotalMilliseconds / Iterations;

        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            var info = SixLabors.ImageSharp.Image.Identify(TestImage);
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

    // ====== 策略 1：GDI+（當前 CoverManager 走的路徑）======
    private static Bitmap GdiDecode(int maxWidth)
    {
        using var img = System.Drawing.Image.FromFile(TestImage);
        if (img.Width <= maxWidth) return new Bitmap(img);
        double ratio = (double)maxWidth / img.Width;
        int newHeight = (int)(img.Height * ratio);
        var res = new Bitmap(maxWidth, newHeight);
        using var g = Graphics.FromImage(res);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, 0, 0, maxWidth, newHeight);
        return res;
    }

    // ====== 策略 2：ImageSharp 全解碼 + resize（pure managed）======
    private static SixLabors.ImageSharp.Image<Rgba32> ImageSharpFull(int maxWidth)
    {
        var img = SixLabors.ImageSharp.Image.Load<Rgba32>(TestImage);
        img.Mutate(x => x.Resize(maxWidth, 0));
        return img;
    }

    // ====== 策略 3：ImageSharp + TargetSize (JPEG DCT pre-scaling)======
    private static SixLabors.ImageSharp.Image<Rgba32> ImageSharpTargetSize(int maxWidth)
    {
        var options = new DecoderOptions
        {
            TargetSize = new SixLabors.ImageSharp.Size(maxWidth, 0)
        };
        var img = SixLabors.ImageSharp.Image.Load<Rgba32>(options, TestImage);
        // DCT 縮放只能落在 1/2、1/4、1/8 等冪次點；補一次 fine resize
        if (img.Width != maxWidth)
        {
            img.Mutate(x => x.Resize(maxWidth, 0));
        }
        return img;
    }
}
