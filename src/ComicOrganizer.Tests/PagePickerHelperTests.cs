using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.Json;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 E（IReaderSource.GetPageMetadata、PagePickerHelper、ReaderImageLoader.DecodeThumbnail 與 UserPreferences）單元測試。
/// </summary>
public class PagePickerHelperTests : IDisposable
{
    private readonly string _tempDir;

    public PagePickerHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_wi_picker_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ===== 1. ArchiveReaderSource & FolderReaderSource GetPageMetadata =====

    [Fact]
    public void FolderReaderSource_GetPageMetadata_ReadsNameSizeTimeAndChecksBounds()
    {
        var folder = Path.Combine(_tempDir, "Pages");
        Directory.CreateDirectory(folder);

        var file1 = Path.Combine(folder, "001.png");
        var file2 = Path.Combine(folder, "002.jpg");
        File.WriteAllBytes(file1, new byte[123]);
        File.WriteAllBytes(file2, new byte[456]);
        var mtime1 = new DateTime(2025, 11, 1, 9, 30, 0, DateTimeKind.Local);
        File.SetLastWriteTime(file1, mtime1);

        using var source = new FolderReaderSource(folder);
        Assert.Equal(2, source.PageCount);

        var meta0 = source.GetPageMetadata(0);
        Assert.Equal(0, meta0.PageIndex);
        Assert.Equal("001.png", meta0.Name);
        Assert.Equal(123L, meta0.SizeBytes);
        Assert.NotNull(meta0.LastModified);
        Assert.Equal(mtime1, meta0.LastModified!.Value);

        var meta1 = source.GetPageMetadata(1);
        Assert.Equal(1, meta1.PageIndex);
        Assert.Equal("002.jpg", meta1.Name);
        Assert.Equal(456L, meta1.SizeBytes);

        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetPageMetadata(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetPageMetadata(2));
    }

    [Fact]
    public void ArchiveReaderSource_GetPageMetadata_ReadsEntryMetadataWithoutDecompressingAndChecksBounds()
    {
        var zipPath = Path.Combine(_tempDir, "comic.cbz");
        var entryTime = new DateTime(2025, 6, 15, 14, 20, 0, DateTimeKind.Local);

        using (var fs = File.Create(zipPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("sub/02.png", CompressionLevel.Fastest);
            e1.LastWriteTime = new DateTimeOffset(entryTime);
            using (var s = e1.Open()) s.Write(new byte[321], 0, 321);

            var e2 = zip.CreateEntry("sub/10.webp", CompressionLevel.Fastest);
            using (var s = e2.Open()) s.Write(new byte[654], 0, 654);
        }

        using var source = new ArchiveReaderSource(zipPath);
        Assert.Equal(2, source.PageCount);

        var m0 = source.GetPageMetadata(0);
        Assert.Equal(0, m0.PageIndex);
        Assert.Equal("02.png", m0.Name);
        Assert.Equal(321L, m0.SizeBytes);
        Assert.NotNull(m0.LastModified);

        var m1 = source.GetPageMetadata(1);
        Assert.Equal(1, m1.PageIndex);
        Assert.Equal("10.webp", m1.Name);
        Assert.Equal(654L, m1.SizeBytes);

        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetPageMetadata(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetPageMetadata(2));

        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.GetPageMetadata(0));
    }

    // ===== 2. PagePickerHelper =====

    [Fact]
    public void BuildDisplayIndices_FiltersCaseInsensitivelyAndSortsNaturallyOrByPageOrder()
    {
        var names = new[] { "cover.jpg", "page10.png", "page2.png", "Bonus_01.webp" };

        // 依頁序且不過濾
        Assert.Equal(new[] { 0, 1, 2, 3 }, PagePickerHelper.BuildDisplayIndices(names, null, sortByName: false));

        // 依自然名稱排序：Bonus_01 < cover < page2 < page10
        Assert.Equal(new[] { 3, 0, 2, 1 }, PagePickerHelper.BuildDisplayIndices(names, "  ", sortByName: true));

        // 關鍵字篩選 + 頁序
        Assert.Equal(new[] { 1, 2 }, PagePickerHelper.BuildDisplayIndices(names, "PAGE", sortByName: false));

        // 關鍵字篩選 + 自然名稱排序
        Assert.Equal(new[] { 2, 1 }, PagePickerHelper.BuildDisplayIndices(names, "page", sortByName: true));

        // 空清單與無命中
        Assert.Empty(PagePickerHelper.BuildDisplayIndices(Array.Empty<string>(), null, true));
        Assert.Empty(PagePickerHelper.BuildDisplayIndices(names, "not_found", false));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(-1L, "—")]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(2L * 1024 * 1024 + 512 * 1024, "2.5 MB")]
    public void FormatFileSize_FormatsExpectedUnits(long? bytes, string expected)
    {
        Assert.Equal(expected, PagePickerHelper.FormatFileSize(bytes));
    }

    [Fact]
    public void FormatModifiedTime_FormatsExpectedPattern()
    {
        Assert.Equal("—", PagePickerHelper.FormatModifiedTime(null));
        var dt = new DateTime(2025, 3, 7, 8, 5, 0);
        Assert.Equal("2025/3/7 08:05", PagePickerHelper.FormatModifiedTime(dt));
    }

    [Fact]
    public void GetStripWindowPositions_ClampsBoundariesCleanly()
    {
        Assert.Empty(PagePickerHelper.GetStripWindowPositions(0, 0));
        Assert.Empty(PagePickerHelper.GetStripWindowPositions(-1, 5));
        Assert.Empty(PagePickerHelper.GetStripWindowPositions(5, 5));

        Assert.Equal(new[] { 0, 1, 2 }, PagePickerHelper.GetStripWindowPositions(0, 10, 2));
        Assert.Equal(new[] { 0, 1, 2, 3 }, PagePickerHelper.GetStripWindowPositions(1, 10, 2));
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, PagePickerHelper.GetStripWindowPositions(5, 10, 2));
        Assert.Equal(new[] { 7, 8, 9 }, PagePickerHelper.GetStripWindowPositions(9, 10, 2));
    }

    // ===== 3. ReaderImageLoader.DecodeThumbnail =====

    [Fact]
    public void DecodeThumbnail_ScalesWebpJpgAndPngProportionallyWithinBounds()
    {
        // 400x200 WebP → 限制 120x120 → 應縮為 120x60
        byte[] webpBytes;
        using (var skBmp = new SKBitmap(new SKImageInfo(400, 200, SKColorType.Bgra8888, SKAlphaType.Unpremul)))
        {
            skBmp.Erase(SKColors.Crimson);
            using var pixmap = skBmp.PeekPixels();
            using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100));
            webpBytes = data.ToArray();
        }

        using (var ms = new MemoryStream(webpBytes))
        using (var thumb = ReaderImageLoader.DecodeThumbnail(ms, ".webp", maxWidth: 120, maxHeight: 120))
        {
            Assert.Equal(120, thumb.Width);
            Assert.Equal(60, thumb.Height);
            Assert.Equal(PixelFormat.Format32bppArgb, thumb.PixelFormat);
        }

        // 150x300 JPG → 限制 120x120 → 應縮為 60x120
        using (var jpgStream = new MemoryStream())
        {
            using (var srcBmp = new Bitmap(150, 300))
            {
                using var g = Graphics.FromImage(srcBmp);
                g.Clear(Color.Navy);
                srcBmp.Save(jpgStream, ImageFormat.Jpeg);
            }
            jpgStream.Position = 0;

            using var thumb = ReaderImageLoader.DecodeThumbnail(jpgStream, ".jpg", maxWidth: 120, maxHeight: 120);
            Assert.Equal(60, thumb.Width);
            Assert.Equal(120, thumb.Height);
        }

        // 80x50 PNG（小於上限）→ 不放大，維持 80x50
        using (var pngStream = new MemoryStream())
        {
            using (var srcBmp = new Bitmap(80, 50))
            {
                using var g = Graphics.FromImage(srcBmp);
                g.Clear(Color.ForestGreen);
                srcBmp.Save(pngStream, ImageFormat.Png);
            }
            pngStream.Position = 0;

            using var thumb = ReaderImageLoader.DecodeThumbnail(pngStream, ".png", maxWidth: 120, maxHeight: 120);
            Assert.Equal(80, thumb.Width);
            Assert.Equal(50, thumb.Height);
        }
    }

    // ===== 4. UserPreferences.PagePickerHideThumbnails =====

    [Fact]
    public void UserPreferences_PagePickerHideThumbnails_DefaultsAndRoundTrips()
    {
        var defaults = new UserPreferences();
        Assert.False(defaults.PagePickerHideThumbnails);

        var prefsPath = Path.Combine(_tempDir, "preferences.json");
        var manager = new UserPreferencesManager(prefsPath);
        manager.Save(new UserPreferences { PagePickerHideThumbnails = true });

        var loaded = manager.Load();
        Assert.True(loaded.PagePickerHideThumbnails);

        var legacy = JsonSerializer.Deserialize<UserPreferences>("{\"Language\":\"zh-TW\"}")!;
        Assert.False(legacy.PagePickerHideThumbnails);
    }
}
