using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// ReparseAllService 測試（#45 Phase 7）：
/// 整庫重 parse / manually_edited skip / 閱讀記錄保留 / author hint 兩段式 / ParseStats。
/// </summary>
public class ReparseAllTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;
    private readonly RebuildProgressTracker _tracker;
    private readonly ReparseAllService _service;

    public ReparseAllTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_reparse_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
        _tracker = new RebuildProgressTracker(_store);
        _service = new ReparseAllService(_store, _tracker);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private long Seed(string path, string? title = null, bool manuallyEdited = false,
                     int readCount = 0, long? lastRead = null)
    {
        _store.Upsert(new AssetMeta
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsFolder = false,
            FileSize = 1,
            Mtime = 1,
        });
        var id = _store.GetAssetId(path)!.Value;
        if (title != null || manuallyEdited || readCount > 0)
        {
            _store.UpsertMetadata(new AssetMetadata
            {
                AssetId = id,
                Title = title,
                ReadCount = readCount,
                LastRead = lastRead,
                ManuallyEdited = manuallyEdited,
                ParsedAt = 1,
            });
        }
        return id;
    }

    // ── ParseStats ─────────────────────────────────────────────────────

    [Fact]
    public void GetParseStats_Counts_Correctly()
    {
        Seed(@"C:\lib\[A] w1.zip");                                    // unparsed
        Seed(@"C:\lib\[A] w2.zip", title: "Auto");                     // auto-parsed
        Seed(@"C:\lib\[A] w3.zip", title: "Manual", manuallyEdited: true);  // manual

        var stats = _store.GetParseStats();

        Assert.Equal(3, stats.TotalAssets);
        Assert.Equal(2, stats.ParsedCount);
        Assert.Equal(1, stats.UnparsedCount);
        Assert.Equal(1, stats.ManuallyEditedCount);
        Assert.Equal(1, stats.AutoParsedCount);
    }

    // ── Re-parse ──────────────────────────────────────────────────────

    [Fact]
    public async Task Run_Parses_All_With_No_Existing_Metadata()
    {
        Seed(@"C:\lib\[Cuvie] D.L.action 45 (Various) [中国翻訳].zip");

        var summary = await _service.RunAsync();

        Assert.Equal(1, summary.Total);
        Assert.Equal(1, summary.Reparsed);
        Assert.Equal(0, summary.Skipped);

        var meta = _store.GetMetadata(_store.GetAssetId(@"C:\lib\[Cuvie] D.L.action 45 (Various) [中国翻訳].zip")!.Value);
        Assert.NotNull(meta);
        Assert.Equal("D.L.action 45", meta!.Title);
        Assert.Equal("zh-CN", meta.Language);
        Assert.NotNull(meta.AuthorId);
    }

    [Fact]
    public async Task Run_Skips_ManuallyEdited()
    {
        var id = Seed(@"C:\lib\[Cuvie] work.zip", title: "User Custom Title", manuallyEdited: true);

        var summary = await _service.RunAsync();

        Assert.Equal(1, summary.Total);
        Assert.Equal(0, summary.Reparsed);
        Assert.Equal(1, summary.Skipped);

        var meta = _store.GetMetadata(id);
        Assert.Equal("User Custom Title", meta!.Title);   // 沒被覆蓋
        Assert.True(meta.ManuallyEdited);
    }

    [Fact]
    public async Task Run_Preserves_Reading_Records()
    {
        var lastRead = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = Seed(@"C:\lib\[Cuvie] work.zip", readCount: 7, lastRead: lastRead);

        await _service.RunAsync();

        var meta = _store.GetMetadata(id);
        Assert.Equal(7, meta!.ReadCount);
        Assert.Equal(lastRead, meta.LastRead);
        Assert.Equal("work", meta.Title);   // re-parse 改了 title
    }

    [Fact]
    public async Task Run_Author_Hint_Filename_First()
    {
        // filename 自帶 [Author] → 用 filename 不看 folder
        Seed(@"C:\lib\[SomeoneElse]\[Cuvie] work.zip");

        await _service.RunAsync();

        var meta = _store.GetMetadata(_store.GetAssetId(@"C:\lib\[SomeoneElse]\[Cuvie] work.zip")!.Value);
        Assert.NotNull(meta!.AuthorId);
        var author = _store.FindAuthorByMainName("Cuvie");
        Assert.NotNull(author);
        Assert.Equal(author!.Id, meta.AuthorId);
    }

    [Fact]
    public async Task Run_Author_Hint_Folder_Fallback_When_Filename_Has_No_Author()
    {
        // filename 沒 [Author] → 看父目錄
        // 預先建 author with folder_path
        var authorId = _store.UpsertAuthor("FolderAuthor", @"C:\lib\[FolderAuthor]");
        Seed(@"C:\lib\[FolderAuthor]\plain_filename.zip");

        await _service.RunAsync();

        var meta = _store.GetMetadata(_store.GetAssetId(@"C:\lib\[FolderAuthor]\plain_filename.zip")!.Value);
        Assert.Equal(authorId, meta!.AuthorId);
    }

    [Fact]
    public async Task Run_Updates_Tags_From_Filename()
    {
        Seed(@"C:\lib\(C99) [Cuvie] work (オリジナル) [中国翻訳][DL版].zip");

        await _service.RunAsync();

        var id = _store.GetAssetId(@"C:\lib\(C99) [Cuvie] work (オリジナル) [中国翻訳][DL版].zip")!.Value;
        var tags = _store.ListTagsForAsset(id);
        Assert.Contains(tags, t => t.Name == "オリジナル" && t.Category == "parody");
        // C99 / DL版 / language tag 不應入 asset_tag
        Assert.DoesNotContain(tags, t => t.Name is "C99" or "DL版" or "中国翻訳");
    }

    [Fact]
    public async Task Run_Does_Not_Mark_ManuallyEdited()
    {
        var id = Seed(@"C:\lib\[Cuvie] work.zip");

        await _service.RunAsync();

        var meta = _store.GetMetadata(id);
        Assert.False(meta!.ManuallyEdited);
    }

    [Fact]
    public async Task Run_Mixed_All_Three_Types_Counts_Correctly()
    {
        Seed(@"C:\lib\[A] w1.zip");                                                // 應 reparse
        Seed(@"C:\lib\[A] w2.zip", title: "Old Title");                            // 應 reparse (auto)
        Seed(@"C:\lib\[A] w3.zip", title: "Manual", manuallyEdited: true);         // 應 skip

        var summary = await _service.RunAsync();

        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.Reparsed);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public async Task Run_PreservesTagCategories_CleansOrphanTags_AndSkipsAuthorFolder()
    {
        var authorFolder = @"C:\lib\Napata";
        var authorId = _store.UpsertAuthor("Napata", authorFolder);

        // 先模擬過去誤建的作者資料夾 catalog + 舊污染 tag（Chinese / Cat FooD (なぱた)）
        _store.Upsert(new AssetMeta
        {
            Path = authorFolder,
            Name = "Napata",
            IsFolder = true,
            FileSize = 0,
            Mtime = 1,
        });
        var dirId = _store.GetAssetId(authorFolder)!.Value;
        _store.UpsertMetadata(new AssetMetadata { AssetId = dirId, Title = "Napata", ParsedAt = 1 });

        var workPath = @"C:\lib\Napata\(同人誌) (C90) [Cat FooD (なぱた)] Title (艦隊これくしょん-艦これ-) (Chinese) [DL版][無修正].zip";
        var workId = Seed(workPath);
        _store.SetAssetTagEntries(workId, new[]
        {
            new TagEntry("C90", "parody"),
            new TagEntry("Chinese", "parody"),
            new TagEntry("DL版", null),
            new TagEntry("Cat FooD (なぱた)", null),
        });

        // cover.jpg 也不應建立 asset_meta
        var coverId = Seed(@"C:\lib\Napata\cover.jpg", title: "cover");

        await _service.RunAsync();

        // 作者資料夾與 cover.jpg 不該留有 asset_meta
        Assert.Null(_store.GetMetadata(dirId));
        Assert.Null(_store.GetMetadata(coverId));

        // Work 保留父作者並清掉假 tag，正確寫入 parody 分類
        var workMeta = _store.GetMetadata(workId);
        Assert.Equal(authorId, workMeta!.AuthorId);
        Assert.Equal("zh", workMeta.Language);

        var workTags = _store.ListTagsForAsset(workId);
        Assert.Contains(workTags, t => t.Name == "艦隊これくしょん -艦これ-" && t.Category == "parody");
        Assert.DoesNotContain(workTags, t => t.Name is "C90" or "DL版" or "無修正" or "Chinese" || t.Name.Contains("なぱた"));

        // 孤兒污染 tag 與無用規格 tag 應從 tag 主表清除
        var allTags = _store.ListTagsByUsage();
        Assert.DoesNotContain(allTags, t => t.Name is "C90" or "DL版" or "無修正" or "Chinese" || t.Name.Contains("なぱた"));
    }

    [Fact]
    public async Task Run_WithLibraryRoot_RegistersMissingAuthorDirectoriesAndCleansContainerMeta()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"comic_root_{Guid.NewGuid():N}");
        try
        {
            var authorDir = Path.Combine(tempRoot, "一ノ宮(Rosapersica)");
            Directory.CreateDirectory(authorDir);
            var zipPath = Path.Combine(authorDir, "[Rosapersica (一ノ宮)] ヨルヤハギ5 (艦隊これくしょん -艦これ-).zip");
            File.WriteAllBytes(zipPath, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

            var svc = new ReparseAllService(_store, _tracker, () => tempRoot);
            _store.Upsert(new AssetMeta { Path = authorDir, Name = Path.GetFileName(authorDir), IsFolder = true, FileSize = 0, Mtime = 1 });
            var dirId = _store.GetAssetId(authorDir)!.Value;
            _store.UpsertMetadata(new AssetMetadata { AssetId = dirId, Title = Path.GetFileName(authorDir), ParsedAt = 1 });
            _store.Upsert(new AssetMeta { Path = zipPath, Name = Path.GetFileName(zipPath), IsFolder = false, FileSize = 22, Mtime = 1 });
            var zipId = _store.GetAssetId(zipPath)!.Value;

            await svc.RunAsync();

            // 未曾建立的作者目錄會自動寫入 author 表並清理目錄自身的假 asset_meta
            var author = _store.FindAuthorByMainName("一ノ宮");
            Assert.NotNull(author);
            Assert.Equal(authorDir, author!.FolderPath);
            Assert.Null(_store.GetMetadata(dirId));

            var workMeta = _store.GetMetadata(zipId);
            Assert.Equal(author.Id, workMeta!.AuthorId);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Run_WithLibraryRoot_AutoCatalogsMissingDiskArchivesAndFolders()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ComicOrganizer_ReparseCatalog_" + Guid.NewGuid().ToString("N"));
        var authorDir = Path.Combine(tempRoot, "[AuthorX]");
        var zipWork = Path.Combine(authorDir, "[AuthorX] Alpha Comic [中国翻訳].zip");
        var pdfWork = Path.Combine(authorDir, "[AuthorX] Beta Comic.pdf");
        var looseImg = Path.Combine(authorDir, "cover.jpg");
        Directory.CreateDirectory(authorDir);
        File.WriteAllBytes(zipWork, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(pdfWork, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(looseImg, new byte[] { 1, 2, 3 });

        try
        {
            // DB 初始為空，模擬重建中斷後磁碟檔案尚未寫入 asset 表
            var svc = new ReparseAllService(_store, _tracker, () => tempRoot);
            var summary = await svc.RunAsync();

            Assert.False(summary.Cancelled);
            Assert.True(summary.Cataloged >= 3);
            Assert.NotNull(_store.GetAssetId(authorDir));
            Assert.True(_store.IsAuthorFolderPath(authorDir));
            Assert.Null(_store.GetAssetId(looseImg));

            var zipDetail = _store.GetWorkDetail(zipWork);
            Assert.NotNull(zipDetail);
            Assert.Equal("Alpha Comic", zipDetail!.Title);
            Assert.Equal("AuthorX", zipDetail.AuthorMainName);
            Assert.Equal("zh-CN", zipDetail.Language);

            var pdfDetail = _store.GetWorkDetail(pdfWork);
            Assert.NotNull(pdfDetail);
            Assert.Equal("Beta Comic", pdfDetail!.Title);
            Assert.Equal("AuthorX", pdfDetail.AuthorMainName);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }
}
