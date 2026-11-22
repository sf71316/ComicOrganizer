using Avalonia.Skia;
using Avalonia.Threading;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 畫廊跨目錄切換（進入子目錄／回到漫畫庫根目錄）不再由上往下補圖：
/// 記憶體快取同步查詢、封面載入名額提早交還、側欄／麵包屑／作品清單一次套用。
/// 使用暫存 SQLite 與目錄，不存取正式資料。
/// </summary>
public sealed class CrossFolderCoverTests : IDisposable
{
    private static readonly object AvaloniaInitLock = new();
    private static bool _avaloniaSkiaInitialized;

    private readonly string _tempDir;
    private readonly string _rootLibDir;
    private readonly SqliteAssetMetaStore _store;
    private readonly FileSystemThumbStore _thumbStore;
    private readonly SkiaCoverService _coverService;

    public CrossFolderCoverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_cross_folder_{Guid.NewGuid():N}");
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

    // ===== SkiaCoverService.TryGetMemoryCachedCover =====

    [Fact]
    public async Task TryGetMemoryCachedCover_OnlyHitsMemory_AndFindsOriginalSizeKeyForNarrowImages()
    {
        EnsureAvaloniaSkiaBackend();
        await WriteThumbAsync("aaaa", 300, 420);

        // 冷快取：只查記憶體，不讀磁碟縮圖
        var bigBox = new CoverDecodeSize(1000, 1400);
        Assert.False(_coverService.TryGetMemoryCachedCover("x.zip", "aaaa", bigBox, out _));

        // 縮圖（330 寬）比目標框窄：非同步載入會以「原尺寸」key 存放，同步查詢也要找得到
        var loaded = await _coverService.LoadOrExtractCoverAsync("x.zip", "aaaa", default, bigBox);
        Assert.NotNull(loaded);
        Assert.True(_coverService.TryGetMemoryCachedCover("x.zip", "aaaa", bigBox, out var hit));
        Assert.Same(loaded, hit);

        // 原尺寸請求也命中同一張
        Assert.True(_coverService.TryGetMemoryCachedCover("x.zip", "aaaa", CoverDecodeSize.Original, out var hitOriginal));
        Assert.Same(loaded, hitOriginal);

        // 框比圖窄：非同步載入會另外縮小解碼一張存在自己的 key，同步查詢不拿原尺寸的大張代替
        Assert.False(_coverService.TryGetMemoryCachedCover("x.zip", "aaaa", new CoverDecodeSize(100, 140), out _));
    }

    // ===== CoverSlotVm =====

    [Fact]
    public async Task CoverSlot_EnsureLoadedOnUiThread_MemoryHit_SetsImageWithoutWaitingForInflightLoad()
    {
        EnsureAvaloniaSkiaBackend();
        await WriteThumbAsync("bbbb", 300, 420);
        var warm = await _coverService.LoadOrExtractCoverAsync("b.zip", "bbbb");
        Assert.NotNull(warm);

        var slot = new CoverSlotVm(CreateWorkItem(1, "b.zip", "bbbb"), "#888888");

        // 模擬切換目錄：背景預載先開始了這張的載入，但名額全被占住（UI 忙著排版時就是這樣）
        using var busyLimiter = new SemaphoreSlim(0, 4);
        Task? inflight = null;
        await Task.Run(() =>
        {
            inflight = slot.EnsureLoadedAsync(_coverService, busyLimiter, InlineRunOnUi);
        });
        Assert.False(inflight!.IsCompleted);

        // 卡片在 UI 執行緒建立：記憶體快取有這張就當下設好，不必等那筆排隊中的載入
        bool hasImageRightAway = false;
        AvaloniaTestThread.Run(() =>
        {
            _ = slot.EnsureLoadedAsync(_coverService, busyLimiter, ExploreCardVmBase.DefaultRunOnUiAsync);
            hasImageRightAway = slot.HasImage;
        });

        Assert.True(hasImageRightAway);
        Assert.Same(warm, slot.Image);

        // 交還名額後，排隊中的那筆發現封面已設好，直接結束
        busyLimiter.Release(4);
        await inflight.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Same(warm, slot.Image);
    }

    [Fact]
    public async Task CoverSlot_ReleasesConcurrencySlotBeforeHoppingToUiThread()
    {
        EnsureAvaloniaSkiaBackend();
        await WriteThumbAsync("cccc", 300, 420);
        var slot = new CoverSlotVm(CreateWorkItem(1, "c.zip", "cccc"), "#888888");

        using var limiter = new SemaphoreSlim(1, 1);
        var uiGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action? pendingUiAction = null;
        Task BlockedRunOnUi(Action action)
        {
            // 模擬 UI 執行緒正忙：設定封面的工作要等一陣子才輪到
            pendingUiAction = action;
            return uiGate.Task;
        }

        var load = slot.EnsureLoadedAsync(_coverService, limiter, BlockedRunOnUi);
        await WaitUntilAsync(() => pendingUiAction != null);

        // 封面已解碼、等著回 UI 執行緒設定：名額已交還，其他卡片可以先開始讀檔
        Assert.Equal(1, limiter.CurrentCount);
        Assert.False(slot.HasImage);

        pendingUiAction!();
        uiGate.SetResult();
        await load.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(slot.HasImage);
        Assert.Equal(1, limiter.CurrentCount);
    }

    // ===== 畫廊跨目錄切換 =====

    [Fact]
    public async Task Gallery_NavigateBackToRoot_SidebarBreadcrumbsAndItemsInOneUiUpdate_WithCachedCovers()
    {
        EnsureAvaloniaSkiaBackend();
        var author = _store.UpsertAuthor("NavAuthor");
        var rootWorks = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            rootWorks.Add(await SeedWorkWithThumbAsync(Path.Combine(_rootLibDir, $"r{i}.zip"), $"Root {i}", author, $"r{i}r{i}"));
        }

        string subDir = Path.Combine(_rootLibDir, "Sub");
        Directory.CreateDirectory(Path.Combine(subDir, "Deeper"));
        string subWork = await SeedWorkWithThumbAsync(Path.Combine(subDir, "s0.zip"), "Sub 0", author, "s0s0");

        var ui = new UiJobRecorder();
        using var vm = new GalleryPageViewModel(_store, _coverService, rootDirectory: _rootLibDir, runOnUiAsync: ui.RunAsync, autoLoad: false);
        await vm.ReloadAllAsync();
        await WaitUntilAsync(() => !vm.IsLoading && vm.Items.Count(i => !i.IsFolder) == rootWorks.Count);

        // 第一次看根目錄：卡片建立時載入封面（之後就在記憶體快取裡）
        foreach (var item in vm.Items.Where(i => !i.IsFolder).ToList())
        {
            Task? realize = null;
            AvaloniaTestThread.Run(() => realize = vm.NotifyItemRealized(item));
            await realize!;
        }
        await WaitUntilAsync(() => vm.Items.Where(i => !i.IsFolder).All(i => i.HasCover1));

        AvaloniaTestThread.Run(() => vm.NavigateToFolder(subDir));
        await WaitUntilAsync(() => !vm.IsLoading && vm.Items.Any(i => i.Path == subWork));

        // 記錄回到根目錄時，側欄、麵包屑、作品清單各在第幾次 UI 更新中變動，以及那次更新結束時已有封面的卡片數
        var sidebarJobs = new HashSet<int>();
        var breadcrumbJobs = new HashSet<int>();
        var itemJobs = new HashSet<int>();
        int coveredAtItemsJobEnd = -1;
        vm.SidebarNodes.CollectionChanged += (_, _) => sidebarJobs.Add(ui.CurrentJob);
        vm.Breadcrumbs.CollectionChanged += (_, _) => breadcrumbJobs.Add(ui.CurrentJob);
        vm.Items.CollectionChanged += (_, _) => itemJobs.Add(ui.CurrentJob);
        ui.AfterJob = job =>
        {
            if (itemJobs.Contains(job))
            {
                coveredAtItemsJobEnd = vm.Items.Count(i => !i.IsFolder && i.HasCover1);
            }
        };

        AvaloniaTestThread.Run(() => vm.NavigateToFolder(_rootLibDir));
        await WaitUntilAsync(() => !vm.IsLoading && vm.Items.Count(i => !i.IsFolder) == rootWorks.Count);

        // 1. 側欄、麵包屑與作品清單在同一次 UI 更新內完成（不是先換側欄、下一拍才換作品）
        int itemsJob = Assert.Single(itemJobs);
        Assert.NotEqual(-1, itemsJob);
        Assert.Equal(new[] { itemsJob }, sidebarJobs);
        Assert.Equal(new[] { itemsJob }, breadcrumbJobs);

        // 2. 那次更新結束時（畫面繪製前），記憶體快取裡的封面已全部設好，不必等背景載入一張張補上
        Assert.Equal(rootWorks.Count, coveredAtItemsJobEnd);
    }

    // ===== 輔助 =====

    /// <summary>
    /// 測試用 UI 派送：在 UI 執行緒上直接執行，否則排進 <see cref="Dispatcher.UIThread"/>。
    /// 記錄目前執行的是第幾次 UI 更新（巢狀呼叫算同一次），並在每次更新結束時呼叫 <see cref="AfterJob"/>。
    /// </summary>
    private sealed class UiJobRecorder
    {
        private int _nextJob;

        public int CurrentJob { get; private set; } = -1;

        public Action<int>? AfterJob { get; set; }

        public Task RunAsync(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Execute(action);
                return Task.CompletedTask;
            }

            return Dispatcher.UIThread.InvokeAsync(() => Execute(action)).GetTask();
        }

        private void Execute(Action action)
        {
            if (CurrentJob != -1)
            {
                action();
                return;
            }

            CurrentJob = ++_nextJob;
            try
            {
                action();
                AfterJob?.Invoke(CurrentJob);
            }
            finally
            {
                CurrentJob = -1;
            }
        }
    }

    private static Task InlineRunOnUi(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private async Task WriteThumbAsync(string thumbKey, int width, int height)
    {
        var png = Path.Combine(_tempDir, $"src_{thumbKey}.png");
        WriteSolidPng(png, width, height, new SKColor(80, 120, 200));
        var jpeg = await _coverService.ExtractCoverJpegBytesAsync(png);
        Assert.NotNull(jpeg);
        _thumbStore.Write(thumbKey, jpeg!);
    }

    private async Task<string> SeedWorkWithThumbAsync(string path, string title, long authorId, string thumbKey)
    {
        await WriteThumbAsync(thumbKey, 300, 420);
        _store.Upsert(new AssetMeta
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsFolder = false,
            FileSize = 1024,
            Mtime = 1000,
            CoverThumbKey = thumbKey,
            CoverWidth = 330,
            CoverHeight = 460,
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
        return path;
    }

    private static ExploreWorkItem CreateWorkItem(long id, string path, string thumbKey)
    {
        return new ExploreWorkItem(
            id, path, Path.GetFileName(path), Path.GetFileNameWithoutExtension(path), null, string.Empty, null, null, 10,
            ReadStatus.Unread, 0, null, 0, null, thumbKey, Array.Empty<ExploreTagBadge>());
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
