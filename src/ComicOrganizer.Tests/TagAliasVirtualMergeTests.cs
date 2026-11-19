using ComicOrganizer.App.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// Schema v9 虛擬主標籤合併與別名自動導向機制測試。
/// </summary>
public class TagAliasVirtualMergeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public TagAliasVirtualMergeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_tag_alias_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private long SeedAsset(string path)
    {
        _store.UpsertCatalogOnly(new AssetMeta
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsFolder = false,
            FileSize = 1024,
            Mtime = 100,
        });
        return _store.GetAssetId(path)!.Value;
    }

    [Fact]
    public void SchemaV8_MigratesToV9_CreatesTagAliasTableAndPreservesData()
    {
        var v8DbPath = Path.Combine(Path.GetTempPath(), $"comic_v8_to_v9_{Guid.NewGuid():N}.db");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={v8DbPath};"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE schema_version (version INTEGER PRIMARY KEY);
INSERT INTO schema_version (version) VALUES (8);

CREATE TABLE asset (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    path TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    is_folder INTEGER NOT NULL,
    file_size INTEGER,
    mtime INTEGER NOT NULL,
    added_at INTEGER NOT NULL
);
CREATE TABLE asset_thumb (
    asset_id INTEGER PRIMARY KEY,
    cover_origin TEXT, cover_thumb_key TEXT, cover_width INTEGER, cover_height INTEGER,
    last_scanned INTEGER NOT NULL, last_accessed INTEGER NOT NULL,
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE
);
CREATE TABLE author (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    main_name TEXT NOT NULL UNIQUE, folder_path TEXT, learned_at INTEGER NOT NULL
);
CREATE TABLE author_alias (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    author_id INTEGER NOT NULL, name TEXT NOT NULL, lang TEXT,
    FOREIGN KEY (author_id) REFERENCES author(id) ON DELETE CASCADE,
    UNIQUE(author_id, name)
);
CREATE TABLE asset_meta (
    asset_id INTEGER PRIMARY KEY,
    author_id INTEGER,
    title TEXT, series TEXT, chapter_no TEXT, language TEXT, translation_group TEXT,
    page_count INTEGER, rating INTEGER,
    read_status INTEGER NOT NULL DEFAULT 0, read_count INTEGER NOT NULL DEFAULT 0,
    last_read INTEGER, external_id TEXT, parsed_at INTEGER NOT NULL,
    last_page INTEGER,
    manually_edited INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE,
    FOREIGN KEY (author_id) REFERENCES author(id) ON DELETE SET NULL
);
CREATE TABLE tag (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    category TEXT
);
CREATE TABLE asset_tag (
    asset_id INTEGER NOT NULL, tag_id INTEGER NOT NULL,
    PRIMARY KEY (asset_id, tag_id),
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE CASCADE
);
CREATE TABLE author_tag (
    author_id INTEGER NOT NULL, tag_id INTEGER NOT NULL,
    PRIMARY KEY (author_id, tag_id),
    FOREIGN KEY (author_id) REFERENCES author(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE CASCADE
);
CREATE TABLE tag_translation (
    tag_id INTEGER NOT NULL, lang TEXT NOT NULL, name TEXT NOT NULL,
    source TEXT, updated_at INTEGER,
    PRIMARY KEY (tag_id, lang),
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE CASCADE
);
CREATE TABLE asset_external_source (
    asset_id INTEGER NOT NULL, source TEXT NOT NULL, source_id TEXT NOT NULL,
    media_id TEXT, upload_at INTEGER, raw_json TEXT NOT NULL, fetched_at INTEGER NOT NULL,
    PRIMARY KEY (asset_id, source),
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE
);
CREATE TABLE virtual_library (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL, dsl TEXT NOT NULL, icon_svg TEXT,
    sort_order INTEGER NOT NULL DEFAULT 0, pinned INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL
);
CREATE TABLE bookmark (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_id INTEGER NOT NULL, page_index INTEGER NOT NULL, label TEXT, created_at INTEGER NOT NULL,
    UNIQUE (asset_id, page_index),
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE
);
CREATE TABLE folder_role (
    path TEXT PRIMARY KEY, role TEXT NOT NULL, confidence REAL, template TEXT, learned_at INTEGER
);
CREATE TABLE app_state (key TEXT PRIMARY KEY, value TEXT);

INSERT INTO asset (id, path, name, is_folder, file_size, mtime, added_at)
VALUES (1, 'C:\Lib\W1.zip', 'W1.zip', 0, 500, 100, 100);
INSERT INTO tag (id, name, category) VALUES (10, 'stockings', 'tag');
INSERT INTO asset_tag (asset_id, tag_id) VALUES (1, 10);
";
                cmd.ExecuteNonQuery();
            }

            using (var migratedStore = new SqliteAssetMetaStore(v8DbPath))
            {
                Assert.NotNull(migratedStore.Get(@"C:\Lib\W1.zip"));
                var tags = migratedStore.ListTagsForAsset(1);
                Assert.Single(tags);
                Assert.Equal("stockings", tags[0].Name);

                var masterId = migratedStore.MergeTagsIntoVirtual(new[] { 10L }, "絲襪系", "virtual", "絲襪系");
                Assert.Equal(masterId, migratedStore.ResolveTagAlias("stockings"));
            }

            using var verifyConn = new SqliteConnection($"Data Source={v8DbPath};");
            verifyConn.Open();
            using var vCmd = verifyConn.CreateCommand();
            vCmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
            Assert.Equal(9, Convert.ToInt32(vCmd.ExecuteScalar()!));

            vCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='tag_alias';";
            Assert.Equal("tag_alias", vCmd.ExecuteScalar()?.ToString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(v8DbPath)) File.Delete(v8DbPath);
        }
    }

    [Fact]
    public void MergeTagsIntoVirtual_MergesMultipleTags_TransfersAssetsAndAuthors_CreatesAliases()
    {
        var t1 = _store.UpsertTag("stockings", "tag");
        var t2 = _store.UpsertTag("pantyhose", "tag");
        var t3 = _store.UpsertTag("thighhighs", "tag");

        _store.UpsertTagTranslation(t1, "zh-TW", "過膝襪", "ehtag");
        _store.UpsertTagTranslation(t2, "zh-TW", "連褲襪", "ehtag");

        var w1 = SeedAsset(@"C:\Lib\Work1.zip");
        var w2 = SeedAsset(@"C:\Lib\Work2.zip");
        // w1 同時有 t1 與 t2（合併時應去重），w2 只有 t3
        _store.AssociateTag(w1, t1);
        _store.AssociateTag(w1, t2);
        _store.AssociateTag(w2, t3);

        var authorId = _store.UpsertAuthor("ArtistA");
        _store.AssociateAuthorTag(authorId, t1);
        _store.AssociateAuthorTag(authorId, t2);

        var masterId = _store.MergeTagsIntoVirtual(
            new[] { t1, t2, t3 },
            masterName: "絲襪系",
            category: "virtual",
            zhTwDisplayName: "絲襪合集");

        // 舊 tag 已刪除
        Assert.Empty(_store.SearchTags("stockings"));
        Assert.Empty(_store.SearchTags("pantyhose"));
        Assert.Empty(_store.SearchTags("thighhighs"));

        // 作品與作者關聯皆轉移到新虛擬主標籤
        Assert.Single(_store.ListTagsForAsset(w1), t => t.Id == masterId && t.Category == "virtual");
        Assert.Single(_store.ListTagsForAsset(w2), t => t.Id == masterId);
        Assert.Single(_store.ListTagsForAuthor(authorId), t => t.Id == masterId);

        // 別名完整建立且可反查
        var aliases = _store.ListTagAliases(masterId);
        Assert.Equal(3, aliases.Count);
        Assert.Equal(masterId, _store.ResolveTagAlias("stockings"));
        Assert.Equal(masterId, _store.ResolveTagAlias("pantyhose"));
        Assert.Equal(masterId, _store.ResolveTagAlias("thighhighs"));

        // 譯文維持 manual 指定的名稱，不應混入舊 tag 的譯文
        Assert.Equal("絲襪合集", _store.GetTagTranslation(masterId, "zh-TW"));
        var trans = _store.ListTagTranslations(masterId);
        Assert.Single(trans);
        Assert.Equal("manual", trans[0].Source);
    }

    [Fact]
    public void MergeTagsIntoVirtual_IntoExistingMasterTag_ReusesMasterAndAppendsAlias()
    {
        var masterExisting = _store.UpsertTag("絲襪系", "tag");
        var t1 = _store.UpsertTag("stockings", "tag");
        var t2 = _store.UpsertTag("pantyhose", "tag");

        var w0 = SeedAsset(@"C:\Lib\W0.zip");
        var w1 = SeedAsset(@"C:\Lib\W1.zip");
        _store.AssociateTag(w0, masterExisting);
        _store.AssociateTag(w1, t2);
        _store.AddTagAlias(masterExisting, "knee-highs");

        var returnedId = _store.MergeTagsIntoVirtual(
            new[] { masterExisting, t1, t2 },
            masterName: "絲襪系",
            category: "virtual",
            zhTwDisplayName: null);

        Assert.Equal(masterExisting, returnedId);
        Assert.Equal(2, _store.ListWorksByTags(new[] { masterExisting }).Count);

        var aliases = _store.ListTagAliases(masterExisting).Select(a => a.AliasName).ToList();
        Assert.Contains("knee-highs", aliases);
        Assert.Contains("stockings", aliases);
        Assert.Contains("pantyhose", aliases);
        Assert.DoesNotContain("絲襪系", aliases);
    }

    [Fact]
    public void MergeTagsIntoVirtual_ChainMerge_FlattensAllAliasesWithoutCycles()
    {
        var a = _store.UpsertTag("stockings");
        var b = _store.UpsertTag("pantyhose");
        var c = _store.UpsertTag("garter belt");

        var w1 = SeedAsset(@"C:\Lib\Chain1.zip");
        var w2 = SeedAsset(@"C:\Lib\Chain2.zip");
        _store.AssociateTag(w1, a);
        _store.AssociateTag(w2, c);

        // 第一輪：A + B -> V1
        var v1 = _store.MergeTagsIntoVirtual(new[] { a, b }, "絲襪初版", "virtual");

        // 第二輪：V1 + C -> V2
        var v2 = _store.MergeTagsIntoVirtual(new[] { v1, c }, "腿部飾品終極版", "virtual");

        Assert.NotEqual(v1, v2);
        Assert.Empty(_store.ListTagAliases(v1));

        // 全部舊別名皆直接扁平指向 v2，沒有多跳鏈或循環
        var allAliases = _store.ListTagAliases(v2).Select(x => x.AliasName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("stockings", allAliases);
        Assert.Contains("pantyhose", allAliases);
        Assert.Contains("絲襪初版", allAliases);
        Assert.Contains("garter belt", allAliases);
        Assert.Equal(v2, _store.ResolveTagAlias("stockings"));
        Assert.Equal(v2, _store.ResolveTagAlias("pantyhose"));
        Assert.Equal(v2, _store.ResolveTagAlias("絲襪初版"));
        Assert.Equal(v2, _store.ResolveTagAlias("garter belt"));

        // 兩部作品都關聯在 v2
        Assert.Equal(2, _store.ListWorksByTags(new[] { v2 }).Count);
    }

    [Fact]
    public void UpsertTag_And_SetAssetTagEntries_AutoRedirectAliases_CaseInsensitive()
    {
        var t1 = _store.UpsertTag("stockings", "tag");
        var t2 = _store.UpsertTag("pantyhose", "tag");
        var masterId = _store.MergeTagsIntoVirtual(new[] { t1, t2 }, "絲襪系", "virtual", "絲襪系");

        // 直接 UpsertTag（不同大小寫）應該導向 masterId，且不覆寫 category
        var redirected1 = _store.UpsertTag("STOCKINGS", "tag");
        var redirected2 = _store.UpsertTag("  pantyhose ", "parody");
        Assert.Equal(masterId, redirected1);
        Assert.Equal(masterId, redirected2);

        // SetAssetTagEntries 模擬 nhentai 同步匯入新作品的標籤清單（同時帶了兩個別名 + 一個新 tag）
        var newWorkId = SeedAsset(@"C:\Lib\SyncedWork.zip");
        _store.SetAssetTagEntries(newWorkId, new[]
        {
            new TagEntry("stockings", "tag"),
            new TagEntry("PANTYHOSE", "tag"),
            new TagEntry("sole female", "tag"),
        });

        var workTags = _store.ListTagsForAsset(newWorkId);
        Assert.Equal(2, workTags.Count);
        Assert.Contains(workTags, t => t.Id == masterId && t.Name == "絲襪系" && t.Category == "virtual");
        Assert.Contains(workTags, t => t.Name == "sole female" && t.Category == "tag");

        // 舊英文 tag 不應在 tag 表被復活
        Assert.Empty(_store.SearchTags("stockings"));
        Assert.Empty(_store.SearchTags("pantyhose"));
        Assert.Equal("絲襪系", _store.GetTagTranslation(masterId, "zh-TW"));
    }

    [Fact]
    public void BatchExternalFetchService_ApplyMergeResult_DoesNotOverwriteVirtualTagTranslation()
    {
        var t1 = _store.UpsertTag("stockings", "tag");
        var masterId = _store.MergeTagsIntoVirtual(new[] { t1 }, "絲襪系", "virtual", "絲襪自訂譯名");

        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
            {
              "data": [
                {
                  "namespace": "female",
                  "data": {
                    "stockings": { "name": "EhTag系統絲襪譯名" },
                    "sole female": { "name": "唯二女主角" }
                  }
                }
              ]
            }
            """));
        var db = TagTranslationDb.LoadFromJsonStream(ms);
        var translator = new TagTranslator(() => db);
        var svc = new BatchExternalFetchService(_store, new StubClient(), translator);

        var workId = SeedAsset(@"C:\Lib\[Artist] Sample.zip");
        var detail = _store.GetWorkDetail(@"C:\Lib\[Artist] Sample.zip")!;

        svc.ApplyMergeResult(detail, new MergeResult
        {
            TagEntries = new List<TagEntry>
            {
                new("stockings", "tag"),
                new("sole female", "tag"),
            }
        });

        // 1. 作品已掛上虛擬主標籤 + 新標籤
        var workTags = _store.ListTagsForAsset(workId);
        Assert.Contains(workTags, t => t.Id == masterId);
        var soleFemaleTag = workTags.First(t => t.Name == "sole female");

        // 2. GetTagIdsByNames 不會把英文別名反查成虛擬主標籤，故虛擬主標籤譯名完全保留不被 EhTag 覆蓋
        Assert.Equal("絲襪自訂譯名", _store.GetTagTranslation(masterId, "zh-TW"));
        // 3. 新一般標籤仍有寫入 EhTag 翻譯
        Assert.Equal("唯二女主角", _store.GetTagTranslation(soleFemaleTag.Id, "zh-TW"));
    }

    [Fact]
    public void DeleteOrphanTags_PreservesMasterTagHoldingAliases()
    {
        var t1 = _store.UpsertTag("stockings", "tag");
        var orphan = _store.UpsertTag("unused-orphan", "tag");
        var masterId = _store.MergeTagsIntoVirtual(new[] { t1 }, "絲襪規則庫", "virtual");

        // 目前沒有任何作品或作者掛載 masterId 與 orphan，但 masterId 持有 tag_alias
        int deleted = _store.DeleteOrphanTags();

        Assert.Equal(1, deleted);
        Assert.Empty(_store.SearchTags("unused-orphan"));
        Assert.Single(_store.SearchTags("絲襪規則庫"));
        Assert.Equal(masterId, _store.ResolveTagAlias("stockings"));
    }

    [Fact]
    public void DeleteTagAlias_UnlinksWithoutAffectingExistingAssets_AndAllowsRecreatingTag()
    {
        var t1 = _store.UpsertTag("stockings", "tag");
        var masterId = _store.MergeTagsIntoVirtual(new[] { t1 }, "絲襪系", "virtual");
        var w1 = SeedAsset(@"C:\Lib\Existing.zip");
        _store.AssociateTag(w1, masterId);

        _store.DeleteTagAlias("STOCKINGS");

        Assert.Null(_store.ResolveTagAlias("stockings"));
        Assert.Empty(_store.ListTagAliases(masterId));
        // 既有關聯維持不變
        Assert.Contains(_store.ListTagsForAsset(w1), t => t.Id == masterId);

        // 解除綁定後，再進來 stockings 就會新建獨立 tag
        var recreatedId = _store.UpsertTag("stockings", "tag");
        Assert.NotEqual(masterId, recreatedId);
    }

    [Fact]
    public void AddTagAlias_And_RenameTag_EnforceConflictRules()
    {
        var t1 = _store.UpsertTag("stockings");
        var otherTag = _store.UpsertTag("gloves");
        var masterId = _store.MergeTagsIntoVirtual(new[] { t1 }, "絲襪系", "virtual");

        // 1. AddTagAlias 不可與現有 tag 同名
        Assert.Throws<InvalidOperationException>(() => _store.AddTagAlias(masterId, "GLOVES"));

        // 2. 可新增全新別名
        _store.AddTagAlias(masterId, "tights");
        Assert.Equal(masterId, _store.ResolveTagAlias("tights"));

        // 3. RenameTag 不可改名成指向其他 tag 的別名
        Assert.Throws<InvalidOperationException>(() => _store.RenameTag(otherTag, "TIGHTS"));

        // 4. 若 tag 本身改名成自己所持有的別名（例如想從虛擬名改回 stockings），應自動移除該自我別名並成功改名
        _store.RenameTag(masterId, "stockings");
        Assert.Null(_store.ResolveTagAlias("stockings"));
        Assert.Single(_store.SearchTags("stockings"));
    }

    [Fact]
    public void SearchExecutor_And_FuzzyRecommender_ResolveVirtualTagsByMasterAndAlias()
    {
        var t1 = _store.UpsertTag("stockings", "tag");
        var t2 = _store.UpsertTag("pantyhose", "tag");
        var masterId = _store.MergeTagsIntoVirtual(new[] { t1, t2 }, "virtual_socks", "virtual", "絲襪系");

        var w1 = SeedAsset(@"C:\Lib\WithSocks.zip");
        var w2 = SeedAsset(@"C:\Lib\NoSocks.zip");
        _store.AssociateTag(w1, masterId);

        var executor = new SearchExecutor(_store, "zh-TW");

        // 1. 搜主標籤譯名
        var byZh = executor.Execute(SearchQueryParser.Parse("tag:絲襪系"));
        Assert.Equal(new[] { @"C:\Lib\WithSocks.zip" }, byZh);

        // 2. 搜舊英文別名（stockings）與另一個別名（pantyhose）
        var byAlias1 = executor.Execute(SearchQueryParser.Parse("tag:stockings"));
        var byAlias2 = executor.Execute(SearchQueryParser.Parse("tag:pantyhose"));
        Assert.Equal(new[] { @"C:\Lib\WithSocks.zip" }, byAlias1);
        Assert.Equal(new[] { @"C:\Lib\WithSocks.zip" }, byAlias2);

        // 3. 負向過濾別名（-tag:pantyhose）
        var negated = executor.Execute(SearchQueryParser.Parse("-tag:pantyhose"));
        Assert.Equal(new[] { @"C:\Lib\NoSocks.zip" }, negated);

        // 4. 拼錯別名（tag:stokings）→ FuzzyRecommender 應推薦虛擬主標籤（譯名「絲襪系」）
        var typoRaw = "tag:stokings";
        var recs = SearchFuzzyRecommender.FindRecommendations(_store, typoRaw, SearchQueryParser.Parse(typoRaw), "zh-TW");
        Assert.Contains(recs, r => r.DisplayText == "tag:絲襪系" && r.ReplacementQuery == "tag:絲襪系");
    }

    private sealed class StubClient : IMetadataAPIClient
    {
        public string SourceName => "nhentai";
        public Task<MetadataLookupResult?> GetByExternalIdAsync(string externalId, CancellationToken ct = default) => Task.FromResult<MetadataLookupResult?>(null);
        public Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(string query, int page = 1, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MetadataSearchHit>>(Array.Empty<MetadataSearchHit>());
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(new ConnectionTestResult(true, "ok", null));
    }
}
