using System.Drawing;
using System.Drawing.Imaging;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 1 Core 層測試：
/// - virtual_library_item 手動閱讀清單 CRUD / 查詢 / ON DELETE CASCADE
/// - MyVirtualLibraryNode.ResolveWorks 合併 DSL 與手動清單（含空 DSL 與去重）
/// - CoverManager.RefreshCoverAsync 強制清舊縮圖並重解碼寫回快取
/// </summary>
public class WorkItem1CoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _thumbDir;
    private readonly SqliteAssetMetaStore _store;

    public WorkItem1CoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_wi1_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "cache.db");
        _thumbDir = Path.Combine(_tempDir, "thumbs");
        Directory.CreateDirectory(_thumbDir);
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private long SeedAsset(string path, bool isFolder = false)
    {
        _store.UpsertCatalogOnly(new AssetMeta
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsFolder = isFolder,
            FileSize = isFolder ? null : 1024,
            Mtime = 1700000000,
            LastScanned = 1700000000,
        });
        return _store.GetAssetId(path)!.Value;
    }

    private static void SaveSolidPng(string filePath, Color color, int width = 80, int height = 100)
    {
        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(color);
        }
        bmp.Save(filePath, ImageFormat.Png);
    }

    // ===== 1. 手動閱讀清單 CRUD & 查詢 =====

    [Fact]
    public void AddWorkToVirtualLibrary_ExistingAsset_And_Idempotent()
    {
        var libId = _store.CreateVirtualLibrary("精選", "", null);
        var work1 = @"C:\Library\AuthorA\Work1.zip";
        var work2 = @"C:\Library\AuthorA\Work2.zip";
        SeedAsset(work1);
        SeedAsset(work2);

        Assert.True(_store.AddWorkToVirtualLibrary(libId, work1));
        Thread.Sleep(5); // 確保 added_at 不同以驗證排序
        Assert.True(_store.AddWorkToVirtualLibrary(libId, work2));
        // 重複加入同一作品應成功（INSERT OR IGNORE）且不產生重複列
        Assert.True(_store.AddWorkToVirtualLibrary(libId, work1));

        var works = _store.ListManualVirtualLibraryWorks(libId);
        Assert.Equal(2, works.Count);
        // 依 added_at DESC, a.id DESC：後加入的 work2 在前
        Assert.Equal(work2, works[0]);
        Assert.Equal(work1, works[1]);

        var libIds = _store.ListVirtualLibraryIdsForWork(work1);
        Assert.IsType<HashSet<long>>(libIds);
        Assert.Single(libIds);
        Assert.Contains(libId, libIds);
    }

    [Fact]
    public void AddWorkToVirtualLibrary_UnscannedDiskPath_AutoCreatesAssetRow()
    {
        var libId = _store.CreateVirtualLibrary("未掃描清單", "", null);
        var fileOnDisk = Path.Combine(_tempDir, "unscanned_book.zip");
        File.WriteAllBytes(fileOnDisk, new byte[] { 1, 2, 3, 4 });

        var folderOnDisk = Path.Combine(_tempDir, "unscanned_folder");
        Directory.CreateDirectory(folderOnDisk);

        Assert.Null(_store.GetAssetId(fileOnDisk));
        Assert.Null(_store.GetAssetId(folderOnDisk));

        Assert.True(_store.AddWorkToVirtualLibrary(libId, fileOnDisk));
        Assert.True(_store.AddWorkToVirtualLibrary(libId, folderOnDisk));

        Assert.NotNull(_store.GetAssetId(fileOnDisk));
        Assert.NotNull(_store.GetAssetId(folderOnDisk));

        var works = _store.ListManualVirtualLibraryWorks(libId);
        Assert.Equal(2, works.Count);
        Assert.Contains(fileOnDisk, works);
        Assert.Contains(folderOnDisk, works);
    }

    [Fact]
    public void AddWorkToVirtualLibrary_InvalidLibraryOrMissingPath_ReturnsFalse()
    {
        var libId = _store.CreateVirtualLibrary("清單", "", null);
        var validPath = @"C:\Library\Valid.zip";
        SeedAsset(validPath);

        // 不存在的 libraryId
        Assert.False(_store.AddWorkToVirtualLibrary(999999, validPath));
        // 不存在於 DB 且磁碟無此檔
        Assert.False(_store.AddWorkToVirtualLibrary(libId, Path.Combine(_tempDir, "not_exists.zip")));
        // 空白路徑
        Assert.False(_store.AddWorkToVirtualLibrary(libId, "   "));

        Assert.Empty(_store.ListManualVirtualLibraryWorks(libId));
    }

    [Fact]
    public void RemoveWorkFromVirtualLibrary_WorksAndReturnsAffectedStatus()
    {
        var libA = _store.CreateVirtualLibrary("A", "", null);
        var libB = _store.CreateVirtualLibrary("B", "", null);
        var work = @"C:\Library\Work.zip";
        SeedAsset(work);

        _store.AddWorkToVirtualLibrary(libA, work);
        _store.AddWorkToVirtualLibrary(libB, work);

        var idsBefore = _store.ListVirtualLibraryIdsForWork(work);
        Assert.Equal(2, idsBefore.Count);

        Assert.True(_store.RemoveWorkFromVirtualLibrary(libA, work));
        // 再次移除已不在清單的作品應回 false
        Assert.False(_store.RemoveWorkFromVirtualLibrary(libA, work));
        Assert.False(_store.RemoveWorkFromVirtualLibrary(libA, @"C:\Library\NeverAdded.zip"));

        Assert.Empty(_store.ListManualVirtualLibraryWorks(libA));
        Assert.Single(_store.ListManualVirtualLibraryWorks(libB));

        var idsAfter = _store.ListVirtualLibraryIdsForWork(work);
        Assert.Single(idsAfter);
        Assert.Contains(libB, idsAfter);
        Assert.DoesNotContain(libA, idsAfter);
    }

    [Fact]
    public void CascadeDelete_RemovesVirtualLibraryItems_WhenLibraryOrAssetDeleted()
    {
        var lib1 = _store.CreateVirtualLibrary("L1", "", null);
        var lib2 = _store.CreateVirtualLibrary("L2", "", null);
        var work1 = @"C:\Library\W1.zip";
        var work2 = @"C:\Library\W2.zip";
        SeedAsset(work1);
        SeedAsset(work2);

        _store.AddWorkToVirtualLibrary(lib1, work1);
        _store.AddWorkToVirtualLibrary(lib1, work2);
        _store.AddWorkToVirtualLibrary(lib2, work1);

        // 1. 刪除 virtual_library → cascade 清除該清單關聯
        _store.DeleteVirtualLibrary(lib1);
        Assert.Empty(_store.ListManualVirtualLibraryWorks(lib1));
        var w1Libs = _store.ListVirtualLibraryIdsForWork(work1);
        Assert.Single(w1Libs);
        Assert.Contains(lib2, w1Libs);

        // 2. 刪除 asset → cascade 清除該作品在所有清單的關聯
        _store.Delete(work1);
        Assert.Empty(_store.ListManualVirtualLibraryWorks(lib2));
        Assert.Empty(_store.ListVirtualLibraryIdsForWork(work1));
    }

    // ===== 2. MyVirtualLibraryNode.ResolveWorks 合併與去重 =====

    [Fact]
    public void MyVirtualLibraryNode_EmptyDsl_ReturnsManualWorksOnly()
    {
        var libId = _store.CreateVirtualLibrary("純手動清單", "   ", null);
        var w1 = @"C:\Library\Manual1.zip";
        var w2 = @"C:\Library\Manual2.zip";
        SeedAsset(w1);
        SeedAsset(w2);

        _store.AddWorkToVirtualLibrary(libId, w1);
        Thread.Sleep(5);
        _store.AddWorkToVirtualLibrary(libId, w2);

        var lib = _store.GetVirtualLibrary(libId)!;
        var node = new MyVirtualLibraryNode(lib);
        var works = node.ResolveWorks(_store);

        Assert.Equal(new[] { w2, w1 }, works);
    }

    [Fact]
    public void MyVirtualLibraryNode_CombinesDslAndManual_PreservesOrderAndDeduplicatesCaseInsensitively()
    {
        var authorId = _store.UpsertAuthor("Flugel");
        var dslOnly = @"C:\Library\Flugel\DslOnly.zip";
        var overlapUpper = @"C:\Library\Flugel\Overlap.zip";
        var overlapLower = @"c:\library\flugel\overlap.zip";
        var manualOnly = @"C:\Library\Other\ManualOnly.zip";

        var idDsl = SeedAsset(dslOnly);
        var idOverlap = SeedAsset(overlapUpper);
        SeedAsset(overlapLower);
        SeedAsset(manualOnly);

        _store.UpsertMetadata(new AssetMetadata { AssetId = idDsl, AuthorId = authorId, Title = "DslOnly", ParsedAt = 100 });
        _store.UpsertMetadata(new AssetMetadata { AssetId = idOverlap, AuthorId = authorId, Title = "Overlap", ParsedAt = 100 });

        var libId = _store.CreateVirtualLibrary("混合清單", "artist:Flugel", null);
        // 加入：完全同筆 overlapUpper、大小寫不同的 overlapLower、以及純手動 manualOnly
        _store.AddWorkToVirtualLibrary(libId, overlapUpper);
        Thread.Sleep(5);
        _store.AddWorkToVirtualLibrary(libId, overlapLower);
        Thread.Sleep(5);
        _store.AddWorkToVirtualLibrary(libId, manualOnly);

        var lib = _store.GetVirtualLibrary(libId)!;
        var node = new MyVirtualLibraryNode(lib);
        var works = node.ResolveWorks(_store);

        // DSL 結果先出現（SearchExecutor 依 path 排序：DslOnly, Overlap），再接去重後的手動清單（manualOnly）
        Assert.Equal(3, works.Count);
        Assert.Equal(dslOnly, works[0]);
        Assert.Equal(overlapUpper, works[1]);
        Assert.Equal(manualOnly, works[2]);
    }

    // ===== 3. CoverManager.RefreshCoverAsync =====

    [Fact]
    public async Task RefreshCoverAsync_BypassesStaleCache_DeletesOldThumbAndWritesNewCover()
    {
        var workDir = Path.Combine(_tempDir, "MangaWork");
        Directory.CreateDirectory(workDir);
        var coverPath = Path.Combine(workDir, "cover.png");
        SaveSolidPng(coverPath, Color.Red);

        var originalMtime = Directory.GetLastWriteTimeUtc(workDir);
        var config = new AppConfig { MinCoverFileSizeBytes = 0 };
        var thumbStore = new FileSystemThumbStore(_thumbDir);
        var coverManager = new CoverManager(config);

        // Step 1: 首次載入並透過 CacheWriter 寫入快取（dispose 等 flush）
        using (var writer1 = new CacheWriter(_store, thumbStore, config))
        {
            coverManager.AttachCache(_store, thumbStore, writer1);
            using var initialImg = await coverManager.GetCoverAsync(workDir, CoverManager.ThumbWidth);
            Assert.NotNull(initialImg);
        }

        var metaBefore = _store.Get(workDir);
        Assert.NotNull(metaBefore);
        Assert.NotNull(metaBefore!.CoverThumbKey);

        // Step 2: 替換磁碟上的封面成藍色，並刻意把資料夾 mtime 調回原值模擬「mtime 未變但封面已改」
        SaveSolidPng(coverPath, Color.Blue);
        Directory.SetLastWriteTimeUtc(workDir, originalMtime);

        using (var writer2 = new CacheWriter(_store, thumbStore, config))
        {
            coverManager.AttachCache(_store, thumbStore, writer2);

            // 未強制刷新時，磁碟快取縮圖仍為舊的紅色版本，且 GetCoverAsync 直接命中快取
            using (var oldThumbStream = thumbStore.OpenRead(metaBefore.CoverThumbKey!))
            {
                Assert.NotNull(oldThumbStream);
                using var oldThumbBmp = new Bitmap(oldThumbStream!);
                var oldPixel = oldThumbBmp.GetPixel(oldThumbBmp.Width / 2, oldThumbBmp.Height / 2);
                Assert.True(oldPixel.R > 200 && oldPixel.B < 50,
                    $"Expected cached red thumb before refresh, got {oldPixel}");
            }
            using (var staleImg = await coverManager.GetCoverAsync(workDir, CoverManager.ThumbWidth))
            {
                Assert.NotNull(staleImg);
            }

            // Step 3: 呼叫 RefreshCoverAsync 應忽略舊快取、刪除舊縮圖並重讀藍色封面
            using var refreshedImg = await coverManager.RefreshCoverAsync(workDir, CoverManager.ThumbWidth);
            Assert.NotNull(refreshedImg);
            using var refreshedBmp = new Bitmap(refreshedImg!);
            var refreshedPixel = refreshedBmp.GetPixel(refreshedBmp.Width / 2, refreshedBmp.Height / 2);
            Assert.True(refreshedPixel.B > 200 && refreshedPixel.R < 50,
                $"Expected refreshed blue pixel, got {refreshedPixel}");
        }

        // Step 4: CacheWriter drain 後，磁碟 thumb 檔也必須已更新為藍色
        var metaAfter = _store.Get(workDir);
        Assert.NotNull(metaAfter?.CoverThumbKey);
        using var thumbStream = thumbStore.OpenRead(metaAfter!.CoverThumbKey!);
        Assert.NotNull(thumbStream);
        using var thumbBmp = new Bitmap(thumbStream!);
        var thumbPixel = thumbBmp.GetPixel(thumbBmp.Width / 2, thumbBmp.Height / 2);
        Assert.True(thumbPixel.B > 200 && thumbPixel.R < 50,
            $"Expected updated blue thumb on disk, got {thumbPixel}");
    }
}
