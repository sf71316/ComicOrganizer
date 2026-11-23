using System.IO.Compression;
using System.Runtime.InteropServices;
using ComicOrganizer.App.Core;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 C（ArchiveReaderSource 有界 LRU 快取 / 釋放 與 PdfReaderSource Dispose 保護）單元測試。
/// </summary>
public class ArchiveReaderCacheTests : IDisposable
{
    private readonly string _tempDir;

    public ArchiveReaderCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_arc_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static byte[] CreateNoisePng(int width, int height, int seed)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        var raw = new byte[width * height * 4];
        new Random(seed).NextBytes(raw);
        Marshal.Copy(raw, 0, bmp.GetPixels(), raw.Length);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private string CreateTestZip(string fileName, out byte[][] pageBytes, params (int Width, int Height)[] dimensions)
    {
        var zipPath = Path.Combine(_tempDir, fileName);
        pageBytes = new byte[dimensions.Length][];
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        for (int i = 0; i < dimensions.Length; i++)
        {
            pageBytes[i] = CreateNoisePng(dimensions[i].Width, dimensions[i].Height, seed: 100 + i);
            var entry = zip.CreateEntry($"{i + 1:D3}.png", CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            entryStream.Write(pageBytes[i], 0, pageBytes[i].Length);
        }
        return zipPath;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using (stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    [Fact]
    public void OpenPage_ExceedingBudget_EvictsLruAndMaintainsBudgetWithAccurateFallback()
    {
        // 建立 4 頁（每頁 ~10KB-12KB，彼此尺寸不同且任兩頁總和均小於預算、任三頁總和必大於預算）
        var zipPath = CreateTestZip(
            "lru_evict.zip",
            out var expectedPages,
            (50, 50), // page 0 (~10.0KB)
            (53, 53), // page 1 (~11.2KB)
            (56, 56), // page 2 (~12.5KB)
            (51, 51)  // page 3 (~10.4KB)
        );

        // 預算設為確保任兩頁皆可共存（含 [0,1]、[0,2]、[2,3]），且 backgroundBudget (= budget/4 ≈ 6KB) 小於最小頁不會背景偷塞
        long p0 = expectedPages[0].Length;
        long p1 = expectedPages[1].Length;
        long p2 = expectedPages[2].Length;
        long p3 = expectedPages[3].Length;
        long budget = Math.Max(p0 + p1, Math.Max(p0 + p2, p2 + p3)) + 512;
        Assert.True(p0 != p1);
        Assert.True(p0 + p1 + p3 > budget);

        using var source = new ArchiveReaderSource(zipPath, maxCacheBytes: budget);
        Assert.Equal(budget, source.MaxCacheBytes);

        // 讀取 0, 1 → 都在快取中（MRU: 1, 0）
        Assert.Equal(expectedPages[0], ReadAllBytes(source.OpenPage(0)));
        Assert.Equal(expectedPages[1], ReadAllBytes(source.OpenPage(1)));
        Assert.Equal(2, source.CachedPageCount);
        Assert.True(source.CachedBytes <= source.MaxCacheBytes);

        // 重新存取 page 0，刷新 MRU（MRU: 0, 1）
        Assert.Equal(expectedPages[0], ReadAllBytes(source.OpenPage(0)));

        // 讀取 page 2 → 觸發淘汰最久未用的 page 1，保留 page 0 與 page 2
        Assert.Equal(expectedPages[2], ReadAllBytes(source.OpenPage(2)));
        Assert.True(source.CachedBytes <= source.MaxCacheBytes);
        Assert.Equal(2, source.CachedPageCount);
        Assert.Equal(expectedPages[0].Length + expectedPages[2].Length, source.CachedBytes);

        // 讀取 page 3 → 淘汰 page 0（保留 MRU 的 page 2 與 page 3）
        Assert.Equal(expectedPages[3], ReadAllBytes(source.OpenPage(3)));
        Assert.True(source.CachedBytes <= source.MaxCacheBytes);
        Assert.Equal(expectedPages[2].Length + expectedPages[3].Length, source.CachedBytes);

        // 遭淘汰的 page 1 仍可從壓縮檔正確重讀
        Assert.Equal(expectedPages[1], ReadAllBytes(source.OpenPage(1)));
        Assert.True(source.CachedBytes <= source.MaxCacheBytes);
    }

    [Fact]
    public void OpenPage_SinglePageLargerThanBudget_RetainsAccessedPageWithoutUnderflow()
    {
        var zipPath = CreateTestZip("oversized_page.zip", out var expectedPages, (64, 64), (64, 64));
        long tinyBudget = 1024; // 遠小於單頁 (~16KB)

        using var source = new ArchiveReaderSource(zipPath, maxCacheBytes: tinyBudget);

        Assert.Equal(expectedPages[0], ReadAllBytes(source.OpenPage(0)));
        Assert.Equal(1, source.CachedPageCount);
        Assert.Equal(expectedPages[0].Length, source.CachedBytes);

        // 存取 page 1 時淘汰 page 0，但仍保留剛存取的 page 1
        Assert.Equal(expectedPages[1], ReadAllBytes(source.OpenPage(1)));
        Assert.Equal(1, source.CachedPageCount);
        Assert.Equal(expectedPages[1].Length, source.CachedBytes);
    }

    [Fact]
    public void ArchiveReaderSource_Dispose_ClearsCacheIdempotentlyAndThrowsOnOpenPage()
    {
        var zipPath = CreateTestZip("dispose_test.zip", out var expectedPages, (48, 48), (48, 48));
        var source = new ArchiveReaderSource(zipPath);

        Assert.Equal(expectedPages[0], ReadAllBytes(source.OpenPage(0)));
        Assert.True(source.CachedBytes > 0);
        Assert.True(source.CachedPageCount >= 1);

        source.Dispose();
        Assert.Equal(0, source.CachedBytes);
        Assert.Equal(0, source.CachedPageCount);

        // 冪等重複 Dispose 不得拋錯
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.OpenPage(0));
    }

    [Fact]
    public void PdfReaderSource_Dispose_ReleasesBytesIdempotentlyAndThrowsOnOpenPage()
    {
        var pdfPath = Path.Combine(_tempDir, "sample.pdf");
        using (var doc = SKDocument.CreatePdf(pdfPath))
        using (var canvas = doc.BeginPage(100, 100))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            doc.EndPage();
        }

        var pdfSource = new PdfReaderSource(pdfPath, dpi: 72);
        Assert.Equal(1, pdfSource.PageCount);

        using (var pageStream = pdfSource.OpenPage(0))
        {
            Assert.True(pageStream.Length > 0);
        }

        pdfSource.Dispose();
        pdfSource.Dispose(); // 冪等重複釋放

        Assert.Throws<ObjectDisposedException>(() => pdfSource.OpenPage(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    public void ArchiveReaderSource_ShiftedZipWithLeadingBytes_OpensAllPagesAndReleasesFileLockOnDispose(int prefixByteCount)
    {
        var rawZip = CreateTestZip("raw_valid.zip", out var expectedPages, (32, 32), (36, 36), (40, 40));
        var rawBytes = File.ReadAllBytes(rawZip);

        // 模擬開頭多出 0x0A（換行）或 HTTP 前置文字的畸形 ZIP
        var shiftedPath = Path.Combine(_tempDir, $"shifted_{prefixByteCount}.zip");
        var prefix = Enumerable.Repeat((byte)0x0A, prefixByteCount).ToArray();
        using (var outFs = File.Create(shiftedPath))
        {
            outFs.Write(prefix, 0, prefix.Length);
            outFs.Write(rawBytes, 0, rawBytes.Length);
        }

        using (var source = new ArchiveReaderSource(shiftedPath))
        {
            Assert.Equal(3, source.PageCount);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(expectedPages[i], ReadAllBytes(source.OpenPage(i)));
            }
        }

        // Dispose 後底層 OffsetReadOnlyStream / FileStream 應完整釋放，不可留下檔案鎖定
        using var lockCheck = File.Open(shiftedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(lockCheck.Length > rawBytes.Length);
    }

    [Fact]
    public void ArchiveReaderSource_InvalidNonArchiveFile_Throws()
    {
        var badPath = Path.Combine(_tempDir, "not_an_archive.zip");
        File.WriteAllText(badPath, "definitely not a valid zip archive");

        Assert.ThrowsAny<Exception>(() => new ArchiveReaderSource(badPath));
    }
}
