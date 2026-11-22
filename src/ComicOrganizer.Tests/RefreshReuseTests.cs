using System.Collections.Specialized;
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
/// 重整時沿用沒變的項目（畫廊 / 探索牆 / 閱讀軌跡）與清單差量同步（<see cref="BatchObservableCollection{T}.SyncTo"/>）。
/// 目的：重整或修改後畫面不再清空、退回佔位圖再由上往下補圖，也不再捲回頂端。
/// 使用暫存 SQLite 與目錄，不存取正式資料。
/// </summary>
public sealed class RefreshReuseTests : IDisposable
{
    private static readonly object AvaloniaInitLock = new();
    private static bool _avaloniaSkiaInitialized;

    private static readonly Func<Action, Task> Inline = static action =>
    {
        action();
        return Task.CompletedTask;
    };

    private readonly string _tempDir;
    private readonly string _rootLibDir;
    private readonly SqliteAssetMetaStore _store;
    private readonly FileSystemThumbStore _thumbStore;
    private readonly SkiaCoverService _coverService;

    public RefreshReuseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_refresh_reuse_{Guid.NewGuid():N}");
        _rootLibDir = Path.Combine(_tempDir, "Library");
        Directory.CreateDirectory(_rootLibDir);
        _store = new SqliteAssetMetaStore(Path.Combine(_tempDir, "cache.db"));
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

    // ===== BatchObservableCollection.SyncTo =====

    private sealed class Box
    {
        public Box(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public override string ToString() => Value.ToString();
    }

    [Fact]
    public void SyncTo_IdenticalSequence_RaisesNoNotifications()
    {
        var items = Enumerable.Range(0, 50).Select(i => new Box(i)).ToList();
        var collection = new BatchObservableCollection<Box>();
        collection.ReplaceAll(items);
        var events = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => events.Add(e.Action);

        bool reset = collection.SyncTo(items.ToList());

        Assert.False(reset);
        Assert.Empty(events);
    }

    [Fact]
    public void SyncTo_SmallChanges_UsesIncrementalNotificationsWithoutReset()
    {
        var items = Enumerable.Range(0, 200).Select(i => new Box(i)).ToList();
        var collection = new BatchObservableCollection<Box>();
        collection.ReplaceAll(items);
        var events = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => events.Add(e.Action);

        // 刪 1 筆、換 1 筆、插 1 筆、把第 150 筆移到最前面（例如讀完後依最近閱讀排序）
        var target = items.ToList();
        target.RemoveAt(10);
        target[20] = new Box(1000);
        target.Insert(50, new Box(2000));
        var moved = target[150];
        target.RemoveAt(150);
        target.Insert(0, moved);

        bool reset = collection.SyncTo(target);

        Assert.False(reset);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, events);
        Assert.InRange(events.Count, 1, 8);
        Assert.Equal(target, collection);
    }

    [Fact]
    public void SyncTo_LargeReorder_FallsBackToSingleReset()
    {
        var items = Enumerable.Range(0, 500).Select(i => new Box(i)).ToList();
        var collection = new BatchObservableCollection<Box>();
        collection.ReplaceAll(items);
        var events = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => events.Add(e.Action);

        var target = items.AsEnumerable().Reverse().ToList();
        bool reset = collection.SyncTo(target);

        Assert.True(reset);
        Assert.Equal(NotifyCollectionChangedAction.Reset, events[^1]);
        Assert.Equal(target, collection);
    }

    [Fact]
    public void SyncTo_RandomEdits_AlwaysMatchesTarget()
    {
        var rng = new Random(20261122);
        for (int trial = 0; trial < 300; trial++)
        {
            var items = Enumerable.Range(0, rng.Next(0, 120)).Select(i => new Box(i)).ToList();
            var collection = new BatchObservableCollection<Box>();
            collection.ReplaceAll(items);

            var target = items.ToList();
            int edits = rng.Next(0, 12);
            for (int e = 0; e < edits; e++)
            {
                int op = rng.Next(3);
                if (op == 0 && target.Count > 0)
                {
                    target.RemoveAt(rng.Next(target.Count));
                }
                else if (op == 1)
                {
                    target.Insert(rng.Next(target.Count + 1), new Box(10_000 + e));
                }
                else if (target.Count > 1)
                {
                    int from = rng.Next(target.Count);
                    var x = target[from];
                    target.RemoveAt(from);
                    target.Insert(rng.Next(target.Count + 1), x);
                }
            }

            collection.SyncTo(target);
            Assert.Equal(target, collection);
        }
    }

    // ===== 探索牆內容比對 =====

    [Fact]
    public void ExploreContentEquality_ComparesListFieldsByContent()
    {
        var tag = new ExploreTagBadge(1, "maid", "女僕", "tag", 3, false);
        var w1 = CreateWorkItem(1, "a", tags: new[] { tag });
        var w1Copy = CreateWorkItem(1, "a", tags: new[] { tag with { } });
        var w1Read = w1 with { ReadStatus = ReadStatus.Completed };

        // record 內建比對只比清單參考：內容相同但清單是不同物件時判為不相等，所以需要逐項比對
        Assert.NotEqual(w1, w1Copy);
        Assert.True(ExploreContentEquality.SameWork(w1, w1Copy));
        Assert.False(ExploreContentEquality.SameWork(w1, w1Read));

        var card = new ExploreAuthorCard(7, "A", new[] { "a1" }, null, 1, 1, 1, 0, 0, 1, new[] { tag }, new[] { w1 });
        var cardCopy = new ExploreAuthorCard(7, "A", new[] { "a1" }, null, 1, 1, 1, 0, 0, 1, new[] { tag }, new[] { w1Copy });
        var cardChanged = cardCopy with { RepresentativeWorks = new[] { w1Read } };
        Assert.True(ExploreContentEquality.SameAuthorCard(card, cardCopy));
        Assert.False(ExploreContentEquality.SameAuthorCard(card, cardChanged));
    }

    // ===== 畫廊 =====

    [Fact]
    public async Task Gallery_RefreshSameScope_ReusesUnchangedItems_KeepsCoversSelectionAndScroll()
    {
        EnsureAvaloniaSkiaBackend();
        var author = _store.UpsertAuthor("GalleryAuthor");
        string pa = await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, "a.zip"), "Work A", author, "aaaa");
        string pb = await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, "b.zip"), "Work B", author, "bbbb");
        await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, "c.zip"), "Work C", author, "cccc");

        using var vm = new GalleryPageViewModel(_store, _coverService, rootDirectory: _rootLibDir, runOnUiAsync: Inline, autoLoad: false);
        int scrollResets = 0;
        vm.ScrollResetRequested += () => scrollResets++;
        await vm.ReloadAllAsync();
        Assert.Equal(3, vm.Items.Count);
        Assert.Equal(1, scrollResets);

        foreach (var item in vm.Items.ToList())
        {
            await vm.NotifyItemRealized(item);
        }
        Assert.All(vm.Items, i => Assert.True(i.HasCover1));

        var itemA = vm.Items.Single(i => i.Path == pa);
        var itemB = vm.Items.Single(i => i.Path == pb);
        itemB.InspectDetailCommand.Execute(null);
        Assert.True(itemB.IsSelected);

        // 只改 B 的閱讀狀態後重整（等同閱讀器關閉或詳情抽屜儲存後的自動刷新）
        _store.SetReadStatus(pb, ReadStatus.Completed);
        var events = new List<NotifyCollectionChangedAction>();
        vm.Items.CollectionChanged += (_, e) => events.Add(e.Action);
        await vm.RefreshAsync();

        // 1. 同一範圍：不捲回頂端，清單不整批重建
        Assert.Equal(1, scrollResets);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, events);

        // 2. 沒變的 A 直接沿用（同一個物件、封面還在）
        Assert.Same(itemA, vm.Items.Single(i => i.Path == pa));
        Assert.True(itemA.HasCover1);

        // 3. 變了的 B 換成新項目：一建立就沿用舊封面，並接手選取狀態
        var newB = vm.Items.Single(i => i.Path == pb);
        Assert.NotSame(itemB, newB);
        Assert.Equal(ReadStatus.Completed, newB.ReadStatus);
        Assert.True(newB.HasCover1);
        Assert.Same(itemB.Cover1, newB.Cover1);
        Assert.True(newB.IsSelected);
        Assert.False(itemB.IsSelected);
        Assert.Same(newB.Cover1, vm.DetailDrawer.PreviewCover);

        // 4. 換到別的範圍（搜尋）才捲回頂端
        await vm.ApplyExternalDslQueryAsync("@artist:GalleryAuthor");
        Assert.Equal(2, scrollResets);
    }

    [Fact]
    public async Task Gallery_RefreshInTagsMode_KeepsSidebarSelectionHighlightAndReusesNodes()
    {
        var author = _store.UpsertAuthor("TagAuthor");
        var t1 = _store.UpsertTag("alpha", "tag");
        var t2 = _store.UpsertTag("beta", "tag");
        SeedWork(Path.Combine(_rootLibDir, "t1.zip"), "T1", author, null, t1);
        SeedWork(Path.Combine(_rootLibDir, "t2.zip"), "T2", author, null, t2);

        using var vm = new GalleryPageViewModel(_store, _coverService, rootDirectory: _rootLibDir, runOnUiAsync: Inline, autoLoad: false);
        await vm.ReloadAllAsync();
        vm.SelectTagsSidebarCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.IsTagsSidebarMode && vm.SidebarNodes.Count == 2);

        var second = vm.SidebarNodes[1];
        second.SelectCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.Items.Count == 1);
        Assert.True(second.IsSelected);
        var nodesBefore = vm.SidebarNodes.ToList();

        int sidebarEvents = 0;
        vm.SidebarNodes.CollectionChanged += (_, _) => sidebarEvents++;
        await vm.RefreshAsync();

        // 節點沒變：直接沿用、側欄清單不發任何通知，選取高亮仍在原節點上
        Assert.Equal(0, sidebarEvents);
        Assert.Equal(nodesBefore, vm.SidebarNodes);
        Assert.Same(second, vm.SelectedSidebarNode);
        Assert.True(second.IsSelected);
        Assert.False(vm.SidebarNodes[0].IsSelected);
        Assert.Single(vm.Items);
    }

    // ===== 探索牆 =====

    [Fact]
    public async Task Explore_RefreshSameScope_ReusesUnchangedCards_AdoptsCoversForChangedCards_KeepsScroll()
    {
        EnsureAvaloniaSkiaBackend();
        var a1 = _store.UpsertAuthor("ExploreA");
        var a2 = _store.UpsertAuthor("ExploreB");
        string changedPath = await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, "ea1.zip"), "EA 1", a1, "dddd");
        await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, "ea2.zip"), "EA 2", a1, "eeee");
        await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, "eb1.zip"), "EB 1", a2, "ffff");

        using var vm = new ExplorePageViewModel(new ExploreWallService(_store), _coverService, autoLoad: true, runOnUiAsync: Inline);
        int scrollResets = 0;
        vm.ScrollResetRequested += () => scrollResets++;
        await vm.InitializationTask;
        await WaitUntilAsync(() => !vm.IsLoading && vm.AuthorCards.Count == 2);
        int resetsAfterLoad = scrollResets;

        foreach (var card in vm.AuthorCards.ToList())
        {
            await vm.NotifyCardRealized(card);
        }

        var cardA = vm.AuthorCards.Single(c => c.MainName == "ExploreA");
        var cardB = vm.AuthorCards.Single(c => c.MainName == "ExploreB");
        await WaitUntilAsync(() => cardA.HasCover1 && cardB.HasCover1);

        // 改第 2 部代表作的狀態（代表作依「未讀優先」排序，第 1 部不受影響，疊牌位置不變）
        string secondPath = cardA.CoverSlots[1].SourcePath;
        Assert.NotEqual(changedPath, secondPath);
        _store.SetReadStatus(secondPath, ReadStatus.Completed);
        await vm.RefreshCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.Equal(resetsAfterLoad, scrollResets);
        Assert.Same(cardB, vm.AuthorCards.Single(c => c.MainName == "ExploreB"));

        var newA = vm.AuthorCards.Single(c => c.MainName == "ExploreA");
        Assert.NotSame(cardA, newA);
        Assert.True(newA.HasCover1);
        Assert.Same(cardA.Cover1, newA.Cover1);
        Assert.Same(cardA.CoverSlots[1].Image, newA.CoverSlots[1].Image);
    }

    [Fact]
    public async Task Explore_RefreshWhenDeckOrderChanges_DoesNotReuseSmallDeckCoverAsPrimary()
    {
        EnsureAvaloniaSkiaBackend();
        var a1 = _store.UpsertAuthor("ReorderAuthor");
        await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, "r1.zip"), "R 1", a1, "r1r1");
        await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, "r2.zip"), "R 2", a1, "r2r2");

        using var vm = new ExplorePageViewModel(new ExploreWallService(_store), _coverService, autoLoad: true, runOnUiAsync: Inline);
        await vm.InitializationTask;
        var card = Assert.Single(vm.AuthorCards);
        await vm.NotifyCardRealized(card);
        await WaitUntilAsync(() => card.CoverSlots.All(s => s.HasImage));

        // 第 1 部讀完 → 第 2 部（原本以疊牌小尺寸解碼）變成第 1 張（完整尺寸），解碼尺寸不同不可沿用
        string firstPath = card.CoverSlots[0].SourcePath;
        string secondPath = card.CoverSlots[1].SourcePath;
        _store.SetReadStatus(firstPath, ReadStatus.Completed);
        await vm.RefreshCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !vm.IsLoading);

        var newCard = Assert.Single(vm.AuthorCards);
        Assert.Equal(secondPath, newCard.CoverSlots[0].SourcePath);
        Assert.NotSame(card.CoverSlots[1].Image, newCard.CoverSlots[0].Image);

        // 被建立時照常以正確尺寸重新載入
        await vm.NotifyCardRealized(newCard);
        await WaitUntilAsync(() => newCard.HasCover1);
    }

    [Fact]
    public async Task Explore_DrillDownRefresh_ReusesWorkCardsAndKeepsScroll()
    {
        EnsureAvaloniaSkiaBackend();
        var a1 = _store.UpsertAuthor("DrillAuthor");
        for (int i = 0; i < 5; i++)
        {
            await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, $"d{i}.zip"), $"Drill {i}", a1, $"d{i}d{i}");
        }

        using var vm = new ExplorePageViewModel(new ExploreWallService(_store), _coverService, autoLoad: true, runOnUiAsync: Inline);
        int scrollResets = 0;
        vm.ScrollResetRequested += () => scrollResets++;
        await vm.InitializationTask;
        vm.AuthorCards[0].SelectCardCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsDrillDownMode && !vm.IsLoading && vm.DrillDownWorks.Count == 5);
        int resetsAfterEnter = scrollResets;

        var before = vm.DrillDownWorks.ToList();
        foreach (var card in before)
        {
            await vm.NotifyCardRealized(card);
        }
        Assert.All(before, c => Assert.True(c.HasCover1));

        await vm.RefreshCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.True(vm.IsDrillDownMode);
        Assert.Equal(resetsAfterEnter, scrollResets);
        Assert.Equal(before, vm.DrillDownWorks);
        Assert.All(vm.DrillDownWorks, c => Assert.True(c.HasCover1));
    }

    // ===== 閱讀軌跡 =====

    [Fact]
    public async Task ReadingTrack_Refresh_KeepsExpandedCountAndReusesItems()
    {
        var author = _store.UpsertAuthor("TrackAuthor");
        for (int i = 0; i < 30; i++)
        {
            string path = Path.Combine(_rootLibDir, $"done{i:00}.zip");
            SeedWork(path, $"Done {i:00}", author, null);
            _store.SetReadStatus(path, ReadStatus.Completed);
        }

        using var vm = new ReadingTrackPageViewModel(_store, _coverService, runOnUiAsync: Inline, autoLoad: false);
        await vm.RefreshAsync();
        vm.SelectCompletedTabCommand.Execute(null);
        Assert.Equal(24, vm.ActiveRecordWorks.Count);
        vm.LoadMoreRecordsCommand.Execute(null);
        Assert.Equal(30, vm.ActiveRecordWorks.Count);
        var before = vm.ActiveRecordWorks.ToList();

        var events = new List<NotifyCollectionChangedAction>();
        vm.ActiveRecordWorks.CollectionChanged += (_, e) => events.Add(e.Action);
        await vm.RefreshAsync();

        // 已展開的 30 筆維持展開，項目沿用、清單不發任何通知
        Assert.Equal(30, vm.ActiveRecordWorks.Count);
        Assert.Equal(before, vm.ActiveRecordWorks);
        Assert.Empty(events);

        // 切換分頁才收回初始筆數
        vm.SelectRecentlyAddedTabCommand.Execute(null);
        vm.SelectCompletedTabCommand.Execute(null);
        Assert.Equal(24, vm.ActiveRecordWorks.Count);
    }

    [Fact]
    public async Task ReadingTrack_MarkCompleted_MovedItemAdoptsLoadedCover()
    {
        EnsureAvaloniaSkiaBackend();
        var author = _store.UpsertAuthor("MoveAuthor");
        string path = await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, "move.zip"), "Move Me", author, "9999");
        _store.RecordRead(path);

        using var vm = new ReadingTrackPageViewModel(_store, _coverService, runOnUiAsync: Inline, autoLoad: false, isReadOnlyDatabase: false);
        await vm.RefreshAsync();
        var inProgress = Assert.Single(vm.InProgressWorks);
        await vm.RequestCoverAsync(inProgress);
        Assert.True(inProgress.HasCover1);

        // 「最近閱讀」分頁也有這本；記錄新項目「放進清單當下」是否已有封面。
        // 背景預載在放進清單之後才開始，所以當下有封面只可能來自沿用（封面服務的記憶體快取會回傳同一個點陣圖，
        // 事後比對點陣圖無法區分「沿用」與「重新載入」）
        var recent = Assert.Single(vm.ActiveRecordWorks);
        Assert.Equal(path, recent.Path);
        bool? insertedWithCover = null;
        vm.ActiveRecordWorks.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is { Count: > 0 } added && added[0] is ReadingWorkItemVm inserted)
            {
                insertedWithCover = inserted.HasCover1;
            }
        };

        inProgress.MarkCompletedCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.InProgressWorks.Count == 0);

        var updated = Assert.Single(vm.ActiveRecordWorks);
        Assert.NotSame(recent, updated);
        Assert.Equal(ReadStatus.Completed, updated.ReadStatus);
        Assert.True(insertedWithCover);
        Assert.Same(inProgress.Cover1, updated.Cover1);
    }

    // ===== 輔助 =====

    private void SeedWork(string path, string title, long authorId, string? thumbKey, params long[] tagIds)
    {
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
            PageCount = 20,
            ReadStatus = ReadStatus.Unread,
            ParsedAt = 1000,
        });

        foreach (var tid in tagIds)
        {
            _store.AssociateTag(assetId, tid);
        }
    }

    private async Task<string> SeedWorkWithThumbAsync(string path, string title, long authorId, string thumbKey)
    {
        var png = Path.Combine(_tempDir, $"src_{thumbKey}.png");
        WriteSolidPng(png, 300, 420, new SKColor(80, 120, 200));
        var jpeg = await _coverService.ExtractCoverJpegBytesAsync(png);
        Assert.NotNull(jpeg);
        _thumbStore.Write(thumbKey, jpeg!);
        SeedWork(path, title, authorId, thumbKey);
        return path;
    }

    private static ExploreWorkItem CreateWorkItem(long id, string name, IReadOnlyList<ExploreTagBadge> tags)
    {
        return new ExploreWorkItem(
            id, $@"C:\fake\{name}.zip", $"{name}.zip", name, null, string.Empty, null, null, 10,
            ReadStatus.Unread, 0, null, 0, null, null, tags);
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
}
