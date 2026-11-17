using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 搜尋後端 DSL 擴充（series: / group:）與安全組句工具單元／整合測試。
/// </summary>
public class ExtendedSearchCoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;
    private long _authorFlugel;
    private long _authorOouso;

    public ExtendedSearchCoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_ext_search_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
        SeedFixture();
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private void SeedFixture()
    {
        _authorFlugel = _store.UpsertAuthor("Flugel", @"C:\Lib\Flugel");
        _authorOouso = _store.UpsertAuthor("oouso", @"C:\Lib\oouso");

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var works = new (string Path, bool IsFolder, long? AuthorId, string Title, string? Series, string? Group)[]
        {
            (@"C:\Lib\Flugel\fgo_01.zip", false, _authorFlugel, "FGO Vol.1", "Fate/Grand Order", "星空漢化組"),
            (@"C:\Lib\Flugel\fgo_02.zip", false, _authorFlugel, "FGO Vol.2", "Fate/Grand Order", "月光漢化組"),
            (@"C:\Lib\Flugel\original.zip", false, _authorFlugel, "Original Book", "Original", "C_97"),
            (@"C:\Lib\oouso\percent_50.zip", false, _authorOouso, "Half Price", "Series 50% Off", "CX97"),
            (@"C:\Lib\oouso\percent_500.zip", false, _authorOouso, "Five Hundred", "Series 500 Off", "Team Alpha"),
            (@"C:\Lib\Misc\LeafSeriesFolder", true, null, "Leaf Folder Work", "Fate/Stay Night", "星空漢化組"),
        };

        foreach (var w in works)
        {
            _store.Upsert(new AssetMeta
            {
                Path = w.Path,
                Name = Path.GetFileName(w.Path),
                IsFolder = w.IsFolder,
                Mtime = 100,
                LastScanned = 100,
                LastAccessed = 100,
            });
            var assetId = _store.GetAssetId(w.Path)!.Value;
            _store.UpsertMetadata(new AssetMetadata
            {
                AssetId = assetId,
                AuthorId = w.AuthorId,
                Title = w.Title,
                Series = w.Series,
                TranslationGroup = w.Group,
                ParsedAt = now,
            });
        }

        // 建立作者資料夾與含子項目的容器資料夾，驗證非作品目錄會被排除
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Lib\Flugel",
            Name = "Flugel",
            IsFolder = true,
            Mtime = 100,
            LastScanned = 100,
            LastAccessed = 100,
        });
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = _store.GetAssetId(@"C:\Lib\Flugel")!.Value,
            AuthorId = _authorFlugel,
            Title = "Flugel Container",
            Series = "Fate/Grand Order",
            TranslationGroup = "星空漢化組",
            ParsedAt = now,
        });

        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Lib\SeriesContainer",
            Name = "SeriesContainer",
            IsFolder = true,
            Mtime = 100,
            LastScanned = 100,
            LastAccessed = 100,
        });
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = _store.GetAssetId(@"C:\Lib\SeriesContainer")!.Value,
            Title = "Container",
            Series = "Fate/Grand Order",
            TranslationGroup = "星空漢化組",
            ParsedAt = now,
        });
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Lib\SeriesContainer\Child.zip",
            Name = "Child.zip",
            IsFolder = false,
            Mtime = 100,
            LastScanned = 100,
            LastAccessed = 100,
        });
    }

    // ── Parser：series / group / translator / scanlator ────────────

    [Theory]
    [InlineData("series:Fate", SearchField.Series, "Fate", false)]
    [InlineData("SERIES:Fate", SearchField.Series, "Fate", false)]
    [InlineData("-series:Fate", SearchField.Series, "Fate", true)]
    [InlineData("group:C_97", SearchField.TranslationGroup, "C_97", false)]
    [InlineData("translator:星空漢化組", SearchField.TranslationGroup, "星空漢化組", false)]
    [InlineData("scanlator:TeamAlpha", SearchField.TranslationGroup, "TeamAlpha", false)]
    [InlineData("-group:月光漢化組", SearchField.TranslationGroup, "月光漢化組", true)]
    [InlineData("-translator:foo", SearchField.TranslationGroup, "foo", true)]
    [InlineData("-scanlator:bar", SearchField.TranslationGroup, "bar", true)]
    public void Parser_SeriesAndGroupAliases_ParsedCorrectly(
        string input, SearchField expectedField, string expectedValue, bool expectedNegated)
    {
        var query = SearchQueryParser.Parse(input);
        Assert.Single(query.Terms);
        Assert.Equal(expectedField, query.Terms[0].Field);
        Assert.Equal(expectedValue, query.Terms[0].Value);
        Assert.Equal(expectedNegated, query.Terms[0].Negated);
    }

    [Fact]
    public void Parser_SeriesAndGroup_QuotedWithSpacesAndNegation()
    {
        var query = SearchQueryParser.Parse("series:\"Fate/Grand Order\" -group:\"Team Alpha\"");
        Assert.Equal(2, query.Terms.Count);

        Assert.Equal(SearchField.Series, query.Terms[0].Field);
        Assert.Equal("Fate/Grand Order", query.Terms[0].Value);
        Assert.False(query.Terms[0].Negated);

        Assert.Equal(SearchField.TranslationGroup, query.Terms[1].Field);
        Assert.Equal("Team Alpha", query.Terms[1].Value);
        Assert.True(query.Terms[1].Negated);
    }

    [Fact]
    public void Autocomplete_ExtractActiveFieldToken_SupportsSeriesAndGroup()
    {
        var t1 = SearchQueryParser.ExtractActiveFieldToken("series:Fa");
        Assert.NotNull(t1);
        Assert.Equal(SearchField.Series, t1.Field);
        Assert.Equal("Fa", t1.Value);

        var t2 = SearchQueryParser.ExtractActiveFieldToken("-scanlator:C_");
        Assert.NotNull(t2);
        Assert.Equal(SearchField.TranslationGroup, t2.Field);
        Assert.Equal("-scanlator:", t2.Prefix);
        Assert.Equal("C_", t2.Value);
    }

    // ── BuildFieldToken & AppendTermToken ──────────────────────────

    [Fact]
    public void BuildFieldToken_FormatsAndQuotesSafely()
    {
        Assert.Equal("series:50%", SearchQueryParser.BuildFieldToken("series", "50%"));
        Assert.Equal("group:C_97", SearchQueryParser.BuildFieldToken("group:", "C_97"));
        Assert.Equal("-series:50%", SearchQueryParser.BuildFieldToken("series", "50%", negated: true));

        // 含空白、冒號、連字號、雙引號或控制字元
        Assert.Equal("series:\"Fate/Grand Order\"", SearchQueryParser.BuildFieldToken("series", "Fate/Grand Order"));
        Assert.Equal("series:\"Re:Zero\"", SearchQueryParser.BuildFieldToken("series", "Re:Zero"));
        Assert.Equal("group:\"A-B\"", SearchQueryParser.BuildFieldToken("group", "A-B"));
        Assert.Equal("-group:\"Team A\"", SearchQueryParser.BuildFieldToken(" group: ", "Team\"A", negated: true));
        Assert.Equal("series:\"Line1 Line2\"", SearchQueryParser.BuildFieldToken("series", "Line1\tLine2\n"));
        Assert.Equal("series:Clean", SearchQueryParser.BuildFieldToken(":series:", "  \"Clean\"  "));
    }

    [Theory]
    [InlineData("", "abc")]
    [InlineData("   ", "abc")]
    [InlineData(":", "abc")]
    [InlineData("series", "")]
    [InlineData("series", "   ")]
    [InlineData("series", "\"\"")]
    [InlineData("series", "\t\r\n")]
    public void BuildFieldToken_InvalidInput_ReturnsEmpty(string fieldName, string value)
    {
        Assert.Equal(string.Empty, SearchQueryParser.BuildFieldToken(fieldName, value));
    }

    [Fact]
    public void BuildFieldToken_RoundTripsThroughParser()
    {
        var tokens = new[]
        {
            SearchQueryParser.BuildFieldToken("series", "50%"),
            SearchQueryParser.BuildFieldToken("group", "C_97"),
            SearchQueryParser.BuildFieldToken("series", "Fate - Stay:Night"),
            SearchQueryParser.BuildFieldToken("group", "漢化\"組", negated: true),
        };

        var dsl = string.Join(" ", tokens);
        var parsed = SearchQueryParser.Parse(dsl);

        Assert.Equal(4, parsed.Terms.Count);
        Assert.Equal(new SearchTerm(SearchField.Series, "50%", false), parsed.Terms[0]);
        Assert.Equal(new SearchTerm(SearchField.TranslationGroup, "C_97", false), parsed.Terms[1]);
        Assert.Equal(new SearchTerm(SearchField.Series, "Fate - Stay:Night", false), parsed.Terms[2]);
        Assert.Equal(new SearchTerm(SearchField.TranslationGroup, "漢化 組", true), parsed.Terms[3]);
    }

    [Fact]
    public void AppendTermToken_AppendsDeduplicatesAndGuardsInvalidInputs()
    {
        // 空 DSL / null
        Assert.Equal("series:50%", SearchQueryParser.AppendTermToken(null, "series:50%"));
        Assert.Equal("series:50%", SearchQueryParser.AppendTermToken("   ", "series:50%"));

        // 單一空白追加（含尾端既有空白）
        Assert.Equal("artist:Flugel series:50%", SearchQueryParser.AppendTermToken("artist:Flugel", "series:50%"));
        Assert.Equal("artist:Flugel series:\"My Series\"", SearchQueryParser.AppendTermToken("artist:Flugel   ", "series:\"My Series\""));

        // 相同 token 不重複追加（含引號與大小寫與別名語意相同）
        Assert.Equal("artist:Flugel series:50%", SearchQueryParser.AppendTermToken("artist:Flugel series:50%", "series:50%"));
        Assert.Equal("artist:Flugel series:\"My Series\"", SearchQueryParser.AppendTermToken("artist:Flugel series:\"My Series\"", "SERIES:\"my series\""));
        Assert.Equal("group:C_97", SearchQueryParser.AppendTermToken("group:C_97", "translator:C_97"));

        // 正負號不同視為不同條件，應可追加
        Assert.Equal("group:C_97 -group:C_97", SearchQueryParser.AppendTermToken("group:C_97", "-group:C_97"));

        // 無效 token 回原字串
        Assert.Equal("artist:Flugel", SearchQueryParser.AppendTermToken("artist:Flugel", ""));
        Assert.Equal("artist:Flugel", SearchQueryParser.AppendTermToken("artist:Flugel", "   "));
        Assert.Equal("artist:Flugel", SearchQueryParser.AppendTermToken("artist:Flugel", "series:a\nb"));
        Assert.Equal("artist:Flugel", SearchQueryParser.AppendTermToken("artist:Flugel", "series:\"unclosed"));
        Assert.Equal(string.Empty, SearchQueryParser.AppendTermToken(null, ""));
    }

    [Fact]
    public void BuildAndAppend_ComposedDsl_RoundTripsThroughParser()
    {
        string dsl = "";
        dsl = SearchQueryParser.AppendTermToken(dsl, SearchQueryParser.BuildFieldToken("artist", "Flugel"));
        dsl = SearchQueryParser.AppendTermToken(dsl, SearchQueryParser.BuildFieldToken("series", "Fate/Grand Order"));
        dsl = SearchQueryParser.AppendTermToken(dsl, SearchQueryParser.BuildFieldToken("group", "50% \"Special\""));
        dsl = SearchQueryParser.AppendTermToken(dsl, SearchQueryParser.BuildFieldToken("group", "C_97", negated: true));
        // 重複追加同一條件應被略過
        dsl = SearchQueryParser.AppendTermToken(dsl, SearchQueryParser.BuildFieldToken("series", "Fate/Grand Order"));

        var parsed = SearchQueryParser.Parse(dsl);
        Assert.Equal(4, parsed.Terms.Count);
        Assert.Equal(new SearchTerm(SearchField.Artist, "Flugel", false), parsed.Terms[0]);
        Assert.Equal(new SearchTerm(SearchField.Series, "Fate/Grand Order", false), parsed.Terms[1]);
        Assert.Equal(new SearchTerm(SearchField.TranslationGroup, "50%  Special", false), parsed.Terms[2]);
        Assert.Equal(new SearchTerm(SearchField.TranslationGroup, "C_97", true), parsed.Terms[3]);
    }

    // ── SqliteAssetMetaStore & SearchExecutor 整合測試 ──────────────

    [Fact]
    public void Store_ListWorksBySeries_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(_store.ListWorksBySeries(""));
        Assert.Empty(_store.ListWorksBySeries("   "));
    }

    [Fact]
    public void Store_ListWorksByTranslationGroup_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(_store.ListWorksByTranslationGroup(""));
        Assert.Empty(_store.ListWorksByTranslationGroup("   "));
    }

    [Fact]
    public void Store_ListWorksBySeries_SubstringCaseInsensitiveAndExcludesContainers()
    {
        var results = _store.ListWorksBySeries("fate");

        Assert.Equal(3, results.Count);
        Assert.Contains(@"C:\Lib\Flugel\fgo_01.zip", results);
        Assert.Contains(@"C:\Lib\Flugel\fgo_02.zip", results);
        Assert.Contains(@"C:\Lib\Misc\LeafSeriesFolder", results);
        // 作者資料夾與含子項目容器資料夾必須被排除
        Assert.DoesNotContain(@"C:\Lib\Flugel", results);
        Assert.DoesNotContain(@"C:\Lib\SeriesContainer", results);

        // 確認依 path COLLATE NOCASE 排序
        var sorted = results.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, results);
    }

    [Fact]
    public void Store_ListWorksBySeriesAndGroup_EscapesLikeWildcards()
    {
        // 50% 不該誤中 500
        var seriesResults = _store.ListWorksBySeries("50%");
        Assert.Single(seriesResults);
        Assert.Equal(@"C:\Lib\oouso\percent_50.zip", seriesResults[0]);

        // C_97 的底線不該誤中 CX97
        var groupResults = _store.ListWorksByTranslationGroup("C_97");
        Assert.Single(groupResults);
        Assert.Equal(@"C:\Lib\Flugel\original.zip", groupResults[0]);
    }

    [Fact]
    public void Exec_SeriesAndGroup_CombinedWithArtistAndNegation()
    {
        var executor = new SearchExecutor(_store);

        // artist: + series: + -group:
        var q1 = SearchQueryParser.Parse("artist:Flugel series:\"Fate/Grand Order\" -group:月光");
        var r1 = executor.Execute(q1);
        Assert.Single(r1);
        Assert.Equal(@"C:\Lib\Flugel\fgo_01.zip", r1[0]);

        // translator: 與 scanlator: 別名查詢
        var rTranslator = executor.Execute(SearchQueryParser.Parse("translator:星空"));
        Assert.Equal(2, rTranslator.Count);
        Assert.Contains(@"C:\Lib\Flugel\fgo_01.zip", rTranslator);
        Assert.Contains(@"C:\Lib\Misc\LeafSeriesFolder", rTranslator);

        var rScanlator = executor.Execute(SearchQueryParser.Parse("scanlator:C_97"));
        Assert.Single(rScanlator);
        Assert.Equal(@"C:\Lib\Flugel\original.zip", rScanlator[0]);

        // 單獨 negative：-series:Fate 應從全作品排除 Fate 系列
        var rOnlyNeg = executor.Execute(SearchQueryParser.Parse("-series:Fate"));
        Assert.DoesNotContain(@"C:\Lib\Flugel\fgo_01.zip", rOnlyNeg);
        Assert.DoesNotContain(@"C:\Lib\Flugel\fgo_02.zip", rOnlyNeg);
        Assert.DoesNotContain(@"C:\Lib\Misc\LeafSeriesFolder", rOnlyNeg);
        Assert.Contains(@"C:\Lib\Flugel\original.zip", rOnlyNeg);
        Assert.Contains(@"C:\Lib\oouso\percent_50.zip", rOnlyNeg);
        Assert.Contains(@"C:\Lib\oouso\percent_500.zip", rOnlyNeg);
    }
}
