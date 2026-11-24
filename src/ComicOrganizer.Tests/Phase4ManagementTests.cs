using System.Text.Json;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.App.UI;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// Phase 4（W4.1 ～ W4.4）自動歸檔、資料管理、維護中心、設定套用與唯讀保護單元測試。
/// </summary>
public sealed class Phase4ManagementTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ComicOrg_Phase4_{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static void WriteDummyImage(string filePath)
    {
        using var bmp = new SKBitmap(32, 48, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.CornflowerBlue);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(filePath, data.ToArray());
    }

    private static Task ImmediateUiAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task AutoArchive_Scan_DryRun_ReadOnlyGuard_AndWritableMoveWithLearning()
    {
        var dataDir = CreateTempDir("AutoData");
        var srcDir = CreateTempDir("Unorg");
        var rootDir = CreateTempDir("LibRoot");

        // 建立目標庫作者資料夾
        var targetFlugelDir = Path.Combine(rootDir, "Flugel");
        Directory.CreateDirectory(targetFlugelDir);
        Directory.CreateDirectory(Path.Combine(rootDir, "Takeda Hiromitsu"));

        // 建立待歸檔漫畫資料夾
        var workFolderName = "(C99) [Flugel] Heartbeat Sample [中国翻訳]";
        var srcWorkDir = Path.Combine(srcDir, workFolderName);
        Directory.CreateDirectory(srcWorkDir);
        WriteDummyImage(Path.Combine(srcWorkDir, "001.png"));

        // 預先準備 appsettings.json 與空 cache.db
        var configJson = JsonSerializer.Serialize(new AppConfig
        {
            UnorganizedDirectory = srcDir,
            RootDirectory = rootDir,
            MaxScanDepth = 2
        });
        await File.WriteAllTextAsync(Path.Combine(dataDir, "appsettings.json"), configJson);
        using (var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            Assert.NotNull(initStore);
        }

        // 1. 唯讀模式下：允許掃描與模擬測試，但禁止實際搬移與寫入 mappings/db
        using (var roCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: true))
        {
            var roVm = new AutoArchivePageViewModel(roCtx, runOnUiAsync: ImmediateUiAsync);
            Assert.True(roVm.IsReadOnlyDatabase);

            await roVm.ScanAsync();
            Assert.Equal(1, roVm.TotalScannedCount);
            Assert.Single(roVm.VisibleCandidates);

            var candidate = roVm.VisibleCandidates[0];
            Assert.Equal("Flugel", candidate.SelectedTargetFolder);
            Assert.True(candidate.Confidence >= 85.0);
            Assert.True(candidate.IsChecked);

            await roVm.SimulateExecuteAsync();
            Assert.True(Directory.Exists(srcWorkDir));
            Assert.False(Directory.Exists(Path.Combine(targetFlugelDir, workFolderName)));

            await roVm.ExecuteArchiveAsync();
            Assert.True(Directory.Exists(srcWorkDir));
            Assert.False(File.Exists(Path.Combine(dataDir, "mappings.json")));
        }

        // 2. 讀寫模式下：執行實際搬檔並回寫 mappings.json 與 catalog
        using (var rwCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false))
        {
            var rwVm = new AutoArchivePageViewModel(rwCtx, runOnUiAsync: ImmediateUiAsync);
            await rwVm.ScanAsync();
            Assert.Single(rwVm.VisibleCandidates);
            rwVm.VisibleCandidates[0].IsChecked = true;

            await rwVm.ExecuteArchiveAsync();

            var movedPath = Path.Combine(targetFlugelDir, workFolderName);
            Assert.False(Directory.Exists(srcWorkDir));
            Assert.True(Directory.Exists(movedPath));
            Assert.Single(rwVm.VisibleCandidates);
            Assert.Equal("✓ 已歸檔", rwVm.VisibleCandidates[0].ExecutionStatus);
            Assert.True(File.Exists(Path.Combine(dataDir, "mappings.json")));
            Assert.NotNull(rwCtx.MetaStore.GetAssetId(movedPath));
        }
    }

    [Fact]
    public async Task DataManagement_TagAuthorOperations_Diagnostics_AndReadOnlyProtection()
    {
        var dataDir = CreateTempDir("DmData");
        var libDir = CreateTempDir("DmLib");

        var comic1 = Path.Combine(libDir, "AuthorA", "Work1");
        Directory.CreateDirectory(comic1);
        WriteDummyImage(Path.Combine(comic1, "001.png"));

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig { RootDirectory = libDir }));

        long orphanTagId;
        using (var seedStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            var authorA = seedStore.UpsertAuthor("AuthorA", Path.Combine(libDir, "AuthorA"));
            var authorB = seedStore.UpsertAuthor("AuthorB");
            Assert.True(authorB > 0);

            seedStore.UpsertCatalogOnly(new AssetMeta
            {
                Path = comic1,
                Name = "Work1",
                IsFolder = true,
                Mtime = 100
            });
            long assetId = seedStore.GetAssetId(comic1)!.Value;
            seedStore.UpsertMetadata(new AssetMetadata
            {
                AssetId = assetId,
                AuthorId = authorA,
                Title = "Work1 Title",
                PageCount = 1
            });

            var maidTag = seedStore.UpsertTag("maid", "tag");
            var waitressTag = seedStore.UpsertTag("waitress", "tag");
            orphanTagId = seedStore.UpsertTag("unused_orphan", "tag");
            Assert.True(waitressTag > 0 && orphanTagId > 0);
            seedStore.SetAssetTags(assetId, ["maid", "waitress"]);
            Assert.True(maidTag > 0);
        }

        // 1. 唯讀模式保護驗證
        using (var roCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: true))
        using (var roCover = new SkiaCoverService(roCtx.ThumbStore))
        using (var roVm = new DataManagementPageViewModel(roCtx, roCover, ImmediateUiAsync))
        {
            Assert.True(roVm.IsReadOnlyDatabase);
            roVm.SelectedTag = roVm.VisibleTags.First(t => t.Name == "maid");
            roVm.EditTagTranslation = "女僕";
            roVm.SaveSelectedTag();

            Assert.Empty(roCtx.MetaStore.GetTagTranslations([roVm.SelectedTag.Id], "zh-TW"));

            // 只讀診斷仍可正常執行
            await roVm.ScanIntegrityAsync();
            await roVm.AnalyzeReconcileAsync();
        }

        // 2. 讀寫模式完整驗證：標籤編輯 / 別名 / 虛擬合併 / 孤兒清理 / 作者合併 / 完整性修復
        using (var rwCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false))
        using (var rwCover = new SkiaCoverService(rwCtx.ThumbStore))
        using (var vm = new DataManagementPageViewModel(rwCtx, rwCover, ImmediateUiAsync))
        {
            // 標籤翻譯 + 別名 + 虛擬合併
            vm.SelectedTag = vm.VisibleTags.First(t => t.Name == "maid");
            vm.EditTagTranslation = "女僕";
            vm.SaveSelectedTag();
            Assert.Equal("女僕", rwCtx.MetaStore.GetTagTranslations([vm.SelectedTag.Id], "zh-TW")[vm.SelectedTag.Id]);

            vm.NewTagAliasText = "housemaid";
            vm.AddTagAlias();
            Assert.Contains(vm.SelectedTagAliases, a => a.AliasName == "housemaid");

            // 勾選 maid 與 waitress 合併為虛擬主標籤 uniform_staff
            foreach (var t in vm.VisibleTags.Where(t => t.Name is "maid" or "waitress"))
            {
                t.IsChecked = true;
            }
            vm.VirtualMasterName = "uniform_staff";
            vm.VirtualMasterZhName = "制服人員";
            vm.MergeCheckedTagsIntoVirtual();
            Assert.Contains(vm.VisibleTags, t => t.Name == "uniform_staff" && t.IsVirtual);

            // 清理孤兒標籤
            vm.CleanupOrphanTags();
            Assert.DoesNotContain(vm.VisibleTags, t => t.Id == orphanTagId);

            // 作者重命名、別名與合併
            vm.SelectedAuthor = vm.VisibleAuthors.First(a => a.MainName == "AuthorB");
            vm.EditAuthorMainName = "AuthorB_Renamed";
            vm.RenameSelectedAuthor();
            Assert.Contains(vm.VisibleAuthors, a => a.MainName == "AuthorB_Renamed");

            vm.SelectedAuthor = vm.VisibleAuthors.First(a => a.MainName == "AuthorA");
            vm.NewAuthorAliasName = "作家A";
            vm.NewAuthorAliasLang = "zh-TW";
            vm.AddAuthorAlias();
            Assert.Contains(vm.SelectedAuthorAliases, a => a.Name == "作家A");

            // 合併 AuthorB_Renamed → AuthorA
            vm.SelectedAuthor = vm.VisibleAuthors.First(a => a.MainName == "AuthorB_Renamed");
            vm.MergeDestinationAuthor = vm.VisibleAuthors.First(a => a.MainName == "AuthorA");
            vm.MergeSelectedAuthorIntoTarget();
            Assert.DoesNotContain(vm.VisibleAuthors, a => a.MainName == "AuthorB_Renamed");

            // 驗證畫廊跳轉事件
            string? openedQuery = null;
            vm.OpenInGalleryRequested += q => openedQuery = q;
            vm.SelectedAuthor = vm.VisibleAuthors.First(a => a.MainName == "AuthorA");
            vm.OpenSelectedAuthorInGalleryCommand.Execute(null);
            Assert.Equal("artist:AuthorA", openedQuery);

            // 資料庫對帳 & 完整性檢查 + Skia 縮圖補齊
            await vm.ScanIntegrityAsync();
            await vm.AnalyzeReconcileAsync();
            await vm.SyncSkiaThumbnailsAsync();

            var refreshedAsset = rwCtx.MetaStore.Get(comic1);
            Assert.NotNull(refreshedAsset);
            Assert.False(string.IsNullOrEmpty(refreshedAsset!.CoverThumbKey));
        }
    }

    [Fact]
    public void MainWindow_WiresPhase4Pages_AndReaderRespectsUserPreferencesDefaults()
    {
        var dataDir = CreateTempDir("PrefsData");
        var comicDir = CreateTempDir("PrefsComic");
        WriteDummyImage(Path.Combine(comicDir, "001.png"));

        using (var setupStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            setupStore.UpsertCatalogOnly(new AssetMeta
            {
                Path = comicDir,
                Name = "PrefsComic",
                IsFolder = true,
                Mtime = 100
            });
        }

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        ctx.SavePreferences(new UserPreferences
        {
            DefaultReadingDirection = "ltr",
            DefaultReaderViewMode = "ContinuousScroll",
            DefaultReaderZoomMode = "FitWidth"
        });

        using var mainVm = new MainWindowViewModel(ctx);
        Assert.IsType<AutoArchivePageViewModel>(mainVm.AutoArchivePage);
        Assert.IsType<DataManagementPageViewModel>(mainVm.DataManagementPage);

        using var readerVm = new ReaderViewModel(ctx, comicDir, runOnUiAsync: ImmediateUiAsync);
        Assert.False(readerVm.IsRightToLeft);
        Assert.Equal(ComicOrganizer.App.UI.ReaderViewMode.ContinuousScroll, readerVm.ViewMode);
        Assert.Equal(ComicOrganizer.App.UI.ReaderZoomMode.FitWidth, readerVm.ZoomMode);
    }

    [Fact]
    public void AppDataContext_AccentThemesIncludeLowSaturationPalettes_AndSynchronizeSelection()
    {
        var dataDir = CreateTempDir("accent_themes");
        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);

        var expectedNames = new[]
        {
            "Blue", "Purple", "Rose", "Emerald", "Amber",
            "SlateBlue", "Sage", "Mauve", "Sand",
        };
        Assert.Equal(expectedNames, ctx.AvailableAccentNames);
        Assert.Equal(global::Avalonia.Media.Color.Parse("#5B7394"), AppDataContext.GetAccentPreviewColor("SlateBlue"));
        Assert.Equal(global::Avalonia.Media.Color.Parse("#547A6F"), AppDataContext.GetAccentPreviewColor("Sage"));
        Assert.Equal(global::Avalonia.Media.Color.Parse("#746585"), AppDataContext.GetAccentPreviewColor("Mauve"));
        Assert.Equal(global::Avalonia.Media.Color.Parse("#8C7762"), AppDataContext.GetAccentPreviewColor("Sand"));

        using var mainVm = new MainWindowViewModel(ctx);
        Assert.Equal(9, mainVm.AccentOptions.Count);
        Assert.Equal("霧霾藍灰 SlateBlue", mainVm.AccentOptions.Single(x => x.Name == "SlateBlue").DisplayName);
        Assert.Equal("鼠尾草綠 Sage", mainVm.AccentOptions.Single(x => x.Name == "Sage").DisplayName);
        Assert.Equal("煙燻藕紫 Mauve", mainVm.AccentOptions.Single(x => x.Name == "Mauve").DisplayName);
        Assert.Equal("暖沙棕灰 Sand", mainVm.AccentOptions.Single(x => x.Name == "Sand").DisplayName);

        mainVm.AccentOptions.Single(x => x.Name == "SlateBlue").SelectCommand.Execute(null);
        Assert.Equal("SlateBlue", ctx.CurrentAccentName);
        Assert.Equal("SlateBlue", mainVm.CurrentAccentName);
        Assert.Equal("霧霾藍灰", mainVm.CurrentAccentShortLabel);
        Assert.True(mainVm.AccentOptions.Single(x => x.Name == "SlateBlue").IsSelected);

        var settingsVm = new SettingsPageViewModel(ctx, runOnUiAsync: ImmediateUiAsync);
        Assert.Equal(9, settingsVm.AccentOptions.Count);
        Assert.True(settingsVm.AccentOptions.Single(x => x.Name == "SlateBlue").IsSelected);

        settingsVm.AccentOptions.Single(x => x.Name == "Sage").SelectCommand.Execute(null);
        Assert.Equal("Sage", ctx.CurrentAccentName);
        Assert.Equal("Sage", mainVm.CurrentAccentName);
        Assert.True(mainVm.AccentOptions.Single(x => x.Name == "Sage").IsSelected);
        Assert.False(mainVm.AccentOptions.Single(x => x.Name == "SlateBlue").IsSelected);
    }
}
