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
        // oouso 在 EhTag 簡體名為「大嘘」；台灣繁體 culture 下 OpenCC 轉成「大噓」
        var oouso = t.TranslateArtist("oouso");
        Assert.NotNull(oouso);
        Assert.Contains("大", oouso!);
        Assert.True(oouso == "大嘘" || oouso == "大噓");
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

    // ── ZhConverter (字形 簡↔繁 轉換) ──────────────────────────────────

    [Theory]
    [InlineData("学校", "學校")]
    [InlineData("妈妈", "媽媽")]
    [InlineData("头发", "頭髮")]
    [InlineData("巨乳", "巨乳")]   // 簡繁同字
    [InlineData("中出", "中出")]   // 簡繁同字
    [InlineData("Flugel", "Flugel")]  // 純英文不變
    [InlineData("", "")]
    public void ZhConverter_S2T(string simplified, string expected)
    {
        Assert.Equal(expected, ZhConverter.ToTraditional(simplified));
    }

    [Theory]
    [InlineData("學校", "学校")]
    [InlineData("頭髮", "头发")]
    [InlineData("巨乳", "巨乳")]
    public void ZhConverter_T2S(string traditional, string expected)
    {
        Assert.Equal(expected, ZhConverter.ToSimplified(traditional));
    }

    // ── Phase 5-A：disk cache → embedded fallback ───────────────────────

    /// <summary>把 embedded resource 抄一份到指定 path（給 disk cache 測試用）。</summary>
    private static void CopyEmbeddedToFile(string destPath)
    {
        var asm = typeof(TagTranslationDb).Assembly;
        using var src = asm.GetManifestResourceStream("ComicOrganizer.App.Resources.db.text.json.gz")!;
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        using var dst = File.Create(destPath);
        src.CopyTo(dst);
    }

    [Fact]
    public void DiskFile_LoadFromFile_HasDiskCacheSource()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tt_disk_{Guid.NewGuid():N}.gz");
        try
        {
            CopyEmbeddedToFile(tmp);
            var db = TagTranslationDb.LoadFromFile(tmp);
            Assert.True(db.Count > 30000);
            Assert.Equal(TagTranslationDbSource.DiskCache, db.Source);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    [Fact]
    public void Embedded_HasEmbeddedSource()
    {
        var db = TagTranslationDb.LoadFromEmbedded();
        Assert.Equal(TagTranslationDbSource.Embedded, db.Source);
    }

    [Fact]
    public void Translator_DiskPath_PrioritizesDiskOverEmbedded()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tt_pri_{Guid.NewGuid():N}.gz");
        try
        {
            CopyEmbeddedToFile(tmp);
            var t = new TagTranslator(tmp);
            Assert.True(t.IsAvailable);
            Assert.Equal(TagTranslationDbSource.DiskCache, t.Source);
            // 仍能正常翻譯
            Assert.Equal("Flugel", t.TranslateArtist("flugel"));
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    [Fact]
    public void Translator_DiskPathMissing_FallsBackToEmbedded()
    {
        var nope = Path.Combine(Path.GetTempPath(), $"tt_nope_{Guid.NewGuid():N}.gz");
        Assert.False(File.Exists(nope));
        var t = new TagTranslator(nope);
        Assert.True(t.IsAvailable);
        Assert.Equal(TagTranslationDbSource.Embedded, t.Source);
        Assert.Equal("Flugel", t.TranslateArtist("flugel"));
    }

    [Fact]
    public void Translator_DiskPathCorrupt_FallsBackToEmbedded()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tt_bad_{Guid.NewGuid():N}.gz");
        try
        {
            File.WriteAllBytes(tmp, new byte[] { 0x00, 0x01, 0x02, 0x03 });   // 不是合法 gzip
            var t = new TagTranslator(tmp);
            Assert.True(t.IsAvailable);
            Assert.Equal(TagTranslationDbSource.Embedded, t.Source);
            Assert.Equal("Flugel", t.TranslateArtist("flugel"));
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    [Fact]
    public void Translator_NullDiskPath_UsesEmbedded()
    {
        var t = new TagTranslator((string?)null);
        Assert.True(t.IsAvailable);
        Assert.Equal(TagTranslationDbSource.Embedded, t.Source);
    }
}
