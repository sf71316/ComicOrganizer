using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// CatalogReconcileService 測試（#45 Phase 6.1）：
/// path-change 偵測 / orphan / ambiguous / Apply 保留 metadata / Delete orphan cascade。
///
/// 用 temp dir 模擬檔案系統 + temp DB 模擬 catalog。
/// </summary>
public class CatalogReconcileTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;
    private readonly AppConfig _config;
    private readonly CatalogReconcileService _service;

    public CatalogReconcileTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"comic_reconcile_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_reconcile_db_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);

        _config = new AppConfig { RootDirectory = _tempRoot };
        _service = new CatalogReconcileService(_config, _store);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_tempRoot))
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private string MkAuthorFolder(string name)
    {
        var p = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(p);
        return p;
    }

    private string MkWorkArchive(string authorFolder, string fileName, long sizeBytes = 1024)
    {
        var p = Path.Combine(authorFolder, fileName);
        File.WriteAllBytes(p, new byte[sizeBytes]);
        return p;
    }

    private string MkWorkFolder(string authorFolder, string folderName, int pageCount = 8)
    {
        var p = Path.Combine(authorFolder, folderName);
        Directory.CreateDirectory(p);
        for (int i = 0; i < pageCount; i++)
            File.WriteAllBytes(Path.Combine(p, $"{i:D2}.jpg"), new byte[10]);
        return p;
    }

    /// <summary>把現有 FS 路徑 register 進 DB。回 asset.id。</summary>
    private long RegisterAsset(string fsPath, bool isFolder)
    {
        long? size = null;
        if (!isFolder && File.Exists(fsPath)) size = new FileInfo(fsPath).Length;
        _store.Upsert(new AssetMeta
        {
            Path = fsPath,
            Name = Path.GetFileName(fsPath),
            IsFolder = isFolder,
            FileSize = size,
            Mtime = 1234567890,
        });
        return _store.GetAssetId(fsPath)!.Value;
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Empty_RootDir_Returns_Empty_Report()
    {
        var report = _service.Analyze();
        Assert.True(report.IsEmpty);
        Assert.Empty(report.PathChanges);
        Assert.Empty(report.Orphans);
    }

    [Fact]
    public void Synced_Catalog_No_Changes()
    {
        var author = MkAuthorFolder("[作者A]");
        var work = MkWorkArchive(author, "work1.zip");
        RegisterAsset(work, isFolder: false);

        var report = _service.Analyze();

        Assert.True(report.IsEmpty);
        Assert.Equal(1, report.DbAssetsScanned);
        Assert.Equal(1, report.FsWorksScanned);
    }

    [Fact]
    public void Work_Moved_To_Different_Author_Folder_Detects_PathChange()
    {
        // Setup: work 原本在 [作者A]，DB 記錄到那
        var authorA = MkAuthorFolder("[作者A]");
        var oldPath = MkWorkArchive(authorA, "work1.zip", 2048);
        var assetId = RegisterAsset(oldPath, isFolder: false);

        // 模擬使用者搬到 [作者B]
        var authorB = MkAuthorFolder("[作者B]");
        var newPath = Path.Combine(authorB, "work1.zip");
        File.Move(oldPath, newPath);

        var report = _service.Analyze();

        Assert.Single(report.PathChanges);
        Assert.Empty(report.Orphans);
        Assert.Empty(report.Ambiguous);

        var change = report.PathChanges[0];
        Assert.Equal(assetId, change.AssetId);
        Assert.Equal(oldPath, change.OldPath);
        Assert.Equal(newPath, change.NewPath);
        Assert.Equal(2048, change.Size);
    }

    [Fact]
    public void Work_Folder_Moved_Detects_PathChange_Even_With_Null_Size()
    {
        // folder 的 file_size 在 catalog 是 NULL，FS 端 reconcile 也視為 0 → 配對應該成功
        var authorA = MkAuthorFolder("[作者A]");
        var oldFolder = MkWorkFolder(authorA, "[作者A] 作品X");
        var assetId = RegisterAsset(oldFolder, isFolder: true);

        var authorB = MkAuthorFolder("[作者B]");
        var newFolder = Path.Combine(authorB, "[作者A] 作品X");
        Directory.Move(oldFolder, newFolder);

        var report = _service.Analyze();

        Assert.Single(report.PathChanges);
        Assert.Equal(oldFolder, report.PathChanges[0].OldPath);
        Assert.Equal(newFolder, report.PathChanges[0].NewPath);
    }

    [Fact]
    public void Deleted_Work_Becomes_Orphan()
    {
        var author = MkAuthorFolder("[作者A]");
        var work = MkWorkArchive(author, "deleted.zip");
        var assetId = RegisterAsset(work, isFolder: false);

        // 真實刪掉，沒搬走
        File.Delete(work);

        var report = _service.Analyze();

        Assert.Empty(report.PathChanges);
        Assert.Single(report.Orphans);
        Assert.Equal(assetId, report.Orphans[0].AssetId);
        Assert.Equal(work, report.Orphans[0].Path);
    }

    [Fact]
    public void Ambiguous_When_Multiple_Files_Have_Same_Name_And_Size()
    {
        // 同檔名 + 同 size 出現在兩個地方 → ambiguous，不自動配對
        var authorA = MkAuthorFolder("[作者A]");
        var oldPath = MkWorkArchive(authorA, "work.zip", 1024);
        var assetId = RegisterAsset(oldPath, isFolder: false);
        File.Delete(oldPath);

        var authorB = MkAuthorFolder("[作者B]");
        MkWorkArchive(authorB, "work.zip", 1024);
        var authorC = MkAuthorFolder("[作者C]");
        MkWorkArchive(authorC, "work.zip", 1024);

        var report = _service.Analyze();

        Assert.Empty(report.PathChanges);
        Assert.Empty(report.Orphans);
        Assert.Single(report.Ambiguous);
        Assert.Equal(2, report.Ambiguous[0].CandidateFsPaths.Count);
    }

    [Fact]
    public void New_Work_In_Fs_Not_In_Db_Is_Ignored_By_Reconcile()
    {
        // fs 多出來的 work（沒對應的 dbMissing）→ 不會出現在 report 任何分組
        // (使用者該跑 ResumeScanAsync 處理新檔)
        var author = MkAuthorFolder("[作者A]");
        MkWorkArchive(author, "new.zip");

        var report = _service.Analyze();

        Assert.True(report.IsEmpty);
        Assert.Equal(1, report.FsWorksScanned);
        Assert.Equal(0, report.DbAssetsScanned);
    }

    [Fact]
    public void ApplyPathChanges_Updates_Db_Path_And_Preserves_Metadata()
    {
        var authorA = MkAuthorFolder("[作者A]");
        var oldPath = MkWorkArchive(authorA, "work.zip", 2048);
        var assetId = RegisterAsset(oldPath, isFolder: false);

        // 寫一些 metadata（模擬使用者編輯過）
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = assetId,
            Title = "Important Title",
            ReadCount = 5,
            LastRead = 1700000000,
            ManuallyEdited = true,
            ParsedAt = 1234567890,
        });

        // 搬檔
        var authorB = MkAuthorFolder("[作者B]");
        var newPath = Path.Combine(authorB, "work.zip");
        File.Move(oldPath, newPath);

        var report = _service.Analyze();
        Assert.Single(report.PathChanges);

        // Apply
        var applied = _service.ApplyPathChanges(report.PathChanges);
        Assert.Equal(1, applied);

        // 驗證：path 變了，metadata 還在
        var entry = _store.ListAssetEntries().FirstOrDefault(e => e.Id == assetId);
        Assert.NotNull(entry);
        Assert.Equal(newPath, entry!.Path);

        var meta = _store.GetMetadata(assetId);
        Assert.NotNull(meta);
        Assert.Equal("Important Title", meta!.Title);
        Assert.Equal(5, meta.ReadCount);
        Assert.True(meta.ManuallyEdited);
    }

    [Fact]
    public void DeleteOrphans_Removes_Asset_And_Cascade_Metadata()
    {
        var author = MkAuthorFolder("[作者A]");
        var work = MkWorkArchive(author, "orphan.zip");
        var assetId = RegisterAsset(work, isFolder: false);
        _store.UpsertMetadata(new AssetMetadata { AssetId = assetId, Title = "X", ParsedAt = 1 });

        File.Delete(work);

        var report = _service.Analyze();
        Assert.Single(report.Orphans);

        var deleted = _service.DeleteOrphans(report.Orphans.Select(o => o.AssetId));
        Assert.Equal(1, deleted);

        // asset 不在 → metadata 也 cascade 掉
        Assert.Empty(_store.ListAssetEntries());
        Assert.Null(_store.GetMetadata(assetId));
    }

    [Fact]
    public void Renamed_Work_Detected_By_Size_Mtime_Match()
    {
        // basename 變了 (rename) → Pass 1 (basename, size) 不中
        // Pass 2 (size, mtime) 配對 → 應該偵測為 PathChange
        var author = MkAuthorFolder("[作者A]");
        var oldPath = MkWorkArchive(author, "old_name.zip", 4096);
        var assetId = RegisterAsset(oldPath, isFolder: false);

        // 對齊 mtime — RegisterAsset 寫死 1234567890，FS 真實 mtime 是 now，所以要 set FS 檔的 mtime
        var fakeMtime = DateTimeOffset.FromUnixTimeSeconds(1234567890).UtcDateTime;
        File.SetLastWriteTimeUtc(oldPath, fakeMtime);

        // rename
        var newPath = Path.Combine(author, "new_name.zip");
        File.Move(oldPath, newPath);
        File.SetLastWriteTimeUtc(newPath, fakeMtime);   // 確保 mtime 不變（File.Move 通常保留，雙保險）

        var report = _service.Analyze();

        Assert.Single(report.PathChanges);
        Assert.Empty(report.Orphans);
        Assert.Empty(report.Ambiguous);

        var change = report.PathChanges[0];
        Assert.Equal(assetId, change.AssetId);
        Assert.Equal(oldPath, change.OldPath);
        Assert.Equal(newPath, change.NewPath);
    }

    [Fact]
    public void Mixed_Scenario_Detects_All_Three_Categories()
    {
        // 三種狀況同時：path change / orphan / ambiguous
        var authorA = MkAuthorFolder("[作者A]");
        var authorB = MkAuthorFolder("[作者B]");

        // 1. 會被 path change 偵測
        var path1 = MkWorkArchive(authorA, "moved.zip", 1024);
        var id1 = RegisterAsset(path1, isFolder: false);
        var newPath1 = Path.Combine(authorB, "moved.zip");
        File.Move(path1, newPath1);

        // 2. 會變 orphan
        var path2 = MkWorkArchive(authorA, "deleted.zip", 2048);
        var id2 = RegisterAsset(path2, isFolder: false);
        File.Delete(path2);

        // 3. 會變 ambiguous（同 name+size 在兩個 author 下）
        var path3 = MkWorkArchive(authorA, "duplicate.zip", 4096);
        var id3 = RegisterAsset(path3, isFolder: false);
        File.Delete(path3);
        var authorC = MkAuthorFolder("[作者C]");
        MkWorkArchive(authorB, "duplicate.zip", 4096);
        MkWorkArchive(authorC, "duplicate.zip", 4096);

        var report = _service.Analyze();

        Assert.Single(report.PathChanges);
        Assert.Equal(id1, report.PathChanges[0].AssetId);

        Assert.Single(report.Orphans);
        Assert.Equal(id2, report.Orphans[0].AssetId);

        Assert.Single(report.Ambiguous);
        Assert.Equal(id3, report.Ambiguous[0].AssetId);
    }
}
