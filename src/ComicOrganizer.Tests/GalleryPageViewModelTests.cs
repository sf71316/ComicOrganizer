using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// Phase 2 W2.1 漫畫庫畫廊與 <c>@</c> 觸發區塊化 DSL 搜尋框單元測試。
/// 使用暫存 SQLite 與目錄，不存取 D:\ComicOrganizer 正式資料。
/// </summary>
public sealed class GalleryPageViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _rootLibDir;
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;
    private readonly FileSystemThumbStore _thumbStore;
    private readonly SkiaCoverService _coverService;

    public GalleryPageViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ComicOrg_GalleryVmTests_{Guid.NewGuid():N}");
        _rootLibDir = Path.Combine(_tempDir, "LibraryRoot");
        Directory.CreateDirectory(_rootLibDir);
        _dbPath = Path.Combine(_tempDir, "cache.db");
        _store = new SqliteAssetMetaStore(_dbPath);
        _thumbStore = new FileSystemThumbStore(Path.Combine(_tempDir, "thumbs"));
        _coverService = new SkiaCoverService(_thumbStore);
    }

    public void Dispose()
    {
        _coverService.Dispose();
        _store.Dispose();
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
    public void DslSearchBox_AtTrigger_TwoStageAutoComplete_TokenizesBlocksAndSupportsBackspaceAndEdit()
    {
        var aFlugel = _store.UpsertAuthor("Flugel");
        _store.AddAuthorAlias(aFlugel, "フリューゲル", "ja");
        var aTakeda = _store.UpsertAuthor("Takeda Hiromitsu");

        var tMaid = _store.UpsertTag("maid", "tag");
        var tNtr = _store.UpsertTag("netorare", "tag");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");
        _store.UpsertTagTranslation(tNtr, "zh-TW", "NTR", "manual");

        SeedWork(Path.Combine(_rootLibDir, "w1.zip"), "Maid Story 1", aFlugel, ReadStatus.Unread, 100, tMaid);
        SeedWork(Path.Combine(_rootLibDir, "w2.zip"), "Maid Story 2", aFlugel, ReadStatus.Completed, 200, tMaid);
        SeedWork(Path.Combine(_rootLibDir, "w3.zip"), "Other Story", aTakeda, ReadStatus.Unread, 150, tNtr);

        var searchVm = new DslSearchBoxViewModel(_store, "zh-TW");

        // 1. 輸入 "@" → 展開第一階段欄位提示清單（@artist:、@tag: 等）
        searchVm.InputText = "@";
        Assert.True(searchVm.IsSuggestionPopupOpen);
        Assert.Contains(searchVm.Suggestions, s => s.IsFieldPrefix && s.Source.Field == SearchField.Artist);
        Assert.Contains(searchVm.Suggestions, s => s.IsFieldPrefix && s.Source.Field == SearchField.Tag);

        // 2. 選取 "@artist:" 欄位 → 輸入框變為 "@artist:" 且無縫切換為 DB 作者清單
        var artistFieldSuggestion = searchVm.Suggestions.First(s => s.IsFieldPrefix && s.Source.Field == SearchField.Artist);
        searchVm.AcceptSuggestion(artistFieldSuggestion);

        Assert.Equal("@artist:", searchVm.InputText);
        Assert.True(searchVm.IsSuggestionPopupOpen);
        Assert.All(searchVm.Suggestions, s => Assert.False(s.IsFieldPrefix));
        Assert.Equal("Flugel", searchVm.Suggestions[0].Source.InsertValue);

        // 3. 測試以日文別名過濾 "@artist:フリュー" 仍能命中 Flugel，並按 Enter/Tab 收斂為區塊 Chip
        searchVm.InputText = "@artist:フリュー";
        Assert.True(searchVm.IsSuggestionPopupOpen);
        var flugelSuggestion = Assert.Single(searchVm.Suggestions);
        Assert.Contains("Flugel", flugelSuggestion.DisplayText);
        Assert.Contains("フリューゲル", flugelSuggestion.DisplayText);

        Assert.True(searchVm.TryHandleEnterOrTab(isTab: true));
        Assert.False(searchVm.IsSuggestionPopupOpen);
        Assert.Equal(string.Empty, searchVm.InputText);
        var block1 = Assert.Single(searchVm.TokenBlocks);
        Assert.Equal(SearchField.Artist, block1.Field);
        Assert.Equal("Flugel", block1.Value);
        Assert.False(block1.IsNegated);
        Assert.Equal("artist:Flugel", searchVm.QueryText);

        // 4. 輸入排除標籤 "-@tag:女" → 命中繁中譯名「女僕 (maid)」並收斂為排除區塊
        searchVm.InputText = "-@tag:女";
        Assert.True(searchVm.IsSuggestionPopupOpen);
        var maidSuggestion = Assert.Single(searchVm.Suggestions);
        Assert.Equal("女僕", maidSuggestion.Source.InsertValue);
        Assert.True(maidSuggestion.Source.Negated);

        searchVm.AcceptSuggestion(maidSuggestion);
        Assert.Equal(2, searchVm.TokenBlocks.Count);
        Assert.True(searchVm.TokenBlocks[1].IsNegated);
        Assert.Equal("-標籤", searchVm.TokenBlocks[1].FieldBadgeText);
        Assert.Equal("artist:Flugel -tag:女僕", searchVm.QueryText);

        // 5. 手動輸入 "@lang:zh "（結尾空白）應自動收斂為第 3 顆區塊
        searchVm.InputText = "@lang:zh ";
        Assert.Equal(3, searchVm.TokenBlocks.Count);
        Assert.Equal(SearchField.Language, searchVm.TokenBlocks[2].Field);
        Assert.Equal("artist:Flugel -tag:女僕 lang:zh", searchVm.QueryText);

        // 6. 輸入框為空時按 Backspace 應移除最後一顆區塊
        Assert.True(searchVm.TryHandleBackspaceWhenInputEmpty());
        Assert.Equal(2, searchVm.TokenBlocks.Count);
        Assert.Equal("artist:Flugel -tag:女僕", searchVm.QueryText);

        // 7. 點擊第一顆區塊的 EditCommand 應將其退回輸入框 "@artist:Flugel" 供修改
        searchVm.TokenBlocks[0].EditCommand.Execute(null);
        Assert.Single(searchVm.TokenBlocks);
        Assert.Equal("@artist:Flugel", searchVm.InputText);
    }

    [Fact]
    public void DslSearchBox_DirectAtKeyword_SuggestsCrossCategoryAuthorsAndTags()
    {
        var aFlugel = _store.UpsertAuthor("Flugel");
        var tMaid = _store.UpsertTag("maid", "tag");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");
        SeedWork(Path.Combine(_rootLibDir, "w1.zip"), "Work 1", aFlugel, ReadStatus.Unread, 100, tMaid);

        // 打 "@女僕"（未打 tag:）應直接推薦資料庫中的「女僕」標籤區塊
        var items = DslSuggestionService.GetSuggestions(_store, "@女僕", "zh-TW");
        var directTag = Assert.Single(items, x => x.Kind == DslSuggestionKind.TokenValue && x.Field == SearchField.Tag);
        Assert.Equal("女僕", directTag.InsertValue);
    }

    [Fact]
    public async Task GalleryPage_FolderNavigation_VirtualLibraries_DslSearch_FuzzyRecommendation_AndSorting()
    {
        var subFolder = Path.Combine(_rootLibDir, "AuthorA_Folder");
        Directory.CreateDirectory(subFolder);

        var a1 = _store.UpsertAuthor("Flugel", subFolder);
        var a2 = _store.UpsertAuthor("Shindol");
        var tMaid = _store.UpsertTag("maid", "tag");
        var tGlasses = _store.UpsertTag("glasses", "tag");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");
        _store.UpsertTagTranslation(tGlasses, "zh-TW", "眼鏡", "manual");

        // 在根目錄註冊子資料夾與 2 部作品，在子資料夾註冊 1 部作品
        _store.UpsertCatalogOnly(new AssetMeta
        {
            Path = subFolder,
            Name = "AuthorA_Folder",
            IsFolder = true,
            FileSize = 0,
            Mtime = 1000,
            LastScanned = 1000,
        });

        string rootWork1 = Path.Combine(_rootLibDir, "alpha.zip");
        string rootWork2 = Path.Combine(_rootLibDir, "beta.zip");
        string subWork1 = Path.Combine(subFolder, "inner.zip");

        SeedWork(rootWork1, "Alpha Maid", a1, ReadStatus.Unread, sizeBytes: 5000, tMaid);
        SeedWork(rootWork2, "Beta Glasses", a2, ReadStatus.Completed, sizeBytes: 1000, tGlasses);
        SeedWork(subWork1, "Inner Flugel Work", a1, ReadStatus.Reading, sizeBytes: 3000, tMaid, tGlasses);

        _store.CreateVirtualLibrary("我的最愛女僕", "tag:女僕", null);

        var fakeLauncher = new FakeLauncher();
        using var vm = new GalleryPageViewModel(
            _store,
            _coverService,
            rootDirectory: _rootLibDir,
            externalLauncher: fakeLauncher,
            runOnUiAsync: static action => { action(); return Task.CompletedTask; },
            autoLoad: false);

        await vm.ReloadAllAsync();

        // 1. 根目錄應包含 1 個子資料夾（排最前）+ 2 部作品 = 3 項
        Assert.Equal(3, vm.Items.Count);
        Assert.True(vm.Items[0].IsFolder);
        Assert.True(vm.Items[0].IsAuthorFolder);
        Assert.Equal("AuthorA_Folder", vm.Items[0].Name);

        // 2. 點擊資料夾項目 → 進入子資料夾，麵包屑變為 2 層且可返回上層
        vm.Items[0].ActivateCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.CurrentFolderPath == subFolder && vm.Items.Count == 1);
        Assert.Equal("Inner Flugel Work", vm.Items[0].DisplayTitle);
        Assert.True(vm.CanNavigateUpFolder);
        Assert.Equal(2, vm.Breadcrumbs.Count);

        // 點擊作品 → 委派給 IExternalLauncher 開啟
        vm.Items[0].ActivateCommand.Execute(null);
        Assert.Single(fakeLauncher.OpenedPaths);
        Assert.Equal(subWork1, fakeLauncher.OpenedPaths[0]);

        // 返回上層回根目錄
        vm.NavigateUpFolderCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.CurrentFolderPath == _rootLibDir && vm.Items.Count == 3);

        // 3. 切換左側欄至「👤 依作者」虛擬庫 → 自動選中第一位作者 Flugel（共 2 部作品）
        vm.SelectAuthorsSidebarCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.IsAuthorsSidebarMode && vm.SidebarNodes.Count == 2);
        Assert.Equal(2, vm.Items.Count);

        // 4. 切換左側欄至「⭐ 我的閱讀清單」→ 命中自訂虛擬庫「我的最愛女僕」（2 部作品）
        vm.SelectMyLibrariesSidebarCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.IsMyLibrariesSidebarMode && vm.SidebarNodes.Count == 1);
        Assert.Equal(2, vm.Items.Count);

        // 5. 測試透過 @artist: 拼錯字觸發 FuzzySharp「您是不是要找」建議，點擊後自動修正並命中
        await vm.ApplyExternalDslQueryAsync("@artist:Flugle");
        Assert.Empty(vm.Items);
        Assert.True(vm.HasFuzzyRecommendations);
        Assert.Contains(vm.FuzzyRecommendations, r => r.DisplayText == "artist:Flugel");

        vm.FuzzyRecommendations.First(r => r.DisplayText == "artist:Flugel").ApplyCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.Items.Count == 2);
        Assert.Single(vm.SearchBox.TokenBlocks);
        Assert.Equal("Flugel", vm.SearchBox.TokenBlocks[0].Value);

        // 6. 測試閱讀狀態過濾與排序方向切換
        vm.SelectedReadFilterOption = vm.ReadFilterOptions.First(o => o.Filter == GalleryReadFilter.Unread);
        Assert.Single(vm.Items);
        Assert.Equal("Alpha Maid", vm.Items[0].DisplayTitle);
    }

    [Fact]
    public async Task GalleryPage_LoadsAllFilteredItemsAtOnce_WithoutPaging()
    {
        // 清單已虛擬化，超過舊的首批 30 筆也要一次全部放進 Items
        var author = _store.UpsertAuthor("BulkArtist");
        for (int i = 0; i < 75; i++)
        {
            SeedWork(Path.Combine(_rootLibDir, $"bulk_{i:D3}.zip"), $"Bulk Work {i:D3}", author, ReadStatus.Unread, 1000 + i);
        }

        using var vm = new GalleryPageViewModel(
            _store,
            _coverService,
            rootDirectory: _rootLibDir,
            externalLauncher: new FakeLauncher(),
            runOnUiAsync: static action => { action(); return Task.CompletedTask; },
            autoLoad: false);

        await vm.ReloadAllAsync();
        await WaitUntilAsync(() => !vm.IsLoading && vm.Items.Count == 75);

        Assert.Equal(75, vm.TotalMatchingItems);
        Assert.Equal(vm.TotalMatchingItems, vm.CurrentItemCount);

        // 位置膠囊只顯示捲動進度，不再有「已載入 N 筆」
        vm.UpdateViewportRange(1, 20, offsetY: 0);
        Assert.Equal("第 1–20 筆 / 共 75 筆", vm.PositionBadgeTitle);
        Assert.DoesNotContain("已載入", vm.PositionBadgeDetail);

        // 套用過濾後同樣一次全部放入
        vm.LocalFilterKeyword = "Bulk Work 01";
        await WaitUntilAsync(() => vm.Items.Count == 10);
        Assert.Equal(10, vm.TotalMatchingItems);
    }

    [Fact]
    public async Task GalleryPageView_InstantiatesAndSyncsCardSizeResourcesAcrossViewModes()
    {
        var a1 = _store.UpsertAuthor("TemplateArtist");
        SeedWork(Path.Combine(_rootLibDir, "t1.zip"), "Template Work", a1, ReadStatus.Unread, 1000);

        using var vm = new GalleryPageViewModel(
            _store,
            _coverService,
            rootDirectory: _rootLibDir,
            runOnUiAsync: static action => { action(); return Task.CompletedTask; },
            autoLoad: false);
        await vm.ReloadAllAsync();

        // Avalonia 12 起控制項（如 Transitions）會檢查 Dispatcher.UIThread 存取權，需在專用 UI 測試執行緒上建立與操作 View
        AvaloniaTestThread.Run(() =>
        {
            var view = new ComicOrganizer.Avalonia.Views.GalleryPageView
            {
                DataContext = vm,
            };

            Assert.Equal(226.0, view.Resources["GalleryCardWidth"]);
            Assert.Equal(264.0, view.Resources["GalleryCardCoverHeight"]);
            Assert.Equal(206.0, view.Resources["GalleryCardCoverWidth"]);

            vm.SelectMediumCardViewCommand.Execute(null);
            Assert.Equal(182.0, view.Resources["GalleryCardWidth"]);
            Assert.Equal(212.0, view.Resources["GalleryCardCoverHeight"]);
            Assert.Equal(162.0, view.Resources["GalleryCardCoverWidth"]);

            vm.SelectSmallCardViewCommand.Execute(null);
            Assert.Equal(148.0, view.Resources["GalleryCardWidth"]);
            Assert.Equal(168.0, view.Resources["GalleryCardCoverHeight"]);
            Assert.Equal(128.0, view.Resources["GalleryCardCoverWidth"]);
        });
    }

    [Fact]
    public async Task DetailDrawer_InspectsWorkAuthorAndFolder_AndSupportsWritableMetadataTagsAndLibraryEdits()
    {
        var authorDir = Path.Combine(_rootLibDir, "FlugelFolder");
        var plainDir = Path.Combine(_rootLibDir, "MiscCategory");
        Directory.CreateDirectory(authorDir);
        Directory.CreateDirectory(plainDir);

        var aFlugel = _store.UpsertAuthor("Flugel", authorDir);
        var tMaid = _store.UpsertTag("maid", "tag");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");

        _store.UpsertCatalogOnly(new AssetMeta { Path = authorDir, Name = "FlugelFolder", IsFolder = true, Mtime = 1000, LastScanned = 1000 });
        _store.UpsertCatalogOnly(new AssetMeta { Path = plainDir, Name = "MiscCategory", IsFolder = true, Mtime = 1000, LastScanned = 1000 });

        string workPath = Path.Combine(_rootLibDir, "editable.zip");
        SeedWork(workPath, "Original Title", aFlugel, ReadStatus.Unread, 2048, tMaid);

        using var vm = new GalleryPageViewModel(
            _store,
            _coverService,
            rootDirectory: _rootLibDir,
            runOnUiAsync: static action => { action(); return Task.CompletedTask; },
            autoLoad: false,
            isReadOnlyDatabase: false);

        await vm.ReloadAllAsync();

        // 1. 檢視一般資料夾
        var plainItem = vm.Items.First(i => i.Path == plainDir);
        plainItem.InspectDetailCommand.Execute(null);
        Assert.True(vm.DetailDrawer.IsOpen);
        Assert.True(vm.DetailDrawer.IsFolderKind);

        // 2. 檢視作者資料夾
        var authorFolderItem = vm.Items.First(i => i.Path == authorDir);
        authorFolderItem.InspectDetailCommand.Execute(null);
        Assert.True(vm.DetailDrawer.IsAuthorKind);
        Assert.Equal("Flugel", vm.DetailDrawer.AuthorMainName);
        Assert.Equal("📁 進入此資料夾", vm.DetailDrawer.OpenTargetButtonText);

        // 3. 檢視與編輯作品（閱讀狀態 + Metadata + 標籤 + 自訂虛擬庫）
        var workItem = vm.Items.First(i => i.Path == workPath);
        workItem.InspectDetailCommand.Execute(null);
        Assert.True(vm.DetailDrawer.IsWorkKind);
        Assert.Equal("📖 開啟閱讀", vm.DetailDrawer.OpenTargetButtonText);
        Assert.Equal("Original Title", vm.DetailDrawer.EditTitle);
        Assert.Single(vm.DetailDrawer.WorkTags);

        vm.DetailDrawer.SetReadingStatusCommand.Execute(null);
        Assert.Equal(ReadStatus.Reading, _store.GetReadStatus(workPath));

        vm.DetailDrawer.EditTitle = "Edited Title";
        vm.DetailDrawer.EditSeries = "My Series";
        vm.DetailDrawer.SaveWorkMetadataCommand.Execute(null);

        var updatedDetail = _store.GetWorkDetail(workPath)!;
        Assert.Equal("Edited Title", updatedDetail.Title);
        Assert.Equal("My Series", updatedDetail.Series);
        Assert.True(updatedDetail.ManuallyEdited);

        vm.DetailDrawer.NewTagInput = "glasses";
        vm.DetailDrawer.AddWorkTagCommand.Execute(null);
        Assert.Equal(2, vm.DetailDrawer.WorkTags.Count);

        vm.DetailDrawer.WorkTags[0].RemoveCommand.Execute(null);
        Assert.Single(vm.DetailDrawer.WorkTags);

        // 4. 儲存目前 DSL 為自訂閱讀清單、切換清單納入、並測試刪除清單
        await vm.ApplyExternalDslQueryAsync("tag:glasses");
        vm.NewVirtualLibraryName = "眼鏡精選";
        vm.SaveCurrentSearchAsVirtualLibraryCommand.Execute(null);
        var libs = _store.ListVirtualLibraries();
        var createdLib = Assert.Single(libs);
        Assert.Equal("眼鏡精選", createdLib.Name);

        vm.DetailDrawer.InspectItem(workItem, openDrawer: true);
        var libToggle = Assert.Single(vm.DetailDrawer.VirtualLibraryMemberships);
        Assert.False(libToggle.IsMember);
        libToggle.ToggleCommand.Execute(null);
        Assert.True(libToggle.IsMember);
        Assert.Single(_store.ListManualVirtualLibraryWorks(createdLib.Id));

        vm.SelectMyLibrariesSidebarCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.IsMyLibrariesSidebarMode && vm.SidebarNodes.Count == 1);
        Assert.True(vm.CanDeleteSelectedVirtualLibrary);
        vm.DeleteSelectedVirtualLibraryCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading);
        Assert.Empty(_store.ListVirtualLibraries());
    }

    [Fact]
    public async Task DetailDrawer_ReadOnlyDatabaseMode_BlocksAllWritesAndKeepsDbUnchanged()
    {
        var aFlugel = _store.UpsertAuthor("Flugel");
        var tMaid = _store.UpsertTag("maid", "tag");
        string workPath = Path.Combine(_rootLibDir, "readonly.zip");
        SeedWork(workPath, "Protected Title", aFlugel, ReadStatus.Unread, 2048, tMaid);
        var existingLibId = _store.CreateVirtualLibrary("Existing List", "tag:maid", null);

        using var vm = new GalleryPageViewModel(
            _store,
            _coverService,
            rootDirectory: _rootLibDir,
            runOnUiAsync: static action => { action(); return Task.CompletedTask; },
            autoLoad: false,
            isReadOnlyDatabase: true);

        await vm.ReloadAllAsync();

        Assert.True(vm.IsReadOnlyDatabase);
        Assert.True(vm.DetailDrawer.IsReadOnlyDatabase);
        Assert.False(vm.DetailDrawer.IsWritable);

        var workItem = Assert.Single(vm.Items);
        workItem.InspectDetailCommand.Execute(null);

        vm.DetailDrawer.SetCompletedStatusCommand.Execute(null);
        Assert.Equal(ReadStatus.Unread, _store.GetReadStatus(workPath));
        Assert.Contains("唯讀", vm.DetailDrawer.StatusFeedbackText);

        vm.DetailDrawer.EditTitle = "Hacked Title";
        vm.DetailDrawer.SaveWorkMetadataCommand.Execute(null);
        Assert.Equal("Protected Title", _store.GetWorkDetail(workPath)!.Title);

        vm.DetailDrawer.NewTagInput = "blocked_tag";
        vm.DetailDrawer.AddWorkTagCommand.Execute(null);
        Assert.Single(_store.GetWorkDetail(workPath)!.Tags);

        vm.DetailDrawer.VirtualLibraryMemberships[0].ToggleCommand.Execute(null);
        Assert.Empty(_store.ListManualVirtualLibraryWorks(existingLibId));

        await vm.ApplyExternalDslQueryAsync("tag:maid");
        vm.NewVirtualLibraryName = "Should Not Create";
        vm.SaveCurrentSearchAsVirtualLibraryCommand.Execute(null);
        Assert.Single(_store.ListVirtualLibraries());
    }

    [Fact]
    public void SystemExternalLauncher_BuildExplorerArguments_OpensDirectoryDirectlyAndSelectsFile()
    {
        Assert.Equal(
            "\"E:\\H\\Comic\\きのこのみ (kino)\"",
            SystemExternalLauncher.BuildExplorerArguments("E:\\H\\Comic\\きのこのみ (kino)\\", isDirectory: true));

        Assert.Equal(
            "/select,\"E:\\H\\Comic\\work.zip\"",
            SystemExternalLauncher.BuildExplorerArguments("E:\\H\\Comic\\work.zip", isDirectory: false));
    }

    private void SeedWork(
        string fullPath,
        string title,
        long authorId,
        ReadStatus status,
        long sizeBytes,
        params long[] tagIds)
    {
        var fileName = Path.GetFileName(fullPath);
        _store.Upsert(new AssetMeta
        {
            Path = fullPath,
            Name = fileName,
            IsFolder = false,
            FileSize = sizeBytes,
            Mtime = 2000,
            LastScanned = 2000,
        });

        var assetId = _store.GetAssetId(fullPath)!.Value;
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = assetId,
            AuthorId = authorId,
            Title = title,
            Language = "zh",
            PageCount = 24,
            ParsedAt = 2000,
        });
        _store.SetReadStatus(fullPath, status);

        foreach (var tid in tagIds)
        {
            _store.AssociateTag(assetId, tid);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 4000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"條件未於 {timeoutMs}ms 內達成。");
            }
            await Task.Delay(15);
        }
    }

    private sealed class FakeLauncher : IExternalLauncher
    {
        public List<string> OpenedPaths { get; } = [];
        public List<string> RevealedPaths { get; } = [];

        public bool OpenWithDefaultApp(string path, out string? errorMessage)
        {
            OpenedPaths.Add(path);
            errorMessage = null;
            return true;
        }

        public bool RevealInFileExplorer(string path, out string? errorMessage)
        {
            RevealedPaths.Add(path);
            errorMessage = null;
            return true;
        }
    }
}
