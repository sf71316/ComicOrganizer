using System.IO.Compression;
using System.Text;
using Avalonia.Skia;
using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// W0.3 純 SkiaSharp 封面服務（<see cref="SkiaCoverService"/>）單元測試。
/// 所有 I/O 一律使用 <see cref="Path.GetTempPath"/> 暫存目錄，絕不碰觸真實資料目錄。
/// </summary>
public class SkiaCoverServiceTests : IDisposable
{
    private static readonly object AvaloniaInitLock = new();
    private static bool _avaloniaSkiaInitialized;

    private readonly string _tempDir;
    private readonly string _thumbsDir;
    private readonly FileSystemThumbStore _thumbStore;

    public SkiaCoverServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_skia_cover_{Guid.NewGuid():N}");
        _thumbsDir = Path.Combine(_tempDir, "thumbs");
        Directory.CreateDirectory(_tempDir);
        _thumbStore = new FileSystemThumbStore(_thumbsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 忽略暫存目錄清理失敗
        }
    }

    [Fact]
    public async Task ExtractCoverJpegBytesAsync_LargeImage_ResizesProportionallyTo330AndEncodesValidJpeg()
    {
        var imagePath = Path.Combine(_tempDir, "large.png");
        WriteSolidPng(imagePath, 990, 660, new SKColor(30, 120, 220));

        using var service = new SkiaCoverService(_thumbStore);
        var jpeg = await service.ExtractCoverJpegBytesAsync(imagePath);

        Assert.NotNull(jpeg);
        AssertValidJpegHeader(jpeg!);

        using var codec = SKCodec.Create(new MemoryStream(jpeg!));
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec!.EncodedFormat);
        Assert.Equal(330, codec.Info.Width);
        Assert.Equal(220, codec.Info.Height);
    }

    [Fact]
    public async Task ExtractCoverJpegBytesAsync_SmallImage_DoesNotUpscale()
    {
        var imagePath = Path.Combine(_tempDir, "small.png");
        WriteSolidPng(imagePath, 120, 80, new SKColor(200, 150, 40));

        using var service = new SkiaCoverService(_thumbStore);
        var jpeg = await service.ExtractCoverJpegBytesAsync(imagePath, targetWidth: 330);

        Assert.NotNull(jpeg);
        AssertValidJpegHeader(jpeg!);

        using var codec = SKCodec.Create(new MemoryStream(jpeg!));
        Assert.NotNull(codec);
        Assert.Equal(120, codec!.Info.Width);
        Assert.Equal(80, codec.Info.Height);
    }

    [Fact]
    public async Task ExtractCoverJpegBytesAsync_Folder_PrefersCoverFileOverNumberedImages()
    {
        var folder = Path.Combine(_tempDir, "folder_cover_priority");
        Directory.CreateDirectory(folder);

        // 01.png 為純紅，cover.png 為純綠；預期優先挑選 cover.png
        WriteSolidPng(Path.Combine(folder, "01.png"), 400, 400, new SKColor(230, 20, 20));
        WriteSolidPng(Path.Combine(folder, "cover.png"), 400, 400, new SKColor(20, 210, 40));

        using var service = new SkiaCoverService(_thumbStore);
        var jpeg = await service.ExtractCoverJpegBytesAsync(folder);

        Assert.NotNull(jpeg);
        var center = DecodeCenterPixel(jpeg!);
        Assert.True(center.Green > 150 && center.Green > center.Red + 80, $"預期選中綠色 cover.png，實際像素={center}");
    }

    [Fact]
    public async Task ExtractCoverJpegBytesAsync_FolderWithoutCover_UsesNaturalSortPicking2Before10()
    {
        var folder = Path.Combine(_tempDir, "folder_natural_sort");
        Directory.CreateDirectory(folder);

        // 字典序 "10.png" < "2.png"，自然排序應挑 "2.png"（純藍）勝過 "10.png"（純紅）
        WriteSolidPng(Path.Combine(folder, "10.png"), 400, 400, new SKColor(230, 20, 20));
        WriteSolidPng(Path.Combine(folder, "2.png"), 400, 400, new SKColor(20, 40, 230));

        using var service = new SkiaCoverService(_thumbStore);
        var jpeg = await service.ExtractCoverJpegBytesAsync(folder);

        Assert.NotNull(jpeg);
        var center = DecodeCenterPixel(jpeg!);
        Assert.True(center.Blue > 150 && center.Blue > center.Red + 80, $"預期自然排序選中藍色 2.png，實際像素={center}");
    }

    [Fact]
    public async Task ExtractCoverJpegBytesAsync_Folder_SupportsArchiveFallbackAndDepthLimit()
    {
        // 子壓縮檔 fallback
        var folderWithZip = Path.Combine(_tempDir, "folder_with_zip");
        Directory.CreateDirectory(folderWithZip);
        var innerZip = Path.Combine(folderWithZip, "vol01.zip");
        CreateZipWithEntries(innerZip, ("01.png", CreateSolidPngBytes(500, 500, new SKColor(15, 200, 200))));

        // 2 層巢狀子目錄（在最多遞迴 2 層範圍內）
        var depth2Root = Path.Combine(_tempDir, "depth2_root");
        var depth2Leaf = Path.Combine(depth2Root, "sub1", "sub2");
        Directory.CreateDirectory(depth2Leaf);
        WriteSolidPng(Path.Combine(depth2Leaf, "page1.png"), 400, 300, new SKColor(200, 20, 200));

        // 3 層巢狀子目錄（超過最多遞迴 2 層）
        var depth3Root = Path.Combine(_tempDir, "depth3_root");
        var depth3Leaf = Path.Combine(depth3Root, "sub1", "sub2", "sub3");
        Directory.CreateDirectory(depth3Leaf);
        WriteSolidPng(Path.Combine(depth3Leaf, "page1.png"), 400, 300, new SKColor(200, 20, 200));

        using var service = new SkiaCoverService(_thumbStore);

        var fromZipFolder = await service.ExtractCoverJpegBytesAsync(folderWithZip);
        Assert.NotNull(fromZipFolder);
        AssertValidJpegHeader(fromZipFolder!);

        var fromDepth2 = await service.ExtractCoverJpegBytesAsync(depth2Root);
        Assert.NotNull(fromDepth2);
        AssertValidJpegHeader(fromDepth2!);

        var fromDepth3 = await service.ExtractCoverJpegBytesAsync(depth3Root);
        Assert.Null(fromDepth3);
    }

    [Fact]
    public async Task ExtractCoverJpegBytesAsync_ZipArchive_ExtractsFirstImageByNaturalSortAndIgnoresMacOsNoise()
    {
        var zipPath = Path.Combine(_tempDir, "sample.zip");
        CreateZipWithEntries(
            zipPath,
            ("__MACOSX/._01.png", Encoding.UTF8.GetBytes("not-a-valid-png")),
            ("pages/10.png", CreateSolidPngBytes(660, 440, new SKColor(230, 20, 20))),
            ("pages/2.png", CreateSolidPngBytes(660, 440, new SKColor(20, 210, 40))));

        using var service = new SkiaCoverService(_thumbStore);
        var jpeg = await service.ExtractCoverJpegBytesAsync(zipPath);

        Assert.NotNull(jpeg);
        AssertValidJpegHeader(jpeg!);

        using var codec = SKCodec.Create(new MemoryStream(jpeg!));
        Assert.NotNull(codec);
        Assert.Equal(330, codec!.Info.Width);
        Assert.Equal(220, codec.Info.Height);

        var center = DecodeCenterPixel(jpeg!);
        Assert.True(center.Green > 150 && center.Green > center.Red + 80, $"預期自然排序選中綠色 pages/2.png，實際像素={center}");
    }

    [Fact]
    public async Task ExtractCoverJpegBytesAsync_PdfFile_RendersFirstPageToValidJpeg()
    {
        var pdfPath = Path.Combine(_tempDir, "minimal.pdf");
        await File.WriteAllBytesAsync(pdfPath, CreateMinimalSinglePagePdfBytes());

        using var service = new SkiaCoverService(_thumbStore);
        var jpeg = await service.ExtractCoverJpegBytesAsync(pdfPath);

        Assert.NotNull(jpeg);
        AssertValidJpegHeader(jpeg!);

        using var codec = SKCodec.Create(new MemoryStream(jpeg!));
        Assert.NotNull(codec);
        Assert.True(codec!.Info.Width > 0 && codec.Info.Width <= 330);
        Assert.True(codec.Info.Height > 0);
    }

    [Fact]
    public async Task ReadCachedThumbBytesAsync_ReadsWrittenThumbFromFileSystemThumbStore()
    {
        var srcImg = Path.Combine(_tempDir, "thumb_src.png");
        WriteSolidPng(srcImg, 600, 400, new SKColor(60, 140, 240));

        using var service = new SkiaCoverService(_thumbStore);
        var generated = await service.ExtractCoverJpegBytesAsync(srcImg);
        Assert.NotNull(generated);

        const string thumbKey = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
        _thumbStore.Write(thumbKey, generated!);

        var cachedBytes = await service.ReadCachedThumbBytesAsync(thumbKey);
        Assert.NotNull(cachedBytes);
        Assert.Equal(generated, cachedBytes);

        Assert.Null(await service.ReadCachedThumbBytesAsync("ffffffffffffffffffffffffffffffffffffffff"));
        Assert.Null(await service.ReadCachedThumbBytesAsync(null));
        Assert.Null(await service.ReadCachedThumbBytesAsync(""));
        Assert.Null(await service.ReadCachedThumbBytesAsync("a"));
    }

    [Fact]
    public async Task GetCachedThumbAsync_And_LoadOrExtractCoverAsync_UsesLruCacheAndDoesNotWriteBackToThumbStore()
    {
        EnsureAvaloniaSkiaBackend();

        var img1 = Path.Combine(_tempDir, "c1.png");
        var img2 = Path.Combine(_tempDir, "c2.png");
        var img3 = Path.Combine(_tempDir, "c3.png");
        WriteSolidPng(img1, 400, 300, new SKColor(220, 40, 40));
        WriteSolidPng(img2, 400, 300, new SKColor(40, 220, 40));
        WriteSolidPng(img3, 400, 300, new SKColor(40, 40, 220));

        using var service = new SkiaCoverService(_thumbStore, maxMemoryCacheItems: 2);

        var jpeg1 = await service.ExtractCoverJpegBytesAsync(img1);
        var jpeg2 = await service.ExtractCoverJpegBytesAsync(img2);
        Assert.NotNull(jpeg1);
        Assert.NotNull(jpeg2);

        const string key1 = "11aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string key2 = "22bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string missKey = "33cccccccccccccccccccccccccccccccccccccc";
        _thumbStore.Write(key1, jpeg1!);
        _thumbStore.Write(key2, jpeg2!);

        // 命中磁碟並寫入記憶體 LRU 快取；第二次讀取應回傳同一個快取 Bitmap 實例
        var bmp1First = await service.GetCachedThumbAsync(key1);
        var bmp1Second = await service.GetCachedThumbAsync(key1);
        Assert.NotNull(bmp1First);
        Assert.Same(bmp1First, bmp1Second);
        Assert.Equal(330, bmp1First!.PixelSize.Width);
        Assert.Equal(1, service.MemoryCacheCount);

        // 讀取 key2 後快取滿（2 項）
        var bmp2 = await service.GetCachedThumbAsync(key2);
        Assert.NotNull(bmp2);
        Assert.Equal(2, service.MemoryCacheCount);

        // LoadOrExtractCoverAsync：missKey 不在 thumbStore，應現場提取 img3 且「不主動寫回」thumbStore
        var bmp3 = await service.LoadOrExtractCoverAsync(img3, missKey);
        Assert.NotNull(bmp3);
        Assert.Equal(330, bmp3!.PixelSize.Width);
        Assert.Null(_thumbStore.OpenRead(missKey));
        // LRU 上限為 2，加入第 3 項後快取數維持在 2
        Assert.Equal(2, service.MemoryCacheCount);

        service.ClearMemoryCache();
        Assert.Equal(0, service.MemoryCacheCount);
    }

    [Fact]
    public async Task GetCachedThumbAsync_ConcurrentReadsSameKey_ReturnsSharedCachedBitmapInstance()
    {
        EnsureAvaloniaSkiaBackend();

        var srcImg = Path.Combine(_tempDir, "concurrent_src.png");
        WriteSolidPng(srcImg, 500, 400, new SKColor(100, 180, 90));

        using var service = new SkiaCoverService(_thumbStore, maxMemoryCacheItems: 16);
        var jpeg = await service.ExtractCoverJpegBytesAsync(srcImg);
        Assert.NotNull(jpeg);

        const string key = "44dddddddddddddddddddddddddddddddddddddd";
        _thumbStore.Write(key, jpeg!);

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => service.GetCachedThumbAsync(key))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, bmp => Assert.NotNull(bmp));
        var first = results[0]!;
        Assert.All(results, bmp => Assert.Same(first, bmp));
        Assert.Equal(1, service.MemoryCacheCount);
    }

    [Fact]
    public async Task MissingAndCorruptedInputs_ReturnNullSafelyWithoutThrowing()
    {
        var missingPath = Path.Combine(_tempDir, "does_not_exist.png");
        var corruptPng = Path.Combine(_tempDir, "corrupt.png");
        var corruptZip = Path.Combine(_tempDir, "corrupt.zip");
        var corruptPdf = Path.Combine(_tempDir, "corrupt.pdf");
        var emptyDir = Path.Combine(_tempDir, "empty_dir");
        Directory.CreateDirectory(emptyDir);

        await File.WriteAllBytesAsync(corruptPng, [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0xAA]);
        await File.WriteAllBytesAsync(corruptZip, Encoding.UTF8.GetBytes("not a valid zip file"));
        await File.WriteAllBytesAsync(corruptPdf, Encoding.UTF8.GetBytes("%PDF-1.4 broken"));

        const string corruptThumbKey = "deadbeef00112233445566778899aabbccddeeff";
        _thumbStore.Write(corruptThumbKey, Encoding.UTF8.GetBytes("not-a-valid-jpeg"));

        EnsureAvaloniaSkiaBackend();

        using var service = new SkiaCoverService(_thumbStore);

        Assert.Null(await service.ExtractCoverJpegBytesAsync(missingPath));
        Assert.Null(await service.ExtractCoverJpegBytesAsync(corruptPng));
        Assert.Null(await service.ExtractCoverJpegBytesAsync(corruptZip));
        Assert.Null(await service.ExtractCoverJpegBytesAsync(corruptPdf));
        Assert.Null(await service.ExtractCoverJpegBytesAsync(emptyDir));
        Assert.Null(await service.ExtractCoverJpegBytesAsync(""));
        Assert.Null(await service.GetCachedThumbAsync(corruptThumbKey));
        Assert.Null(await service.GetCachedThumbAsync("9900000000000000000000000000000000000000"));
        Assert.Null(await service.LoadOrExtractCoverAsync(missingPath, corruptThumbKey));

        // Dispose 後所有公開操作仍應安全回傳 null 不拋例外
        service.Dispose();
        Assert.Null(await service.ReadCachedThumbBytesAsync(corruptThumbKey));
        Assert.Null(await service.GetCachedThumbAsync(corruptThumbKey));
        Assert.Null(await service.ExtractCoverJpegBytesAsync(corruptPng));
        Assert.Null(await service.LoadOrExtractCoverAsync(corruptPng, corruptThumbKey));
    }

    private static void EnsureAvaloniaSkiaBackend()
    {
        if (_avaloniaSkiaInitialized)
        {
            return;
        }

        lock (AvaloniaInitLock)
        {
            if (_avaloniaSkiaInitialized)
            {
                return;
            }

            SkiaPlatform.Initialize();
            _avaloniaSkiaInitialized = true;
        }
    }

    private static void WriteSolidPng(string path, int width, int height, SKColor color)
    {
        File.WriteAllBytes(path, CreateSolidPngBytes(width, height, color));
    }

    private static byte[] CreateSolidPngBytes(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void CreateZipWithEntries(string zipPath, params (string EntryName, byte[] Content)[] entries)
    {
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var s = entry.Open();
            s.Write(content, 0, content.Length);
        }
    }

    private static void AssertValidJpegHeader(byte[] bytes)
    {
        Assert.True(bytes.Length >= 4, "JPEG 位元組長度不足");
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        Assert.Equal(0xFF, bytes[2]);
    }

    private static SKColor DecodeCenterPixel(byte[] imageBytes)
    {
        using var bmp = SKBitmap.Decode(imageBytes);
        Assert.NotNull(bmp);
        return bmp!.GetPixel(bmp.Width / 2, bmp.Height / 2);
    }

    private static byte[] CreateMinimalSinglePagePdfBytes()
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        var offsets = new List<int> { 0 };
        void AppendObj(string body)
        {
            offsets.Add(sb.Length);
            sb.Append(body);
        }

        AppendObj("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        AppendObj("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        AppendObj("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 120 160] >>\nendobj\n");

        int xrefOffset = sb.Length;
        sb.Append("xref\n0 4\n");
        sb.Append("0000000000 65535 f \n");
        for (int i = 1; i < offsets.Count; i++)
        {
            sb.Append($"{offsets[i]:D10} 00000 n \n");
        }
        sb.Append("trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n");
        sb.Append(xrefOffset);
        sb.Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
