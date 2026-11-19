using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

/// <summary>
/// 端對端整合測試：建一個假漫畫庫 → 跑 UpdateIndex → 驗 author / asset_meta / tag 都被填入。
/// 涵蓋 Phase 2-2 wiring：FolderRoleDetector → ParsedMetadata → CacheWriter → DB
///
/// 注意：CacheWriter 是 async batch worker，測試需 wait drain。每筆 batch 預設 100ms 視窗，
/// 寫完之後 Dispose CacheWriter 等 join。本測試的 fixture 規模小（< 10 筆）一次 batch 就完成。
/// </summary>
public class ScanMetadataIntegrationTest : IDisposable
{
    private readonly string _tempDir;
    private readonly string _libraryRoot;
    private readonly string _dbPath;
    private readonly string _thumbDir;
    private readonly ITestOutputHelper _output;

    public ScanMetadataIntegrationTest(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_scan_{Guid.NewGuid():N}");
        _libraryRoot = Path.Combine(_tempDir, "Library");
        _dbPath = Path.Combine(_tempDir, "cache.db");
        _thumbDir = Path.Combine(_tempDir, "thumbs");
        Directory.CreateDirectory(_libraryRoot);
        Directory.CreateDirectory(_thumbDir);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task UpdateIndex_FillsAuthorAndMetadata()
    {
        // === Build a synthetic library ===
        // Library/
        //   Flugel/
        //     [Flugel] くぎづけはーと [中文][DL版].zip   (Work, parent is Author)
        //   タイガー[Tiger Roman]/
        //     (C99) [タイガー] Title (Original) [中国翻訳].zip
        BuildSyntheticLibrary();

        // === wire up services ===
        var config = new AppConfig { RootDirectory = _libraryRoot };
        using var metaStore = new SqliteAssetMetaStore(_dbPath);
        var thumbStore = new FileSystemThumbStore(_thumbDir);
        using var cacheWriter = new CacheWriter(metaStore, thumbStore, config);
        var coverManager = new CoverManager(config);
        coverManager.AttachCache(metaStore, thumbStore, cacheWriter);
        var tracker = new RebuildProgressTracker(metaStore);
        var checker = new CacheIntegrityChecker(metaStore, thumbStore, tracker);
        var service = new CacheRebuildService(config, coverManager, metaStore, thumbStore, checker, tracker, cacheWriter);

        // === run UpdateIndex ===
        var summary = await service.UpdateIndexAsync();
        Assert.NotNull(summary);
        _output.WriteLine($"summary: added={summary!.AddedCount} updated={summary.UpdatedCount} removed={summary.RemovedCount}");

        // 等 CacheWriter drain（dispose 會 drain，但這裡先確認 metadata 寫入）
        cacheWriter.Dispose();

        // === asserts ===
        // 1. Author "Flugel" 存在
        var author = metaStore.FindAuthorByMainName("Flugel");
        Assert.NotNull(author);
        Assert.Equal(@$"{_libraryRoot}\Flugel", author!.FolderPath);

        // 2. 雙語作者 — main "タイガー" + alias "Tiger Roman"
        var tigerJp = metaStore.FindAuthorByMainName("タイガー");
        Assert.NotNull(tigerJp);
        var aliases = metaStore.ListAuthorAliases(tigerJp!.Id);
        Assert.Contains(aliases, a => a.Name == "Tiger Roman");

        // 3. Work asset_meta 應被填入
        var workPath = Path.Combine(_libraryRoot, "Flugel", "[Flugel] くぎづけはーと [中文][DL版].zip");
        var workId = metaStore.GetAssetId(workPath);
        Assert.NotNull(workId);
        var workMeta = metaStore.GetMetadata(workId!.Value);
        Assert.NotNull(workMeta);
        Assert.Equal("くぎづけはーと", workMeta!.Title);
        Assert.Equal("zh", workMeta.Language);
        Assert.Equal(author.Id, workMeta.AuthorId);
        // tags（DL版 / C99 等無用規格/場次已忽略，不應存入 asset_tag）
        var tags = metaStore.ListTagsForAsset(workId.Value);
        Assert.DoesNotContain(tags, t => t.Name == "DL版");

        // 4. C99 work 應有 parody + zh-CN（C99 場次不應寫入 tag）
        var workC99Path = Path.Combine(_libraryRoot, "タイガー[Tiger Roman]", "(C99) [タイガー] Title (Original) [中国翻訳].zip");
        var workC99Id = metaStore.GetAssetId(workC99Path);
        Assert.NotNull(workC99Id);
        var workC99Meta = metaStore.GetMetadata(workC99Id!.Value);
        Assert.NotNull(workC99Meta);
        Assert.Equal("Title", workC99Meta!.Title);
        Assert.Equal("zh-CN", workC99Meta.Language);
        Assert.Equal(tigerJp.Id, workC99Meta.AuthorId);
        var c99Tags = metaStore.ListTagsForAsset(workC99Id.Value);
        Assert.DoesNotContain(c99Tags, t => t.Name == "C99");
        Assert.Contains(c99Tags, t => t.Name == "Original" && t.Category == "parody");
    }

    private void BuildSyntheticLibrary()
    {
        // Author folder 1: Flugel/
        var flugelDir = Path.Combine(_libraryRoot, "Flugel");
        Directory.CreateDirectory(flugelDir);
        // 放至少 5 個 .jpg 騙 FolderRoleDetector 不把這個 .zip 當 chapter（這是 zip 不會被誤判）
        // .zip 沒實質內容，只是「檔案存在」就足以讓 detector 判 Work
        File.WriteAllBytes(Path.Combine(flugelDir, "[Flugel] くぎづけはーと [中文][DL版].zip"), MakeMinimalZip());

        // Author folder 2: タイガー[Tiger Roman]/
        var tigerDir = Path.Combine(_libraryRoot, "タイガー[Tiger Roman]");
        Directory.CreateDirectory(tigerDir);
        File.WriteAllBytes(Path.Combine(tigerDir, "(C99) [タイガー] Title (Original) [中国翻訳].zip"), MakeMinimalZip());
    }

    /// <summary>產生最小有效 zip（單一空白 entry）讓 FolderRoleDetector 可識別為 archive。</summary>
    private static byte[] MakeMinimalZip()
    {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("placeholder.txt");
            using var stream = entry.Open();
            stream.WriteByte(0);
        }
        return ms.ToArray();
    }
}
