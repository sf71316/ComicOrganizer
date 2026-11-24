using System.IO.Compression;
using Avalonia.Skia;
using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

public sealed class WorkBasketAndPreviewTests : IDisposable
{
    private static readonly object SkiaLock = new();
    private static bool _skiaReady;
    private static readonly Func<Action, Task> InlineUi = static a => { a(); return Task.CompletedTask; };

    private readonly string _tempDir;
    private readonly SqliteAssetMetaStore _store;
    private readonly FileSystemThumbStore _thumbStore;
    private readonly SkiaCoverService _coverService;

    public WorkBasketAndPreviewTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_basket_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
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
        }
    }

    [Fact]
    public void WorkPreviewPageSampler_ComputesExpectedIndices_ForFrontAndUniformModes()
    {
        Assert.Empty(WorkPreviewPageSampler.ComputeSamplePageIndices(0, WorkPreviewSampleMode.FrontPages));
        Assert.Equal([0, 1, 2], WorkPreviewPageSampler.ComputeSamplePageIndices(3, WorkPreviewSampleMode.UniformSample, maxSamples: 6));
        Assert.Equal([0, 1, 2, 3, 4, 5], WorkPreviewPageSampler.ComputeSamplePageIndices(32, WorkPreviewSampleMode.FrontPages, maxSamples: 6));

        var uniform = WorkPreviewPageSampler.ComputeSamplePageIndices(32, WorkPreviewSampleMode.UniformSample, maxSamples: 6);
        Assert.Equal(6, uniform.Count);
        Assert.Equal(6, uniform.Distinct().Count());
        Assert.True(uniform.SequenceEqual(uniform.OrderBy(x => x)));
        Assert.True(uniform[^1] > 20);
    }

    [Fact]
    public void WorkBasketViewModel_IsInMemoryOnly_Deduplicates_Reorders_AutoRemovesAfterRead_AndSavesToVirtualLibrary()
    {
        var zip1 = CreateTestZip("book1.zip", pages: 3);
        var zip2 = CreateTestZip("book2.zip", pages: 4);
        var existingLibId = _store.CreateVirtualLibrary("既有精選", string.Empty, null);

        var reader = new FakeReaderLauncher();
        var preview = new FakePreviewLauncher();

        using var basket = new WorkBasketViewModel(
            _store,
            _coverService,
            reader,
            preview,
            isReadOnlyDatabase: false,
            toastManager: null,
            runOnUiAsync: InlineUi);

        // 1. 去重加入與順序調整
        var e1 = new WorkBasketEntry(zip1, "作品一", "作者A", PageCount: 3);
        var e2 = new WorkBasketEntry(zip2, "作品二", "作者B", PageCount: 4);
        Assert.True(basket.Add(e1));
        Assert.False(basket.Add(new WorkBasketEntry(zip1 + Path.DirectorySeparatorChar, "作品一", "作者A")));
        Assert.True(basket.Add(e2));
        Assert.Equal(2, basket.Count);
        Assert.True(basket.ContainsPath(zip1));

        basket.Items[1].MoveUpCommand.Execute(null);
        Assert.Equal(zip2, basket.Items[0].Path);
        Assert.Equal(zip1, basket.Items[1].Path);

        // 2. 預覽觸發與讀完後自動移出書籃
        basket.Items[0].PreviewCommand.Execute(null);
        Assert.Equal(zip2, preview.LastOpenedEntry?.Path);

        basket.AutoRemoveAfterRead = true;
        basket.Items[0].OpenInReaderCommand.Execute(null);
        Assert.Equal(zip2, reader.LastOpenedPath);
        reader.SimulateClosed();
        Assert.False(basket.ContainsPath(zip2));
        Assert.Equal(1, basket.Count);

        // 3. 儲存書籃至新書單與併入既有書單
        int savedEvents = 0;
        basket.VirtualLibrarySaved += () => savedEvents++;
        basket.NewLibraryName = "今晚待看";
        basket.SaveToLibraryCommand.Execute(null);
        Assert.Equal(1, savedEvents);

        var createdLib = Assert.Single(_store.ListVirtualLibraries(), l => l.Name == "今晚待看");
        var createdWorks = _store.ListManualVirtualLibraryWorks(createdLib.Id);
        Assert.Contains(zip1, createdWorks);

        basket.SelectedSaveTarget = Assert.Single(basket.SaveTargets, t => t.LibraryId == existingLibId);
        basket.SaveToLibraryCommand.Execute(null);
        Assert.Contains(zip1, _store.ListManualVirtualLibraryWorks(existingLibId));

        // 4. 新建立的書籃實例（模擬重開程式）：不載入舊暫存書籃，記憶體一開始必定為空；單獨刪除可移除
        using var reopenedBasket = new WorkBasketViewModel(
            _store,
            _coverService,
            reader,
            preview,
            isReadOnlyDatabase: false,
            toastManager: null,
            runOnUiAsync: InlineUi);
        Assert.Equal(0, reopenedBasket.Count);
        Assert.True(reopenedBasket.IsEmpty);

        basket.Items[0].RemoveCommand.Execute(null);
        Assert.True(basket.IsEmpty);
    }

    [Fact]
    public async Task WorkPreviewViewModel_LoadsFrontAndUniformSamples_AndZoomsSelectedPage()
    {
        EnsureSkia();
        var zipPath = CreateTestZip("preview_12p.zip", pages: 12);
        var reader = new FakeReaderLauncher();
        var preview = new FakePreviewLauncher();

        using var basket = new WorkBasketViewModel(
            _store,
            _coverService,
            reader,
            preview,
            runOnUiAsync: InlineUi);
        var entry = new WorkBasketEntry(zipPath, "十二頁作品", "作者C", PageCount: 12);

        using var vm = new WorkPreviewViewModel(entry, basket, reader, runOnUiAsync: InlineUi);
        await vm.LoadingTask;

        Assert.Equal(12, vm.ResolvedTotalPages);
        Assert.Equal(6, vm.PreviewPages.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5], vm.PreviewPages.Select(p => p.PageIndexZeroBased).ToArray());
        Assert.All(vm.PreviewPages, p => Assert.True(p.HasThumbnail));

        // 切換「均勻抽 6 頁」後頁碼應涵蓋後段
        vm.SelectUniformSampleCommand.Execute(null);
        await vm.LoadingTask;
        Assert.Equal(6, vm.PreviewPages.Count);
        Assert.Contains(vm.PreviewPages, p => p.PageIndexZeroBased >= 9);

        // 點擊縮圖放大檢視
        vm.PreviewPages[1].SelectCommand.Execute(null);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while ((vm.IsZoomLoading || vm.ZoomBitmap == null) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(15);
        }
        Assert.NotNull(vm.ZoomBitmap);
        Assert.True(vm.PreviewPages[1].IsZoomed);

        // 預覽小窗內切換加入書籃應即時反映到 WorkBasketViewModel
        vm.ToggleBasketCommand.Execute(null);
        Assert.True(vm.IsInBasket);
        Assert.True(basket.ContainsPath(zipPath));
    }

    private string CreateTestZip(string name, int pages)
    {
        var path = Path.Combine(_tempDir, name);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        for (int i = 1; i <= pages; i++)
        {
            var e = zip.CreateEntry($"{i:D3}.png", CompressionLevel.NoCompression);
            using var es = e.Open();
            var bytes = CreateSolidPngBytes(120, 160, new SKColor((byte)(i * 15 % 255), 90, 200));
            es.Write(bytes, 0, bytes.Length);
        }
        return path;
    }

    private static byte[] CreateSolidPngBytes(int w, int h, SKColor color)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void EnsureSkia()
    {
        if (_skiaReady) return;
        lock (SkiaLock)
        {
            if (_skiaReady) return;
            SkiaPlatform.Initialize();
            _skiaReady = true;
        }
    }

    private sealed class FakeReaderLauncher : IReaderWindowLauncher
    {
        public string? LastOpenedPath { get; private set; }
        private Action? _onClosed;

        public bool OpenInReader(string path, string? displayTitle, string? authorName, Action? onReaderClosed, out string? errorMessage)
        {
            LastOpenedPath = path;
            _onClosed = onReaderClosed;
            errorMessage = null;
            return true;
        }

        public void SimulateClosed() => _onClosed?.Invoke();
    }

    private sealed class FakePreviewLauncher : IWorkPreviewLauncher
    {
        public WorkBasketEntry? LastOpenedEntry { get; private set; }

        public bool OpenPreview(WorkBasketEntry entry, WorkBasketViewModel basket, IReaderWindowLauncher readerLauncher, out string? errorMessage)
        {
            LastOpenedEntry = entry;
            errorMessage = null;
            return true;
        }
    }

    [Fact]
    public async Task ExploreAndGallery_ShareWorkBasketAndPreview_AcrossAuthorsFoldersAndTags()
    {
        EnsureSkia();
        var libDir = Path.Combine(_tempDir, "lib");
        var authorDir = Path.Combine(libDir, "AuthorShared");
        var folderWorkDir = Path.Combine(authorDir, "ImageWorkFolder");
        Directory.CreateDirectory(folderWorkDir);
        await File.WriteAllBytesAsync(Path.Combine(folderWorkDir, "01.png"), CreateSolidPngBytes(60, 80, SKColors.Blue));
        await File.WriteAllBytesAsync(Path.Combine(folderWorkDir, "02.png"), CreateSolidPngBytes(60, 80, SKColors.Green));

        var zipPath = CreateTestZip("shared_zip.zip", pages: 3);
        var movedZipPath = Path.Combine(authorDir, "shared_zip.zip");
        File.Move(zipPath, movedZipPath);

        var authorId = _store.UpsertAuthor("AuthorShared", folderPath: authorDir);
        var tagId = _store.UpsertTag("pure", "content");

        _store.UpsertCatalogOnly(new AssetMeta { Path = authorDir, Name = "AuthorShared", IsFolder = true, Mtime = 10 });
        _store.Upsert(new AssetMeta { Path = movedZipPath, Name = "shared_zip.zip", IsFolder = false, FileSize = 1024, Mtime = 20, LastScanned = 20 });
        var zipAssetId = _store.GetAssetId(movedZipPath)!.Value;
        _store.UpsertMetadata(new AssetMetadata { AssetId = zipAssetId, AuthorId = authorId, Title = "Shared Zip Work", PageCount = 3, ParsedAt = 20 });
        _store.AssociateTag(zipAssetId, tagId);

        _store.Upsert(new AssetMeta { Path = folderWorkDir, Name = "ImageWorkFolder", IsFolder = true, Mtime = 30, LastScanned = 30 });
        var dirAssetId = _store.GetAssetId(folderWorkDir)!.Value;
        _store.UpsertMetadata(new AssetMetadata { AssetId = dirAssetId, AuthorId = authorId, Title = "Image Folder Work", PageCount = 2, ParsedAt = 30 });
        _store.AssociateTag(dirAssetId, tagId);

        var reader = new FakeReaderLauncher();
        var preview = new FakePreviewLauncher();
        using var basket = new WorkBasketViewModel(
            _store,
            _coverService,
            reader,
            preview,
            runOnUiAsync: InlineUi);

        using var exploreVm = new ExplorePageViewModel(
            new ExploreWallService(_store),
            _coverService,
            autoLoad: true,
            runOnUiAsync: InlineUi,
            readerLauncher: reader,
            workBasket: basket);

        using var galleryVm = new GalleryPageViewModel(
            _store,
            _coverService,
            rootDirectory: libDir,
            runOnUiAsync: InlineUi,
            autoLoad: false,
            readerLauncher: reader,
            workBasket: basket);

        await exploreVm.InitializationTask;
        await WaitUntilAsync(() => !exploreVm.IsLoading && exploreVm.AuthorCards.Count == 1);

        // 1. 探索牆鑽取作品並點選「挑書」＋「預覽」
        exploreVm.AuthorCards[0].SelectCardCommand.Execute(null);
        await WaitUntilAsync(() => exploreVm.IsDrillDownMode && !exploreVm.IsLoading && exploreVm.DrillDownWorks.Count == 2);

        var exploreZipCard = Assert.Single(exploreVm.DrillDownWorks, w => w.Path == movedZipPath);
        exploreZipCard.PreviewCommand.Execute(null);
        Assert.Equal(movedZipPath, preview.LastOpenedEntry?.Path);

        exploreZipCard.ToggleBasketCommand.Execute(null);
        Assert.True(exploreZipCard.IsInBasket);
        Assert.Equal(1, basket.Count);

        // 2. 切換到畫廊「目錄」根層：作者目錄卡 CanPreviewAndBasket 應為 false
        await galleryVm.ReloadAllAsync();
        await WaitUntilAsync(() => !galleryVm.IsLoading && galleryVm.Items.Count == 1);
        var authorFolderItem = Assert.Single(galleryVm.Items);
        Assert.True(authorFolderItem.IsAuthorFolder);
        Assert.False(authorFolderItem.CanPreviewAndBasket);

        // 進入作者目錄：.zip 與純圖資料夾作品皆可預覽與挑書，且 shared_zip 同步已入籃
        await galleryVm.NavigateToFolderAndSelectWorkAsync(authorDir);
        await WaitUntilAsync(() => !galleryVm.IsLoading && galleryVm.Items.Count == 2);

        var galleryZipItem = Assert.Single(galleryVm.Items, i => i.Path == movedZipPath);
        var galleryFolderWorkItem = Assert.Single(galleryVm.Items, i => i.Path == folderWorkDir);
        Assert.True(galleryZipItem.CanPreviewAndBasket);
        Assert.True(galleryZipItem.IsInBasket);
        Assert.True(galleryFolderWorkItem.IsWorkFolder);
        Assert.True(galleryFolderWorkItem.CanPreviewAndBasket);
        Assert.False(galleryFolderWorkItem.IsInBasket);

        // 在畫廊點圖片資料夾的「預覽」與「挑書」，會同步進同一個書籃
        galleryFolderWorkItem.PreviewCommand.Execute(null);
        Assert.Equal(folderWorkDir, preview.LastOpenedEntry?.Path);

        galleryFolderWorkItem.ToggleBasketCommand.Execute(null);
        Assert.True(galleryFolderWorkItem.IsInBasket);
        Assert.Equal(2, basket.Count);

        // 3. 畫廊切換「作者」與「標籤」頁籤同樣保持共用書籃與狀態
        galleryVm.SelectAuthorsSidebarCommand.Execute(null);
        await WaitUntilAsync(() => !galleryVm.IsLoading && galleryVm.SidebarNodes.Count == 1);
        galleryVm.SidebarNodes[0].SelectCommand.Execute(null);
        await WaitUntilAsync(() => !galleryVm.IsLoading && galleryVm.Items.Count == 2);
        Assert.All(galleryVm.Items, i => Assert.True(i.IsInBasket));

        galleryVm.SelectTagsSidebarCommand.Execute(null);
        await WaitUntilAsync(() => !galleryVm.IsLoading && galleryVm.SidebarNodes.Count == 1);
        galleryVm.SidebarNodes[0].SelectCommand.Execute(null);
        await WaitUntilAsync(() => !galleryVm.IsLoading && galleryVm.Items.Count == 2);
        Assert.All(galleryVm.Items, i => Assert.True(i.IsInBasket));

        // 4. 從書籃單獨移除 shared_zip：探索牆與畫廊兩邊卡片狀態立即同步取消
        Assert.True(basket.RemoveByPath(movedZipPath));
        Assert.False(exploreZipCard.IsInBasket);
        Assert.False(galleryVm.Items.Single(i => i.Path == movedZipPath).IsInBasket);
        Assert.True(galleryVm.Items.Single(i => i.Path == folderWorkDir).IsInBasket);
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
