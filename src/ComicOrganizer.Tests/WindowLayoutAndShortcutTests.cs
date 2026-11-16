using System.Drawing;
using System.Text.Json;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 5（D5 視窗版面持久化、側邊欄分割距離防呆與分頁循環切換）單元測試。
/// </summary>
public class WindowLayoutAndShortcutTests : IDisposable
{
    private readonly string _tempDir;

    public WindowLayoutAndShortcutTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_wi5_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ===== 1. UserPreferences 視窗與側邊欄記憶欄位 =====

    [Fact]
    public void UserPreferences_WindowAndSidebarFields_DefaultsAndJsonRoundTrip()
    {
        var defaults = new UserPreferences();
        Assert.Null(defaults.WindowX);
        Assert.Null(defaults.WindowY);
        Assert.Null(defaults.WindowWidth);
        Assert.Null(defaults.WindowHeight);
        Assert.False(defaults.WindowMaximized);
        Assert.Null(defaults.SidebarSplitterWidth);

        var prefsPath = Path.Combine(_tempDir, "preferences.json");
        var manager = new UserPreferencesManager(prefsPath);

        manager.Save(new UserPreferences
        {
            WindowX = 120,
            WindowY = 80,
            WindowWidth = 1440,
            WindowHeight = 900,
            WindowMaximized = true,
            SidebarSplitterWidth = 310,
        });

        var loaded = manager.Load();
        Assert.Equal(120, loaded.WindowX);
        Assert.Equal(80, loaded.WindowY);
        Assert.Equal(1440, loaded.WindowWidth);
        Assert.Equal(900, loaded.WindowHeight);
        Assert.True(loaded.WindowMaximized);
        Assert.Equal(310, loaded.SidebarSplitterWidth);

        // 舊版 JSON 不含這些欄位時應安全回到預設值
        var legacyJson = JsonSerializer.Serialize(new { Language = "zh-TW" });
        var fromLegacy = JsonSerializer.Deserialize<UserPreferences>(legacyJson)!;
        Assert.Null(fromLegacy.WindowX);
        Assert.Null(fromLegacy.WindowY);
        Assert.Null(fromLegacy.WindowWidth);
        Assert.Null(fromLegacy.WindowHeight);
        Assert.False(fromLegacy.WindowMaximized);
        Assert.Null(fromLegacy.SidebarSplitterWidth);
    }

    // ===== 2. ValidateWindowBounds =====

    [Fact]
    public void ValidateWindowBounds_MissingOrNonPositiveSize_ReturnsNull()
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        var areas = new[] { primary };

        Assert.Null(WindowLayoutHelper.ValidateWindowBounds(100, 100, null, 800, areas, primary));
        Assert.Null(WindowLayoutHelper.ValidateWindowBounds(100, 100, 1200, null, areas, primary));
        Assert.Null(WindowLayoutHelper.ValidateWindowBounds(100, 100, 0, 800, areas, primary));
        Assert.Null(WindowLayoutHelper.ValidateWindowBounds(100, 100, 1200, -10, areas, primary));
    }

    [Fact]
    public void ValidateWindowBounds_SingleAndMultiMonitor_ValidRestoresExactBounds()
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        var secondary = new Rectangle(1920, 0, 1920, 1040);
        var areas = new[] { primary, secondary };

        var onPrimary = WindowLayoutHelper.ValidateWindowBounds(100, 80, 1280, 800, areas, primary);
        Assert.Equal(new Rectangle(100, 80, 1280, 800), onPrimary);

        var onSecondary = WindowLayoutHelper.ValidateWindowBounds(2000, 120, 1400, 900, areas, primary);
        Assert.Equal(new Rectangle(2000, 120, 1400, 900), onSecondary);
    }

    [Fact]
    public void ValidateWindowBounds_PartiallyOutOfBoundsAndOversized_ClampsIntoWorkingArea()
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        var areas = new[] { primary };

        // 左邊界部分超出 → 夾回 Left=0
        var leftClipped = WindowLayoutHelper.ValidateWindowBounds(-200, 60, 1200, 800, areas, primary);
        Assert.Equal(new Rectangle(0, 60, 1200, 800), leftClipped);

        // 右下超出 → 夾回 Right-Width / Bottom-Height
        var rightBottomClipped = WindowLayoutHelper.ValidateWindowBounds(1500, 600, 1200, 800, areas, primary);
        Assert.Equal(new Rectangle(720, 240, 1200, 800), rightBottomClipped);

        // 過大尺寸 → 夾到工作區大小
        var oversized = WindowLayoutHelper.ValidateWindowBounds(100, 100, 3000, 2000, areas, primary);
        Assert.Equal(new Rectangle(0, 0, 1920, 1040), oversized);

        // 過小尺寸 → 夾到最小寬高
        var undersized = WindowLayoutHelper.ValidateWindowBounds(100, 100, 400, 300, areas, primary);
        Assert.Equal(new Rectangle(100, 100, WindowLayoutHelper.MinWindowWidth, WindowLayoutHelper.MinWindowHeight), undersized);
    }

    [Fact]
    public void ValidateWindowBounds_UnpluggedMonitorOrMissingPosition_CentersOnPrimary()
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        var areas = new[] { primary };

        // 原視窗位於已拔除的右側副螢幕 → 重定位到主螢幕置中
        var unplugged = WindowLayoutHelper.ValidateWindowBounds(2500, 100, 1200, 800, areas, primary);
        Assert.Equal(new Rectangle(360, 120, 1200, 800), unplugged);

        // 僅有寬高無 X/Y → 置中於主螢幕
        var noXY = WindowLayoutHelper.ValidateWindowBounds(null, null, 1200, 800, areas, primary);
        Assert.Equal(new Rectangle(360, 120, 1200, 800), noXY);
    }

    // ===== 3. ClampSplitterDistance =====

    [Theory]
    [InlineData(null, 1200, WindowLayoutHelper.DefaultSidebarWidth)]
    [InlineData(0, 1200, WindowLayoutHelper.DefaultSidebarWidth)]
    [InlineData(-50, 1200, WindowLayoutHelper.DefaultSidebarWidth)]
    [InlineData(80, 1200, WindowLayoutHelper.MinSidebarWidth)]   // 低於 140 → 夾到 140
    [InlineData(320, 1200, 320)]                                 // 正常範圍
    [InlineData(1000, 1200, 840)]                                // 1200 - 360 = 840
    [InlineData(300, 420, WindowLayoutHelper.MinSidebarWidth)]   // 極窄容器：420 - 360 < 140 → 守住 MinSidebarWidth
    [InlineData(300, 0, WindowLayoutHelper.MinSidebarWidth)]     // 尚未量測寬度為 0 時不拋錯
    public void ClampSplitterDistance_RespectsDefaultsAndBounds(int? savedDistance, int containerWidth, int expected)
    {
        Assert.Equal(expected, WindowLayoutHelper.ClampSplitterDistance(savedDistance, containerWidth));
    }

    // ===== 4. GetNextTabIndex =====

    [Theory]
    [InlineData(0, 0, false, -1)]
    [InlineData(0, 1, false, -1)]
    [InlineData(0, 1, true, -1)]
    [InlineData(0, 3, false, 1)]
    [InlineData(2, 3, false, 0)] // 尾端往後循環回 0
    [InlineData(0, 3, true, 2)]  // 開頭往前循環回尾端
    [InlineData(2, 3, true, 1)]
    [InlineData(-1, 3, false, 0)]
    [InlineData(-1, 3, true, 2)]
    public void GetNextTabIndex_WrapsAroundAndHandlesSingleTab(int currentIndex, int tabCount, bool reverse, int expected)
    {
        Assert.Equal(expected, WindowLayoutHelper.GetNextTabIndex(currentIndex, tabCount, reverse));
    }
}
