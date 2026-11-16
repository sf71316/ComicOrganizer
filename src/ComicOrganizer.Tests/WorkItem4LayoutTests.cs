using System.Text.Json;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 4（D4 Drawer 寬度限制與自適應折行寬度 + UserPreferences.DrawerWidth）單元測試。
/// </summary>
public class WorkItem4LayoutTests : IDisposable
{
    private readonly string _tempDir;

    public WorkItem4LayoutTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_wi4_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void UserPreferences_DrawerWidth_DefaultsToNull_And_RoundTripsJson()
    {
        var defaults = new UserPreferences();
        Assert.Null(defaults.DrawerWidth);

        var prefsPath = Path.Combine(_tempDir, "preferences.json");
        var manager = new UserPreferencesManager(prefsPath);

        manager.Save(new UserPreferences { DrawerWidth = 520 });
        var loaded = manager.Load();
        Assert.Equal(520, loaded.DrawerWidth);

        manager.Save(new UserPreferences { DrawerWidth = null });
        var loadedNull = manager.Load();
        Assert.Null(loadedNull.DrawerWidth);

        // 舊版 JSON 不含 DrawerWidth 時應維持 null
        var legacyJson = JsonSerializer.Serialize(new { Language = "zh-TW" });
        var fromLegacy = JsonSerializer.Deserialize<UserPreferences>(legacyJson)!;
        Assert.Null(fromLegacy.DrawerWidth);
    }

    [Theory]
    [InlineData(null, 1200, DrawerWidthHelper.DefaultDrawerWidth)]
    [InlineData(0, 1200, DrawerWidthHelper.DefaultDrawerWidth)]
    [InlineData(-100, 1200, DrawerWidthHelper.DefaultDrawerWidth)]
    [InlineData(200, 1200, DrawerWidthHelper.MinDrawerWidth)]
    [InlineData(520, 1200, 520)]
    [InlineData(1500, 1200, 1140)] // 1200 - 60 = 1140
    [InlineData(500, 440, 380)]   // 440 - 60 = 380
    [InlineData(500, 350, DrawerWidthHelper.MinDrawerWidth)] // 窄視窗：350 - 60 < 340 → 夾在 MinDrawerWidth
    [InlineData(null, 300, DrawerWidthHelper.MinDrawerWidth)] // 窄視窗下連預設 420 也必須夾到 MinDrawerWidth
    public void ClampDrawerWidth_RespectsDefaultsAndBounds(int? requestedWidth, int ownerClientWidth, int expected)
    {
        var actual = DrawerWidthHelper.ClampDrawerWidth(requestedWidth, ownerClientWidth);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(420, 40, 0, 180, 380)]
    [InlineData(420, 40, 17, 180, 363)]
    [InlineData(200, 60, 17, 180, 180)] // 低於 minWidth → 夾回 180
    [InlineData(100, 50, 20, 120, 120)] // 自訂 minWidth
    [InlineData(0, 40, 0, 180, 180)]
    public void ComputeContentWrapWidth_ComputesAndClampsToMinWidth(
        int containerClientWidth,
        int horizontalOverhead,
        int verticalScrollbarWidth,
        int minWidth,
        int expected)
    {
        var actual = DrawerWidthHelper.ComputeContentWrapWidth(
            containerClientWidth,
            horizontalOverhead,
            verticalScrollbarWidth,
            minWidth);
        Assert.Equal(expected, actual);
    }
}
