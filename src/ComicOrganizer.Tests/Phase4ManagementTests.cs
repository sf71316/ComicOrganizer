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

    [Fact]
    public async Task AutoArchive_GroupsByTargetFolder_SupportsGroupToggleReassignAndExpandState()
    {
        var dataDir = CreateTempDir("GroupData");
        var srcDir = CreateTempDir("GroupSrc");
        var rootDir = CreateTempDir("GroupRoot");

        var authorLong = "VeryLongCircleName(很長的作者與社團名稱範例測試目錄)";
        var authorB = "BetaArtist";
        Directory.CreateDirectory(Path.Combine(rootDir, authorLong));
        Directory.CreateDirectory(Path.Combine(rootDir, authorB));

        // 建立來源：2 筆會配對到 authorLong，1 筆會配對到 BetaArtist，1 筆完全未匹配
        foreach (var srcName in new[]
        {
            $"[2024] [{authorLong}] First Super Long Comic Title 01",
            $"[2024] [{authorLong}] Second Super Long Comic Title 02",
            $"[2024] [{authorB}] Single Beta Comic",
            "CompletelyUnmatched_MysteryBook_999"
        })
        {
            var dir = Path.Combine(srcDir, srcName);
            Directory.CreateDirectory(dir);
            WriteDummyImage(Path.Combine(dir, "001.png"));
        }

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig
            {
                UnorganizedDirectory = srcDir,
                RootDirectory = rootDir,
                MaxScanDepth = 2
            }));
        using (var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            Assert.NotNull(initStore);
        }

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        var vm = new AutoArchivePageViewModel(ctx, runOnUiAsync: ImmediateUiAsync);

        // 預設依目錄分群顯示
        Assert.True(vm.IsGroupedView);
        Assert.False(vm.IsFlatView);

        await vm.ScanAsync();
        Assert.Equal(4, vm.TotalScannedCount);
        Assert.Equal(3, vm.TargetGroups.Count);

        // 已匹配且項目數最多（2 筆）排最前，其次 BetaArtist（1 筆），最後未匹配（1 筆）
        Assert.Equal(authorLong, vm.TargetGroups[0].TargetFolderName);
        Assert.Equal(2, vm.TargetGroups[0].TotalCount);
        Assert.Equal(authorB, vm.TargetGroups[1].TargetFolderName);
        Assert.Single(vm.TargetGroups[1].Items);
        Assert.True(vm.TargetGroups[2].IsUnmatchedGroup);
        Assert.Equal("CompletelyUnmatched_MysteryBook_999", vm.TargetGroups[2].Items[0].SourceName);
        Assert.False(vm.TargetGroups[2].Items[0].IsChecked);

        // 整組取消勾選與全選同步
        vm.TargetGroups[0].IsGroupChecked = false;
        Assert.All(vm.TargetGroups[0].Items, i => Assert.False(i.IsChecked));
        Assert.Equal(1, vm.CheckedCount);

        vm.TargetGroups[0].IsGroupChecked = true;
        Assert.All(vm.TargetGroups[0].Items, i => Assert.True(i.IsChecked));
        Assert.Equal(3, vm.CheckedCount);

        // 單筆取消勾選會將群組轉為半選 (null)
        vm.TargetGroups[0].Items[0].IsChecked = false;
        Assert.Null(vm.TargetGroups[0].IsGroupChecked);
        Assert.Equal(1, vm.TargetGroups[0].CheckedCount);

        // 收合後重組（改選另一項目的目標目錄）應記憶收合狀態
        vm.TargetGroups[1].ToggleExpandCommand.Execute(null);
        Assert.False(vm.TargetGroups[1].IsExpanded);

        // 將「未匹配」項目手動改派到 BetaArtist，應自動重新歸入 BetaArtist 且展開狀態維持記憶
        var unmatchedItem = vm.TargetGroups[2].Items[0];
        unmatchedItem.SelectedTargetFolder = authorB;

        Assert.Equal(2, vm.TargetGroups.Count);
        var updatedBetaGroup = vm.TargetGroups.Single(g => g.TargetFolderName == authorB);
        Assert.Equal(2, updatedBetaGroup.TotalCount);
        Assert.False(updatedBetaGroup.IsExpanded);

        // 全部展開／收合與切換平面檢視皆正常運作
        vm.ExpandAllGroupsCommand.Execute(null);
        Assert.All(vm.TargetGroups, g => Assert.True(g.IsExpanded));

        vm.SelectViewModeCommand.Execute("Flat");
        Assert.True(vm.IsFlatView);
        Assert.False(vm.IsGroupedView);
        Assert.Equal(4, vm.VisibleCandidates.Count);
    }

    [Fact]
    public async Task AutoArchive_GroupAddToQueue_RemoveRestore_AndExecuteQueueEndToEnd()
    {
        var dataDir = CreateTempDir("QueueData");
        var srcDir = CreateTempDir("QueueSrc");
        var rootDir = CreateTempDir("QueueRoot");

        var authorA = "ArtistAlpha";
        var authorB = "ArtistBeta";
        var targetA = Path.Combine(rootDir, authorA);
        var targetB = Path.Combine(rootDir, authorB);
        Directory.CreateDirectory(targetA);
        Directory.CreateDirectory(targetB);

        var work1 = "[ArtistAlpha] Work One [DL版]";
        var work2 = "[ArtistAlpha] Work Two [DL版]";
        var work3 = "[ArtistBeta] Work Three [DL版]";
        foreach (var w in new[] { work1, work2, work3 })
        {
            var dir = Path.Combine(srcDir, w);
            Directory.CreateDirectory(dir);
            WriteDummyImage(Path.Combine(dir, "001.png"));
        }

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig
            {
                UnorganizedDirectory = srcDir,
                RootDirectory = rootDir,
                MaxScanDepth = 2
            }));
        using (var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            Assert.NotNull(initStore);
        }

        // 1. 唯讀模式下：掃描完成時待歸檔清單必須一開始為空；放入清單後該作者整個分類（含未勾選項目）應隱藏
        using (var roCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: true))
        {
            var roVm = new AutoArchivePageViewModel(roCtx, runOnUiAsync: ImmediateUiAsync);
            Assert.Empty(roVm.QueuedItems);
            Assert.False(roVm.IsQueueDrawerOpen);

            await roVm.ScanAsync();
            Assert.Empty(roVm.QueuedItems);
            Assert.Equal(2, roVm.TargetGroups.Count);

            var alphaGroup = roVm.TargetGroups.Single(g => g.TargetFolderName == authorA);
            var alphaFirstItem = alphaGroup.Items[0];
            var alphaUncheckedSecondItem = alphaGroup.Items[1];
            alphaUncheckedSecondItem.IsChecked = false; // 僅保留第 1 筆勾選，第 2 筆刻意不勾選
            Assert.True(alphaGroup.CanAddCheckedToQueue);
            Assert.Equal("📥 放入清單 (1)", alphaGroup.AddToQueueButtonText);

            alphaGroup.AddCheckedToQueueCommand.Execute(null);
            Assert.True(roVm.IsQueueDrawerOpen);
            Assert.Single(roVm.QueuedItems);
            Assert.Equal(alphaFirstItem.SourceName, roVm.QueuedItems[0].SourceName);

            // 只要該作者已加入待歸檔清單，整個分類（含未勾選的第 2 筆）必須立即從左側隱藏
            Assert.DoesNotContain(roVm.TargetGroups, g => g.TargetFolderName == authorA);
            Assert.DoesNotContain(roVm.VisibleCandidates, c => c.SelectedTargetFolder == authorA);
            Assert.Single(roVm.TargetGroups); // 僅剩 ArtistBeta

            // 從右側清單刪除後，左側整個 ArtistAlpha 分類應恢復顯示，且狀態恢復待處理
            roVm.QueuedItems[0].RemoveCommand.Execute(null);
            Assert.Empty(roVm.QueuedItems);
            var restoredAlphaGroup = roVm.TargetGroups.Single(g => g.TargetFolderName == authorA);
            Assert.Equal(2, restoredAlphaGroup.TotalCount);
            Assert.Equal("待處理", restoredAlphaGroup.Items[0].ExecutionStatus);

            // 重新全部勾選並放入清單後測試模擬與唯讀保護
            restoredAlphaGroup.IsGroupChecked = true;
            restoredAlphaGroup.AddCheckedToQueueCommand.Execute(null);
            Assert.Equal(2, roVm.QueueCount);
            Assert.DoesNotContain(roVm.TargetGroups, g => g.TargetFolderName == authorA);

            await roVm.SimulateQueueCommand.ExecuteAsync(null);
            Assert.All(roVm.QueuedItems, q => Assert.Equal("🧪 模擬通過", q.StatusText));
            Assert.False(Directory.Exists(Path.Combine(targetA, work1)));

            await roVm.ExecuteQueueCommand.ExecuteAsync(null);
            Assert.Equal(2, roVm.QueueCount);
            Assert.True(Directory.Exists(Path.Combine(srcDir, work1)));
        }

        // 2. 讀寫模式下：從多個群組加入清單並一鍵全部歸檔
        using (var rwCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false))
        {
            var rwVm = new AutoArchivePageViewModel(rwCtx, runOnUiAsync: ImmediateUiAsync);
            await rwVm.ScanAsync();
            Assert.Empty(rwVm.QueuedItems);

            var alphaGroup = rwVm.TargetGroups.Single(g => g.TargetFolderName == authorA);
            alphaGroup.AddCheckedToQueueCommand.Execute(null);
            var betaGroup = rwVm.TargetGroups.Single(g => g.TargetFolderName == authorB);
            betaGroup.AddCheckedToQueueCommand.Execute(null);

            Assert.Empty(rwVm.TargetGroups);
            Assert.Equal(3, rwVm.QueueCount);
            await rwVm.ExecuteQueueCommand.ExecuteAsync(null);

            Assert.Empty(rwVm.QueuedItems);
            Assert.Empty(rwVm.TargetGroups);
            Assert.True(Directory.Exists(Path.Combine(targetA, work1)));
            Assert.True(Directory.Exists(Path.Combine(targetA, work2)));
            Assert.True(Directory.Exists(Path.Combine(targetB, work3)));
            Assert.False(Directory.Exists(Path.Combine(srcDir, work1)));
            Assert.NotNull(rwCtx.MetaStore.GetAssetId(Path.Combine(targetB, work3)));
        }
    }

    [Fact]
    public async Task AutoArchive_UnmatchedCollapsedByDefault_AndIncrementalQueueRemoveRestore()
    {
        var dataDir = CreateTempDir("IncrData");
        var srcDir = CreateTempDir("IncrSrc");
        var rootDir = CreateTempDir("IncrRoot");

        var authorA = "IncrArtistA";
        var authorB = "IncrArtistB";
        Directory.CreateDirectory(Path.Combine(rootDir, authorA));
        Directory.CreateDirectory(Path.Combine(rootDir, authorB));

        foreach (var name in new[]
        {
            $"[{authorA}] Item 01",
            $"[{authorA}] Item 02",
            $"[{authorB}] Item 03",
            "TotallyUnknown_Work_001"
        })
        {
            var dir = Path.Combine(srcDir, name);
            Directory.CreateDirectory(dir);
            WriteDummyImage(Path.Combine(dir, "001.png"));
        }

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig
            {
                UnorganizedDirectory = srcDir,
                RootDirectory = rootDir,
                MaxScanDepth = 2
            }));
        using (var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            Assert.NotNull(initStore);
        }

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        var vm = new AutoArchivePageViewModel(ctx, runOnUiAsync: ImmediateUiAsync);
        await vm.ScanAsync();

        // 1. 「未匹配」分群在 All 頁籤預設必須為收合狀態，切換到 Unmatched 頁籤時才自動展開
        var unmatchedInAll = vm.TargetGroups.Single(g => g.IsUnmatchedGroup);
        var alphaGroupBefore = vm.TargetGroups.Single(g => g.TargetFolderName == authorA);
        var betaGroupBefore = vm.TargetGroups.Single(g => g.TargetFolderName == authorB);
        Assert.False(unmatchedInAll.IsExpanded);
        Assert.True(alphaGroupBefore.IsExpanded);
        Assert.True(betaGroupBefore.IsExpanded);

        vm.SelectFilterTabCommand.Execute("Unmatched");
        Assert.Single(vm.TargetGroups);
        Assert.True(vm.TargetGroups[0].IsUnmatchedGroup);
        Assert.True(vm.TargetGroups[0].IsExpanded);

        vm.SelectFilterTabCommand.Execute("All");
        Assert.False(vm.TargetGroups.Single(g => g.IsUnmatchedGroup).IsExpanded);

        // 2. 放入清單應採增量移除：不應重建其他群組實例
        alphaGroupBefore = vm.TargetGroups.Single(g => g.TargetFolderName == authorA);
        betaGroupBefore = vm.TargetGroups.Single(g => g.TargetFolderName == authorB);
        var alphaSecondItem = alphaGroupBefore.Items[1];
        alphaSecondItem.IsChecked = false;

        alphaGroupBefore.AddCheckedToQueueCommand.Execute(null);
        Assert.Single(vm.QueuedItems);
        Assert.DoesNotContain(vm.TargetGroups, g => g.TargetFolderName == authorA);
        Assert.Same(betaGroupBefore, vm.TargetGroups.Single(g => g.TargetFolderName == authorB));

        // 3. 執行佇列後應即時移除已完成項目，並增量還原同作者尚餘未勾選的項目
        await vm.ExecuteQueueCommand.ExecuteAsync(null);
        Assert.Empty(vm.QueuedItems);
        Assert.Same(betaGroupBefore, vm.TargetGroups.Single(g => g.TargetFolderName == authorB));
        var restoredAlpha = vm.TargetGroups.Single(g => g.TargetFolderName == authorA);
        Assert.Single(restoredAlpha.Items);
        Assert.Equal(alphaSecondItem.SourceName, restoredAlpha.Items[0].SourceName);
    }

    [Fact]
    public async Task AutoArchive_ConflictPrompt_OverwriteSkipCancel_AndPerItemRemoval()
    {
        var dataDir = CreateTempDir("ConflictData");
        var srcDir = CreateTempDir("ConflictSrc");
        var rootDir = CreateTempDir("ConflictRoot");

        var author1 = "ConflictAuthor1";
        var author2 = "ConflictAuthor2";
        var author3 = "ConflictAuthor3";
        var author4 = "ConflictAuthor4";
        foreach (var a in new[] { author1, author2, author3, author4 })
        {
            Directory.CreateDirectory(Path.Combine(rootDir, a));
        }

        var w1 = $"[{author1}] Conflict Work 1";
        var w2 = $"[{author2}] Conflict Work 2";
        var w3 = $"[{author3}] Conflict Work 3";
        var w4 = $"[{author4}] Clean Work 4";
        foreach (var w in new[] { w1, w2, w3, w4 })
        {
            var src = Path.Combine(srcDir, w);
            Directory.CreateDirectory(src);
            WriteDummyImage(Path.Combine(src, "001.png"));
            await File.WriteAllTextAsync(Path.Combine(src, "marker.txt"), $"new:{w}");
        }

        // 建立既有目標項目以製造前三筆檔名衝突
        foreach (var (author, work) in new[] { (author1, w1), (author2, w2), (author3, w3) })
        {
            var existingDest = Path.Combine(rootDir, author, work);
            Directory.CreateDirectory(existingDest);
            await File.WriteAllTextAsync(Path.Combine(existingDest, "marker.txt"), $"old:{work}");
        }

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig
            {
                UnorganizedDirectory = srcDir,
                RootDirectory = rootDir,
                MaxScanDepth = 2
            }));
        using (var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            Assert.NotNull(initStore);
        }

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        AutoArchivePageViewModel? vmRef = null;
        var seenQueueCountsDuringPrompts = new List<int>();
        var decisions = new Queue<ArchiveConflictDecision>(new[]
        {
            ArchiveConflictDecision.Overwrite,
            ArchiveConflictDecision.Skip,
            ArchiveConflictDecision.CancelRemaining
        });

        var prompt = new FakeConflictPrompt(conflict =>
        {
            seenQueueCountsDuringPrompts.Add(vmRef!.QueuedItems.Count);
            return Task.FromResult(decisions.Dequeue());
        });

        var vm = new AutoArchivePageViewModel(ctx, runOnUiAsync: ImmediateUiAsync, conflictPrompt: prompt);
        vmRef = vm;

        await vm.ScanAsync();
        foreach (var author in new[] { author1, author2, author3, author4 })
        {
            vm.TargetGroups.Single(g => g.TargetFolderName == author).AddCheckedToQueueCommand.Execute(null);
        }

        Assert.Equal(4, vm.QueuedItems.Count);

        await vm.ExecuteQueueCommand.ExecuteAsync(null);

        // 提示第 1 筆時清單尚有 4 筆；第 1 筆覆蓋成功後立刻消失，提示第 2 筆時只剩 3 筆；第 2 筆略過保留，提示第 3 筆仍為 3 筆
        Assert.Equal(new[] { 4, 3, 3 }, seenQueueCountsDuringPrompts);

        // Work1 覆蓋並即時移除
        Assert.DoesNotContain(vm.QueuedItems, q => q.SourceName == w1);
        Assert.Equal($"new:{w1}", await File.ReadAllTextAsync(Path.Combine(rootDir, author1, w1, "marker.txt")));
        Assert.False(Directory.Exists(Path.Combine(srcDir, w1)));

        // Work2 略過保留舊目標
        var q2 = vm.QueuedItems.Single(q => q.SourceName == w2);
        Assert.Equal("⏭️ 已略過", q2.StatusText);
        Assert.Equal($"old:{w2}", await File.ReadAllTextAsync(Path.Combine(rootDir, author2, w2, "marker.txt")));
        Assert.True(Directory.Exists(Path.Combine(srcDir, w2)));

        // Work3 中止，保留舊目標與來源；Work4 未執行仍維持待歸檔
        var q3 = vm.QueuedItems.Single(q => q.SourceName == w3);
        Assert.Equal("⏹️ 已中止", q3.StatusText);
        Assert.Equal($"old:{w3}", await File.ReadAllTextAsync(Path.Combine(rootDir, author3, w3, "marker.txt")));

        var q4 = vm.QueuedItems.Single(q => q.SourceName == w4);
        Assert.Equal("待歸檔", q4.StatusText);
        Assert.False(Directory.Exists(Path.Combine(rootDir, author4, w4)));
        Assert.True(Directory.Exists(Path.Combine(srcDir, w4)));
    }

    [Fact]
    public async Task AutoArchive_GroupHeaderCombinedFilter_FiltersSourceItemsAndTargetDropdown_PreserveSelectionAndAllCheck()
    {
        var dataDir = CreateTempDir("GrpFilterData");
        var srcDir = CreateTempDir("GrpFilterSrc");
        var rootDir = CreateTempDir("GrpFilterRoot");

        var authorMain = "TargetMainAuthor";
        var authorAltOne = "SpecialCircleAlpha";
        var authorAltTwo = "SpecialCircleBeta";
        Directory.CreateDirectory(Path.Combine(rootDir, authorMain));
        Directory.CreateDirectory(Path.Combine(rootDir, authorAltOne));
        Directory.CreateDirectory(Path.Combine(rootDir, authorAltTwo));

        // 在 TargetMainAuthor 群組塞入兩個檔名不同的項目，以及 1 筆未匹配項目
        Directory.CreateDirectory(Path.Combine(srcDir, $"[{authorMain}] AlphaChapter1"));
        Directory.CreateDirectory(Path.Combine(srcDir, $"[{authorMain}] OtherStory2"));
        Directory.CreateDirectory(Path.Combine(srcDir, "TotallyUnknown_Mystery"));

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig
            {
                UnorganizedDirectory = srcDir,
                RootDirectory = rootDir
            }));
        using (var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            Assert.NotNull(initStore);
        }

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        var vm = new AutoArchivePageViewModel(ctx, runOnUiAsync: ImmediateUiAsync);
        await vm.ScanAsync();

        var mainGroup = vm.TargetGroups.Single(g => g.TargetFolderName == authorMain);
        var unmatchedGroup = vm.TargetGroups.Single(g => g.IsUnmatchedGroup);
        Assert.Equal(2, mainGroup.Items.Count);
        Assert.Equal(2, mainGroup.FilteredItems.Count);

        // 1. 單一輸入框搜尋檔名關鍵字「Alpha」：來源項目縮減為 1 筆，且保留「未匹配」與已選目標目錄
        mainGroup.GroupFilterText = "Alpha";
        var visibleItem = Assert.Single(mainGroup.FilteredItems);
        Assert.Contains("AlphaChapter1", visibleItem.SourceName);
        Assert.Contains("顯示 1 / 共 2 筆", mainGroup.SummaryBadgeText);
        Assert.Contains("未匹配", visibleItem.TargetFolderOptions);
        Assert.Contains(authorMain, visibleItem.TargetFolderOptions);
        Assert.Contains(authorAltOne, visibleItem.TargetFolderOptions);

        // 2. 分群左上角全選仍作用於該分群「全部項目」（包含被篩選隱藏的 OtherStory2）
        mainGroup.IsGroupChecked = false;
        Assert.All(mainGroup.Items, i => Assert.False(i.IsChecked));
        mainGroup.IsGroupChecked = true;
        Assert.All(mainGroup.Items, i => Assert.True(i.IsChecked));

        // 3. 輸入只命中目標目錄清單的關鍵字「SpecialCircle」：雖然組內檔名沒命中，仍保留項目顯示並收斂下拉選單
        mainGroup.GroupFilterText = "SpecialCircle";
        Assert.Equal(2, mainGroup.FilteredItems.Count);
        Assert.Contains("目錄選單剩 2 個", mainGroup.SummaryBadgeText);
        foreach (var item in mainGroup.Items)
        {
            Assert.Equal(authorMain, item.SelectedTargetFolder);
            Assert.Equal(4, item.TargetFolderOptions.Count); // 未匹配 + TargetMainAuthor + SpecialCircleAlpha + SpecialCircleBeta
        }

        // 4. 在預設收合的「尚未匹配」群組輸入目錄關鍵字「SpecialCircle」應自動展開，並將下拉選單收斂成 3 個
        Assert.False(unmatchedGroup.IsExpanded);
        unmatchedGroup.GroupFilterText = "SpecialCircle";
        Assert.True(unmatchedGroup.IsExpanded);
        Assert.Single(unmatchedGroup.FilteredItems);
        Assert.Equal(3, unmatchedGroup.Items[0].TargetFolderOptions.Count);

        // 5. 切回平面「項目列表」模式應還原完整下拉選項，切回「依目錄分群」應再套用原本過濾
        vm.SelectViewModeCommand.Execute("Flat");
        Assert.Equal(4, unmatchedGroup.Items[0].TargetFolderOptions.Count);
        vm.SelectViewModeCommand.Execute("Grouped");
        Assert.Equal(3, unmatchedGroup.Items[0].TargetFolderOptions.Count);

        // 6. 檔名與目錄皆無匹配時應呈現空篩選狀態；清除後恢復完整顯示
        mainGroup.GroupFilterText = "ZzzzNotExist_999";
        Assert.Empty(mainGroup.FilteredItems);
        Assert.True(mainGroup.IsGroupFilterEmptyResult);

        mainGroup.ClearGroupFilterCommand.Execute(null);
        Assert.False(mainGroup.HasGroupFilter);
        Assert.Equal(2, mainGroup.FilteredItems.Count);
        Assert.Equal(4, mainGroup.Items[0].TargetFolderOptions.Count);
    }

    [Fact]
    public async Task AutoArchive_FuzzyTargetAutoComplete_NewAuthorSuggestionAndCreation_MovesSafelyAndLearnsRule()
    {
        var dataDir = CreateTempDir("NewAuthData");
        var srcDir = CreateTempDir("NewAuthSrc");
        var rootDir = CreateTempDir("NewAuthRoot");

        var existingOne = "CREAYUS (嵐月)";
        var existingTwo = "Cuvie";
        var existingThree = "Flugel";
        Directory.CreateDirectory(Path.Combine(rootDir, existingOne));
        Directory.CreateDirectory(Path.Combine(rootDir, existingTwo));
        Directory.CreateDirectory(Path.Combine(rootDir, existingThree));

        // 新作者作品 1：從括號可抽出 [BrandNewCircle (FreshArtist)]；作品 2：完全無作者前綴
        var newAuthorZip = Path.Combine(srcDir, "(C105) [BrandNewCircle (FreshArtist)] Debut Book.zip");
        var customNewZip = Path.Combine(srcDir, "Untagged Mystery Album.zip");
        await File.WriteAllBytesAsync(newAuthorZip, new byte[] { 0x50, 0x4B, 0x05, 0x06 });
        await File.WriteAllBytesAsync(customNewZip, new byte[] { 0x50, 0x4B, 0x05, 0x06 });

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig
            {
                UnorganizedDirectory = srcDir,
                RootDirectory = rootDir
            }));
        using (var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            Assert.NotNull(initStore);
        }

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        var vm = new AutoArchivePageViewModel(ctx, runOnUiAsync: ImmediateUiAsync);
        await vm.ScanAsync();

        var unmatchedGroup = Assert.Single(vm.TargetGroups, g => g.IsUnmatchedGroup);
        var debutItem = Assert.Single(unmatchedGroup.Items, i => i.SourceName.Contains("Debut Book"));
        var mysteryItem = Assert.Single(unmatchedGroup.Items, i => i.SourceName.Contains("Untagged Mystery"));

        // 1. 應自動抽出建議新作者「BrandNewCircle (FreshArtist)」，無作者括號者為 null
        Assert.True(debutItem.ShowSuggestedNewAuthorButton);
        Assert.Equal("BrandNewCircle (FreshArtist)", debutItem.SuggestedNewAuthorFolder);
        Assert.False(mysteryItem.ShowSuggestedNewAuthorButton);

        // 2. 模糊搜尋輸入：打 "flgel" 仍可模糊推薦 "Flugel" 且輸入途中不可改寫 SelectedTargetFolder（不跳群組）；打別名 "嵐月" 應命中 "CREAYUS (嵐月)"
        mysteryItem.TargetDraftText = "flgel";
        Assert.Equal("未匹配", mysteryItem.SelectedTargetFolder);
        Assert.Contains(mysteryItem.TargetSuggestions, s => s.FolderName == existingThree);

        mysteryItem.TargetDraftText = "嵐月";
        Assert.Equal(existingOne, mysteryItem.TargetSuggestions[0].FolderName);

        // 3. 輸入尚不存在的新作者資料夾名稱應出現「➕ 新建目標資料夾」，且非法名稱不提供新建、也不會誤提交
        mysteryItem.TargetDraftText = "bad/name*";
        Assert.DoesNotContain(mysteryItem.TargetSuggestions, s => s.Kind == AutoArchiveTargetSuggestionKind.CreateNewFolder);
        Assert.False(mysteryItem.CommitTargetSelection("bad/name*", allowCreateNew: true));
        Assert.Equal("未匹配", mysteryItem.SelectedTargetFolder);

        mysteryItem.TargetDraftText = "CustomNewAuthor";
        var createOption = Assert.Single(mysteryItem.TargetSuggestions, s => s.Kind == AutoArchiveTargetSuggestionKind.CreateNewFolder);
        Assert.Equal("CustomNewAuthor", createOption.FolderName);
        Assert.True(mysteryItem.CommitTargetSelection(createOption, allowCreateNew: true));

        // 確認已搬進新建群組「📁 CustomNewAuthor」並自動勾選，且其他列下拉選單也可見此新目標
        Assert.True(mysteryItem.IsNewTargetFolder);
        Assert.Equal("CustomNewAuthor", mysteryItem.SelectedTargetFolder);
        Assert.Contains(vm.TargetGroups, g => g.TargetFolderName == "CustomNewAuthor");
        Assert.Contains("CustomNewAuthor", debutItem.TargetFolderOptions);

        // 4. 點擊「💡 建議新建」一鍵採用檔名抽出的新作者
        debutItem.ApplySuggestedNewAuthorCommand.Execute(null);
        Assert.Equal("BrandNewCircle (FreshArtist)", debutItem.SelectedTargetFolder);
        Assert.Contains(vm.TargetGroups, g => g.TargetFolderName == "BrandNewCircle (FreshArtist)");

        // 5. 真正執行歸檔：自動在磁碟建立兩個新作者資料夾並搬移，且寫回知識庫下次可自動配對
        await vm.ExecuteArchiveCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(rootDir, "BrandNewCircle (FreshArtist)", Path.GetFileName(newAuthorZip))));
        Assert.True(File.Exists(Path.Combine(rootDir, "CustomNewAuthor", Path.GetFileName(customNewZip))));

        var intel = new MappingIntelligence(Path.Combine(dataDir, "mappings.json"));
        Assert.Equal("BrandNewCircle (FreshArtist)", intel.TryGetMappedFolder("[BrandNewCircle] Second Book.zip"));
    }

    [Fact]
    public void AutoArchivePageView_InstantiatesWithAutoCompleteBoxOnUiThread()
    {
        var dataDir = CreateTempDir("ViewInitData");
        using var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db"));
        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: true);
        var vm = new AutoArchivePageViewModel(ctx, runOnUiAsync: ImmediateUiAsync);

        AvaloniaTestThread.Run(() =>
        {
            var view = new ComicOrganizer.Avalonia.Views.AutoArchivePageView
            {
                DataContext = vm
            };
            Assert.Same(vm, view.DataContext);
        });
    }

    [Fact]
    public async Task AutoArchive_LazySuggestions_SharedFolderOptions_AndDebounce_SkipPerKeystrokeRebuilds()
    {
        var dataDir = CreateTempDir("PerfFilterData");
        var srcDir = CreateTempDir("PerfFilterSrc");
        var rootDir = CreateTempDir("PerfFilterRoot");

        for (int a = 0; a < 20; a++)
        {
            Directory.CreateDirectory(Path.Combine(rootDir, $"Author_{a:D2}"));
        }

        // 塞入 120 個未匹配項目
        for (int i = 0; i < 120; i++)
        {
            var tag = i < 15 ? $"QueryHit_{i:D3}" : $"Other_{i:D3}";
            await File.WriteAllBytesAsync(Path.Combine(srcDir, $"{tag}.zip"), new byte[] { 0x50, 0x4B, 0x05, 0x06 });
        }

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig
            {
                UnorganizedDirectory = srcDir,
                RootDirectory = rootDir
            }));
        using (var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            Assert.NotNull(initStore);
        }

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        var vm = new AutoArchivePageViewModel(ctx, runOnUiAsync: ImmediateUiAsync, filterDebounceMs: 60);
        await vm.ScanAsync();

        var unmatchedGroup = Assert.Single(vm.TargetGroups, g => g.IsUnmatchedGroup);
        Assert.Equal(120, unmatchedGroup.Items.Count);
        // 1. 掃描後所有項目尚未預填彈窗建議（點開才計算）
        Assert.All(unmatchedGroup.Items, i => Assert.Empty(i.TargetSuggestions));

        // 2. 連打字防抖動：快速連續變更時不會立刻同步重篩
        unmatchedGroup.GroupFilterText = "Q";
        unmatchedGroup.GroupFilterText = "Qu";
        unmatchedGroup.GroupFilterText = "QueryHit";
        Assert.Equal(120, unmatchedGroup.FilteredItems.Count);

        // 等待防抖動時間窗過去後只會執行一次最終關鍵字過濾
        await Task.Delay(140);
        Assert.Equal(15, unmatchedGroup.FilteredItems.Count);
        // 2.1 分群篩選期間各項目不得提前建出彈窗建議（避免 120 列同時重繪通知）
        Assert.All(unmatchedGroup.Items, i => Assert.Empty(i.TargetSuggestions));
        // 2.2 共用同一份過濾後目錄清單參考（O(1) 分群運算）
        Assert.Same(unmatchedGroup.Items[0].TargetFolderOptions, unmatchedGroup.Items[1].TargetFolderOptions);

        // 3. 清除為空字時應立即套用，不拖延
        unmatchedGroup.ClearGroupFilterCommand.Execute(null);
        Assert.Equal(120, unmatchedGroup.FilteredItems.Count);
    }

    [Fact]
    public async Task AutoArchive_SubcategoryAuthor_CanonicalMatch_MovesAndCatalogsLeafAuthor()
    {
        var dataDir = CreateTempDir("SubCatVmData");
        var srcDir = CreateTempDir("SubCatVmSrc");
        var rootDir = CreateTempDir("SubCatVmRoot");

        var expectedTargetRel = Path.Combine("_Legacy", "尾崎未來");
        var ozakiTargetDir = Path.Combine(rootDir, expectedTargetRel);
        Directory.CreateDirectory(ozakiTargetDir);
        await File.WriteAllBytesAsync(Path.Combine(ozakiTargetDir, "old_work.zip"), new byte[] { 0x50, 0x4B, 0x05, 0x06 });

        var srcBook1 = Path.Combine(srcDir, "[BEAT-POP (尾崎未来)] 放課後ラブモード2 [DL版]");
        var srcBook2 = Path.Combine(srcDir, "[尾崎未来] 放課後ラブモード [中国翻訳]");
        var srcBook3 = Path.Combine(srcDir, "尾崎未來同人無括號合集.zip");
        Directory.CreateDirectory(srcBook1);
        Directory.CreateDirectory(srcBook2);
        await File.WriteAllBytesAsync(Path.Combine(srcBook1, "01.jpg"), new byte[] { 0xFF, 0xD8, 0xFF });
        await File.WriteAllBytesAsync(Path.Combine(srcBook2, "01.jpg"), new byte[] { 0xFF, 0xD8, 0xFF });
        await File.WriteAllBytesAsync(srcBook3, new byte[] { 0x50, 0x4B, 0x05, 0x06 });

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig
            {
                UnorganizedDirectory = srcDir,
                RootDirectory = rootDir,
                MaxScanDepth = 3,
                MinMatchConfidence = 75.0
            }));
        using (var initStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            Assert.NotNull(initStore);
        }

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        var vm = new AutoArchivePageViewModel(ctx, runOnUiAsync: ImmediateUiAsync);
        await vm.ScanAsync();

        Assert.Equal(2, vm.MatchedCount);
        Assert.Equal(1, vm.UnmatchedCount);

        var ozakiGroup = Assert.Single(vm.TargetGroups, g => g.TargetFolderName == expectedTargetRel);
        Assert.Equal(2, ozakiGroup.Items.Count);
        Assert.All(ozakiGroup.Items, item =>
        {
            Assert.True(item.HasValidDestination);
            Assert.Equal(expectedTargetRel, item.SelectedTargetFolder);
            Assert.True(item.Confidence >= 95.0);
            Assert.True(item.IsChecked);
            Assert.False(item.HasSuggestedNewAuthor);
            Assert.Equal(Path.Combine(rootDir, expectedTargetRel), item.DestinationPath);
        });

        // 驗證下拉搜尋輸入日文「尾崎未来」時第一名即是「_Legacy\尾崎未來」且手動選用後也能一併搬移
        var extraItem = Assert.Single(vm.TargetGroups.Single(g => g.IsUnmatchedGroup).Items);
        extraItem.TargetDraftText = "尾崎未来";
        Assert.NotEmpty(extraItem.TargetSuggestions);
        Assert.Equal(expectedTargetRel, extraItem.TargetSuggestions[0].FolderName);
        Assert.True(extraItem.CommitTargetSelection(extraItem.TargetSuggestions[0]));

        await vm.ExecuteArchiveCommand.ExecuteAsync(null);

        var moved1 = Path.Combine(ozakiTargetDir, "[BEAT-POP (尾崎未来)] 放課後ラブモード2 [DL版]");
        var moved2 = Path.Combine(ozakiTargetDir, "[尾崎未来] 放課後ラブモード [中国翻訳]");
        var moved3 = Path.Combine(ozakiTargetDir, "尾崎未來同人無括號合集.zip");
        Assert.True(Directory.Exists(moved1));
        Assert.True(Directory.Exists(moved2));
        Assert.True(File.Exists(moved3));

        var meta3 = ctx.MetaStore.GetMetadataView(moved3);
        Assert.NotNull(meta3);
        Assert.Equal("尾崎未來", meta3!.AuthorMainName);
        Assert.DoesNotContain(ctx.MetaStore.ListAuthorsByUsage(), a => a.Author.MainName.Contains("_Legacy", StringComparison.OrdinalIgnoreCase));

        var intel = new MappingIntelligence(Path.Combine(dataDir, "mappings.json"));
        Assert.Equal(expectedTargetRel, intel.TryGetMappedFolder("[尾崎未来] 全新作品.zip"));
    }

    [Fact]
    public void SqliteAssetMetaStore_DeferOpen_DelaysFileCreationUntilFirstQuery()
    {
        var tempDir = CreateTempDir("DeferSqlite");
        var dbPath = Path.Combine(tempDir, "cache.db");

        using (var unusedStore = new SqliteAssetMetaStore(dbPath, readOnly: false, deferOpen: true))
        {
            Assert.False(File.Exists(dbPath));
        }

        Assert.False(File.Exists(dbPath));

        using (var lazyStore = new SqliteAssetMetaStore(dbPath, readOnly: false, deferOpen: true))
        {
            Assert.False(File.Exists(dbPath));
            var count = lazyStore.TotalAssetCount();
            Assert.Equal(0, count);
            Assert.True(File.Exists(dbPath));

            lazyStore.UpsertCatalogOnly(new AssetMeta
            {
                Path = Path.Combine(tempDir, "sample.zip"),
                Name = "sample.zip",
                IsFolder = false,
                Mtime = 123
            });
            Assert.Equal(1, lazyStore.TotalAssetCount());
        }
    }

    [Fact]
    public async Task MainWindowViewModel_DeferStartupLoads_DefersHeavyPagesAndInitializesOnActivation()
    {
        var dataDir = CreateTempDir("DeferStartupData");
        var rootDir = CreateTempDir("DeferStartupLib");
        var unorgDir = CreateTempDir("DeferStartupUnorg");

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig
            {
                RootDirectory = rootDir,
                UnorganizedDirectory = unorgDir,
                MaxScanDepth = 4,
                EnableVisualSearch = false
            }));

        var cacheDbPath = Path.Combine(dataDir, "cache.db");
        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        using var mainVm = new MainWindowViewModel(ctx, deferStartupLoads: true);

        // 啟動階段建構主視窗 ViewModel 不得同步開啟或建立 cache.db
        Assert.False(File.Exists(cacheDbPath));
        Assert.Equal(rootDir, mainVm.SettingsPage.RootDirectory);
        Assert.Equal(unorgDir, mainVm.SettingsPage.UnorganizedDirectory);
        Assert.Equal(4, mainVm.SettingsPage.MaxScanDepth);
        Assert.Equal(unorgDir, mainVm.AutoArchivePage.UnorganizedDirectoryText);

        // 尚未切到設定頁前直接儲存設定，仍能完整保留原設定欄位而不遺失
        mainVm.SettingsPage.EnableVisualSearch = true;
        mainVm.SettingsPage.SaveCommand.Execute(null);
        Assert.True(ctx.Config.EnableVisualSearch);
        Assert.Equal(rootDir, ctx.Config.RootDirectory);
        Assert.Equal(unorgDir, ctx.Config.UnorganizedDirectory);
        Assert.Equal(4, ctx.Config.MaxScanDepth);

        // 切頁通知應依需觸發各次要頁面的重初始化，並在背景啟動探索牆載入
        mainVm.NotifyPageShown(mainVm.SettingsPage);
        mainVm.NotifyPageShown(mainVm.VisualSearchPage);
        mainVm.NotifyPageShown(mainVm.AutoArchivePage);
        Assert.True(mainVm.VisualSearchPage.IsFeatureEnabled);
        Assert.NotEmpty(mainVm.SettingsPage.OcrEngineOptions);

        mainVm.StartDeferredInitialization();
        await mainVm.ExplorePage.InitializationTask;
        Assert.True(File.Exists(cacheDbPath));
    }

    [Fact]
    public void StartupHelpers_SkipNativePluginDll_AndResolveParentDataDirectory()
    {
        var parentDir = CreateTempDir("IsolatedLayoutRoot");
        var appDir = Path.Combine(parentDir, "app");
        var pluginSubDir = Path.Combine(parentDir, "plugins", "PaddleOCR");
        var fakeInferDir = Path.Combine(parentDir, "inference");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(pluginSubDir);
        Directory.CreateDirectory(fakeInferDir);

        File.WriteAllText(Path.Combine(parentDir, "appsettings.json"), "{}");
        // 模擬非 .NET 原生 DLL（如 mkldnn.dll / PaddleOCR.dll）不應造成崩潰或誤判為外掛
        File.WriteAllBytes(Path.Combine(pluginSubDir, "fake_native.dll"), [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);

        using var ctx = new AppDataContext(explicitDataDirectory: parentDir, forceReadOnlyDatabase: true);
        Assert.Equal(parentDir, ctx.DataDirectory);

        var engines = SkiaOcrPluginManager.GetAvailableEngines();
        Assert.NotEmpty(engines);
    }

    private sealed class FakeConflictPrompt : IArchiveConflictPrompt
    {
        private readonly Func<ArchiveConflictInfo, Task<ArchiveConflictDecision>> _handler;

        public FakeConflictPrompt(Func<ArchiveConflictInfo, Task<ArchiveConflictDecision>> handler)
        {
            _handler = handler;
        }

        public Task<ArchiveConflictDecision> ConfirmOverwriteAsync(ArchiveConflictInfo conflict)
            => _handler(conflict);
    }
}
