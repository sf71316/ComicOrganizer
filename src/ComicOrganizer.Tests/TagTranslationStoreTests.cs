using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// schema v6 tag_translation 測試 — 6 個新 API + migration 確認。
/// </summary>
public class TagTranslationStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public TagTranslationStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_tagtr_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private long SeedTag(string name, string? category = null)
    {
        // 用 SetAssetTags 觸發 tag upsert（store 沒有 public UpsertTag）
        var p = $@"C:\L\seed_{Guid.NewGuid():N}.zip";
        _store.Upsert(new AssetMeta { Path = p, Name = "x", IsFolder = false, FileSize = 1, Mtime = 1 });
        var assetId = _store.GetAssetId(p)!.Value;
        _store.UpsertMetadata(new AssetMetadata { AssetId = assetId, Title = "T", ParsedAt = 1 });
        _store.SetAssetTags(assetId, new[] { name });
        var tag = _store.SearchTags(name, 5).First();
        if (category != null) _store.SetTagCategory(tag.Id, category);
        return tag.Id;
    }

    [Fact]
    public void V6_TagTranslationTable_ExistsAndCanInsert()
    {
        var tagId = SeedTag("big breasts");
        _store.UpsertTagTranslation(tagId, "zh-TW", "巨乳", "ehtag");
        Assert.Equal("巨乳", _store.GetTagTranslation(tagId, "zh-TW"));
    }

    [Fact]
    public void GetTagTranslation_NoMatch_ReturnsNull()
    {
        var tagId = SeedTag("vanilla");
        Assert.Null(_store.GetTagTranslation(tagId, "zh-TW"));
    }

    [Fact]
    public void Upsert_DuplicateLang_Overwrites()
    {
        var tagId = SeedTag("milf");
        _store.UpsertTagTranslation(tagId, "zh-TW", "人妻", "ehtag");
        _store.UpsertTagTranslation(tagId, "zh-TW", "熟女", "manual");

        var translations = _store.ListTagTranslations(tagId);
        Assert.Single(translations);
        Assert.Equal("熟女", translations[0].Name);
        Assert.Equal("manual", translations[0].Source);
    }

    [Fact]
    public void Upsert_DifferentLangs_CoexistOnSameTag()
    {
        var tagId = SeedTag("translated");
        _store.UpsertTagTranslation(tagId, "zh-TW", "翻譯版", "ehtag");
        _store.UpsertTagTranslation(tagId, "zh-CN", "翻译版", "ehtag");
        _store.UpsertTagTranslation(tagId, "ja", "翻訳版", "manual");

        var translations = _store.ListTagTranslations(tagId);
        Assert.Equal(3, translations.Count);
        var dict = translations.ToDictionary(t => t.Lang, t => t.Name);
        Assert.Equal("翻譯版", dict["zh-TW"]);
        Assert.Equal("翻译版", dict["zh-CN"]);
        Assert.Equal("翻訳版", dict["ja"]);
    }

    [Fact]
    public void DeleteTagTranslation_RemovesOnlyThatLang()
    {
        var tagId = SeedTag("big breasts");
        _store.UpsertTagTranslation(tagId, "zh-TW", "巨乳", "ehtag");
        _store.UpsertTagTranslation(tagId, "ja", "巨乳", "ehtag");
        _store.DeleteTagTranslation(tagId, "zh-TW");

        Assert.Null(_store.GetTagTranslation(tagId, "zh-TW"));
        Assert.Equal("巨乳", _store.GetTagTranslation(tagId, "ja"));
    }

    [Fact]
    public void GetTagTranslations_BatchLookup()
    {
        var t1 = SeedTag("tag1");
        var t2 = SeedTag("tag2");
        var t3 = SeedTag("tag3_no_translation");
        _store.UpsertTagTranslation(t1, "zh-TW", "標籤一", "manual");
        _store.UpsertTagTranslation(t2, "zh-TW", "標籤二", "manual");

        var batch = _store.GetTagTranslations(new[] { t1, t2, t3 }, "zh-TW");
        Assert.Equal(2, batch.Count);
        Assert.Equal("標籤一", batch[t1]);
        Assert.Equal("標籤二", batch[t2]);
        Assert.False(batch.ContainsKey(t3));   // 沒譯文不出現
    }

    [Fact]
    public void SearchTagIdsByTranslatedName_ChineseLike()
    {
        var t1 = SeedTag("milf");
        var t2 = SeedTag("teenage");
        _store.UpsertTagTranslation(t1, "zh-TW", "人妻", "manual");
        _store.UpsertTagTranslation(t2, "zh-TW", "少女", "manual");

        var hits = _store.SearchTagIdsByTranslatedName("人妻", "zh-TW");
        Assert.Single(hits);
        Assert.Equal(t1, hits[0]);
    }

    [Fact]
    public void SearchTagIdsByTranslatedName_NullLangSearchesAllLangs()
    {
        var t1 = SeedTag("a");
        var t2 = SeedTag("b");
        _store.UpsertTagTranslation(t1, "zh-TW", "繁體", "manual");
        _store.UpsertTagTranslation(t2, "ja", "日文", "manual");

        var hits = _store.SearchTagIdsByTranslatedName("文", null);   // 跨語言搜尋
        Assert.Contains(t2, hits);
    }

    [Fact]
    public void DeleteTag_CascadesTranslation()
    {
        var tagId = SeedTag("doomed");
        _store.UpsertTagTranslation(tagId, "zh-TW", "已死", "manual");
        _store.DeleteTag(tagId);
        // tag 刪了，translation 也應 cascade 清掉
        Assert.Empty(_store.ListTagTranslations(tagId));
    }

    [Fact]
    public void SyncTranslationsToDb_PopulatesDbAndPreservesManual()
    {
        var translator = new TagTranslator();
        Assert.True(translator.IsAvailable);

        var idBigBreasts = SeedTag("big breasts");
        var idMilf = SeedTag("milf");

        // 手動譯文
        _store.UpsertTagTranslation(idBigBreasts, "zh-TW", "自訂巨乳", "manual");

        var map = new Dictionary<string, long>
        {
            { "big breasts", idBigBreasts },
            { "milf", idMilf },
        };

        int written = translator.SyncTranslationsToDb(_store, "zh-TW", map);

        Assert.Equal(1, written); // 只寫入 milf，big breasts 被 manual 保護

        // big breasts 保持手動
        var trBig = _store.ListTagTranslations(idBigBreasts).First(t => t.Lang == "zh-TW");
        Assert.Equal("自訂巨乳", trBig.Name);
        Assert.Equal("manual", trBig.Source);

        // milf 寫入自動譯文 (source='ehtag')
        var trMilf = _store.ListTagTranslations(idMilf).First(t => t.Lang == "zh-TW");
        Assert.Equal("ehtag", trMilf.Source);
        Assert.NotEmpty(trMilf.Name);
    }
}
