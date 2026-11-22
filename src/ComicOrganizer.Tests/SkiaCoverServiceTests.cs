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
    public async Task ExtractCoverJpegBytesAsync_ImageLargerThan64MB_StillExtractsCover()
    {
        // 正常 PNG 後面接 65 MB 填充：解碼器讀到 IEND 就停，但讀檔必須讀完整個檔案（超過舊的 64 MB 上限）
        const int PaddingBytes = 65 * 1024 * 1024;
        var png = CreateSolidPngBytes(660, 440, new SKColor(230, 20, 20));
        var imagePath = Path.Combine(_tempDir, "huge.png");
        using (var fs = File.Create(imagePath))
        {
            fs.Write(png, 0, png.Length);
            fs.SetLength(png.Length + PaddingBytes);
        }

        // 壓縮檔內同樣大小的條目（deflate 後很小，但解壓後超過 64 MB）
        var zipPath = Path.Combine(_tempDir, "huge.zip");
        using (var fs = File.Create(zipPath))
        {
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var entry = zip.CreateEntry("01.png", CompressionLevel.Fastest);
            using var s = entry.Open();
            s.Write(png, 0, png.Length);
            s.Write(new byte[PaddingBytes], 0, PaddingBytes);
        }

        using var service = new SkiaCoverService(_thumbStore);
        foreach (var path in new[] { imagePath, zipPath })
        {
            var jpeg = await service.ExtractCoverJpegBytesAsync(path);
            Assert.NotNull(jpeg);
            var center = DecodeCenterPixel(jpeg!);
            Assert.True(center.Red > 150 && center.Red > center.Green + 80, $"{Path.GetFileName(path)} 封面顏色不符：{center}");
        }
    }

    [Fact]
    public async Task ExtractCoverJpegBytesAsync_ZipWithLegacyNamesAndUnicodePathField_PicksSameCoverAsSharpCompress()
    {
        // 模擬日文 Windows 舊版壓縮工具：檔名以 Shift-JIS 存放（未設 UTF-8 旗標），另附 Unicode 路徑附加欄位（0x7075）。
        // 「い.png」（藍）排在壓縮檔前面、「あ.png」（紅）在後；依正確名稱自然排序應選「あ.png」。
        // .NET ZipFile 會把兩個名稱都解成相同的 U+FFFD 亂碼而按存放順序選到藍色，必須交給 SharpCompress。
        var zipPath = Path.Combine(_tempDir, "legacy_names.zip");
        WriteStoredZipWithUnicodePathField(
            zipPath,
            (new byte[] { 0x82, 0xA2, (byte)'.', (byte)'p', (byte)'n', (byte)'g' }, "い.png", CreateSolidPngBytes(660, 440, new SKColor(20, 40, 230))),
            (new byte[] { 0x82, 0xA0, (byte)'.', (byte)'p', (byte)'n', (byte)'g' }, "あ.png", CreateSolidPngBytes(660, 440, new SKColor(230, 20, 20))));

        // 前提：.NET 確實解成亂碼
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            Assert.All(zip.Entries, e => Assert.Contains('�', e.FullName));
        }

        using var service = new SkiaCoverService(_thumbStore);
        var jpeg = await service.ExtractCoverJpegBytesAsync(zipPath);

        Assert.NotNull(jpeg);
        var center = DecodeCenterPixel(jpeg!);
        Assert.True(center.Red > 150 && center.Red > center.Blue + 80, $"預期依正確檔名選中紅色「あ.png」，實際像素={center}");
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
        // LRU 上限為 2，加入第 3 項後快取數維持在 2，且已交付給 UI 的舊 Bitmap 不可被 Dispose（避免 Image.MeasureOverride NullReferenceException）
        Assert.Equal(2, service.MemoryCacheCount);
        Assert.Equal(330, bmp1First!.PixelSize.Width);
        Assert.Equal(330, bmp2!.PixelSize.Width);

        service.ClearMemoryCache();
        Assert.Equal(0, service.MemoryCacheCount);
        Assert.Equal(330, bmp1First.PixelSize.Width);
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

    [Fact]
    public async Task GetCachedThumbAsync_WithDecodeWidth_DownscalesProportionallyAndCachesPerWidth()
    {
        EnsureAvaloniaSkiaBackend();

        var srcImg = Path.Combine(_tempDir, "decode_width_src.png");
        WriteSolidPng(srcImg, 400, 300, new SKColor(200, 120, 40));

        using var service = new SkiaCoverService(_thumbStore, maxMemoryCacheItems: 16);
        var jpeg = await service.ExtractCoverJpegBytesAsync(srcImg);
        Assert.NotNull(jpeg);

        const string key = "55eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        _thumbStore.Write(key, jpeg!);

        // 縮小解碼：330x248 → 188 寬，高度等比（約 141）
        var small = await service.GetCachedThumbAsync(key, decodeSize: new CoverDecodeSize(188, 0));
        Assert.NotNull(small);
        Assert.Equal(188, small!.PixelSize.Width);
        Assert.InRange(small.PixelSize.Height, 140, 142);

        // 同寬度命中同一個快取實例
        var smallAgain = await service.GetCachedThumbAsync(key, decodeSize: new CoverDecodeSize(188, 0));
        Assert.Same(small, smallAgain);

        // 原尺寸與縮小尺寸在快取中各自獨立
        var original = await service.GetCachedThumbAsync(key);
        Assert.NotNull(original);
        Assert.Equal(330, original!.PixelSize.Width);
        Assert.NotSame(small, original);
        Assert.Equal(2, service.MemoryCacheCount);

        // 縮小後顏色仍正確（高品質縮放不應產生明顯色偏）
        var center = ReadCenterPixel(small);
        Assert.InRange(center.Red, 185, 215);
        Assert.InRange(center.Green, 105, 135);
        Assert.InRange(center.Blue, 25, 55);
    }

    [Fact]
    public async Task GetCachedThumbAsync_DecodeWidthNotSmallerThanOriginal_DoesNotUpscaleAndSharesOriginalCacheEntry()
    {
        EnsureAvaloniaSkiaBackend();

        var srcImg = Path.Combine(_tempDir, "decode_width_noupscale.png");
        WriteSolidPng(srcImg, 400, 300, new SKColor(60, 90, 200));

        using var service = new SkiaCoverService(_thumbStore, maxMemoryCacheItems: 16);
        var jpeg = await service.ExtractCoverJpegBytesAsync(srcImg);
        Assert.NotNull(jpeg);

        const string key = "66ffffffffffffffffffffffffffffffffffffff";
        _thumbStore.Write(key, jpeg!);

        var original = await service.GetCachedThumbAsync(key);
        Assert.NotNull(original);

        // 要求寬度 >= 原圖（330）時不放大，且直接共用原尺寸快取，不另存一份
        var wide = await service.GetCachedThumbAsync(key, decodeSize: new CoverDecodeSize(500, 0));
        var exact = await service.GetCachedThumbAsync(key, decodeSize: new CoverDecodeSize(330, 0));
        Assert.Same(original, wide);
        Assert.Same(original, exact);
        Assert.Equal(330, wide!.PixelSize.Width);
        Assert.Equal(1, service.MemoryCacheCount);
    }

    [Fact]
    public async Task LoadOrExtractCoverAsync_WithDecodeWidth_ExtractsAndDownscalesWithoutWritingBack()
    {
        EnsureAvaloniaSkiaBackend();

        var srcImg = Path.Combine(_tempDir, "decode_width_extract.png");
        WriteSolidPng(srcImg, 660, 936, new SKColor(30, 160, 90));

        using var service = new SkiaCoverService(_thumbStore, maxMemoryCacheItems: 16);
        const string missKey = "77abababababababababababababababababababab";

        var bmp = await service.LoadOrExtractCoverAsync(srcImg, missKey, decodeSize: new CoverDecodeSize(120, 0));
        Assert.NotNull(bmp);
        Assert.Equal(120, bmp!.PixelSize.Width);
        Assert.InRange(bmp.PixelSize.Height, 169, 171);
        Assert.Null(_thumbStore.OpenRead(missKey));

        // 第二次同寬度命中快取
        var again = await service.LoadOrExtractCoverAsync(srcImg, missKey, decodeSize: new CoverDecodeSize(120, 0));
        Assert.Same(bmp, again);
    }

    [Fact]
    public async Task MemoryCacheEviction_ReportsReleasedPixelBytes_WithoutDisposingBitmap()
    {
        EnsureAvaloniaSkiaBackend();

        var src = Path.Combine(_tempDir, "evict_src.png");
        WriteSolidPng(src, 330, 470, new SKColor(120, 60, 200));
        using var generator = new SkiaCoverService(_thumbStore);
        var jpeg = await generator.ExtractCoverJpegBytesAsync(src);
        Assert.NotNull(jpeg);
        const string k1 = "e1abababababababababababababababababababab";
        const string k2 = "e2abababababababababababababababababababab";
        const string k3 = "e3abababababababababababababababababababab";
        _thumbStore.Write(k1, jpeg!);
        _thumbStore.Write(k2, jpeg!);
        _thumbStore.Write(k3, jpeg!);

        int collects = 0;
        var trigger = new BitmapReleaseGcTrigger(long.MaxValue, TimeSpan.Zero, () => collects++);
        using var service = new SkiaCoverService(_thumbStore, maxMemoryCacheItems: 2, releaseGcTrigger: trigger);

        var b1 = await service.GetCachedThumbAsync(k1);
        await service.GetCachedThumbAsync(k2);
        Assert.Equal(0, trigger.PendingBytes);

        // 第 3 張擠掉第 1 張：回報第 1 張的像素量，但不 Dispose（畫面上可能還在用）
        await service.GetCachedThumbAsync(k3);
        long onePicture = (long)b1!.PixelSize.Width * b1.PixelSize.Height * 4;
        Assert.Equal(onePicture, trigger.PendingBytes);
        Assert.Equal(330, b1.PixelSize.Width);

        // 清空快取：剩下 2 張也回報
        service.ClearMemoryCache();
        Assert.Equal(onePicture * 3, trigger.PendingBytes);
        Assert.Equal(0, collects);
    }

    [Fact]
    public async Task LoadOrExtractCoverAsync_WithWriteBack_WritesExtractedCoverToThumbsAndIndex()
    {
        EnsureAvaloniaSkiaBackend();

        var zipPath = Path.Combine(_tempDir, "writeback.zip");
        CreateZipWithEntries(zipPath, ("01.png", CreateSolidPngBytes(660, 936, new SKColor(200, 60, 60))));

        using (var store = new SqliteAssetMetaStore(Path.Combine(_tempDir, "cache.db")))
        {
            store.UpsertCatalogOnly(new AssetMeta
            {
                Path = zipPath,
                Name = "writeback.zip",
                IsFolder = false,
                FileSize = new FileInfo(zipPath).Length,
                Mtime = 1_700_000_000,
            });

            using (var writeBack = new ThumbWriteBackQueue(store, _thumbStore, () => 0))
            {
                using (var service = new SkiaCoverService(_thumbStore, writeBack: writeBack))
                {
                    // 作品沒有縮圖 key（與使用者資料相同），封面從壓縮檔現場解出
                    var bmp = await service.LoadOrExtractCoverAsync(zipPath, thumbKey: null);
                    Assert.NotNull(bmp);
                }
            }

            string expectedKey = ThumbWriteBackQueue.BuildThumbKey(zipPath);
            Assert.Equal(expectedKey, store.Get(zipPath)!.CoverThumbKey);
            using (var stream = _thumbStore.OpenRead(expectedKey))
            {
                Assert.NotNull(stream);
            }

            // 下次用寫回的 key 載入：直接讀縮圖檔，不必再解壓縮
            using (var service = new SkiaCoverService(_thumbStore))
            {
                var fromThumb = await service.GetCachedThumbAsync(expectedKey);
                Assert.NotNull(fromThumb);
                Assert.Equal(330, fromThumb!.PixelSize.Width);
            }
        }
    }

    [Fact]
    public void ReportDisplayScale_OnlyIncreases_AndToDecodeSizeRoundsUp()
    {
        using var service = new SkiaCoverService(_thumbStore);

        // 預設 1.0
        Assert.Equal(1.0, service.DisplayScale);
        Assert.Equal(new CoverDecodeSize(125, 175), service.ToDecodeSize(124.8, 174.72));

        // 150%：疊牌 120x168 DIP × 1.04（懸停放大）= 124.8x174.72 → 187.2x262.08 → 進位 188x263
        service.ReportDisplayScale(1.5);
        Assert.Equal(1.5, service.DisplayScale);
        Assert.Equal(new CoverDecodeSize(188, 263), service.ToDecodeSize(120 * 1.04, 168 * 1.04));

        // 移到較低縮放的螢幕：維持 1.5（只升不降）
        service.ReportDisplayScale(1.0);
        Assert.Equal(1.5, service.DisplayScale);

        // 無效值忽略
        service.ReportDisplayScale(0);
        service.ReportDisplayScale(-2);
        service.ReportDisplayScale(double.NaN);
        service.ReportDisplayScale(double.PositiveInfinity);
        Assert.Equal(1.5, service.DisplayScale);

        // 移到更高縮放的螢幕：升到 2.0
        service.ReportDisplayScale(2.0);
        Assert.Equal(2.0, service.DisplayScale);
        Assert.Equal(new CoverDecodeSize(124, 176), service.ToDecodeSize(62, 88));

        // 非正數 / 無效值 → 該方向不限制；兩方向皆無效 → 原尺寸
        Assert.Equal(new CoverDecodeSize(124, 0), service.ToDecodeSize(62, 0));
        Assert.Equal(CoverDecodeSize.Original, service.ToDecodeSize(-10, double.NaN));
        Assert.False(service.ToDecodeSize(0, 0).IsSpecified);
    }

    [Fact]
    public async Task GetCachedThumbAsync_FillBox_KeepsEnoughWidthForWideCoversAndDownscalesPortraitCovers()
    {
        EnsureAvaloniaSkiaBackend();

        using var service = new SkiaCoverService(_thumbStore, maxMemoryCacheItems: 16);

        // 直式封面（縮圖 330x468）放進 188x263 的框：填滿所需寬度 = max(188, 263×330/468≈186) = 188 → 縮小
        var portraitSrc = Path.Combine(_tempDir, "fill_portrait.png");
        WriteSolidPng(portraitSrc, 660, 936, new SKColor(120, 60, 160));
        var portraitJpeg = await service.ExtractCoverJpegBytesAsync(portraitSrc);
        const string portraitKey = "88cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd";
        _thumbStore.Write(portraitKey, portraitJpeg!);

        var portrait = await service.GetCachedThumbAsync(portraitKey, decodeSize: new CoverDecodeSize(188, 263));
        Assert.NotNull(portrait);
        Assert.Equal(188, portrait!.PixelSize.Width);
        Assert.True(portrait.PixelSize.Height >= 263 - 1, $"高度 {portrait.PixelSize.Height} 不足以填滿框高");

        // 寬扁封面（如跨頁，縮圖 330x165）放進同一個框：填滿框高需要寬度 263×2 = 526 > 330 → 不縮小，避免被放大而模糊
        var wideSrc = Path.Combine(_tempDir, "fill_wide.png");
        WriteSolidPng(wideSrc, 800, 400, new SKColor(40, 140, 140));
        var wideJpeg = await service.ExtractCoverJpegBytesAsync(wideSrc);
        const string wideKey = "99efefefefefefefefefefefefefefefefefefef";
        _thumbStore.Write(wideKey, wideJpeg!);

        var wideOriginal = await service.GetCachedThumbAsync(wideKey);
        var wide = await service.GetCachedThumbAsync(wideKey, decodeSize: new CoverDecodeSize(188, 263));
        Assert.Same(wideOriginal, wide);
        Assert.Equal(330, wide!.PixelSize.Width);

        // 稍微寬一點的封面（縮圖 330x330）：填滿框高需要寬度 263 → 縮到 263 而不是 188
        var squareSrc = Path.Combine(_tempDir, "fill_square.png");
        WriteSolidPng(squareSrc, 500, 500, new SKColor(200, 200, 60));
        var squareJpeg = await service.ExtractCoverJpegBytesAsync(squareSrc);
        const string squareKey = "aa12121212121212121212121212121212121212";
        _thumbStore.Write(squareKey, squareJpeg!);

        var square = await service.GetCachedThumbAsync(squareKey, decodeSize: new CoverDecodeSize(188, 263));
        Assert.NotNull(square);
        Assert.Equal(263, square!.PixelSize.Width);
        Assert.Equal(263, square.PixelSize.Height);
    }

    private static (byte Red, byte Green, byte Blue) ReadCenterPixel(global::Avalonia.Media.Imaging.Bitmap bitmap)
    {
        int width = bitmap.PixelSize.Width;
        int height = bitmap.PixelSize.Height;
        var pixels = new byte[width * height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new global::Avalonia.PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        int offset = ((height / 2) * width + (width / 2)) * 4;
        bool isBgra = bitmap.Format is { } format && format == global::Avalonia.Platform.PixelFormat.Bgra8888;
        return isBgra
            ? (pixels[offset + 2], pixels[offset + 1], pixels[offset])
            : (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
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

    /// <summary>
    /// 手工寫出不壓縮（stored）的 ZIP：條目名稱用給定的原始位元組（不設 UTF-8 旗標），
    /// 並在本地與中央目錄標頭都附上 Unicode 路徑附加欄位（0x7075）存放正確名稱。
    /// </summary>
    private static void WriteStoredZipWithUnicodePathField(
        string zipPath,
        params (byte[] LegacyName, string UnicodeName, byte[] Content)[] entries)
    {
        using var fs = File.Create(zipPath);
        using var w = new BinaryWriter(fs);
        var central = new List<(byte[] Name, byte[] Extra, uint Crc, int Size, uint Offset)>();

        foreach (var (legacyName, unicodeName, content) in entries)
        {
            byte[] utf8Name = Encoding.UTF8.GetBytes(unicodeName);
            byte[] extra = new byte[4 + 5 + utf8Name.Length];
            BitConverter.GetBytes((ushort)0x7075).CopyTo(extra, 0);
            BitConverter.GetBytes((ushort)(5 + utf8Name.Length)).CopyTo(extra, 2);
            extra[4] = 1;
            BitConverter.GetBytes(Crc32(legacyName)).CopyTo(extra, 5);
            utf8Name.CopyTo(extra, 9);

            uint crc = Crc32(content);
            uint offset = (uint)fs.Position;
            w.Write(0x04034b50u);
            w.Write((ushort)20);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)0x21);
            w.Write(crc);
            w.Write(content.Length);
            w.Write(content.Length);
            w.Write((ushort)legacyName.Length);
            w.Write((ushort)extra.Length);
            w.Write(legacyName);
            w.Write(extra);
            w.Write(content);
            central.Add((legacyName, extra, crc, content.Length, offset));
        }

        uint cdOffset = (uint)fs.Position;
        foreach (var (name, extra, crc, size, offset) in central)
        {
            w.Write(0x02014b50u);
            w.Write((ushort)20);
            w.Write((ushort)20);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)0x21);
            w.Write(crc);
            w.Write(size);
            w.Write(size);
            w.Write((ushort)name.Length);
            w.Write((ushort)extra.Length);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write(0u);
            w.Write(offset);
            w.Write(name);
            w.Write(extra);
        }

        uint cdSize = (uint)fs.Position - cdOffset;
        w.Write(0x06054b50u);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)central.Count);
        w.Write((ushort)central.Count);
        w.Write(cdSize);
        w.Write(cdOffset);
        w.Write((ushort)0);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
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
