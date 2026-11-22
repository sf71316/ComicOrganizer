using Avalonia.Skia;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// Phase 1 W1.2 探索牆 ViewModels 與 MainWindowViewModel 接線單元測試。
/// 全數使用 Path.GetTempPath() 暫存資料庫與縮圖目錄，不依賴 D:\ComicOrganizer 與 Avalonia 視窗啟動。
/// </summary>
public class ExplorePageViewModelTests : IDisposable
{
    private static readonly object AvaloniaInitLock = new();
    private static bool _avaloniaSkiaInitialized;

    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;
    private readonly FileSystemThumbStore _thumbStore;
    private readonly SkiaCoverService _coverService;

    public ExplorePageViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_explore_vm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "cache.db");
        _store = new SqliteAssetMetaStore(_dbPath);
        _thumbStore = new FileSystemThumbStore(Path.Combine(_tempDir, "thumbs"));
        _coverService = new SkiaCoverService(_thumbStore);
    }

    public void Dispose()
    {
        _coverService.Dispose();
        _store.Dispose();
        SqliteConnection.ClearAllPools();
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

    private long SeedWork(
        string fileName,
        string title,
        long? authorId,
        string? thumbKey = null,
        ReadStatus status = ReadStatus.Unread,
        params long[] tagIds)
    {
        var path = Path.Combine(_tempDir, fileName);
        _store.Upsert(new AssetMeta
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsFolder = false,
            FileSize = 1024,
            Mtime = 1000,
            CoverThumbKey = thumbKey,
            CoverWidth = thumbKey != null ? 330 : null,
            CoverHeight = thumbKey != null ? 460 : null,
            LastScanned = 1000,
            LastAccessed = 1000,
        });

        var assetId = _store.GetAssetId(path)!.Value;
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = assetId,
            AuthorId = authorId,
            Title = title,
            ReadStatus = status,
            ReadCount = status == ReadStatus.Unread ? 0 : 1,
            ParsedAt = 1000,
        });

        foreach (var tid in tagIds)
        {
            _store.AssociateTag(assetId, tid);
        }

        return assetId;
    }

    private ExplorePageViewModel CreateVm(
        bool autoLoad = false,
        TimeSpan? debounceDelay = null,
        IExternalLauncher? externalLauncher = null)
    {
        var svc = new ExploreWallService(_store);
        return new ExplorePageViewModel(
            svc,
            _coverService,
            toastManager: null,
            autoLoad: autoLoad,
            searchDebounceDelay: debounceDelay ?? TimeSpan.FromMilliseconds(30),
            externalLauncher: externalLauncher);
    }

    [Fact]
    public async Task AuthorCards_LimitTopTagsTo3_ComputeOverflowAndSummary()
    {
        var authorId = _store.UpsertAuthor("Flugel");
        _store.AddAuthorAlias(authorId, "フューゲル", "ja");
        _store.AddAuthorAlias(authorId, "飛鳥", "zh-TW");

        var t1 = _store.UpsertTag("stockings", "tag");
        var t2 = _store.UpsertTag("maid", "tag");
        var t3 = _store.UpsertTag("glasses", "tag");
        var t4 = _store.UpsertTag("catgirl", "tag");
        var t5 = _store.UpsertTag("schoolgirl uniform", "tag");
        _store.UpsertTagTranslation(t1, "zh-TW", "絲襪", "manual");
        _store.UpsertTagTranslation(t2, "zh-TW", "女僕", "manual");

        SeedWork("w1.zip", "Maid 1", authorId, "k1", ReadStatus.Unread, t1, t2, t3, t4, t5);
        SeedWork("w2.zip", "Maid 2", authorId, "k2", ReadStatus.Reading, t1, t2, t3);
        SeedWork("w3.zip", "Maid 3", authorId, "k3", ReadStatus.Completed, t1);

        using var vm = CreateVm(autoLoad: true);
        await vm.InitializationTask;

        Assert.True(vm.IsAuthorMode);
        Assert.False(vm.IsThemeWallMode);
        Assert.True(vm.IsDeckCoverStyle);
        Assert.False(vm.IsSingleCoverStyle);

        var card = Assert.Single(vm.AuthorCards);
        Assert.Equal("Flugel", card.MainName);
        Assert.Equal("フューゲル / 飛鳥", card.AliasesDisplay);
        Assert.True(card.HasAliases);
        Assert.Equal(3, card.TotalWorks);
        Assert.Equal(1, card.UnreadWorks);
        Assert.Equal(1, card.ReadingWorks);
        Assert.Equal(1, card.CompletedWorks);
        Assert.Equal(5, card.DistinctTagCount);
        Assert.Equal(3, card.TopDisplayTags.Count);
        Assert.Equal(2, card.OverflowTagCount);
        Assert.True(card.HasOverflowTags);
        Assert.Equal("+2", card.OverflowTagText);
        Assert.Equal("3 本 · 1 未讀", card.TotalSummaryText);
        Assert.Equal(3, card.RepresentativeCount);
    }

    [Fact]
    public async Task CoverStyle_AndMainAndSubTabs_SwitchStatesAndLoadCorrespondingCards()
    {
        var authorId = _store.UpsertAuthor("ArtistA");
        var tMaid = _store.UpsertTag("maid", "tag");
        var tGlasses = _store.UpsertTag("glasses", "tag");
        var tTouhou = _store.UpsertTag("touhou project", "parody");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");
        _store.UpsertTagTranslation(tGlasses, "zh-TW", "眼鏡", "manual");
        _store.UpsertTagTranslation(tTouhou, "zh-TW", "東方Project", "manual");

        SeedWork("a1.zip", "Work 1", authorId, "ka1", ReadStatus.Unread, tMaid, tGlasses, tTouhou);
        SeedWork("a2.zip", "Work 2", authorId, "ka2", ReadStatus.Completed, tMaid, tGlasses);

        using var vm = CreateVm(autoLoad: true);
        await vm.InitializationTask;

        // 雙封面模式切換（僅 Deck / Single）
        Assert.True(vm.IsDeckCoverStyle);
        vm.ToggleCoverStyleCommand.Execute(null);
        Assert.True(vm.IsSingleCoverStyle);
        Assert.False(vm.IsDeckCoverStyle);
        vm.SelectDeckCoverStyleCommand.Execute(null);
        Assert.True(vm.IsDeckCoverStyle);

        // 切換至 主題・原作牆 → 內容主題
        vm.SelectMainTabCommand.Execute(ExploreMainTab.ThemeParodyWall);
        await WaitUntilAsync(() => vm.IsThemeWallMode && !vm.IsLoading && vm.ThemeCards.Count > 0);
        Assert.True(vm.IsContentThemeSubTabActive);
        Assert.Contains(vm.ThemeCards, c => c.TagId == tMaid && c.DisplayName == "女僕");

        // 切換至 原作・角色子頁籤
        vm.SelectThemeSubTabCommand.Execute(ExploreThemeCategory.ParodiesAndCharacters);
        await WaitUntilAsync(() => vm.IsParodySubTabActive && !vm.IsLoading && vm.ThemeCards.Any(c => c.TagId == tTouhou));
        var parodyCard = Assert.Single(vm.ThemeCards);
        Assert.Equal("東方Project", parodyCard.DisplayName);
        Assert.Equal("touhou project", parodyCard.OriginalName);

        // 切換至 熱門雙組合子頁籤
        vm.SelectHotCombosSubTabCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsHotComboSubTabActive && !vm.IsLoading && vm.ComboCards.Count > 0);
        var comboCard = Assert.Single(vm.ComboCards);
        Assert.Equal(2, comboCard.WorkCount);
        Assert.Contains("×", comboCard.PairTitle);
    }

    [Fact]
    public async Task SpotlightAndPickerTags_SupportMultiTagAndFilteringAndClear()
    {
        var aAlpha = _store.UpsertAuthor("Alpha");
        var aBeta = _store.UpsertAuthor("Beta");
        var tMaid = _store.UpsertTag("maid", "tag");
        var tGlasses = _store.UpsertTag("glasses", "tag");
        var tParody = _store.UpsertTag("blue archive", "parody");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");
        _store.UpsertTagTranslation(tGlasses, "zh-TW", "眼鏡", "manual");
        _store.UpsertTagTranslation(tParody, "zh-TW", "蔚藍檔案", "manual");

        SeedWork("p1.zip", "Story 1", aAlpha, "kp1", ReadStatus.Unread, tMaid, tGlasses);
        SeedWork("p2.zip", "Story 2", aBeta, "kp2", ReadStatus.Unread, tMaid, tParody);

        using var vm = CreateVm(autoLoad: true);
        await vm.InitializationTask;
        Assert.Equal(2, vm.AuthorCards.Count);

        // 打開玻璃彈窗挑選器並依分類與關鍵字過濾
        vm.ToggleTagPickerCommand.Execute(null);
        Assert.True(vm.IsTagPickerOpen);
        await WaitUntilAsync(() => vm.PickerTags.Count == 3);

        vm.SelectPickerCategoryCommand.Execute("parody_character");
        await WaitUntilAsync(() => vm.PickerTags.Count == 1 && vm.PickerTags[0].TagId == tParody);

        vm.PickerCategoryOptions[0].SelectCommand.Execute(null); // "all"
        vm.TagPickerSearchText = "眼鏡";
        await WaitUntilAsync(() => vm.PickerTags.Count == 1 && vm.PickerTags[0].TagId == tGlasses);

        // 勾選 眼鏡 → 應只剩 Alpha（且不產生迴圈通知）
        vm.PickerTags[0].ToggleCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.SelectedFilterCount == 1 && vm.AuthorCards.Count == 1);
        Assert.True(vm.HasSelectedTags);
        Assert.Equal("Alpha", vm.AuthorCards[0].MainName);
        Assert.Equal(tGlasses, vm.SpotlightTags[0].TagId);
        Assert.True(vm.SpotlightTags[0].IsSelected);
        Assert.Single(vm.SelectedFilterTags);

        // 清除全部篩選
        await vm.ClearTagFiltersCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.SelectedFilterCount == 0 && vm.AuthorCards.Count == 2);
        Assert.False(vm.HasSelectedTags);
        Assert.Empty(vm.SelectedFilterTags);
    }

    [Fact]
    public async Task SortUnreadSearchDebounceAndRefresh_UpdateWallCorrectly()
    {
        var aAlpha = _store.UpsertAuthor("Alpha");
        var aBeta = _store.UpsertAuthor("Beta");
        var aGamma = _store.UpsertAuthor("Gamma");

        // Alpha: 1 本已讀；Beta: 3 本全未讀；Gamma: 2 本 (1 未讀)
        SeedWork("s1.zip", "Alpha Story", aAlpha, "ks1", ReadStatus.Completed);
        SeedWork("s2.zip", "Beta Story 1", aBeta, "ks2", ReadStatus.Unread);
        SeedWork("s3.zip", "Beta Story 2", aBeta, "ks3", ReadStatus.Unread);
        SeedWork("s4.zip", "Beta Story 3", aBeta, "ks4", ReadStatus.Unread);
        SeedWork("s5.zip", "Gamma Story 1", aGamma, "ks5", ReadStatus.Unread);
        SeedWork("s6.zip", "Gamma Story 2", aGamma, "ks6", ReadStatus.Completed);

        using var vm = CreateVm(autoLoad: true, debounceDelay: TimeSpan.FromMilliseconds(40));
        await vm.InitializationTask;
        Assert.Equal(new[] { "Beta", "Gamma", "Alpha" }, vm.AuthorCards.Select(c => c.MainName));

        // 僅看未讀
        vm.UnreadOnly = true;
        await WaitUntilAsync(() => !vm.IsLoading && vm.AuthorCards.Count == 2);
        Assert.DoesNotContain(vm.AuthorCards, c => c.MainName == "Alpha");

        // 切換名稱 A-Z 排序
        vm.UnreadOnly = false;
        vm.SelectedSortOption = vm.SortOptions.First(o => o.Mode == ExploreSortMode.NameAsc);
        await WaitUntilAsync(() => !vm.IsLoading && vm.AuthorCards.Count == 3 && vm.AuthorCards[0].MainName == "Alpha");
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, vm.AuthorCards.Select(c => c.MainName));

        // 搜尋防抖
        vm.SearchKeyword = "Al";
        vm.SearchKeyword = "Gamma";
        await WaitUntilAsync(() => !vm.IsLoading && vm.AuthorCards.Count == 1 && vm.AuthorCards[0].MainName == "Gamma");

        // 清除搜尋 + 隨機靈感再次洗牌
        vm.ClearSearchCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.AuthorCards.Count == 3);
        await vm.ShuffleAgainCommand.ExecuteAsync(null);
        Assert.True(vm.IsRandomSortActive);
        Assert.Equal(3, vm.AuthorCards.Count);

        // 新增作者後按 RefreshCommand 應清快取並讀出新作者
        var aDelta = _store.UpsertAuthor("Delta");
        SeedWork("s7.zip", "Delta Story", aDelta, "ks7", ReadStatus.Unread);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.AuthorCards.Count);
        Assert.Contains(vm.AuthorCards, c => c.MainName == "Delta");
    }

    [Fact]
    public async Task EnsureCoversLoadedAsync_DecodesOnceAndPopulatesCardCoverBitmap()
    {
        EnsureAvaloniaSkiaBackend();

        var srcPng = Path.Combine(_tempDir, "cover_src.png");
        WriteSolidPng(srcPng, 400, 600, new SKColor(60, 140, 240));
        var jpegBytes = await _coverService.ExtractCoverJpegBytesAsync(srcPng);
        Assert.NotNull(jpegBytes);

        const string thumbKey = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
        _thumbStore.Write(thumbKey, jpegBytes!);

        var authorId = _store.UpsertAuthor("CoverAuthor");
        SeedWork("cover_work.zip", "Cover Work", authorId, thumbKey, ReadStatus.Unread);

        using var vm = CreateVm(autoLoad: true);
        await vm.InitializationTask;

        var card = Assert.Single(vm.AuthorCards);
        await WaitUntilAsync(() => card.HasCover1);

        // 重複呼叫 / 併發呼叫 EnsureCoversLoadedAsync 應命中同一張快取 Bitmap 不重覆解碼
        var firstBitmap = card.Cover1;
        Assert.NotNull(firstBitmap);
        await Task.WhenAll(
            card.EnsureCoversLoadedAsync(_coverService),
            card.EnsureCoversLoadedAsync(_coverService),
            vm.RequestCardCoversAsync(card));

        Assert.Same(firstBitmap, card.Cover1);
        Assert.True(card.HasCover1);
    }

    [Fact]
    public async Task AuthorCard_DecodesPrimaryCoverAtFullSizeAndSecondaryCoversAtDeckSize()
    {
        EnsureAvaloniaSkiaBackend();

        // 直式封面：400x600 → 縮圖 330x495
        var srcPng = Path.Combine(_tempDir, "cover_decode_size.png");
        WriteSolidPng(srcPng, 400, 600, new SKColor(150, 90, 200));
        var jpegBytes = await _coverService.ExtractCoverJpegBytesAsync(srcPng);
        Assert.NotNull(jpegBytes);

        const string k1 = "4444444444444444444444444444444444444444";
        const string k2 = "5555555555555555555555555555555555555555";
        const string k3 = "6666666666666666666666666666666666666666";
        _thumbStore.Write(k1, jpegBytes!);
        _thumbStore.Write(k2, jpegBytes!);
        _thumbStore.Write(k3, jpegBytes!);

        var authorId = _store.UpsertAuthor("DecodeSizeAuthor");
        SeedWork("ds1.zip", "Decode Work 1", authorId, k1, ReadStatus.Unread);
        SeedWork("ds2.zip", "Decode Work 2", authorId, k2, ReadStatus.Unread);
        SeedWork("ds3.zip", "Decode Work 3", authorId, k3, ReadStatus.Unread);

        using var vm = CreateVm(autoLoad: false);
        var modelCard = Assert.Single(new ExploreWallService(_store).GetAuthorCards());
        var cardVm = new ExploreAuthorCardVm(modelCard, _ => { });

        // 第 1 張原尺寸（單張精選模式需要），第 2、3 張疊牌大小
        Assert.Null(cardVm.CoverSlots[0].DecodeDipSize);
        Assert.Equal(CoverDipSize.DeckCover, cardVm.CoverSlots[1].DecodeDipSize);
        Assert.Equal(CoverDipSize.DeckCover, cardVm.CoverSlots[2].DecodeDipSize);

        // 150% 縮放：疊牌框 188x263 px → 直式封面縮到 188 寬
        _coverService.ReportDisplayScale(1.5);
        await cardVm.EnsureCoversLoadedAsync(_coverService);

        Assert.Equal(330, cardVm.Cover1!.PixelSize.Width);
        Assert.Equal(188, cardVm.Cover2!.PixelSize.Width);
        Assert.Equal(188, cardVm.Cover3!.PixelSize.Width);
    }

    [Fact]
    public async Task CoverSlot_ReleaseImage_DropsReferenceAndReloadsOnNextRequest()
    {
        EnsureAvaloniaSkiaBackend();
        const string key = "7777777777777777777777777777777777777777";
        await WriteThumbAsync(key);

        Func<Action, Task> inline = static action => { action(); return Task.CompletedTask; };
        using var limiter = new SemaphoreSlim(4, 4);
        var slot = new CoverSlotVm(CreateWorkItem(1, key), "#808080");

        await slot.EnsureLoadedAsync(_coverService, limiter, inline);
        Assert.NotNull(slot.Image);

        // 放掉後不再持有參考；再次要求時重新載入（命中記憶體快取，同一個實例）
        var first = slot.Image;
        slot.ReleaseImage();
        Assert.Null(slot.Image);
        Assert.False(slot.HasImage);

        await slot.EnsureLoadedAsync(_coverService, limiter, inline);
        Assert.Same(first, slot.Image);

        // 沒有封面來源：不會反覆重試，也不會出例外
        var empty = new CoverSlotVm(CreateWorkItem(2, null, path: string.Empty), "#808080");
        await empty.EnsureLoadedAsync(_coverService, limiter, inline);
        empty.ReleaseImage();
        await empty.EnsureLoadedAsync(_coverService, limiter, inline);
        Assert.Null(empty.Image);
    }

    [Fact]
    public async Task CoverSlot_ReleaseDuringInflightLoad_DoesNotApplyStaleImage()
    {
        EnsureAvaloniaSkiaBackend();
        const string key = "8888888888888888888888888888888888888888";
        await WriteThumbAsync(key);

        // UI 派送延後執行：模擬載入完成前卡片就被回收
        var pendingUi = new List<Action>();
        Func<Action, Task> deferred = action => { pendingUi.Add(action); return Task.CompletedTask; };
        using var limiter = new SemaphoreSlim(4, 4);
        var slot = new CoverSlotVm(CreateWorkItem(3, key), "#808080");

        await slot.EnsureLoadedAsync(_coverService, limiter, deferred);
        slot.ReleaseImage();
        foreach (var action in pendingUi)
        {
            action();
        }

        Assert.Null(slot.Image);

        // 之後再次顯示時仍可正常載入
        Func<Action, Task> inline = static action => { action(); return Task.CompletedTask; };
        await slot.EnsureLoadedAsync(_coverService, limiter, inline);
        Assert.NotNull(slot.Image);
    }

    [Fact]
    public async Task CoverSlot_CanceledWhileQueued_SkipsLoad_AndLaterRequestLoads()
    {
        EnsureAvaloniaSkiaBackend();
        const string key = "6666666666666666666666666666666666666666";
        await WriteThumbAsync(key);

        Func<Action, Task> inline = static action => { action(); return Task.CompletedTask; };

        // 名額全被占住：載入只能排隊
        using var limiter = new SemaphoreSlim(0, 4);
        var slot = new CoverSlotVm(CreateWorkItem(4, key), "#808080");
        using var firstCts = new CancellationTokenSource();
        var queued = slot.EnsureLoadedAsync(_coverService, limiter, inline, firstCts.Token);
        Assert.False(queued.IsCompleted);

        // 卡片被回收：排隊中的載入取消，不會套用封面、也不會被當成「沒有封面」
        firstCts.Cancel();
        await queued;
        Assert.Null(slot.Image);

        // 再次顯示（新的權杖）且有名額時，正常載入
        limiter.Release(4);
        using var secondCts = new CancellationTokenSource();
        await slot.EnsureLoadedAsync(_coverService, limiter, inline, secondCts.Token);
        Assert.NotNull(slot.Image);
    }

    [Fact]
    public async Task CoverSlot_RequestAfterCancel_DoesNotReuseCanceledInflightLoad()
    {
        EnsureAvaloniaSkiaBackend();
        const string key = "5555555555555555555555555555555555555555";
        await WriteThumbAsync(key);

        Func<Action, Task> inline = static action => { action(); return Task.CompletedTask; };
        using var limiter = new SemaphoreSlim(0, 4);
        var slot = new CoverSlotVm(CreateWorkItem(5, key), "#808080");

        using var firstCts = new CancellationTokenSource();
        var first = slot.EnsureLoadedAsync(_coverService, limiter, inline, firstCts.Token);
        firstCts.Cancel();

        // 不等第一次結束就再次要求：必須另起一次載入，而不是沿用已取消的那次
        using var secondCts = new CancellationTokenSource();
        var second = slot.EnsureLoadedAsync(_coverService, limiter, inline, secondCts.Token);
        Assert.NotSame(first, second);

        limiter.Release(4);
        await first;
        await second;
        Assert.NotNull(slot.Image);
    }

    [Fact]
    public async Task Gallery_RecycledCoverQueue_ReleasesOnlyOldestBeyondRetention()
    {
        EnsureAvaloniaSkiaBackend();
        const string key = "9999999999999999999999999999999999999999";
        await WriteThumbAsync(key);

        Func<Action, Task> inline = static action => { action(); return Task.CompletedTask; };
        using var gallery = new GalleryPageViewModel(
            _store,
            _coverService,
            rootDirectory: _tempDir,
            runOnUiAsync: inline,
            autoLoad: false);

        int total = GalleryPageViewModel.RecycledCoverRetention + 5;
        var items = Enumerable.Range(0, total)
            .Select(i => new GalleryItemVm(
                new GalleryItemSortMetadata($@"C:\fake\g{i}.zip", $"g{i}.zip", $"G {i}", false, 1, 1, ReadStatus.Unread, 0, null),
                authorName: null,
                coverThumbKey: key,
                pageCount: 10,
                language: null,
                isAuthorFolder: false,
                onActivate: _ => { },
                onRevealInExplorer: _ => { },
                runOnUiAsync: inline))
            .ToList();

        foreach (var item in items)
        {
            await gallery.NotifyItemRealized(item);
        }
        Assert.All(items, item => Assert.True(item.HasCover1));

        // 全部回收：只有最早回收的 5 張超出保留數量而放掉封面
        foreach (var item in items)
        {
            gallery.NotifyItemRecycled(item);
        }
        Assert.All(items.Take(5), item => Assert.False(item.HasCover1));
        Assert.All(items.Skip(5), item => Assert.True(item.HasCover1));

        // 重新顯示的卡片移出佇列：再回收 1 張新的卡片時，被放掉的是佇列中最舊的（第 6 張），不是重新顯示的第 7 張
        await gallery.NotifyItemRealized(items[6]);
        await gallery.NotifyItemRealized(items[0]);
        Assert.True(items[0].HasCover1);
        gallery.NotifyItemRecycled(items[0]);
        gallery.NotifyItemRecycled(items[6]);
        Assert.True(items[0].HasCover1);
        Assert.True(items[6].HasCover1);
        Assert.False(items[5].HasCover1);
    }

    private async Task WriteThumbAsync(string key)
    {
        var srcPng = Path.Combine(_tempDir, $"thumb_{key[..4]}.png");
        WriteSolidPng(srcPng, 400, 600, new SKColor(90, 140, 200));
        var jpegBytes = await _coverService.ExtractCoverJpegBytesAsync(srcPng);
        Assert.NotNull(jpegBytes);
        _thumbStore.Write(key, jpegBytes!);
    }

    private static ExploreWorkItem CreateWorkItem(long id, string? thumbKey, string? path = null)
    {
        return new ExploreWorkItem(
            id, path ?? $@"C:\fake\slot{id}.zip", $"slot{id}.zip", $"Slot {id}", null, string.Empty, null, null, null,
            ReadStatus.Unread, 0, null, 0, null, thumbKey, Array.Empty<ExploreTagBadge>());
    }

    [Fact]
    public void ComboCard_RandomComboPreviewUsesSmallSizeForAllSlots()
    {
        var works = Enumerable.Range(1, 3)
            .Select(i => new ExploreWorkItem(
                i, $@"C:\fake\combo{i}.zip", $"combo{i}.zip", $"Combo {i}", null, string.Empty, null, null, null,
                ReadStatus.Unread, 0, null, 0, null, null, Array.Empty<ExploreTagBadge>()))
            .ToList();
        var card = new ExploreComboCard(
            new ExploreTagBadge(1, "a", "A", "tag", 3, false),
            new ExploreTagBadge(2, "b", "B", "tag", 3, false),
            3,
            3,
            works);

        var preview = new ExploreComboCardVm(card, _ => { }, isRandomComboPreview: true);
        Assert.All(preview.CoverSlots, slot => Assert.Equal(CoverDipSize.RandomComboPreview, slot.DecodeDipSize));

        var deckCard = new ExploreComboCardVm(card, _ => { });
        Assert.Null(deckCard.CoverSlots[0].DecodeDipSize);
        Assert.Equal(CoverDipSize.DeckCover, deckCard.CoverSlots[1].DecodeDipSize);
        Assert.Equal(CoverDipSize.DeckCover, deckCard.CoverSlots[2].DecodeDipSize);
    }

    [Fact]
    public async Task EnsureCoversLoadedAsync_TwoArgOverload_WithAutoLoadFalse_KeepsLimiterAliveUntilAllCoversLoad()
    {
        EnsureAvaloniaSkiaBackend();

        var srcPng = Path.Combine(_tempDir, "cover_multi.png");
        WriteSolidPng(srcPng, 400, 600, new SKColor(90, 180, 120));
        var jpegBytes = await _coverService.ExtractCoverJpegBytesAsync(srcPng);
        Assert.NotNull(jpegBytes);

        const string k1 = "1111111111111111111111111111111111111111";
        const string k2 = "2222222222222222222222222222222222222222";
        const string k3 = "3333333333333333333333333333333333333333";
        _thumbStore.Write(k1, jpegBytes!);
        _thumbStore.Write(k2, jpegBytes!);
        _thumbStore.Write(k3, jpegBytes!);

        var authorId = _store.UpsertAuthor("ColdCoverAuthor");
        SeedWork("cold1.zip", "Cold Work 1", authorId, k1, ReadStatus.Unread);
        SeedWork("cold2.zip", "Cold Work 2", authorId, k2, ReadStatus.Unread);
        SeedWork("cold3.zip", "Cold Work 3", authorId, k3, ReadStatus.Unread);

        // autoLoad: false 確保未啟動預載，直接由冷啟動狀態建立卡片並呼叫 2 參數 EnsureCoversLoadedAsync
        using var vm = CreateVm(autoLoad: false);
        Assert.Empty(vm.AuthorCards);

        var svc = new ExploreWallService(_store);
        var modelCard = Assert.Single(svc.GetAuthorCards());
        var cardVm = new ExploreAuthorCardVm(modelCard, _ => { });

        Assert.False(cardVm.HasCover1);
        Assert.False(cardVm.HasCover2);
        Assert.False(cardVm.HasCover3);

        await cardVm.EnsureCoversLoadedAsync(_coverService);

        Assert.True(cardVm.HasCover1);
        Assert.True(cardVm.HasCover2);
        Assert.True(cardVm.HasCover3);
        Assert.NotNull(cardVm.Cover1);
        Assert.NotNull(cardVm.Cover2);
        Assert.NotNull(cardVm.Cover3);
    }

    [Fact]
    public async Task CardSelectionAndRandomComboHooks_RaiseExpectedPayloads()
    {
        var authorId = _store.UpsertAuthor("HookAuthor");
        var t1 = _store.UpsertTag("maid", "tag");
        var t2 = _store.UpsertTag("glasses", "tag");
        SeedWork("h1.zip", "H1", authorId, "kh1", ReadStatus.Unread, t1, t2);
        SeedWork("h2.zip", "H2", authorId, "kh2", ReadStatus.Unread, t1, t2);

        using var vm = CreateVm(autoLoad: true);
        await vm.InitializationTask;

        ExploreCardSelection? activated = null;
        vm.CardActivated += sel => activated = sel;

        vm.AuthorCards[0].SelectCardCommand.Execute(null);
        Assert.NotNull(activated);
        Assert.Equal(ExploreCardSelectionKind.Author, activated!.Kind);
        Assert.Equal(authorId, activated.AuthorId);

        ExploreComboCard? pickedCombo = null;
        vm.ShowComboPlaceholder = combo => pickedCombo = combo;
        await vm.PickRandomComboCommand.ExecuteAsync(null);
        Assert.NotNull(pickedCombo);
        Assert.Equal(2, pickedCombo!.WorkCount);
    }

    [Fact]
    public async Task MainWindowViewModel_WiresExplorePageAndBidirectionalGlobalSearch_AndDisposesCleanly()
    {
        var dataDir = Path.Combine(_tempDir, "app_data");
        Directory.CreateDirectory(dataDir);

        var tempDbPath = Path.Combine(dataDir, "cache.db");
        using (var seededStore = new SqliteAssetMetaStore(tempDbPath))
        {
            var a1 = seededStore.UpsertAuthor("WiredArtist");
            var a2 = seededStore.UpsertAuthor("OtherArtist");
            seededStore.Upsert(new AssetMeta
            {
                Path = Path.Combine(dataDir, "w1.zip"),
                Name = "w1.zip",
                IsFolder = false,
                FileSize = 100,
                Mtime = 100,
                LastScanned = 100,
                LastAccessed = 100,
            });
            var id1 = seededStore.GetAssetId(Path.Combine(dataDir, "w1.zip"))!.Value;
            seededStore.UpsertMetadata(new AssetMetadata { AssetId = id1, AuthorId = a1, Title = "Wired Work", ParsedAt = 100 });

            seededStore.Upsert(new AssetMeta
            {
                Path = Path.Combine(dataDir, "w2.zip"),
                Name = "w2.zip",
                IsFolder = false,
                FileSize = 100,
                Mtime = 100,
                LastScanned = 100,
                LastAccessed = 100,
            });
            var id2 = seededStore.GetAssetId(Path.Combine(dataDir, "w2.zip"))!.Value;
            seededStore.UpsertMetadata(new AssetMetadata { AssetId = id2, AuthorId = a2, Title = "Other Work", ParsedAt = 100 });
        }

        using var dataContext = new AppDataContext(explicitDataDirectory: dataDir);
        using var mainVm = new MainWindowViewModel(dataContext);

        Assert.IsType<ExplorePageViewModel>(mainVm.ExplorePage);
        await mainVm.ExplorePage.InitializationTask;
        Assert.Equal(2, mainVm.ExplorePage.AuthorCards.Count);

        // 頂部全域搜尋框同步至 ExplorePage.SearchKeyword 並過濾
        mainVm.GlobalSearchText = "Wired";
        Assert.Equal("Wired", mainVm.ExplorePage.SearchKeyword);
        await mainVm.ExecuteGlobalSearchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !mainVm.ExplorePage.IsLoading && mainVm.ExplorePage.AuthorCards.Count == 1);
        Assert.Equal("WiredArtist", mainVm.ExplorePage.AuthorCards[0].MainName);

        // ExplorePage 清除搜尋同步回 MainWindowViewModel.GlobalSearchText
        mainVm.ExplorePage.ClearSearchCommand.Execute(null);
        Assert.Equal(string.Empty, mainVm.GlobalSearchText);

        // Dispose 不應拋例外且可重複呼叫
        mainVm.Dispose();
        mainVm.Dispose();
    }

    [Fact]
    public async Task IncrementalPaging_ForWallCardsAndTagPicker_AndClearsInactiveCollectionOnTabSwitch()
    {
        var tagIds = new long[135];
        for (int i = 0; i < tagIds.Length; i++)
        {
            tagIds[i] = _store.UpsertTag($"tag_{i:000}", "tag");
        }

        // 建立 45 位作者、掛載 135 顆標籤（讓每個 tag 有 WorkCount > 0）
        for (int a = 0; a < 45; a++)
        {
            var authorId = _store.UpsertAuthor($"Author_{a:00}");
            var tagsForWork = new List<long>();
            for (int k = 0; k < 3; k++)
            {
                tagsForWork.Add(tagIds[(a * 3 + k) % tagIds.Length]);
            }

            SeedWork($"work_{a:00}.zip", $"Title {a:00}", authorId, null, ReadStatus.Unread, tagsForWork.ToArray());
        }

        using var vm = CreateVm(autoLoad: true);
        await vm.InitializationTask;

        // 首屏限制 30 張，總命中 45 張
        Assert.Equal(45, vm.TotalMatchingWallCards);
        Assert.Equal(30, vm.AuthorCards.Count);
        Assert.True(vm.HasMoreWallCards);
        Assert.Equal(15, vm.RemainingWallCardsCount);

        // 載入下一批應補齊至 45 張
        vm.LoadMoreWallCardsCommand.Execute(null);
        Assert.Equal(45, vm.AuthorCards.Count);
        Assert.False(vm.HasMoreWallCards);

        // 切至主題・原作牆 → 內容主題，應清空不再顯示的 AuthorCards 且首屏載入 30 個（全庫共 135 個，不再於 120 截斷）
        vm.SelectThemeWallModeCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsThemeWallMode && !vm.IsLoading && vm.ThemeCards.Count == 30);
        Assert.Empty(vm.AuthorCards);
        Assert.Equal(135, vm.TotalMatchingWallCards);
        Assert.True(vm.HasMoreWallCards);
        Assert.True(vm.HasPositionBadge);

        // 驗證可視區間與位置膠囊文字更新
        vm.UpdateViewportRange(13, 24, offsetY: 400);
        Assert.Equal("第 13–24 筆 / 共 135 筆", vm.PositionBadgeTitle);
        Assert.Contains("已載入 30 筆", vm.PositionBadgeDetail);
        Assert.Equal("+18", vm.LoadMoreWallCardsShortText);
        Assert.True(vm.ShowBackToTopButton);

        // 開啟全部標籤挑選器：首屏顯示 100 顆，按顯示更多補齊 135 顆
        vm.ToggleTagPickerCommand.Execute(null);
        await WaitUntilAsync(() => vm.PickerTags.Count == 100);
        Assert.Equal(135, vm.TotalMatchingPickerTags);
        Assert.True(vm.HasMorePickerTags);
        Assert.Equal(35, vm.RemainingPickerTagsCount);

        vm.LoadMorePickerTagsCommand.Execute(null);
        Assert.Equal(135, vm.PickerTags.Count);
        Assert.False(vm.HasMorePickerTags);
    }

    [Fact]
    public async Task PickRandomCombo_PopulatesBanner_SupportsBrowseAndApplyFiltersAndDismiss()
    {
        var a1 = _store.UpsertAuthor("Artist1");
        var a2 = _store.UpsertAuthor("Artist2");
        var tMaid = _store.UpsertTag("maid", "tag");
        var tGlasses = _store.UpsertTag("glasses", "tag");
        var tUniform = _store.UpsertTag("schoolgirl uniform", "tag");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");
        _store.UpsertTagTranslation(tGlasses, "zh-TW", "眼鏡", "manual");

        SeedWork("c1.zip", "Maid Glasses 1", a1, "kc1", ReadStatus.Unread, tMaid, tGlasses);
        SeedWork("c2.zip", "Maid Glasses 2", a1, "kc2", ReadStatus.Unread, tMaid, tGlasses);
        SeedWork("c3.zip", "Other Only", a2, "kc3", ReadStatus.Unread, tUniform);

        using var vm = CreateVm(autoLoad: true);
        await vm.InitializationTask;
        Assert.False(vm.HasActiveRandomCombo);

        await vm.PickRandomComboCommand.ExecuteAsync(null);
        Assert.True(vm.HasActiveRandomCombo);
        Assert.NotNull(vm.ActiveRandomCombo);
        Assert.Equal(2, vm.ActiveRandomCombo!.WorkCount);

        // 1. 瀏覽交集作品 → 進入鑽取作品清單
        await vm.BrowseActiveRandomComboCommand.ExecuteAsync(null);
        Assert.True(vm.IsDrillDownMode);
        Assert.False(vm.HasActiveRandomCombo); // 鑽取期間暫隱藏橫幅
        Assert.Equal(2, vm.DrillDownWorks.Count);

        // 2. 返回探索牆後橫幅仍在，點擊「套用到篩選」應設為雙標籤過濾並只留下 Artist1
        vm.ExitDrillDownCommand.Execute(null);
        Assert.False(vm.IsDrillDownMode);
        Assert.True(vm.HasActiveRandomCombo);

        await vm.ApplyActiveRandomComboFiltersCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.SelectedFilterCount == 2);
        var filteredAuthor = Assert.Single(vm.AuthorCards);
        Assert.Equal("Artist1", filteredAuthor.MainName);

        // 3. 關閉碰撞橫幅
        vm.DismissRandomComboCommand.Execute(null);
        Assert.False(vm.HasActiveRandomCombo);
        Assert.Null(vm.ActiveRandomCombo);
    }

    [Fact]
    public async Task SelectAuthorCard_EntersDrillDown_SupportsFilteringPagingShowAllAndExit()
    {
        var a1 = _store.UpsertAuthor("DrillAuthor");
        var t1 = _store.UpsertTag("stockings", "tag");
        var t2 = _store.UpsertTag("catgirl", "tag");
        _store.UpsertTagTranslation(t1, "zh-TW", "絲襪", "manual");

        // 建立 36 部作品：僅第 0 部掛有 t2，其餘只有 t1（測試超過 30 本分頁載入與篩選後看全部）
        for (int i = 0; i < 36; i++)
        {
            var status = i % 2 == 0 ? ReadStatus.Unread : ReadStatus.Completed;
            if (i == 0)
                SeedWork($"d_{i:00}.zip", $"Work {i:00}", a1, null, status, t1, t2);
            else
                SeedWork($"d_{i:00}.zip", $"Work {i:00}", a1, null, status, t1);
        }

        using var vm = CreateVm(autoLoad: true);
        await vm.InitializationTask;

        // 套用 t2 篩選後點進 DrillAuthor → 只剩 1 本，且可點 ShowAllForDrillDownAuthor 看全部 36 本
        await vm.ToggleTagSelectionAsync(t2);
        await WaitUntilAsync(() => !vm.IsLoading && vm.AuthorCards.Count == 1);

        vm.AuthorCards[0].SelectCardCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsDrillDownMode && !vm.IsLoading);

        Assert.True(vm.HasDrillDownAppliedTags);
        Assert.Single(vm.DrillDownWorks);

        await vm.ShowAllForDrillDownAuthorCommand.ExecuteAsync(null);
        Assert.False(vm.HasDrillDownAppliedTags);
        Assert.Equal(36, vm.TotalDrillDownWorks);
        Assert.Equal(30, vm.DrillDownWorks.Count);
        Assert.True(vm.HasMoreWallCards);

        // 補載剩餘 6 本
        vm.LoadMoreWallCardsCommand.Execute(null);
        Assert.Equal(36, vm.DrillDownWorks.Count);
        Assert.False(vm.HasMoreWallCards);

        // 鑽取模式內勾選「僅看未讀」只留 18 本
        vm.UnreadOnly = true;
        Assert.Equal(18, vm.TotalDrillDownWorks);

        // 再點一次當前「👤 作者探索」頁籤可返回探索牆
        vm.SelectAuthorModeCommand.Execute(null);
        Assert.False(vm.IsDrillDownMode);
        Assert.True(vm.IsWallContentVisible);
    }

    [Fact]
    public async Task OpenWorkAndRevealInExplorer_DelegatesToExternalLauncher_AndHandlesMissingPath()
    {
        var a1 = _store.UpsertAuthor("LaunchArtist");
        SeedWork("launch1.zip", "Launchable Work", a1, null, ReadStatus.Unread);

        var fakeLauncher = new FakeExternalLauncher();
        using var vm = CreateVm(autoLoad: true, externalLauncher: fakeLauncher);
        await vm.InitializationTask;

        vm.AuthorCards[0].SelectCardCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsDrillDownMode && !vm.IsLoading && vm.DrillDownWorks.Count == 1);

        var workCard = vm.DrillDownWorks[0];
        workCard.SelectCardCommand.Execute(null);
        workCard.RevealInExplorerCommand.Execute(null);

        Assert.Single(fakeLauncher.OpenedPaths);
        Assert.Equal(workCard.Path, fakeLauncher.OpenedPaths[0]);
        Assert.Single(fakeLauncher.RevealedPaths);
        Assert.Equal(workCard.Path, fakeLauncher.RevealedPaths[0]);

        // 模擬找不到路徑時應回傳錯誤而不擲出未處理例外
        fakeLauncher.ShouldFail = true;
        workCard.SelectCardCommand.Execute(null);
        Assert.Single(fakeLauncher.OpenedPaths); // 失敗時沒有新增成功記錄
    }

    [Fact]
    public void BatchObservableCollection_AddRange_EmitsAddActionInsteadOfReset()
    {
        var collection = new BatchObservableCollection<int>();
        collection.ReplaceAll([1, 2, 3]);

        var events = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => events.Add(args);

        collection.AddRange([4, 5]);

        Assert.Equal([1, 2, 3, 4, 5], collection);
        var evt = Assert.Single(events);
        Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Add, evt.Action);
        Assert.Equal(3, evt.NewStartingIndex);
        Assert.NotNull(evt.NewItems);
        Assert.Equal(2, evt.NewItems!.Count);
        Assert.Equal(4, evt.NewItems[0]);
        Assert.Equal(5, evt.NewItems[1]);
    }

    [Fact]
    public async Task ExploreCardVm_LoadsPrimaryCoverBeforeSecondaryCovers()
    {
        EnsureAvaloniaSkiaBackend();

        const string key1 = "1111111111111111111111111111111111111111";
        const string key2 = "2222222222222222222222222222222222222222";
        var src1 = Path.Combine(_tempDir, "p1.png");
        WriteSolidPng(src1, 200, 300, new SKColor(200, 80, 80));
        var jpegBytes = await _coverService.ExtractCoverJpegBytesAsync(src1);
        Assert.NotNull(jpegBytes);
        _thumbStore.Write(key1, jpegBytes!);
        _thumbStore.Write(key2, jpegBytes!);

        var authorId = _store.UpsertAuthor("TwoPhaseArtist");
        SeedWork("w1.zip", "Work 1", authorId, key1, ReadStatus.Unread);
        SeedWork("w2.zip", "Work 2", authorId, key2, ReadStatus.Unread);

        using var vm = CreateVm(autoLoad: false);
        await vm.ReloadAllAsync();

        var card = Assert.Single(vm.AuthorCards);
        await WaitUntilAsync(() => card.HasCover1 && card.HasCover2);

        // 直接驗證分段 API：先載主封面不應提前載入副封面
        var stagedVm = new ExploreAuthorCardVm(card.SourceCard, _ => { }, a => { a(); return Task.CompletedTask; });
        using var sem = new SemaphoreSlim(4, 4);

        await stagedVm.EnsurePrimaryCoverLoadedAsync(_coverService, sem);
        Assert.True(stagedVm.HasCover1);
        Assert.False(stagedVm.HasCover2);

        await stagedVm.EnsureSecondaryCoversLoadedAsync(_coverService, sem);
        Assert.True(stagedVm.HasCover2);
    }

    [Fact]
    public async Task OpenDrillDownInGallery_BuildsDslQueryAndRaisesOpenInGalleryRequested()
    {
        var authorId = _store.UpsertAuthor("Flugel");
        var tMaid = _store.UpsertTag("maid", "tag");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");
        SeedWork("f1.zip", "Flugel Work", authorId, null, ReadStatus.Unread, tMaid);

        using var vm = CreateVm(autoLoad: true);
        await vm.InitializationTask;

        string? capturedDsl = null;
        vm.OpenInGalleryRequested += dsl => capturedDsl = dsl;

        vm.AuthorCards[0].SelectCardCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsDrillDownMode && !vm.IsLoading);

        vm.OpenDrillDownInGalleryCommand.Execute(null);
        Assert.Equal("artist:Flugel", capturedDsl);
    }

    private sealed class FakeExternalLauncher : IExternalLauncher
    {
        public List<string> OpenedPaths { get; } = [];
        public List<string> RevealedPaths { get; } = [];
        public bool ShouldFail { get; set; }

        public bool OpenWithDefaultApp(string path, out string? errorMessage)
        {
            if (ShouldFail)
            {
                errorMessage = "找不到目標檔案";
                return false;
            }

            OpenedPaths.Add(path);
            errorMessage = null;
            return true;
        }

        public bool RevealInFileExplorer(string path, out string? errorMessage)
        {
            if (ShouldFail)
            {
                errorMessage = "找不到目標檔案";
                return false;
            }

            RevealedPaths.Add(path);
            errorMessage = null;
            return true;
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
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
