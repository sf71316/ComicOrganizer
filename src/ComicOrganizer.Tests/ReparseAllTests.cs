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
        Seed(@"C:\lib\(C99) [Cuvie] work [中国翻訳][DL版].zip");

        await _service.RunAsync();

        var id = _store.GetAssetId(@"C:\lib\(C99) [Cuvie] work [中国翻訳][DL版].zip")!.Value;
        var tags = _store.ListTagsForAsset(id);
        Assert.Contains(tags, t => t.Name == "C99");        // event tag
        Assert.Contains(tags, t => t.Name == "DL版");
        // language tag (中国翻訳) 不該入 asset_tag — 是 asset_meta.language
        Assert.DoesNotContain(tags, t => t.Name == "中国翻訳");
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
}
