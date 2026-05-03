using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using ComicOrganizer.App.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

public class WebpOptimizationTest
{
    private readonly ITestOutputHelper _output;

    public WebpOptimizationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ConvertImageSharpToBitmap_ShouldPreserveColors()
    {
        // ... (existing test code) ...
        using var imgSharp = new Image<Bgra32>(2, 2);
        imgSharp[0, 0] = new Bgra32(255, 0, 0, 255);
        imgSharp[1, 0] = new Bgra32(0, 255, 0, 255);
        imgSharp[0, 1] = new Bgra32(0, 0, 255, 255);
        imgSharp[1, 1] = new Bgra32(255, 255, 255, 255);

        var method = typeof(CoverManager).GetMethod("ConvertImageSharpToBitmap", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        Assert.NotNull(method);
        using var bmp = (Bitmap)method.Invoke(null, new object[] { imgSharp })!;

        var c00 = bmp.GetPixel(0, 0);
        Assert.Equal(255, c00.R);
        Assert.Equal(0, c00.G);
        Assert.Equal(0, c00.B);

        var c10 = bmp.GetPixel(1, 0);
        Assert.Equal(0, c10.R);
        Assert.Equal(255, c10.G);
        Assert.Equal(0, c10.B);

        var c01 = bmp.GetPixel(0, 1);
        Assert.Equal(0, c01.R);
        Assert.Equal(0, c01.G);
        Assert.Equal(255, c01.B);

        var c11 = bmp.GetPixel(1, 1);
        Assert.Equal(255, c11.R);
        Assert.Equal(255, c11.G);
        Assert.Equal(255, c11.B);
    }

    [Fact]
    public void Benchmark_OldVsNew()
    {
        // 使用 4K 等級的大圖來放大效能差異 (3840x2160)
        int width = 3840;
        int height = 2160;
        int iterations = 20;

        using var imgSharp = new Image<Bgra32>(width, height);
        
        // 取得優化後的私有方法 (New Way)
        var newMethod = typeof(CoverManager).GetMethod("ConvertImageSharpToBitmap", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(newMethod);

        // Warmup
        for(int i=0; i<3; i++) {
            OldWay(imgSharp).Dispose();
            ((Bitmap)newMethod.Invoke(null, new object[] { imgSharp })!).Dispose();
        }

        // Test Old Way
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long startMemOld = GC.GetTotalAllocatedBytes(true);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            using var bmp = OldWay(imgSharp);
        }
        sw.Stop();
        double oldTime = sw.Elapsed.TotalMilliseconds / iterations;
        long endMemOld = GC.GetTotalAllocatedBytes(true);
        double oldAlloc = (endMemOld - startMemOld) / (1024.0 * 1024.0 * iterations);

        // Test New Way
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long startMemNew = GC.GetTotalAllocatedBytes(true);
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            using var bmp = (Bitmap)newMethod.Invoke(null, new object[] { imgSharp })!;
        }
        sw.Stop();
        double newTime = sw.Elapsed.TotalMilliseconds / iterations;
        long endMemNew = GC.GetTotalAllocatedBytes(true);
        double newAlloc = (endMemNew - startMemNew) / (1024.0 * 1024.0 * iterations);

        _output.WriteLine($"=== Benchmark: Old vs New (Image: {width}x{height}, {iterations} iter) ===");
        _output.WriteLine($"Old Way (byte[] + Marshal) : {oldTime,7:F2} ms, Alloc: {oldAlloc,7:F2} MB/op");
        _output.WriteLine($"New Way (Unsafe Direct)    : {newTime,7:F2} ms, Alloc: {newAlloc,7:F2} MB/op");
        _output.WriteLine($"Improvement                : {oldTime / newTime:F2}x Faster");
        _output.WriteLine($"Allocation Reduction       : {oldAlloc - newAlloc,7:F2} MB saved per image");

        File.WriteAllText("benchmark_results.txt", 
            $"Old: {oldTime:F2}ms, {oldAlloc:F2}MB\n" +
            $"New: {newTime:F2}ms, {newAlloc:F2}MB\n" +
            $"Improvement: {oldTime/newTime:F2}x\n" +
            $"Saved: {oldAlloc-newAlloc:F2}MB");
    }

    private Bitmap OldWay(Image<Bgra32> imgSharp)
    {
        int width = imgSharp.Width;
        int height = imgSharp.Height;
        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new System.Drawing.Rectangle(0, 0, width, height);
        var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = width * 4;
            byte[] pixels = new byte[rowBytes * height]; // 模擬舊版的配置
            imgSharp.CopyPixelDataTo(pixels);

            if (bmpData.Stride == rowBytes)
            {
                Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels, y * rowBytes,
                        IntPtr.Add(bmpData.Scan0, y * bmpData.Stride), rowBytes);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }
        return bmp;
    }
}
