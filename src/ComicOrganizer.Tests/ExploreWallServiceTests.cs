using ComicOrganizer.App.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// Phase 1 W1.1 ExploreWallService 聚合查詢與相容性單元測試。
/// </summary>
public class ExploreWallServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public ExploreWallServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_explore_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private long SeedWork(
        string path,
        string title,
        long? authorId,
        string? thumbKey = null,
        ReadStatus status = ReadStatus.Unread,
        string? externalId = null,
        params long[] tagIds)
    {
        _store.Upsert(new AssetMeta
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsFolder = false,
            FileSize = 2048,
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
            ReadStatus = status,
            ReadCount = status == ReadStatus.Unread ? 0 : 1,
            ExternalId = externalId,
            ParsedAt = 1000,
        });

        foreach (var tid in tagIds)
            _store.AssociateTag(assetId, tid);

        return assetId;
    }

    [Fact]
    public void GetAuthorCards_AggregatesWorksReadCountsTopTagsAndRepresentativeCovers()
    {
        var flugelId = _store.UpsertAuthor("Flugel");
        _store.AddAuthorAlias(flugelId, "フューゲル", "ja");

        var tStockings = _store.UpsertTag("stockings", "tag");
        var tMaid = _store.UpsertTag("maid", "tag");
        var tUniform = _store.UpsertTag("schoolgirl uniform", "tag");
        var tCatgirl = _store.UpsertTag("catgirl", "tag");
        var tSoleFemale = _store.UpsertTag("sole female", "tag");

        _store.UpsertTagTranslation(tStockings, "zh-TW", "絲襪", "manual");
        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");
        _store.UpsertTagTranslation(tUniform, "zh-TW", "水手服", "manual");

        // 4 部作品：2 部同標題 (應盡量去重優先挑不同標題)、1 部無縮圖、1 部已完成
        SeedWork(@"C:\Lib\Flugel\W1.zip", "Maid Diary", flugelId, "k_w1", ReadStatus.Unread, "101", tStockings, tMaid, tSoleFemale);
        SeedWork(@"C:\Lib\Flugel\W1_v2.zip", "Maid Diary", flugelId, "k_w1_v2", ReadStatus.Completed, "102", tStockings, tMaid);
        SeedWork(@"C:\Lib\Flugel\W2.zip", "Cat Cafe", flugelId, "k_w2", ReadStatus.Reading, "103", tStockings, tCatgirl);
        SeedWork(@"C:\Lib\Flugel\W3.zip", "After School", flugelId, null, ReadStatus.Unread, null, tStockings, tUniform);

        var svc = new ExploreWallService(_store);
        var cards = svc.GetAuthorCards();

        var card = Assert.Single(cards);
        Assert.Equal("Flugel", card.MainName);
        Assert.Contains("フューゲル", card.Aliases);
        Assert.Equal(4, card.TotalWorks);
        Assert.Equal(4, card.AllLibraryWorks);
        Assert.Equal(2, card.UnreadWorks);
        Assert.Equal(1, card.ReadingWorks);
        Assert.Equal(1, card.CompletedWorks);
        Assert.Equal(5, card.DistinctTagCount);

        // 非結構標籤優先，最多出現的 stockings (zh-TW=絲襪) 排第 1
        Assert.Equal("絲襪", card.TopTags[0].DisplayName);

        // 代表作 3 張：優先挑有縮圖且不重複標題 (Maid Diary, Cat Cafe)，再補齊第 3 張有縮圖作品
        Assert.Equal(3, card.RepresentativeWorks.Count);
        Assert.All(card.RepresentativeWorks, w => Assert.NotNull(w.CoverThumbKey));
        Assert.Contains(card.RepresentativeWorks, w => w.Title == "Maid Diary");
        Assert.Contains(card.RepresentativeWorks, w => w.Title == "Cat Cafe");
    }

    [Fact]
    public void GetAuthorCards_SupportsMultiTagAndFilter_KeywordSearch_UnreadOnly_AndSorting()
    {
        var aAlpha = _store.UpsertAuthor("Alpha");
        var aBeta = _store.UpsertAuthor("Beta");
        _store.AddAuthorAlias(aBeta, "ベータ");

        var tSocks = _store.UpsertTag("stockings", "tag");
        var tGlasses = _store.UpsertTag("glasses", "tag");
        _store.UpsertTagTranslation(tSocks, "zh-TW", "絲襪", "manual");
        _store.UpsertTagTranslation(tGlasses, "zh-TW", "眼鏡", "manual");

        // Alpha: 2 部（1 未讀同時有襪+眼鏡，1 已讀只有襪）
        SeedWork(@"C:\Lib\Alpha\A1.zip", "Story A1", aAlpha, "ka1", ReadStatus.Unread, null, tSocks, tGlasses);
        SeedWork(@"C:\Lib\Alpha\A2.zip", "Story A2", aAlpha, "ka2", ReadStatus.Completed, null, tSocks);

        // Beta: 3 部（全部有襪，全部未讀，但沒有眼鏡）
        SeedWork(@"C:\Lib\Beta\B1.zip", "Story B1", aBeta, "kb1", ReadStatus.Unread, null, tSocks);
        SeedWork(@"C:\Lib\Beta\B2.zip", "Story B2", aBeta, "kb2", ReadStatus.Unread, null, tSocks);
        SeedWork(@"C:\Lib\Beta\B3.zip", "Story B3", aBeta, "kb3", ReadStatus.Unread, null, tSocks);

        var svc = new ExploreWallService(_store);

        // 1. 預設依作品數 DESC：Beta(3) > Alpha(2)
        var allCards = svc.GetAuthorCards();
        Assert.Equal(new[] { "Beta", "Alpha" }, allCards.Select(c => c.MainName));

        // 2. 多選標籤 AND 過濾（絲襪 + 眼鏡）：只剩 Alpha 且命中數 = 1
        var intersected = svc.GetAuthorCards(new ExploreQueryOptions
        {
            SelectedTagIds = new[] { tSocks, tGlasses },
        });
        var single = Assert.Single(intersected);
        Assert.Equal("Alpha", single.MainName);
        Assert.Equal(1, single.TotalWorks);
        Assert.Equal(2, single.AllLibraryWorks);

        // 3. 關鍵字比對別名 & 標籤譯名
        Assert.Equal("Beta", Assert.Single(svc.GetAuthorCards(new ExploreQueryOptions { SearchText = "ベータ" })).MainName);
        Assert.Equal("Alpha", Assert.Single(svc.GetAuthorCards(new ExploreQueryOptions { SearchText = "眼鏡" })).MainName);

        // 4. 名稱 A-Z 排序
        var byName = svc.GetAuthorCards(new ExploreQueryOptions { SortMode = ExploreSortMode.NameAsc });
        Assert.Equal(new[] { "Alpha", "Beta" }, byName.Select(c => c.MainName));

        // 5. 隨機洗牌同 seed 可重現
        var r1 = svc.GetAuthorCards(new ExploreQueryOptions { SortMode = ExploreSortMode.RandomShuffle, RandomSeed = 42 });
        var r2 = svc.GetAuthorCards(new ExploreQueryOptions { SortMode = ExploreSortMode.RandomShuffle, RandomSeed = 42 });
        Assert.Equal(r1.Select(x => x.AuthorId), r2.Select(x => x.AuthorId));
    }

    [Fact]
    public void GetContentThemeCards_And_ParodyCards_ApplyTranslationsAndSeparateCategories()
    {
        var a1 = _store.UpsertAuthor("Artist1");
        var tVirtual = _store.UpsertTag("虛擬絲襪組", "virtual");
        var tMaid = _store.UpsertTag("maid", "tag");
        var tTouhou = _store.UpsertTag("touhou project", "parody");
        var tReimu = _store.UpsertTag("reimu hakurei", "character");
        var tTranslated = _store.UpsertTag("translated", "tag");

        _store.UpsertTagTranslation(tMaid, "zh-TW", "女僕", "manual");

        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
            {
              "data": [
                { "namespace": "parody", "data": { "touhou project": { "name": "东方Project" } } }
              ]
            }
            """));
        var translator = new TagTranslator(() => TagTranslationDb.LoadFromJsonStream(ms));

        SeedWork(@"C:\Lib\1.zip", "Work 1", a1, "k1", ReadStatus.Unread, "1", tVirtual, tMaid, tTouhou, tReimu, tTranslated);
        SeedWork(@"C:\Lib\2.zip", "Work 2", a1, "k2", ReadStatus.Unread, "2", tMaid, tTranslated);

        var svc = new ExploreWallService(_store, translator);

        var content = svc.GetContentThemeCards();
        Assert.Contains(content, c => c.TagId == tMaid && c.DisplayName == "女僕" && c.WorkCount == 2);
        Assert.Contains(content, c => c.TagId == tVirtual && c.WorkCount == 1);
        Assert.DoesNotContain(content, c => c.TagId == tTouhou || c.TagId == tReimu);
        // 結構性標籤 translated 應排在非結構性之後
        Assert.Equal(tTranslated, content[^1].TagId);

        var parodies = svc.GetParodyAndCharacterCards();
        Assert.Equal(2, parodies.Count);
        var touhouCard = parodies.First(p => p.TagId == tTouhou);
        Assert.Equal("東方Project", touhouCard.DisplayName); // Fallback translator 並轉繁體
    }

    [Fact]
    public void GetHotComboCards_And_PickRandomDoubleCombo_GuaranteeRealIntersection()
    {
        var a1 = _store.UpsertAuthor("ComboArtist");
        var tMaid = _store.UpsertTag("maid", "tag");
        var tGlasses = _store.UpsertTag("glasses", "tag");
        var tFox = _store.UpsertTag("fox girl", "tag");
        var tSole = _store.UpsertTag("sole female", "tag"); // 結構性標籤應避開碰撞抽籤

        SeedWork(@"C:\Lib\C1.zip", "Maid Glasses 1", a1, "kc1", ReadStatus.Unread, null, tMaid, tGlasses, tSole);
        SeedWork(@"C:\Lib\C2.zip", "Maid Glasses 2", a1, "kc2", ReadStatus.Unread, null, tMaid, tGlasses, tSole);
        SeedWork(@"C:\Lib\C3.zip", "Fox Glasses 1", a1, "kc3", ReadStatus.Unread, null, tFox, tGlasses, tSole);
        SeedWork(@"C:\Lib\C4.zip", "Fox Glasses 2", a1, "kc4", ReadStatus.Unread, null, tFox, tGlasses, tSole);

        var svc = new ExploreWallService(_store);

        var combos = svc.GetHotComboCards(minPairWorks: 2);
        Assert.Equal(2, combos.Count);
        Assert.All(combos, c =>
        {
            Assert.Equal(2, c.WorkCount);
            Assert.NotEqual(tSole, c.FirstTag.TagId);
            Assert.NotEqual(tSole, c.SecondTag.TagId);
        });

        var firstPick = svc.PickRandomDoubleCombo(minWorks: 2, seed: 7);
        Assert.NotNull(firstPick);
        Assert.Equal(2, firstPick!.WorkCount);
        Assert.NotEqual(tSole, firstPick.FirstTag.TagId);
        Assert.NotEqual(tSole, firstPick.SecondTag.TagId);

        // 下一次抽應盡量避開上一組，抽中另一組真實存在交集的組合
        var secondPick = svc.PickRandomDoubleCombo(
            previousPair: (firstPick.FirstTag.TagId, firstPick.SecondTag.TagId),
            minWorks: 2,
            seed: 7);
        Assert.NotNull(secondPick);
        var set1 = new HashSet<long> { firstPick.FirstTag.TagId, firstPick.SecondTag.TagId };
        var set2 = new HashSet<long> { secondPick!.FirstTag.TagId, secondPick.SecondTag.TagId };
        Assert.False(set1.SetEquals(set2));
    }

    [Fact]
    public void GetTopSpotlightTags_And_GetAllPickerTags_SupportAliasesAndCategories()
    {
        var a1 = _store.UpsertAuthor("A1");
        var tOld1 = _store.UpsertTag("stockings", "tag");
        var tOld2 = _store.UpsertTag("pantyhose", "tag");
        var tVirtual = _store.MergeTagsIntoVirtual(new[] { tOld1, tOld2 }, "絲襪系", "virtual", "絲襪系");
        var tParody = _store.UpsertTag("blue archive", "parody");
        _store.UpsertTagTranslation(tParody, "zh-TW", "蔚藍檔案", "manual");

        SeedWork(@"C:\Lib\P1.zip", "P1", a1, "kp1", ReadStatus.Unread, null, tVirtual, tParody);
        SeedWork(@"C:\Lib\P2.zip", "P2", a1, "kp2", ReadStatus.Unread, null, tVirtual);

        var svc = new ExploreWallService(_store);

        // 已選標籤 tParody 應釘選在第一位
        var spotlight = svc.GetTopSpotlightTags(selectedTagIds: new[] { tParody }, topCount: 5);
        Assert.Equal(tParody, spotlight[0].TagId);

        // 全標籤挑選器可用英文別名（stockings）搜到「絲襪系」
        var byAlias = svc.GetAllPickerTags(keyword: "stockings");
        var matched = Assert.Single(byAlias);
        Assert.Equal(tVirtual, matched.TagId);

        // 分類過濾 parody
        var parodies = svc.GetAllPickerTags(categoryFilter: "parody");
        Assert.Equal(tParody, Assert.Single(parodies).TagId);
    }

    [Fact]
    public void DrillDownQueries_And_InvalidateCache_WorkAsExpected()
    {
        var a1 = _store.UpsertAuthor("AuthorOne");
        var t1 = _store.UpsertTag("yuri", "tag");
        var t2 = _store.UpsertTag("schoolgirl", "tag");

        SeedWork(@"C:\Lib\D1.zip", "Work 1", a1, "kd1", ReadStatus.Unread, null, t1, t2);
        SeedWork(@"C:\Lib\D2.zip", "Work 2", a1, "kd2", ReadStatus.Completed, null, t1);

        var svc = new ExploreWallService(_store);
        Assert.Equal(2, svc.GetWorksForAuthor(a1).Count);
        Assert.Single(svc.GetWorksForAuthor(a1, new[] { t1, t2 }));
        Assert.Equal(2, svc.GetWorksForTags(new[] { t1, t2 }, intersect: false).Count);
        Assert.Single(svc.GetWorksForTags(new[] { t1, t2 }, intersect: true));

        // 寫入第 3 部作品，未清除快取前維持 2，InvalidateCache 後變為 3
        SeedWork(@"C:\Lib\D3.zip", "Work 3", a1, "kd3", ReadStatus.Unread, null, t1);
        Assert.Equal(2, svc.GetStatsSummary().TotalWorks);

        svc.InvalidateCache();
        Assert.Equal(3, svc.GetStatsSummary().TotalWorks);
        Assert.Equal(3, svc.GetWorksForAuthor(a1).Count);
    }

    [Fact]
    public void ExploreWallService_OnReadOnlySchemaV8Database_WorksWithoutTagAliasTable()
    {
        var v8Path = Path.Combine(Path.GetTempPath(), $"comic_explore_v8_{Guid.NewGuid():N}.db");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={v8Path};"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE schema_version (version INTEGER PRIMARY KEY);
INSERT INTO schema_version (version) VALUES (8);
CREATE TABLE asset (id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT NOT NULL UNIQUE, name TEXT NOT NULL, is_folder INTEGER NOT NULL, file_size INTEGER, mtime INTEGER NOT NULL, added_at INTEGER NOT NULL);
CREATE TABLE asset_thumb (asset_id INTEGER PRIMARY KEY, cover_origin TEXT, cover_thumb_key TEXT, cover_width INTEGER, cover_height INTEGER, last_scanned INTEGER NOT NULL, last_accessed INTEGER NOT NULL);
CREATE TABLE author (id INTEGER PRIMARY KEY AUTOINCREMENT, main_name TEXT NOT NULL UNIQUE, folder_path TEXT, learned_at INTEGER NOT NULL);
CREATE TABLE author_alias (id INTEGER PRIMARY KEY AUTOINCREMENT, author_id INTEGER NOT NULL, name TEXT NOT NULL, lang TEXT);
CREATE TABLE asset_meta (asset_id INTEGER PRIMARY KEY, author_id INTEGER, title TEXT, series TEXT, chapter_no TEXT, language TEXT, translation_group TEXT, page_count INTEGER, rating INTEGER, read_status INTEGER NOT NULL DEFAULT 0, read_count INTEGER NOT NULL DEFAULT 0, last_read INTEGER, external_id TEXT, parsed_at INTEGER NOT NULL, last_page INTEGER, manually_edited INTEGER NOT NULL DEFAULT 0);
CREATE TABLE tag (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, category TEXT);
CREATE TABLE asset_tag (asset_id INTEGER NOT NULL, tag_id INTEGER NOT NULL, PRIMARY KEY (asset_id, tag_id));
CREATE TABLE author_tag (author_id INTEGER NOT NULL, tag_id INTEGER NOT NULL, PRIMARY KEY (author_id, tag_id));
CREATE TABLE tag_translation (tag_id INTEGER NOT NULL, lang TEXT NOT NULL, name TEXT NOT NULL, source TEXT, updated_at INTEGER, PRIMARY KEY (tag_id, lang));

INSERT INTO author (id, main_name, learned_at) VALUES (1, 'LegacyAuthor', 100);
INSERT INTO asset (id, path, name, is_folder, file_size, mtime, added_at) VALUES (1, 'C:\V8\1.zip', '1.zip', 0, 100, 100, 100);
INSERT INTO asset_thumb (asset_id, cover_thumb_key, last_scanned, last_accessed) VALUES (1, 'key1', 100, 100);
INSERT INTO asset_meta (asset_id, author_id, title, read_status, read_count, parsed_at) VALUES (1, 1, 'Legacy Title', 0, 0, 100);
INSERT INTO tag (id, name, category) VALUES (1, 'kemonomimi', 'tag');
INSERT INTO tag_translation (tag_id, lang, name, source, updated_at) VALUES (1, 'zh-TW', '獸耳', 'ehtag', 100);
INSERT INTO asset_tag (asset_id, tag_id) VALUES (1, 1);
";
                cmd.ExecuteNonQuery();
            }

            using var readOnlyStore = new SqliteAssetMetaStore(v8Path, readOnly: true);
            var svc = new ExploreWallService(readOnlyStore);

            var stats = svc.GetStatsSummary();
            Assert.Equal(1, stats.TotalWorks);
            Assert.Equal(1, stats.TotalAuthors);

            var authors = svc.GetAuthorCards();
            var card = Assert.Single(authors);
            Assert.Equal("LegacyAuthor", card.MainName);
            Assert.Equal("獸耳", card.TopTags[0].DisplayName);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(v8Path)) File.Delete(v8Path);
        }
    }
}
