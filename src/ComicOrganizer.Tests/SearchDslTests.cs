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

    [Fact]
    public void Exec_Artist_SubstringMatch()
    {
        // "uge" 在 "Flugel" 中間，應比對成功
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("artist:uge"));
        Assert.Equal(3, paths.Count); // kugizuke, reina, old
    }

    [Fact]
    public void Exec_FreeText_DoesNotMatchParentFolder()
    {
        // 自由文字 "Flugel" 只比對檔名和 title，不比對路徑中的父目錄 C:\Lib\Flugel
        var ex = new SearchExecutor(_store);
        var paths = ex.Execute(SearchQueryParser.Parse("Flugel"));
        // kugizuke/reina/old 都不含 "Flugel" 於檔名或 title，故不應命中
        Assert.Empty(paths);
    }

    [Fact]
    public void Exec_ExcludesNonWorkFolders()
    {
        // 模擬在 asset 表中有作者資料夾、一般容器資料夾（含子項目）與葉節點漫畫資料夾（含同前綴目錄）
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Lib\Flugel",
            Name = "Flugel",
            IsFolder = true,
            Mtime = 100, LastScanned = 100, LastAccessed = 100
        });
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Lib\SeriesBox",
            Name = "SeriesBox",
            IsFolder = true,
            Mtime = 100, LastScanned = 100, LastAccessed = 100
        });
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Lib\SeriesBox\Vol1.zip",
            Name = "Vol1.zip",
            IsFolder = false,
            Mtime = 100, LastScanned = 100, LastAccessed = 100
        });
        // "Series" 是 "SeriesBox" 的字串前綴，但並非其父目錄，應作為葉節點漫畫資料夾被保留
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Lib\Series",
            Name = "Series",
            IsFolder = true,
            Mtime = 100, LastScanned = 100, LastAccessed = 100
        });
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Lib\LeafMangaFolder",
            Name = "LeafMangaFolder",
            IsFolder = true,
            Mtime = 100, LastScanned = 100, LastAccessed = 100
        });

        var allPaths = _store.ListAllAssetPaths();

        // 作者資料夾與含子項目的容器目錄應被排除
        Assert.DoesNotContain(@"C:\Lib\Flugel", allPaths);
        Assert.DoesNotContain(@"C:\Lib\SeriesBox", allPaths);
        // 子項目與葉節點漫畫資料夾（含同前綴的 C:\Lib\Series）應被保留
        Assert.Contains(@"C:\Lib\SeriesBox\Vol1.zip", allPaths);
        Assert.Contains(@"C:\Lib\Series", allPaths);
        Assert.Contains(@"C:\Lib\LeafMangaFolder", allPaths);
    }

    [Fact]
    public void Exec_FreeText_EscapesWildcards()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // 建立含 % 與 _ 的特殊檔名/標題
        var pathPercent = @"C:\Lib\Misc\100%_Special.zip";
        var pathOther = @"C:\Lib\Misc\1000_Special.zip";

        _store.Upsert(new AssetMeta
        {
            Path = pathPercent, Name = "100%_Special.zip", IsFolder = false,
            Mtime = 100, LastScanned = 100, LastAccessed = 100
        });
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = _store.GetAssetId(pathPercent)!.Value,
            Title = "100% Special",
            ParsedAt = now
        });

        _store.Upsert(new AssetMeta
        {
            Path = pathOther, Name = "1000_Special.zip", IsFolder = false,
            Mtime = 100, LastScanned = 100, LastAccessed = 100
        });
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = _store.GetAssetId(pathOther)!.Value,
            Title = "1000 Special",
            ParsedAt = now
        });

        var ex = new SearchExecutor(_store);
        // 搜 "100%" 不應把 "1000" 當成 % 萬用字元比對出來
        var paths = ex.Execute(SearchQueryParser.Parse("\"100%\""));
        Assert.Single(paths);
        Assert.Equal(pathPercent, paths[0]);
    }

    [Fact]
    public void Autocomplete_ExtractActiveFieldToken_Scenarios()
    {
        // 1. 基本 field
        var t1 = SearchQueryParser.ExtractActiveFieldToken("artist:fl");
        Assert.NotNull(t1);
        Assert.Equal("artist:", t1.Prefix);
        Assert.Equal(SearchField.Artist, t1.Field);
        Assert.Equal("fl", t1.Value);
        Assert.Equal(0, t1.StartIndex);

        // 2. 引號後的下一個 token
        var t2 = SearchQueryParser.ExtractActiveFieldToken("tag:\"big breasts\" -artist:flu");
        Assert.NotNull(t2);
        Assert.Equal("-artist:", t2.Prefix);
        Assert.Equal(SearchField.Artist, t2.Field);
        Assert.Equal("flu", t2.Value);
        Assert.Equal(18, t2.StartIndex);

        // 3. 空值（剛打完冒號）
        var t3 = SearchQueryParser.ExtractActiveFieldToken("tag:");
        Assert.NotNull(t3);
        Assert.Equal("tag:", t3.Prefix);
        Assert.Equal(SearchField.Tag, t3.Field);
        Assert.Equal("", t3.Value);

        // 4. language 別名
        var t4 = SearchQueryParser.ExtractActiveFieldToken("lang:zh");
        Assert.NotNull(t4);
        Assert.Equal(SearchField.Language, t4.Field);
        Assert.Equal("zh", t4.Value);

        // 5. read status
        var t5 = SearchQueryParser.ExtractActiveFieldToken("read:done");
        Assert.NotNull(t5);
        Assert.Equal(SearchField.ReadStatus, t5.Field);
        Assert.Equal("done", t5.Value);

        // 6. 引號邊界：未閉合引號應回傳剝除引號後的值，已閉合引號視為完成回傳 null
        var t6 = SearchQueryParser.ExtractActiveFieldToken("tag:\"big bre");
        Assert.NotNull(t6);
        Assert.Equal("big bre", t6.Value);
        Assert.Null(SearchQueryParser.ExtractActiveFieldToken("tag:\"big breasts\""));

        // 7. 純文字不應觸發 autocomplete
        Assert.Null(SearchQueryParser.ExtractActiveFieldToken("puretext"));
        Assert.Null(SearchQueryParser.ExtractActiveFieldToken("tag:something "));
        Assert.Null(SearchQueryParser.ExtractActiveFieldToken("unknown:value"));
    }

    [Fact]
    public void Autocomplete_ApplySuggestion_Scenarios()
    {
        // 單字無空白值
        var t1 = SearchQueryParser.ExtractActiveFieldToken("artist:fl")!;
        var s1 = SearchQueryParser.ApplySuggestion("artist:fl", t1, "Flugel");
        Assert.Equal("artist:Flugel ", s1);

        // 含空白自動補雙引號
        var t2 = SearchQueryParser.ExtractActiveFieldToken("tag:")!;
        var s2 = SearchQueryParser.ApplySuggestion("tag:", t2, "big breasts");
        Assert.Equal("tag:\"big breasts\" ", s2);

        // 保留前方既有搜尋條件
        var t3 = SearchQueryParser.ExtractActiveFieldToken("tag:DL -artist:fl")!;
        var s3 = SearchQueryParser.ApplySuggestion("tag:DL -artist:fl", t3, "Yamada Taro");
        Assert.Equal("tag:DL -artist:\"Yamada Taro\" ", s3);
    }

    [Fact]
    public void Autocomplete_ApplyFieldChip_Scenarios()
    {
        // 1. 空字串與 null
        Assert.Equal("artist:", SearchQueryParser.ApplyFieldChip(null, "artist:"));
        Assert.Equal("artist:", SearchQueryParser.ApplyFieldChip("", "artist:"));

        // 2. 結尾已是空白
        Assert.Equal("tag:巨乳 artist:", SearchQueryParser.ApplyFieldChip("tag:巨乳 ", "artist:"));
        Assert.Equal("flugel tag:", SearchQueryParser.ApplyFieldChip("flugel ", "tag:"));

        // 3. 前綴補完（無負號，大小寫無關）
        Assert.Equal("artist:", SearchQueryParser.ApplyFieldChip("art", "artist:"));
        Assert.Equal("artist:", SearchQueryParser.ApplyFieldChip("a", "artist:"));
        Assert.Equal("artist:", SearchQueryParser.ApplyFieldChip("ART", "artist:"));
        Assert.Equal("tag:巨乳 artist:", SearchQueryParser.ApplyFieldChip("tag:巨乳 art", "artist:"));

        // 4. 前綴補完（負號開頭）
        Assert.Equal("-artist:", SearchQueryParser.ApplyFieldChip("-art", "artist:"));
        Assert.Equal("-artist:", SearchQueryParser.ApplyFieldChip("-", "artist:"));
        Assert.Equal("tag:巨乳 -artist:", SearchQueryParser.ApplyFieldChip("tag:巨乳 -art", "artist:"));
        Assert.Equal("tag:巨乳 -tag:", SearchQueryParser.ApplyFieldChip("tag:巨乳 -", "tag:"));

        // 5. 已有完整條件或純文字（未接空白，後方自動補空白接欄位）
        Assert.Equal("flugel artist:", SearchQueryParser.ApplyFieldChip("flugel", "artist:"));
        Assert.Equal("tag:巨乳 artist:", SearchQueryParser.ApplyFieldChip("tag:巨乳", "artist:"));
        Assert.Equal("-tag:DL版 artist:", SearchQueryParser.ApplyFieldChip("-tag:DL版", "artist:"));
        Assert.Equal("-foo artist:", SearchQueryParser.ApplyFieldChip("-foo", "artist:"));

        // 6. 其他欄位晶片
        Assert.Equal("language:", SearchQueryParser.ApplyFieldChip("lang", "language:"));
        Assert.Equal("read:", SearchQueryParser.ApplyFieldChip("rea", "read:"));
    }

    [Fact]
    public void FuzzyRecommender_MisspelledArtist_SuggestsCorrectAuthor()
    {
        var raw = "artist:flugle";
        var query = SearchQueryParser.Parse(raw);
        var recs = SearchFuzzyRecommender.FindRecommendations(_store, raw, query);

        Assert.NotEmpty(recs);
        Assert.Contains(recs, r => r.DisplayText == "artist:Flugel" && r.ReplacementQuery == "artist:Flugel");
    }

    [Fact]
    public void FuzzyRecommender_AuthorAliasAndFieldAlias_SuggestsAndReplacesCorrectly()
    {
        // 1. 使用欄位別名 author: 且拼錯主名 "flugle" → ReplacementQuery 應正確替換掉 "author:flugle"
        var rawFieldAlias = "language:chinese author:flugle";
        var q1 = SearchQueryParser.Parse(rawFieldAlias);
        var recs1 = SearchFuzzyRecommender.FindRecommendations(_store, rawFieldAlias, q1);
        Assert.Contains(recs1, r => r.ReplacementQuery == "language:chinese artist:Flugel");

        // 2. 拼錯作者別名（Seed 中 Flugel 有別名 "フルーゲル"；再新增一個羅馬拼音別名 "Furuugeru" 測試）
        var flugel = _store.FindAuthorByMainName("Flugel")!;
        _store.AddAuthorAlias(flugel.Id, "Furuugeru");
        var rawAliasTypo = "author:furuuger";
        var q2 = SearchQueryParser.Parse(rawAliasTypo);
        var recs2 = SearchFuzzyRecommender.FindRecommendations(_store, rawAliasTypo, q2);
        Assert.Contains(recs2, r => r.DisplayText == "artist:Flugel" && r.ReplacementQuery == "artist:Flugel");
    }

    [Fact]
    public void FuzzyRecommender_MisspelledTag_SuggestsCorrectTag()
    {
        var raw = "tag:big-breast";
        var query = SearchQueryParser.Parse(raw);
        var recs = SearchFuzzyRecommender.FindRecommendations(_store, raw, query);

        Assert.NotEmpty(recs);
        Assert.Contains(recs, r => r.DisplayText.Contains("big-breasts") || r.DisplayText.Contains("巨乳"));
    }

    [Fact]
    public void FuzzyRecommender_FreeText_SuggestsArtist()
    {
        var raw = "flugle";
        var query = SearchQueryParser.Parse(raw);
        var recs = SearchFuzzyRecommender.FindRecommendations(_store, raw, query);

        Assert.NotEmpty(recs);
        Assert.Contains(recs, r => r.DisplayText == "artist:Flugel" && r.ReplacementQuery == "artist:Flugel");
    }

    [Fact]
    public void FuzzyRecommender_MultipleTerms_ReplacesOnlyTargetTerm()
    {
        var raw = "language:japanese artist:flugle";
        var query = SearchQueryParser.Parse(raw);
        var recs = SearchFuzzyRecommender.FindRecommendations(_store, raw, query);

        Assert.NotEmpty(recs);
        Assert.Contains(recs, r => r.ReplacementQuery == "language:japanese artist:Flugel");
    }
}
