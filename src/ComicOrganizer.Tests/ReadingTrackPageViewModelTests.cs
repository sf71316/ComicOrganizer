using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// Phase 2 W2.3「🕒 閱讀軌跡」專頁 ViewModel 與唯讀保護單元測試。
/// </summary>
public sealed class ReadingTrackPageViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteAssetMetaStore _store;
    private readonly FileSystemThumbStore _thumbStore;
    private readonly SkiaCoverService _coverService;

    public ReadingTrackPageViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ComicOrg_TrackVmTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SqliteAssetMetaStore(Path.Combine(_tempDir, "cache.db"));
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
    public async Task ReadingTrack_LoadsInProgressWithProgressBar_RecordTabs_Ranks_AndCrossPageNavigation()
    {
        var aFlugel = _store.UpsertAuthor("Flugel");
        var aTakeda = _store.UpsertAuthor("Takeda Hiromitsu");
        var tMaid = _store.UpsertTag("maid", "tag");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");

        string p1 = Path.Combine(_tempDir, "reading_work.zip");
        string p2 = Path.Combine(_tempDir, "completed_work.zip");
        string p3 = Path.Combine(_tempDir, "unread_work.zip");

        SeedWork(p1, "InProgress Maid", aFlugel, pageCount: 24, lastPage: 11, ReadStatus.Unread, tMaid);
        _store.RecordRead(p1); // 1 次讀過 → Reading，read_count=1
        _store.RecordRead(p1); // 2 次讀過

        SeedWork(p2, "Done Work", aTakeda, pageCount: 30, lastPage: 29, ReadStatus.Unread, tMaid);
        _store.RecordRead(p2);
        _store.SetReadStatus(p2, ReadStatus.Completed);

        SeedWork(p3, "Fresh Added", aTakeda, pageCount: 16, lastPage: null, ReadStatus.Unread);

        var fakeLauncher = new FakeLauncher();
        using var vm = new ReadingTrackPageViewModel(
            _store,
            _coverService,
            externalLauncher: fakeLauncher,
            runOnUiAsync: static action => { action(); return Task.CompletedTask; },
            autoLoad: false,
            isReadOnlyDatabase: false);

        await vm.RefreshAsync();

        // 1. 進行中清單與頁碼百分比（0-based lastPage=11 → 第 12 / 24 頁 = 50%）
        var inProg = Assert.Single(vm.InProgressWorks);
        Assert.Equal("InProgress Maid", inProg.Title);
        Assert.True(inProg.HasProgress);
        Assert.Equal(50.0, inProg.ProgressPercent);
        Assert.Contains("第 12 / 24 頁", inProg.ProgressText);

        // 點擊繼續閱讀應呼叫外部啟動器
        inProg.OpenCommand.Execute(null);
        Assert.Single(fakeLauncher.OpenedPaths);
        Assert.Equal(p1, fakeLauncher.OpenedPaths[0]);

        // 2. 近期紀錄 3 種分頁切換
        Assert.True(vm.IsRecentlyReadTabActive);
        Assert.Equal(2, vm.ActiveRecordWorks.Count);

        vm.SelectRecentlyAddedTabCommand.Execute(null);
        Assert.True(vm.IsRecentlyAddedTabActive);
        Assert.Equal(3, vm.ActiveRecordWorks.Count);

        vm.SelectCompletedTabCommand.Execute(null);
        Assert.True(vm.IsCompletedTabActive);
        var doneItem = Assert.Single(vm.ActiveRecordWorks);
        Assert.Equal("Done Work", doneItem.Title);

        // 3. 作者與標籤排行（Flugel 2 次 > Takeda 1 次；點擊排行觸發 OpenInGalleryRequested）
        Assert.Equal(2, vm.TopAuthors.Count);
        Assert.Equal("Flugel", vm.TopAuthors[0].Title);
        Assert.Equal(2, vm.TopAuthors[0].TotalReads);

        var topTag = Assert.Single(vm.TopTags);
        Assert.Equal("女僕", topTag.Title);
        Assert.Equal(3, topTag.TotalReads);

        string? navigatedDsl = null;
        vm.OpenInGalleryRequested += dsl => navigatedDsl = dsl;
        vm.TopAuthors[0].OpenInGalleryCommand.Execute(null);
        Assert.Equal("artist:Flugel", navigatedDsl);
        topTag.OpenInGalleryCommand.Execute(null);
        Assert.Equal("tag:女僕", navigatedDsl);

        // 4. 可寫入模式下標記已完成應更新 DB
        inProg.MarkCompletedCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading && vm.InProgressCount == 0 && vm.CompletedCount == 2);
        Assert.Equal(ReadStatus.Completed, _store.GetReadStatus(p1));
    }

    [Fact]
    public async Task ReadingTrack_ReadOnlyMode_BlocksStatusChangesAndResets()
    {
        var a1 = _store.UpsertAuthor("ProtectedArtist");
        string path = Path.Combine(_tempDir, "protected.zip");
        SeedWork(path, "Protected Work", a1, pageCount: 20, lastPage: 5, ReadStatus.Unread);
        _store.RecordRead(path);

        using var vm = new ReadingTrackPageViewModel(
            _store,
            _coverService,
            runOnUiAsync: static action => { action(); return Task.CompletedTask; },
            autoLoad: false,
            isReadOnlyDatabase: true);

        await vm.RefreshAsync();

        var item = Assert.Single(vm.InProgressWorks);
        item.MarkCompletedCommand.Execute(null);
        Assert.Equal(ReadStatus.Reading, _store.GetReadStatus(path));

        item.ResetUnreadCommand.Execute(null);
        Assert.Equal(ReadStatus.Reading, _store.GetReadStatus(path));
    }

    [Fact]
    public async Task ReadingTrack_OpenAuthorGallery_NavigatesToAuthorFolderOrArtistDslFallback()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"ComicOrg_TrackCtx_{Guid.NewGuid():N}");
        var rootDir = Path.Combine(Path.GetTempPath(), $"ComicOrg_TrackRoot_{Guid.NewGuid():N}");
        var authorFolder = Path.Combine(rootDir, "Flugel");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(authorFolder);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataDir, "appsettings.json"),
                System.Text.Json.JsonSerializer.Serialize(new ComicOrganizer.App.Models.AppConfig
                {
                    RootDirectory = rootDir
                }));

            using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
            var aFlugel = ctx.MetaStore.UpsertAuthor("Flugel");
            var aSolo = ctx.MetaStore.UpsertAuthor("SoloArtist");

            string folderWorkPath = Path.Combine(authorFolder, "Maid_Book.zip");
            string rootWorkPath = Path.Combine(rootDir, "Root_Book.zip");

            ctx.MetaStore.Upsert(new AssetMeta
            {
                Path = folderWorkPath,
                Name = Path.GetFileName(folderWorkPath),
                IsFolder = false,
                FileSize = 1024,
                Mtime = 1000,
                LastScanned = 1000,
            });
            long folderWorkId = ctx.MetaStore.GetAssetId(folderWorkPath)!.Value;
            ctx.MetaStore.UpsertMetadata(new AssetMetadata
            {
                AssetId = folderWorkId,
                AuthorId = aFlugel,
                Title = "Maid Book",
                PageCount = 20,
                LastPage = 2,
                ReadStatus = ReadStatus.Reading,
                ParsedAt = 1000,
            });
            ctx.MetaStore.RecordRead(folderWorkPath);

            ctx.MetaStore.Upsert(new AssetMeta
            {
                Path = rootWorkPath,
                Name = Path.GetFileName(rootWorkPath),
                IsFolder = false,
                FileSize = 1024,
                Mtime = 1000,
                LastScanned = 1000,
            });
            long rootWorkId = ctx.MetaStore.GetAssetId(rootWorkPath)!.Value;
            ctx.MetaStore.UpsertMetadata(new AssetMetadata
            {
                AssetId = rootWorkId,
                AuthorId = aSolo,
                Title = "Root Only Book",
                PageCount = 12,
                ReadStatus = ReadStatus.Unread,
                ParsedAt = 1000,
            });

            using var coverSvc = new SkiaCoverService(new FileSystemThumbStore(Path.Combine(dataDir, "thumbs")));
            using var vm = new ReadingTrackPageViewModel(ctx, coverSvc, autoLoad: false);
            await WaitUntilAsync(() => !vm.IsLoading);
            await vm.RefreshAsync();

            (string Folder, string? Work)? navigatedFolder = null;
            string? navigatedDsl = null;
            vm.OpenFolderInGalleryRequested += (folder, work) => navigatedFolder = (folder, work);
            vm.OpenInGalleryRequested += dsl => navigatedDsl = dsl;

            // 1. 位於作者子目錄的作品應優先導回該作者目錄並自動選取該作品（方案 A）
            var inProgItem = Assert.Single(vm.InProgressWorks);
            Assert.True(inProgItem.CanOpenAuthorGallery);
            inProgItem.OpenAuthorGalleryCommand.Execute(null);
            Assert.Equal((authorFolder, folderWorkPath), navigatedFolder);
            Assert.Null(navigatedDsl);

            // 2. 若作品直接散落於 RootDirectory 且具作者欄位，則退回 artist:"..." 篩選
            navigatedFolder = null;
            vm.SelectRecentlyAddedTabCommand.Execute(null);
            var rootWorkItem = vm.ActiveRecordWorks.Single(w => w.Path == rootWorkPath);
            rootWorkItem.OpenAuthorGalleryCommand.Execute(null);
            Assert.Null(navigatedFolder);
            Assert.Equal("artist:SoloArtist", navigatedDsl);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { }
            try { Directory.Delete(rootDir, true); } catch { }
        }
    }

    private void SeedWork(
        string path,
        string title,
        long authorId,
        int pageCount,
        int? lastPage,
        ReadStatus status,
        params long[] tagIds)
    {
        _store.Upsert(new AssetMeta
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsFolder = false,
            FileSize = 1024,
            Mtime = 1000,
            LastScanned = 1000,
        });

        var assetId = _store.GetAssetId(path)!.Value;
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = assetId,
            AuthorId = authorId,
            Title = title,
            PageCount = pageCount,
            LastPage = lastPage,
            ReadStatus = status,
            ParsedAt = 1000,
        });

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
