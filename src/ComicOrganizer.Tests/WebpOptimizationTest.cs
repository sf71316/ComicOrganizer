using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ComicOrganizer.App.Core;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

public class WebpOptimizationTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;

    public WebpOptimizationTest(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_webp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static byte[] EncodeLosslessWebp(SKBitmap skBmp)
    {
        using var pixmap = skBmp.PeekPixels();
        using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100));
        Assert.NotNull(data);
        return data.ToArray();
    }

    private static void AssertPixelMatchesColors(Bitmap bmp)
    {
        var c00 = bmp.GetPixel(0, 0);
        Assert.Equal(255, c00.A);
        Assert.Equal(255, c00.R);
        Assert.Equal(0, c00.G);
        Assert.Equal(0, c00.B);

        var c10 = bmp.GetPixel(1, 0);
        Assert.Equal(255, c10.A);
        Assert.Equal(0, c10.R);
        Assert.Equal(255, c10.G);
        Assert.Equal(0, c10.B);

        var c01 = bmp.GetPixel(0, 1);
        Assert.Equal(255, c01.A);
        Assert.Equal(0, c01.R);
        Assert.Equal(0, c01.G);
        Assert.Equal(255, c01.B);

        // 半透明像素驗證 Unpremul：若誤用 Premul，R 會被乘以 128/255 ≈ 128；Unpremul 應維持接近 255
        var c11 = bmp.GetPixel(1, 1);
        Assert.Equal(128, c11.A);
        Assert.True(c11.R >= 250, $"Expected unpremultiplied R >= 250, got {c11.R}");
        Assert.InRange(c11.G, 95, 105);
        Assert.InRange(c11.B, 45, 55);
    }

    [Fact]
    public void DecodeWebpToBitmap_ShouldPreserveColorsAndAlpha()
    {
        var info = new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var skBmp = new SKBitmap(info);
        skBmp.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        skBmp.SetPixel(1, 0, new SKColor(0, 255, 0, 255));
        skBmp.SetPixel(0, 1, new SKColor(0, 0, 255, 255));
        skBmp.SetPixel(1, 1, new SKColor(255, 100, 50, 128));

        var webpBytes = EncodeLosslessWebp(skBmp);

        // 1. 透過 Stream + 小寫副檔名
        using (var ms = new MemoryStream(webpBytes))
        using (var img = ReaderImageLoader.Decode(ms, ".webp"))
        {
            var bmp = Assert.IsType<Bitmap>(img);
            AssertPixelMatchesColors(bmp);
        }

        // 2. 透過大寫副檔名（OrdinalIgnoreCase）
        using (var msUpper = new MemoryStream(webpBytes))
        using (var imgUpper = ReaderImageLoader.Decode(msUpper, ".WEBP"))
        {
            var bmpUpper = Assert.IsType<Bitmap>(imgUpper);
            AssertPixelMatchesColors(bmpUpper);
        }

        // 3. 非可尋址串流
        using (var nonSeekable = new NonSeekableMemoryStream(webpBytes))
        using (var bmpNonSeek = ReaderImageLoader.DecodeWebpToBitmap(nonSeekable))
        {
            AssertPixelMatchesColors(bmpNonSeek);
        }

        // 4. 透過實體檔案路徑
        var filePath = Path.Combine(_tempDir, "sample.webp");
        File.WriteAllBytes(filePath, webpBytes);
        using (var bmpFile = ReaderImageLoader.DecodeWebpToBitmap(filePath))
        {
            AssertPixelMatchesColors(bmpFile);
        }
    }

    [Fact]
    public void DecodeWebpToBitmap_InvalidStream_ThrowsInvalidDataException()
    {
        using var badStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6 });
        Assert.Throws<InvalidDataException>(() => ReaderImageLoader.DecodeWebpToBitmap(badStream));
    }

    [Fact]
    public void Benchmark_OldVsNew()
    {
        int width = 3840;
        int height = 2160;
        int iterations = 10;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var skBmp = new SKBitmap(info);
        skBmp.Erase(new SKColor(120, 160, 200, 255));
        var webpBytes = EncodeLosslessWebp(skBmp);

        // Warmup
        for (int i = 0; i < 2; i++)
        {
            using var msOld = new MemoryStream(webpBytes);
            OldWay(msOld).Dispose();
            using var msNew = new MemoryStream(webpBytes);
            ReaderImageLoader.DecodeWebpToBitmap(msNew).Dispose();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        long startMemOld = GC.GetTotalAllocatedBytes(true);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            using var ms = new MemoryStream(webpBytes);
            using var bmp = OldWay(ms);
        }
        sw.Stop();
        double oldTime = sw.Elapsed.TotalMilliseconds / iterations;
        long endMemOld = GC.GetTotalAllocatedBytes(true);
        double oldAlloc = (endMemOld - startMemOld) / (1024.0 * 1024.0 * iterations);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        long startMemNew = GC.GetTotalAllocatedBytes(true);
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            using var ms = new MemoryStream(webpBytes);
            using var bmp = ReaderImageLoader.DecodeWebpToBitmap(ms);
        }
        sw.Stop();
        double newTime = sw.Elapsed.TotalMilliseconds / iterations;
        long endMemNew = GC.GetTotalAllocatedBytes(true);
        double newAlloc = (endMemNew - startMemNew) / (1024.0 * 1024.0 * iterations);

        _output.WriteLine($"=== Benchmark: SKBitmap+Marshal vs Direct SKCodec→LockBits ({width}x{height}, {iterations} iter) ===");
        _output.WriteLine($"Old Way (SKBitmap + byte[] + Marshal) : {oldTime,7:F2} ms, Alloc: {oldAlloc,7:F2} MB/op");
        _output.WriteLine($"New Way (Direct SKCodec → LockBits)  : {newTime,7:F2} ms, Alloc: {newAlloc,7:F2} MB/op");
    }

    private static Bitmap OldWay(Stream stream)
    {
        using var skBmp = SKBitmap.Decode(stream);
        int width = skBmp.Width;
        int height = skBmp.Height;
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixels = skBmp.Bytes;
            int rowBytes = skBmp.RowBytes;
            if (bmpData.Stride == rowBytes)
            {
                Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            }
            else
            {
                int copyBytes = Math.Min(rowBytes, Math.Abs(bmpData.Stride));
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(bmpData.Scan0, y * bmpData.Stride), copyBytes);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }
        return bmp;
    }

    private sealed class NonSeekableMemoryStream : Stream
    {
        private readonly MemoryStream _inner;
        public NonSeekableMemoryStream(byte[] bytes) => _inner = new MemoryStream(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
