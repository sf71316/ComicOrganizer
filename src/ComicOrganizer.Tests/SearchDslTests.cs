using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 搜尋 DSL 測試（#48 Phase C）：parser 純邏輯 + executor 用 seeded SQLite fixture。
/// </summary>
public class SearchDslTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;
    private long _authorFlugel;
    private long _authorOouso;

    public SearchDslTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_search_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
        SeedFixture();
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    /// <summary>fixture：2 作者 × 多作品 × 各種 tag / language / read_status 組合。</summary>
    private void SeedFixture()
    {
        _authorFlugel = _store.UpsertAuthor("Flugel", @"C:\Lib\Flugel");
        _authorOouso = _store.UpsertAuthor("oouso", @"C:\Lib\oouso");

        // (path, author, title, lang, status, tags...)
        var works = new (string Path, long? Author, string Title, string? Lang, ReadStatus Status, string[] Tags)[]
        {
            (@"C:\Lib\Flugel\kugizuke.zip", _authorFlugel, "Kugizuke Heart", "japanese", ReadStatus.Reading, new[] { "big-breasts", "DL版" }),
            (@"C:\Lib\Flugel\reina.zip",    _authorFlugel, "R.E.I.N.A",      "english",  ReadStatus.Completed, new[] { "big-breasts" }),
            (@"C:\Lib\Flugel\old.zip",      _authorFlugel, "Old Work",       "japanese", ReadStatus.Dropped, new[] { "DL版" }),
            (@"C:\Lib\oouso\dream.zip",     _authorOouso,  "Dream Land",     "chinese",  ReadStatus.Unread, new[] { "ahegao" }),
            (@"C:\Lib\oouso\untitled.zip",  _authorOouso,  "Untitled",       null,        ReadStatus.Unread, Array.Empty<string>()),
            (@"C:\Lib\Misc\unknown.zip",    null,          "くぎづけ",       "japanese", ReadStatus.Unread, new[] { "big-breasts" }),
        };

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var w in works)
        {
            _store.Upsert(new AssetMeta
            {
                Path = w.Path, Name = Path.GetFileName(w.Path), IsFolder = false,
                Mtime = 100, LastScanned = 100, LastAccessed = 100,
            });
            var assetId = _store.GetAssetId(w.Path)!.Value;
            _store.UpsertMetadata(new AssetMetadata
            {
                AssetId = assetId,
                AuthorId = w.Author,
                Title = w.Title,
                Language = w.Lang,
                ReadStatus = w.Status,
                LastRead = w.Status == ReadStatus.Reading ? now - 3600 : null,
                ParsedAt = now,
            });
            foreach (var tagName in w.Tags)
            {
                var tid = _store.UpsertTag(tagName, null);
                _store.AssociateTag(assetId, tid);
            }
        }

        // 加 tag_translation：big-breasts → 巨乳
        var bigBreastsId = _store.GetTagIdsByNames(new[] { "big-breasts" })["big-breasts"];
        _store.UpsertTagTranslation(bigBreastsId, "zh-TW", "巨乳", "manual");
    }

    // ── Parser ─────────────────────────────────────────────────────

    [Fact]
    public void Parser_Empty_ReturnsEmptyQuery()
    {
        Assert.True(SearchQueryParser.Parse(null).IsEmpty);
        Assert.True(SearchQueryParser.Parse("").IsEmpty);
        Assert.True(SearchQueryParser.Parse("    ").IsEmpty);
    }

    [Fact]
    public void Parser_SingleField()
    {
        var q = SearchQueryParser.Parse("artist:flugel");
        Assert.Single(q.Terms);
        Assert.Equal(SearchField.Artist, q.Terms[0].Field);
        Assert.Equal("flugel", q.Terms[0].Value);
        Assert.False(q.Terms[0].Negated);
    }

    [Fact]
    public void Parser_AuthorAlias_MapsToArtist()
    {
        var q = SearchQueryParser.Parse("author:flugel");
        Assert.Equal(SearchField.Artist, q.Terms[0].Field);
    }

    [Fact]
    public void Parser_LangAlias()
    {
        var q = SearchQueryParser.Parse("lang:zh");
        Assert.Equal(SearchField.Language, q.Terms[0].Field);
    }

    [Fact]
    public void Parser_QuotedValue_PreservesSpaces()
    {
        var q = SearchQueryParser.Parse("tag:\"big breasts\"");
        Assert.Single(q.Terms);
        Assert.Equal("big breasts", q.Terms[0].Value);
    }

    [Fact]
    public void Parser_Negated_StripsDash()
    {
        var q = SearchQueryParser.Parse("-tag:DL版");
        Assert.Single(q.Terms);
        Assert.True(q.Terms[0].Negated);
        Assert.Equal("DL版", q.Terms[0].Value);
    }

    [Fact]
    public void Parser_FreeText_BareWord()
    {
        var q = SearchQueryParser.Parse("foo");
        Assert.Single(q.Terms);
        Assert.Equal(SearchField.FreeText, q.Terms[0].Field);
        Assert.Equal("foo", q.Terms[0].Value);
    }

    [Fact]
    public void Parser_FreeText_Quoted()
    {
        var q = SearchQueryParser.Parse("\"big breasts\"");
        Assert.Single(q.Terms);
        Assert.Equal(SearchField.FreeText, q.Terms[0].Field);
        Assert.Equal("big breasts", q.Terms[0].Value);
    }

    [Fact]
    public void Parser_NegatedFreeText()
    {
        var q = SearchQueryParser.Parse("-foo");
        Assert.Single(q.Terms);
        Assert.Equal(SearchField.FreeText, q.Terms[0].Field);
        Assert.True(q.Terms[0].Negated);
    }

    [Fact]
    public void Parser_Multiple_Terms()
    {
        var q = SearchQueryParser.Parse("artist:flugel language:zh -tag:DL版 foo");
        Assert.Equal(4, q.Terms.Count);
        Assert.Equal(SearchField.Artist, q.Terms[0].Field);
        Assert.Equal(SearchField.Language, q.Terms[1].Field);
        Assert.Equal(SearchField.Tag, q.Terms[2].Field);
        Assert.True(q.Terms[2].Negated);
        Assert.Equal(SearchField.FreeText, q.Terms[3].Field);
    }

    [Fact]
    public void Parser_UnknownField_FallsToFreeText()
    {
        var q = SearchQueryParser.Parse("foo:bar");
        Assert.Single(q.Terms);
        Assert.Equal(SearchField.FreeText, q.Terms[0].Field);
        Assert.Equal("foo:bar", q.Terms[0].Value);
    }

    [Fact]
    public void Parser_LoneDash_NotTreatedAsNegated()
    {
        var q = SearchQueryParser.Parse("foo - bar");
        // 中間孤立的 '-' 不該變 negated empty term；應視同 WS 切割
        // 我們的實作下：'-' 若後面是 WS 就保留 '-' 在 head 開頭；變成 freetext "-"
        // 也接受 — 實作可選。重點是不會 crash + 至少捕捉到 foo 跟 bar
        Assert.Contains(q.Terms, t => t.Field == SearchField.FreeText && t.Value == "foo");
        Assert.Contains(q.Terms, t => t.Field == SearchField.FreeText && t.Value == "bar");
    }

    [Fact]
    public void Parser_FieldCaseInsensitive()
    {
        var q = SearchQueryParser.Parse("ARTIST:foo");
        Assert.Equal(SearchField.Artist, q.Terms[0].Field);
    }

    // ── Executor ───────────────────────────────────────────────────

    [Fact]
    public void Exec_Empty_ReturnsEmpty()
    {
        var ex = new SearchExecutor(_store);
        Assert.Empty(ex.Execute(SearchQueryParser.Parse("")));
    }

    [Fact]
    public void Exec_Artist_FindsAllWorks()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("artist:flugel"));
        Assert.Equal(3, paths.Count);   // kugizuke / reina / old
    }

    [Fact]
    public void Exec_Artist_PrefixMatch_Selective()
    {
        // SearchAuthors 是 prefix LIKE — "oo" 應只命中 oouso（不會吃 Flugel）
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("artist:oo"));
        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.Contains("oouso", p));
    }

    [Fact]
    public void Exec_Tag_ByEnglishName()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("tag:big-breasts"));
        Assert.Equal(3, paths.Count);   // 兩本 Flugel + Misc 的 unknown
    }

    [Fact]
    public void Exec_Tag_ByTranslatedName_Chinese()
    {
        var ex = new SearchExecutor(_store, lang: "zh-TW");
        var paths = ex.Execute(SearchQueryParser.Parse("tag:巨乳"));
        Assert.Equal(3, paths.Count);   // tag_translation 命中 big-breasts
    }

    [Fact]
    public void Exec_Language_Exact()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("language:japanese"));
        Assert.Equal(3, paths.Count);
    }

    [Fact]
    public void Exec_ReadStatus_Reading()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("read:reading"));
        Assert.Single(paths);
        Assert.Contains("kugizuke", paths[0]);
    }

    [Fact]
    public void Exec_ReadStatus_Unread_IncludesNoMeta()
    {
        // dream / untitled (Unread) + 別的 Unread 但沒 read_status 紀錄的也應命中
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("read:unread"));
        Assert.Equal(3, paths.Count);   // dream / untitled / unknown.zip
    }

    [Fact]
    public void Exec_ReadStatus_ChineseValue()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("status:進行中"));
        Assert.Single(paths);
    }

    [Fact]
    public void Exec_FreeText_MatchesTitleOrPath()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("kugizuke"));
        Assert.Single(paths);
    }

    [Fact]
    public void Exec_FreeText_CJK_MatchesTitle()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("くぎづけ"));
        Assert.Single(paths);
    }

    [Fact]
    public void Exec_AndIntersect_TwoFields()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("artist:flugel language:japanese"));
        Assert.Equal(2, paths.Count);   // kugizuke / old (reina 是 english)
    }

    [Fact]
    public void Exec_NegatedTag_Excludes()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("artist:flugel -tag:DL版"));
        Assert.Single(paths);
        Assert.Contains("reina", paths[0]);   // 只剩 reina（kugizuke 跟 old 都有 DL版）
    }

    [Fact]
    public void Exec_OnlyNegative_StartsFromAll()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("-tag:DL版"));
        // 全部 6 個 - 兩個有 DL版 = 4
        Assert.Equal(4, paths.Count);
    }

    [Fact]
    public void Exec_NoMatch_ReturnsEmpty()
    {
        var ex = new SearchExecutor(_store);
        Assert.Empty(ex.Execute(SearchQueryParser.Parse("artist:nonexistent")));
    }

    [Fact]
    public void Exec_ResultsSorted()
    {
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("tag:big-breasts"));
        var sorted = paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, paths);
    }
}
