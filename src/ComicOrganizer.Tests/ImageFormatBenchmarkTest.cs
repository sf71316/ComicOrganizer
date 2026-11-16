using System.Diagnostics;
using ComicOrganizer.App.Core;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

/// <summary>
/// 比較 reader pipeline 內不同格式的 decode 成本：
///   jpg / png / bmp → System.Drawing.Image.FromStream（GDI+ native）
///   webp            → SkiaSharp SKCodec + LockBits 直拷（經 ReaderImageLoader）
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

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var srcBmp = new SKBitmap(info);
        var srcBytes = new byte[width * height * 4];
        new Random(42).NextBytes(srcBytes);
        System.Runtime.InteropServices.Marshal.Copy(srcBytes, 0, srcBmp.GetPixels(), srcBytes.Length);

        using var srcImg = SKImage.FromBitmap(srcBmp);
        using var jpgData = srcImg.Encode(SKEncodedImageFormat.Jpeg, 85);
        using var pngData = srcImg.Encode(SKEncodedImageFormat.Png, 100);
        using var webpData = srcImg.Encode(SKEncodedImageFormat.Webp, 85);

        using var jpgStream = new MemoryStream(jpgData.ToArray());
        using var pngStream = new MemoryStream(pngData.ToArray());
        using var webpStream = new MemoryStream(webpData.ToArray());

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
        _output.WriteLine($"jpg  (GDI+ FromStream)          : {jpgTime,7:F2} ms, Alloc: {jpgAlloc,7:F2} MB/op");
        _output.WriteLine($"png  (GDI+ FromStream)          : {pngTime,7:F2} ms, Alloc: {pngAlloc,7:F2} MB/op");
        _output.WriteLine($"webp (SkiaSharp+LockBits copy)  : {webpTime,7:F2} ms, Alloc: {webpAlloc,7:F2} MB/op");
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
