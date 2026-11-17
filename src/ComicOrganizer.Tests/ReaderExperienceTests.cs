using System.Collections.Concurrent;
using System.Drawing;
using System.Text;
using System.Text.Json;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 D（Reader 偏好持久化、快速跳頁解析、同級作品解析與相鄰切換、視窗尺寸夾擠）單元測試。
/// </summary>
public class ReaderExperienceTests : IDisposable
{
    private readonly string _tempDir;

    public ReaderExperienceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_wi_reader_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ===== 1. UserPreferences 閱讀器欄位 =====

    [Fact]
    public void UserPreferences_ReaderFields_DefaultsAndJsonRoundTrip()
    {
        var defaults = new UserPreferences();
        Assert.Null(defaults.ReaderWindowWidth);
        Assert.Null(defaults.ReaderWindowHeight);
        Assert.False(defaults.ReaderWindowMaximized);
        Assert.Equal("SinglePage", defaults.DefaultReaderViewMode);
        Assert.Equal("FitPage", defaults.DefaultReaderZoomMode);

        var prefsPath = Path.Combine(_tempDir, "preferences.json");
        var manager = new UserPreferencesManager(prefsPath);

        manager.Save(new UserPreferences
        {
            ReaderWindowWidth = 1280,
            ReaderWindowHeight = 900,
            ReaderWindowMaximized = true,
            DefaultReaderViewMode = "ContinuousScroll",
            DefaultReaderZoomMode = "FitWidth",
        });

        var loaded = manager.Load();
        Assert.Equal(1280, loaded.ReaderWindowWidth);
        Assert.Equal(900, loaded.ReaderWindowHeight);
        Assert.True(loaded.ReaderWindowMaximized);
        Assert.Equal("ContinuousScroll", loaded.DefaultReaderViewMode);
        Assert.Equal("FitWidth", loaded.DefaultReaderZoomMode);

        // 舊版 JSON 不含這些欄位時應安全維持預設值
        var legacyJson = JsonSerializer.Serialize(new { Language = "zh-TW" });
        var fromLegacy = JsonSerializer.Deserialize<UserPreferences>(legacyJson)!;
        Assert.Null(fromLegacy.ReaderWindowWidth);
        Assert.Null(fromLegacy.ReaderWindowHeight);
        Assert.False(fromLegacy.ReaderWindowMaximized);
        Assert.Equal("SinglePage", fromLegacy.DefaultReaderViewMode);
        Assert.Equal("FitPage", fromLegacy.DefaultReaderZoomMode);
    }

    // ===== 2. TryParseGoToPageInput =====

    [Theory]
    [InlineData("1", 10, true, 0)]
    [InlineData(" 5 ", 10, true, 4)]
    [InlineData("10", 10, true, 9)]
    [InlineData("0", 10, false, 0)]
    [InlineData("-2", 10, false, 0)]
    [InlineData("11", 10, false, 0)]
    [InlineData("", 10, false, 0)]
    [InlineData("   ", 10, false, 0)]
    [InlineData(null, 10, false, 0)]
    [InlineData("abc", 10, false, 0)]
    [InlineData("1", 0, false, 0)]
    public void TryParseGoToPageInput_ValidatesRangeAndFormat(
        string? input, int pageCount, bool expectedOk, int expectedIndex)
    {
        bool ok = ReaderNavigationHelper.TryParseGoToPageInput(input, pageCount, out int idx);
        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedIndex, idx);
    }

    // ===== 3. ResolveSiblingWorks =====

    [Fact]
    public void ResolveSiblingWorks_WithCandidatesContainingCurrent_PreservesOrderAndDeduplicates()
    {
        var candidates = new[]
        {
            @"C:\Lib\BookB.zip",
            @"C:\Lib\BookA.zip",
            @"c:\lib\bookb.zip", // 重複（不同大小寫）
            "   ",
            @"C:\Lib\BookC.pdf",
        };

        var result = ReaderNavigationHelper.ResolveSiblingWorks(@"C:\Lib\BookA.zip\", candidates);
        Assert.Equal(new[] { @"C:\Lib\BookB.zip", @"C:\Lib\BookA.zip", @"C:\Lib\BookC.pdf" }, result);
    }

    [Fact]
    public void ResolveSiblingWorks_WithoutCandidates_EnumeratesDirectoryNaturallyAndExcludesHiddenAndNoise()
    {
        var dir = Path.Combine(_tempDir, "AuthorDir");
        Directory.CreateDirectory(dir);

        var ch2 = Path.Combine(dir, "Ch2.zip");
        var ch10 = Path.Combine(dir, "Ch10.pdf");
        var subFolder = Path.Combine(dir, "Ch1_Folder");
        var hiddenFolder = Path.Combine(dir, ".hidden_dir");
        var txtNoise = Path.Combine(dir, "readme.txt");
        var jpgNoise = Path.Combine(dir, "cover.jpg");

        File.WriteAllBytes(ch2, Array.Empty<byte>());
        File.WriteAllBytes(ch10, Array.Empty<byte>());
        Directory.CreateDirectory(subFolder);
        Directory.CreateDirectory(hiddenFolder);
        File.SetAttributes(hiddenFolder, File.GetAttributes(hiddenFolder) | FileAttributes.Hidden);
        File.WriteAllText(txtNoise, "ignore me");
        File.WriteAllBytes(jpgNoise, Array.Empty<byte>());

        var resolved = ReaderNavigationHelper.ResolveSiblingWorks(ch2);
        // 自然排序：Ch1_Folder < Ch2.zip < Ch10.pdf；排除隱藏資料夾、txt、jpg
        Assert.Equal(new[] { subFolder, ch2, ch10 }, resolved);

        // 若傳入的 candidatePaths 不包含 currentPath，應退回目錄枚舉
        var fallback = ReaderNavigationHelper.ResolveSiblingWorks(ch10, new[] { @"C:\Other\Unrelated.zip" });
        Assert.Equal(new[] { subFolder, ch2, ch10 }, fallback);
    }

    [Fact]
    public void ResolveSiblingWorks_MissingDirectory_SafelyReturnsEmptyOrCurrentIfExists()
    {
        var missingPath = Path.Combine(_tempDir, "NoSuchDir", "Missing.zip");
        Assert.Empty(ReaderNavigationHelper.ResolveSiblingWorks(missingPath));
        Assert.Empty(ReaderNavigationHelper.ResolveSiblingWorks("   "));
    }

    // ===== 4. GetAdjacentWorkPath =====

    [Fact]
    public void GetAdjacentWorkPath_HandlesMiddleBoundariesAndMissing()
    {
        var list = new[] { @"C:\Lib\W1.zip", @"C:\Lib\W2.zip", @"C:\Lib\W3.zip" };

        Assert.Null(ReaderNavigationHelper.GetAdjacentWorkPath(list, @"C:\Lib\W1.zip", -1));
        Assert.Equal(@"C:\Lib\W2.zip", ReaderNavigationHelper.GetAdjacentWorkPath(list, @"c:\lib\w1.zip\", +1));
        Assert.Equal(@"C:\Lib\W1.zip", ReaderNavigationHelper.GetAdjacentWorkPath(list, @"C:\Lib\W2.zip", -1));
        Assert.Equal(@"C:\Lib\W3.zip", ReaderNavigationHelper.GetAdjacentWorkPath(list, @"C:\Lib\W2.zip", +1));
        Assert.Null(ReaderNavigationHelper.GetAdjacentWorkPath(list, @"C:\Lib\W3.zip", +1));
        Assert.Null(ReaderNavigationHelper.GetAdjacentWorkPath(list, @"C:\Lib\Unknown.zip", +1));
        Assert.Null(ReaderNavigationHelper.GetAdjacentWorkPath(list, @"C:\Lib\W2.zip", 0));
    }

    // ===== 5. Normalize Modes & ClampReaderWindowSize =====

    [Theory]
    [InlineData(null, "SinglePage")]
    [InlineData("", "SinglePage")]
    [InlineData("unknown", "SinglePage")]
    [InlineData(" single ", "SinglePage")]
    [InlineData("dualpage", "DualPage")]
    [InlineData("Dual", "DualPage")]
    [InlineData("CONTINUOUSSCROLL", "ContinuousScroll")]
    [InlineData("continuous", "ContinuousScroll")]
    public void NormalizeReaderViewMode_NormalizesAndFallsBack(string? raw, string expected)
    {
        Assert.Equal(expected, ReaderNavigationHelper.NormalizeReaderViewMode(raw));
    }

    [Theory]
    [InlineData(null, "FitPage")]
    [InlineData("invalid", "FitPage")]
    [InlineData(" fitpage ", "FitPage")]
    [InlineData("FITWIDTH", "FitWidth")]
    [InlineData("actual", "Actual")]
    public void NormalizeReaderZoomMode_NormalizesAndFallsBack(string? raw, string expected)
    {
        Assert.Equal(expected, ReaderNavigationHelper.NormalizeReaderZoomMode(raw));
    }

    [Fact]
    public void ClampReaderWindowSize_ClampsDefaultsUndersizedOversizedAndNarrowArea()
    {
        var area = new Rectangle(0, 0, 1920, 1040);

        Assert.Equal(new Size(1024, 768), ReaderNavigationHelper.ClampReaderWindowSize(null, null, area));
        Assert.Equal(new Size(1024, 768), ReaderNavigationHelper.ClampReaderWindowSize(0, -10, area));
        Assert.Equal(new Size(720, 520), ReaderNavigationHelper.ClampReaderWindowSize(300, 200, area));
        Assert.Equal(new Size(1920, 1040), ReaderNavigationHelper.ClampReaderWindowSize(3000, 2000, area));
        Assert.Equal(new Size(1366, 900), ReaderNavigationHelper.ClampReaderWindowSize(1366, 900, area));

        // 窄螢幕不得拋例外，應夾在工作區大小內
        var narrowArea = new Rectangle(0, 0, 640, 480);
        Assert.Equal(new Size(640, 480), ReaderNavigationHelper.ClampReaderWindowSize(1024, 768, narrowArea));
    }

    // ===== 6. BuildExploreOptions & IsLastSpreadReached =====

    [Fact]
    public void BuildExploreOptions_AllFields_BuildsQuotedSafeOptionsInOrder()
    {
        var options = ReaderNavigationHelper.BuildExploreOptions(
            "Flugel",
            "Fate/Grand Order",
            "C_97 Team");

        Assert.Equal(3, options.Count);
        Assert.Equal(new ReaderExploreOption(ReaderExploreKind.Author, "Flugel", "artist:Flugel"), options[0]);
        Assert.Equal(new ReaderExploreOption(ReaderExploreKind.Series, "Fate/Grand Order", "series:\"Fate/Grand Order\""), options[1]);
        Assert.Equal(new ReaderExploreOption(ReaderExploreKind.TranslationGroup, "C_97 Team", "group:\"C_97 Team\""), options[2]);
    }

    [Fact]
    public void BuildExploreOptions_PartialOrEmpty_OmitsInvalidAndBlankFields()
    {
        var authorOnly = ReaderNavigationHelper.BuildExploreOptions("  Yamada Taro  ", null, "   ");
        var single = Assert.Single(authorOnly);
        Assert.Equal(ReaderExploreKind.Author, single.Kind);
        Assert.Equal("Yamada Taro", single.Value);
        Assert.Equal("artist:\"Yamada Taro\"", single.Dsl);

        Assert.Empty(ReaderNavigationHelper.BuildExploreOptions(null, "", "   "));
        Assert.Empty(ReaderNavigationHelper.BuildExploreOptions("\"\"", "\t\n", "  \"  "));
    }

    [Theory]
    [InlineData(0, 1, 0, false)]   // 0 頁邊界
    [InlineData(0, 2, 0, false)]   // 0 頁雙頁邊界
    [InlineData(0, 1, 1, true)]    // 單頁 1 頁本
    [InlineData(0, 1, 10, false)]  // 單頁第一頁
    [InlineData(8, 1, 10, false)]  // 單頁倒數第二頁
    [InlineData(9, 1, 10, true)]   // 單頁最後一頁
    [InlineData(6, 2, 10, false)]  // 雙頁中間跨頁 (7-8/10)
    [InlineData(7, 2, 10, false)]  // 雙頁 (8-9/10)，尚未達第 10 頁
    [InlineData(8, 2, 10, true)]   // 雙頁最後跨頁 (9-10/10)
    [InlineData(8, 2, 9, true)]    // 奇數頁數最後單頁 (9/9)
    [InlineData(0, 0, 1, true)]    // spreadSize <= 0 防護視同 1
    public void IsLastSpreadReached_HandlesSingleDualMiddleAndZeroPageBoundaries(
        int currentIndex, int spreadSize, int pageCount, bool expected)
    {
        Assert.Equal(expected, ReaderNavigationHelper.IsLastSpreadReached(currentIndex, spreadSize, pageCount));
    }

    // ===== 7. PDF 直出 SKBitmap 與 SkiaPageRenderer 優先佇列 =====

    [Fact]
    public async Task SkiaPageRenderer_PriorityQueue_DecodesVisiblePagesFirstWithoutBlockingOrDuplicates()
    {
        var producedOrder = new ConcurrentQueue<int>();
        var produceCounts = new ConcurrentDictionary<int, int>();
        using var firstDecodeStarted = new ManualResetEventSlim(false);
        using var allowFirstDecode = new ManualResetEventSlim(false);
        using var allDone = new CountdownEvent(8); // 頁 0,1,2,4,5,6,7,8

        using var renderer = new SkiaPageRenderer(
            pageCount: 12,
            idx =>
            {
                produceCounts.AddOrUpdate(idx, 1, (_, c) => c + 1);
                producedOrder.Enqueue(idx);
                if (idx == 0)
                {
                    firstDecodeStarted.Set();
                    allowFirstDecode.Wait(TimeSpan.FromSeconds(5));
                }

                var info = new SKImageInfo(16, 16, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                using var surface = SKSurface.Create(info);
                surface.Canvas.Clear(SKColors.White);
                allDone.Signal();
                return surface.Snapshot();
            },
            cacheCap: 12);

        // 先讓背景預載第 0..2 頁，並卡住第 0 頁
        renderer.RequestAround(0, 2);
        Assert.True(firstDecodeStarted.Wait(TimeSpan.FromSeconds(5)));

        // 在第 0 頁仍阻塞期間呼叫優先佇列與重複排程：不得阻塞呼叫端
        renderer.RequestSpreadAndAround(5, 2, 1); // 優先 5,6；背景 7,4
        renderer.RequestPriority(8);              // 優先 8
        renderer.RequestPriority(5);              // 重複 5
        renderer.RequestAround(5, 1);             // 重複 5,6,4

        allowFirstDecode.Set();
        Assert.True(allDone.Wait(TimeSpan.FromSeconds(5)));

        // 等待一點時間確認不會有多餘重複呼叫
        await Task.Delay(50);

        var order = producedOrder.ToArray();
        Assert.Equal(0, order[0]);
        // 優先頁 5, 6, 8 必須在剩餘一般背景頁 (1, 2, 7, 4) 之前完成解碼
        Assert.Equal(new[] { 5, 6, 8 }, order.Skip(1).Take(3).ToArray());
        Assert.All(produceCounts.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public void SkiaBitmapSource_DirectDecodePaths_NeverCallOpenPageStream()
    {
        var source = new ThrowingStreamSkiaBitmapSource(width: 200, height: 300, new SKColor(10, 20, 30, 255));

        using var skImage = SkiaPageRenderer.DecodeFromSource(source, 0);
        Assert.NotNull(skImage);
        Assert.Equal(200, skImage!.Width);
        Assert.Equal(300, skImage.Height);

        using var gdiImage = ReaderImageLoader.Decode(source, 0);
        Assert.Equal(200, gdiImage.Width);
        Assert.Equal(300, gdiImage.Height);
        using (var gdiBmp = Assert.IsType<Bitmap>(gdiImage))
        {
            var pixel = gdiBmp.GetPixel(10, 10);
            Assert.Equal(10, pixel.R);
            Assert.Equal(20, pixel.G);
            Assert.Equal(30, pixel.B);
        }

        using var thumb = ReaderImageLoader.DecodeThumbnail(source, 0, 100, 100);
        Assert.Equal(67, thumb.Width);
        Assert.Equal(100, thumb.Height);

        Assert.Equal(0, source.OpenPageCallCount);
        Assert.Equal(3, source.OpenPageBitmapCallCount);
    }

    [Fact]
    public void PdfReaderSource_OpenPageBitmap_RendersMinimalPdfAndGuardsAfterDispose()
    {
        var pdfPath = Path.Combine(_tempDir, "minimal.pdf");
        File.WriteAllBytes(pdfPath, CreateMinimalSinglePagePdfBytes());

        var pdf = new PdfReaderSource(pdfPath, dpi: 72);
        try
        {
            Assert.Equal(1, pdf.PageCount);

            using var bmp = pdf.OpenPageBitmap(0);
            Assert.NotNull(bmp);
            Assert.True(bmp!.Width > 0);
            Assert.True(bmp.Height > 0);

            Assert.Throws<ArgumentOutOfRangeException>(() => pdf.OpenPageBitmap(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pdf.OpenPageBitmap(1));
        }
        finally
        {
            pdf.Dispose();
        }

        Assert.Throws<ObjectDisposedException>(() => pdf.OpenPageBitmap(0));
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

    private sealed class ThrowingStreamSkiaBitmapSource : IReaderSource, ISkiaBitmapSource
    {
        private readonly int _width;
        private readonly int _height;
        private readonly SKColor _fillColor;

        public int OpenPageCallCount { get; private set; }
        public int OpenPageBitmapCallCount { get; private set; }

        public ThrowingStreamSkiaBitmapSource(int width, int height, SKColor fillColor)
        {
            _width = width;
            _height = height;
            _fillColor = fillColor;
        }

        public string DisplayName => "fake.pdf";
        public string RootPath => @"C:\fake\fake.pdf";
        public int PageCount => 1;
        public string GetPageExtension(int index) => ".png";
        public string GetPageName(int index) => $"Page {index + 1}";

        public Stream OpenPage(int index)
        {
            OpenPageCallCount++;
            throw new InvalidOperationException("OpenPage stream path should not be called for ISkiaBitmapSource.");
        }

        public SKBitmap? OpenPageBitmap(int index)
        {
            OpenPageBitmapCallCount++;
            // 特意使用 Rgba8888 + Premul 驗證 CopySkBitmapToGdiBitmap 的色空間與 Alpha 轉換安全
            var bmp = new SKBitmap(new SKImageInfo(_width, _height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(_fillColor);
            return bmp;
        }

        public void Dispose() { }
    }
}
