using Avalonia.Input;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using ComicOrganizer.Avalonia.Views.Controls;
using Xunit;

namespace ComicOrganizer.Tests;

public sealed class ScrollWheelStepTests : IDisposable
{
    private readonly string _tempDir;

    public ScrollWheelStepTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ComicOrg_WheelStep_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
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
        }
    }

    [Fact]
    public void UserPreferences_DefaultGalleryWheelScrollDip_Is200()
    {
        var prefs = new UserPreferences();
        Assert.Equal(200, prefs.GalleryWheelScrollDip);
    }

    [Fact]
    public void TryComputeNextVerticalOffset_AppliesStep_RespectsBoundsAndModifiers()
    {
        // 向下滾一格（Delta.Y = -1）移動 +200 DIP
        Assert.True(ScrollWheelStepHelper.TryComputeNextVerticalOffset(
            currentY: 100, deltaY: -1, extentHeight: 2000, viewportHeight: 800, stepDip: 200, KeyModifiers.None, out double downY));
        Assert.Equal(300, downY);

        // 向上滾一格（Delta.Y = +1）移動 -200 DIP
        Assert.True(ScrollWheelStepHelper.TryComputeNextVerticalOffset(
            currentY: 300, deltaY: 1, extentHeight: 2000, viewportHeight: 800, stepDip: 200, KeyModifiers.None, out double upY));
        Assert.Equal(100, upY);

        // 高精度觸控板依比例移動（Delta.Y = -0.25 -> +50 DIP）
        Assert.True(ScrollWheelStepHelper.TryComputeNextVerticalOffset(
            currentY: 0, deltaY: -0.25, extentHeight: 2000, viewportHeight: 800, stepDip: 200, KeyModifiers.None, out double trackpadY));
        Assert.Equal(50, trackpadY);

        // 夾限到底部最大值 (2000 - 800 = 1200)
        Assert.True(ScrollWheelStepHelper.TryComputeNextVerticalOffset(
            currentY: 1120, deltaY: -1, extentHeight: 2000, viewportHeight: 800, stepDip: 200, KeyModifiers.None, out double clampedBottom));
        Assert.Equal(1200, clampedBottom);

        // 已在頂部再往上滾，或已在底部再往下滾，回傳 false 讓事件正常傳遞
        Assert.False(ScrollWheelStepHelper.TryComputeNextVerticalOffset(
            currentY: 0, deltaY: 1, extentHeight: 2000, viewportHeight: 800, stepDip: 200, KeyModifiers.None, out _));
        Assert.False(ScrollWheelStepHelper.TryComputeNextVerticalOffset(
            currentY: 1200, deltaY: -1, extentHeight: 2000, viewportHeight: 800, stepDip: 200, KeyModifiers.None, out _));

        // 有修飾鍵（例如 Ctrl+Wheel）時不攔截
        Assert.False(ScrollWheelStepHelper.TryComputeNextVerticalOffset(
            currentY: 200, deltaY: -1, extentHeight: 2000, viewportHeight: 800, stepDip: 200, KeyModifiers.Control, out _));
    }

    [Fact]
    public void SettingsPage_SaveGalleryWheelScrollDip_UpdatesGalleryAndExploreViewModelsImmediately()
    {
        using var ctx = new AppDataContext(explicitDataDirectory: _tempDir, forceReadOnlyDatabase: false);
        using var coverService = new SkiaCoverService(ctx.ThumbStore);
        using var galleryVm = new GalleryPageViewModel(ctx, coverService, autoLoad: false);
        using var exploreVm = new ExplorePageViewModel(ctx, coverService, autoLoad: false);
        var settingsVm = new SettingsPageViewModel(ctx, runOnUiAsync: action => { action(); return Task.CompletedTask; });

        Assert.Equal(200, galleryVm.WheelScrollStepDip);
        Assert.Equal(200, exploreVm.WheelScrollStepDip);
        Assert.Equal(200d, settingsVm.GalleryWheelScrollDip);

        // 修改滾輪距離並儲存（並測試超過 100~400 範圍會被安全夾限）
        settingsVm.GalleryWheelScrollDip = 320d;
        settingsVm.SaveCommand.Execute(null);

        Assert.Equal(320, ctx.Preferences.GalleryWheelScrollDip);
        Assert.Equal(320, galleryVm.WheelScrollStepDip);
        Assert.Equal(320, exploreVm.WheelScrollStepDip);

        settingsVm.GalleryWheelScrollDip = 9999d;
        settingsVm.SaveCommand.Execute(null);
        Assert.Equal(400, ctx.Preferences.GalleryWheelScrollDip);
        Assert.Equal(400, galleryVm.WheelScrollStepDip);
        Assert.Equal(400, exploreVm.WheelScrollStepDip);

        settingsVm.GalleryWheelScrollDip = 48d;
        settingsVm.SaveCommand.Execute(null);
        Assert.Equal(100, ctx.Preferences.GalleryWheelScrollDip);
        Assert.Equal(100, galleryVm.WheelScrollStepDip);
        Assert.Equal(100, exploreVm.WheelScrollStepDip);
        Assert.Equal(100, ScrollWheelStepHelper.ClampOrDefault(48));
        Assert.Equal(400, ScrollWheelStepHelper.ClampOrDefault(600));
    }
}
