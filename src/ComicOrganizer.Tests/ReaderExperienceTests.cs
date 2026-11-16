using System.Drawing;
using System.Text.Json;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
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
}
