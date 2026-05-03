using System.Diagnostics;
using ComicOrganizer.App.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

/// <summary>
/// 比較 reader pipeline 內不同格式的 decode 成本：
///   jpg / png / bmp → System.Drawing.Image.FromStream（GDI+ native）
///   webp           → ImageSharp.Load + unsafe LockBits 直拷
///
/// 註：曾試過加 OpenCvSharp（libjpeg-turbo / libpng / libwebp）跑 benchmark：
/// - jpg/png 反而 0.6× 慢（BitmapConverter.ToBitmap 內部 byte[] 中轉抵銷掉 native decode 優勢）
/// - webp 1.45× 快但只省 ~117ms
/// - 代價 30-50 MB native binary 不划算 → 不採用
/// </summary>
public class ImageFormatBenchmarkTest
{
    private readonly ITestOutputHelper _output;

    public ImageFormatBenchmarkTest(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void Benchmark_JpgPngWebp_DecodeTime()
    {
        const int width = 3840;
        const int height = 2160;
        const int iterations = 20;

        var srcBytes = new byte[width * height * 4];
        new Random(42).NextBytes(srcBytes);
        using var src = SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(srcBytes, width, height);

        var jpgStream = new MemoryStream();
        var pngStream = new MemoryStream();
        var webpStream = new MemoryStream();
        src.Save(jpgStream, new JpegEncoder { Quality = 85 });
        src.Save(pngStream, new PngEncoder());
        src.Save(webpStream, new WebpEncoder { Quality = 85 });

        _output.WriteLine($"Encoded sizes: jpg={jpgStream.Length / 1024}KB, png={pngStream.Length / 1024}KB, webp={webpStream.Length / 1024}KB");

        for (int i = 0; i < 2; i++)
        {
            DecodeViaPath(jpgStream, ".jpg").Dispose();
            DecodeViaPath(pngStream, ".png").Dispose();
            DecodeViaPath(webpStream, ".webp").Dispose();
        }

        var (jpgTime, jpgAlloc) = Measure(jpgStream, ".jpg", iterations);
        var (pngTime, pngAlloc) = Measure(pngStream, ".png", iterations);
        var (webpTime, webpAlloc) = Measure(webpStream, ".webp", iterations);

        _output.WriteLine($"=== Decode Benchmark (4K, {iterations} iter avg, ReaderImageLoader path) ===");
        _output.WriteLine($"jpg  (GDI+ FromStream)         : {jpgTime,7:F2} ms, Alloc: {jpgAlloc,7:F2} MB/op");
        _output.WriteLine($"png  (GDI+ FromStream)         : {pngTime,7:F2} ms, Alloc: {pngAlloc,7:F2} MB/op");
        _output.WriteLine($"webp (ImageSharp+unsafe copy)  : {webpTime,7:F2} ms, Alloc: {webpAlloc,7:F2} MB/op");
    }

    private static (double time, double alloc) Measure(MemoryStream encoded, string ext, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long start = GC.GetTotalAllocatedBytes(true);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            using var img = DecodeViaPath(encoded, ext);
        }
        sw.Stop();
        long end = GC.GetTotalAllocatedBytes(true);
        return (sw.Elapsed.TotalMilliseconds / iterations, (end - start) / (1024.0 * 1024.0 * iterations));
    }

    private static System.Drawing.Image DecodeViaPath(MemoryStream encoded, string ext)
    {
        encoded.Position = 0;
        return ReaderImageLoader.Decode(encoded, ext);
    }
}
