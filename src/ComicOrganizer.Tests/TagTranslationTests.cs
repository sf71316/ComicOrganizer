using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// TagTranslationDb + TagTranslator 測試（#53 Phase 1+2）。
/// 用 bundled embedded resource 載入實際 EhTag DB（v7.24083.1）。
/// </summary>
public class TagTranslationTests
{
    [Fact]
    public void EmbeddedResource_Loads_Successfully()
    {
        var db = TagTranslationDb.LoadFromEmbedded();
        Assert.NotEmpty(db.HeadSha);
        Assert.True(db.Version >= 7);
        Assert.True(db.Count > 30000, $"expected 30000+ entries, got {db.Count}");
    }

    [Fact]
    public void TryGet_KnownArtist_ReturnsEntry()
    {
        var db = TagTranslationDb.LoadFromEmbedded();
        Assert.True(db.TryGet("artist", "flugel", out var entry));
        Assert.Equal("Flugel", entry.Name);
        Assert.Equal("artist", entry.Namespace);
    }

    [Fact]
    public void TryGet_KnownArtist_WithCJKName()
    {
        var db = TagTranslationDb.LoadFromEmbedded();
        Assert.True(db.TryGet("artist", "oouso", out var entry));
        Assert.Equal("大嘘", entry.Name);
    }

    [Fact]
    public void TryGet_UnknownSlug_ReturnsFalse()
    {
        var db = TagTranslationDb.LoadFromEmbedded();
        Assert.False(db.TryGet("artist", "nonexistent_xyz_999", out _));
    }

    [Fact]
    public void TryGet_CaseInsensitive()
    {
        var db = TagTranslationDb.LoadFromEmbedded();
        Assert.True(db.TryGet("artist", "FLUGEL", out var e1));
        Assert.True(db.TryGet("artist", "Flugel", out var e2));
        Assert.Equal(e1.Name, e2.Name);
    }

    [Fact]
    public void TryGet_TagNamespace_BigBreasts()
    {
        var db = TagTranslationDb.LoadFromEmbedded();
        Assert.True(db.TryGet("female", "big breasts", out var entry));
        Assert.Equal("巨乳", entry.Name);
    }

    // ── TagTranslator ──────────────────────────────────────────────────

    [Fact]
    public void Translator_TranslateArtist()
    {
        var t = new TagTranslator();
        Assert.Equal("Flugel", t.TranslateArtist("flugel"));
        Assert.Equal("大嘘", t.TranslateArtist("oouso"));
    }

    [Fact]
    public void Translator_NhentaiTag_FallsThroughToFemale()
    {
        var t = new TagTranslator();
        // nhentai type "tag" → 試 female
        Assert.Equal("巨乳", t.Translate("tag", "big breasts"));
    }

    [Fact]
    public void Translator_NhentaiCategory_Unmapped_ReturnsNull()
    {
        var t = new TagTranslator();
        // category 在 EhTag 沒有對應 namespace
        Assert.Null(t.Translate("category", "manga"));
    }

    [Fact]
    public void Translator_Language_Japanese()
    {
        var t = new TagTranslator();
        var zh = t.Translate("language", "japanese");
        Assert.NotNull(zh);
        Assert.NotEmpty(zh!);
    }

    [Fact]
    public void Translator_IsKnownArtist()
    {
        var t = new TagTranslator();
        Assert.True(t.IsKnownArtist("flugel"));
        Assert.False(t.IsKnownArtist("nonexistent_xyz_999"));
    }

    [Fact]
    public void Translator_SuggestArtists_ByPrefix()
    {
        var t = new TagTranslator();
        var hits = t.SuggestArtists("flu", limit: 5).ToList();
        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Slug == "flugel");
    }

    [Fact]
    public void Translator_SuggestArtists_LimitRespected()
    {
        var t = new TagTranslator();
        var hits = t.SuggestArtists("a", limit: 3).ToList();
        Assert.True(hits.Count <= 3);
    }

    [Fact]
    public void Translator_TranslateMany_BatchTagging()
    {
        var t = new TagTranslator();
        var input = new List<MetadataTag>
        {
            new("artist", "flugel", "flugel", 92),
            new("tag", "big breasts", "big-breasts", 219192),
            new("category", "manga", "manga", 132213),
        };
        var result = t.TranslateMany(input);
        Assert.Equal(3, result.Count);
        Assert.Equal("Flugel", result[0].LocalizedName);
        Assert.Equal("巨乳", result[1].LocalizedName);
        Assert.Null(result[2].LocalizedName);   // category 沒翻譯
    }

    [Fact]
    public void Translator_FallbackOnLoadFailure_StillUsable()
    {
        // 注入一個故意失敗的 loader → translator 仍能運作（永遠回 null）
        var t = new TagTranslator(() => throw new InvalidOperationException("simulated"));
        Assert.False(t.IsAvailable);
        Assert.Null(t.TranslateArtist("flugel"));
        Assert.False(t.IsKnownArtist("flugel"));
        Assert.Empty(t.SuggestArtists("flu"));
    }
}
