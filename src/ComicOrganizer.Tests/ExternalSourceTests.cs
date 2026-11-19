using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// asset_external_source 表 + IAssetMetaStore 外部 metadata API 測試（#52 Phase A）。
/// </summary>
public class ExternalSourceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public ExternalSourceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_extsrc_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private long SeedAsset(string path = @"C:\lib\[Author] work.zip")
    {
        _store.Upsert(new AssetMeta
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsFolder = false,
            FileSize = 1,
            Mtime = 1,
        });
        return _store.GetAssetId(path)!.Value;
    }

    [Fact]
    public void V5_Schema_AddsExternalSourceTable()
    {
        // 透過 API 操作驗證表存在
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource
        {
            AssetId = id,
            Source = "nhentai",
            SourceId = "568742",
            RawJson = "{}",
            FetchedAt = 1,
        });
        Assert.NotNull(_store.GetExternalSource(id, "nhentai"));
    }

    [Fact]
    public void Upsert_GetByAssetIdAndSource()
    {
        var id = SeedAsset();
        var record = new AssetExternalSource
        {
            AssetId = id,
            Source = "nhentai",
            SourceId = "568742",
            MediaId = "3319522",
            UploadAt = 1744974299,
            RawJson = """{"id":568742,"title":{"pretty":"Kugizuke Heart"}}""",
            FetchedAt = 1700000000,
        };
        _store.UpsertExternalSource(record);

        var got = _store.GetExternalSource(id, "nhentai");

        Assert.NotNull(got);
        Assert.Equal(id, got!.AssetId);
        Assert.Equal("nhentai", got.Source);
        Assert.Equal("568742", got.SourceId);
        Assert.Equal("3319522", got.MediaId);
        Assert.Equal(1744974299L, got.UploadAt);
        Assert.Contains("Kugizuke Heart", got.RawJson);
        Assert.Equal(1700000000L, got.FetchedAt);
    }

    [Fact]
    public void Upsert_OverwritesExisting()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource
        {
            AssetId = id, Source = "nhentai", SourceId = "111", RawJson = "{}", FetchedAt = 1,
        });
        _store.UpsertExternalSource(new AssetExternalSource
        {
            AssetId = id, Source = "nhentai", SourceId = "222", RawJson = "{\"new\":true}", FetchedAt = 2,
        });

        var got = _store.GetExternalSource(id, "nhentai");
        Assert.Equal("222", got!.SourceId);
        Assert.Equal(2L, got.FetchedAt);
        Assert.Contains("new", got.RawJson);
    }

    [Fact]
    public void List_ReturnsAllSourcesForAsset()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "nhentai", SourceId = "1", RawJson = "{}", FetchedAt = 1 });
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "ehentai", SourceId = "2", RawJson = "{}", FetchedAt = 2 });

        var list = _store.ListExternalSources(id);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, r => r.Source == "nhentai");
        Assert.Contains(list, r => r.Source == "ehentai");
    }

    [Fact]
    public void Delete_RemovesOnlySpecifiedSource()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "nhentai", SourceId = "1", RawJson = "{}", FetchedAt = 1 });
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "ehentai", SourceId = "2", RawJson = "{}", FetchedAt = 2 });

        _store.DeleteExternalSource(id, "nhentai");

        Assert.Null(_store.GetExternalSource(id, "nhentai"));
        Assert.NotNull(_store.GetExternalSource(id, "ehentai"));
    }

    [Fact]
    public void FindAssetByExternalId_ReverseLookup()
    {
        var id1 = SeedAsset(@"C:\lib\[A] w1.zip");
        var id2 = SeedAsset(@"C:\lib\[A] w2.zip");
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id1, Source = "nhentai", SourceId = "111", RawJson = "{}", FetchedAt = 1 });
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id2, Source = "nhentai", SourceId = "222", RawJson = "{}", FetchedAt = 2 });

        Assert.Equal(id1, _store.FindAssetByExternalId("nhentai", "111"));
        Assert.Equal(id2, _store.FindAssetByExternalId("nhentai", "222"));
        Assert.Null(_store.FindAssetByExternalId("nhentai", "999"));
        Assert.Null(_store.FindAssetByExternalId("ehentai", "111"));   // 同 source_id 不同 source
    }

    [Fact]
    public void Cascade_DeleteAsset_RemovesExternalSources()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "nhentai", SourceId = "111", RawJson = "{}", FetchedAt = 1 });
        Assert.NotNull(_store.GetExternalSource(id, "nhentai"));

        _store.DeleteAssetById(id);

        Assert.Null(_store.GetExternalSource(id, "nhentai"));
        Assert.Null(_store.FindAssetByExternalId("nhentai", "111"));
    }

    [Fact]
    public void Clear_RemovesExternalSources()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "nhentai", SourceId = "111", RawJson = "{}", FetchedAt = 1 });

        _store.Clear();

        Assert.Null(_store.FindAssetByExternalId("nhentai", "111"));
    }

    // ===== 工作項目 M2：搜尋組句 / 多語標題比對 / 分類與翻譯寫入 =====

    private static TagTranslator CreateTestTranslator(string json)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var db = TagTranslationDb.LoadFromJsonStream(ms);
        return new TagTranslator(() => db);
    }

    [Fact]
    public void BuildSearchQuery_JapaneseAuthorDoesNotEmitArtistField_KnownAsciiArtistUsesArtistSlug()
    {
        var translator = CreateTestTranslator("""
            {
              "data": [
                { "namespace": "artist", "data": { "flugel": { "name": "フューゲル" } } }
              ]
            }
            """);

        var jaQuery = BatchExternalFetchService.BuildSearchQuery(
            "(C90) [Rosapersica (一ノ宮)] ヨルヤハギ5 (艦隊これくしょん -艦これ-) [中国翻訳].zip",
            translator);
        Assert.DoesNotContain("artist:", jaQuery);
        Assert.Contains("一ノ宮", jaQuery);
        Assert.Contains("ヨルヤハギ5", jaQuery);

        var knownAscii = BatchExternalFetchService.BuildSearchQuery(
            "(C99) [Flugel] くぎづけはーと [DL版].zip",
            translator);
        Assert.StartsWith("artist:flugel ", knownAscii);
        Assert.Contains("くぎづけはーと", knownAscii);
    }

    [Fact]
    public async Task ProcessOneSearchAsync_FallbacksToTitleOnlyAndMatchesJapaneseTitleBestCandidate()
    {
        var workPath = @"C:\lib\[Rosapersica (一ノ宮)] ヨルヤハギ5 [中国翻訳].zip";
        var id = SeedAsset(workPath);
        _store.UpsertMetadata(new AssetMetadata { AssetId = id, Title = "ヨルヤハギ5", ParsedAt = 1 });

        var fakeClient = new FakeMetadataClient();
        // 第一筆為不相關、第二筆 TitleEnglish 為羅馬拼音但 TitleJapanese 完全吻合本地作品
        fakeClient.SearchHandler = (q, _) =>
        {
            if (q.Contains("一ノ宮")) return Array.Empty<MetadataSearchHit>();
            return new List<MetadataSearchHit>
            {
                new() { ExternalId = "100", TitleEnglish = "Unrelated Story", TitleJapanese = "[Other] 別の作品" },
                new() { ExternalId = "200", TitleEnglish = "Yoru Yahagi 5 [Chinese]", TitleJapanese = "(C90) [Rosapersica (一ノ宮)] ヨルヤハギ5 (艦隊これくしょん) [中国翻訳]" },
            };
        };
        fakeClient.LookupHandler = extId => extId == "200"
            ? new MetadataLookupResult
            {
                Source = "nhentai",
                ExternalId = "200",
                TitlePretty = "Yoru Yahagi 5",
                TitleJapanese = "ヨルヤハギ5",
                NumPages = 32,
                RawJson = "{\"id\":200}",
                Tags =
                {
                    new MetadataTag("parody", "kantai collection", "kantai-collection", 10),
                    new MetadataTag("character", "yahagi", "yahagi", 5),
                    new MetadataTag("tag", "stockings", "stockings", 20),
                },
            }
            : null;

        var translator = CreateTestTranslator("""
            {
              "data": [
                { "namespace": "female", "data": { "stockings": { "name": "黑丝" } } },
                { "namespace": "parody", "data": { "kantai collection": { "name": "艦隊Collection" } } }
              ]
            }
            """);
        var svc = new BatchExternalFetchService(_store, fakeClient, translator);
        var strategy = new BatchSyncStrategy
        {
            TitleSource = FieldSource.Local,
            AuthorSource = FieldSource.Local,
            LanguageSource = FieldSource.Local,
            TranslationGroupSource = FieldSource.Local,
            PageCountSource = FieldSource.External,
            TagsPolicy = TagsMergePolicy.UnionBoth,
        };

        var query = BatchExternalFetchService.BuildSearchQuery(Path.GetFileName(workPath), translator);
        var res = await svc.ProcessOneSearchAsync(workPath, strategy, query, confidenceThreshold: 80);

        Assert.Equal(BatchItemStatus.Applied, res.Status);
        Assert.Equal("200", res.ExternalId);
        Assert.NotNull(res.Merge);
        Assert.Equal(2, fakeClient.SearchQueries.Count); // 主查詢 0 筆後有自動 fallback 純標題

        // 套用後驗證 tag.category 與繁中翻譯皆正確寫入
        var prevUi = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("zh-TW");
            svc.ApplyMergeResult(_store.GetWorkDetail(workPath)!, res.Merge!);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = prevUi;
        }

        var tags = _store.ListTagsForAsset(id);
        Assert.Contains(tags, t => t.Name == "stockings" && t.Category == "tag");
        Assert.Contains(tags, t => t.Name == "kantai collection" && t.Category == "parody");
        Assert.Contains(tags, t => t.Name == "yahagi" && t.Category == "character");

        var stockingsId = tags.First(t => t.Name == "stockings").Id;
        Assert.Equal("黑絲", _store.GetTagTranslation(stockingsId, "zh-TW"));
    }

    [Fact]
    public async Task RunLibraryAutoTagFetchAsync_AppliesHighConfidence_SkipsUnmatchedWithoutBlocking_AndResumesCleanly()
    {
        var authorId = _store.UpsertAuthor("LocalAuthorKeep");
        var authorFolder = @"C:\lib\LocalAuthorKeep";
        _store.Upsert(new AssetMeta { Path = authorFolder, Name = "LocalAuthorKeep", IsFolder = true });
        _store.SetAuthorFolderPath(authorId, authorFolder);

        var coverImg = @"C:\lib\LocalAuthorKeep\cover.jpg";
        SeedAsset(coverImg);

        var work1 = @"C:\lib\[Rosapersica (一ノ宮)] ヨルヤハギ5.zip";
        var id1 = SeedAsset(work1);
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = id1,
            AuthorId = authorId,
            Title = "Local Title Keep",
            TranslationGroup = "LocalGroup",
            ParsedAt = 1,
        });
        _store.SetAssetTags(id1, new[] { "local_tag" });

        var work2 = @"C:\lib\Unknown Unmatched Comic 9999.zip";
        var id2 = SeedAsset(work2);
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = id2,
            Title = "Unknown Unmatched Comic 9999",
            ParsedAt = 1,
        });

        var fakeClient = new FakeMetadataClient
        {
            SearchHandler = (q, _) => q.Contains("ヨルヤハギ5")
                ? new[]
                {
                    new MetadataSearchHit
                    {
                        ExternalId = "301",
                        TitleJapanese = "ヨルヤハギ5",
                        TitleEnglish = "Yoru Yahagi 5",
                    },
                }
                : Array.Empty<MetadataSearchHit>(),
            LookupHandler = id => id == "301"
                ? new MetadataLookupResult
                {
                    Source = "nhentai",
                    ExternalId = "301",
                    TitlePretty = "External Overwrite Attempt",
                    NumPages = 28,
                    RawJson = "{\"id\":301}",
                    Tags =
                    {
                        new MetadataTag("tag", "stockings", "stockings", 1),
                        new MetadataTag("parody", "kantai collection", "kantai-collection", 2),
                        new MetadataTag("language", "chinese", "chinese", 3),
                    },
                }
                : null,
        };

        var svc = new BatchExternalFetchService(_store, fakeClient);
        var tracker = new RebuildProgressTracker(_store);

        // Run 1: work1 套用高信心外部 tag/pageCount/補缺 language（保留本地標題/作者/漢化組），work2 靜默計入 Unmatched
        var summary1 = await svc.RunLibraryAutoTagFetchAsync(tracker, confidenceThreshold: 80);

        Assert.False(summary1.Cancelled);
        Assert.Equal(2, summary1.Total);
        Assert.Equal(1, summary1.Applied);
        Assert.Equal(1, summary1.Unmatched);
        Assert.Equal(0, summary1.Errors);

        var d1 = _store.GetWorkDetail(work1);
        Assert.NotNull(d1);
        Assert.Equal("Local Title Keep", d1!.Title);
        Assert.Equal(authorId, d1.AuthorId);
        Assert.Equal("LocalGroup", d1.TranslationGroup);
        Assert.Equal("chinese", d1.Language);
        Assert.Equal(28, d1.PageCount);
        Assert.Equal("301", d1.ExternalId);
        Assert.Contains(d1.Tags, t => t.Name == "local_tag");
        Assert.Contains(d1.Tags, t => t.Name == "stockings" && t.Category == "tag");
        Assert.Contains(d1.Tags, t => t.Name == "kantai collection" && t.Category == "parody");

        // Run 2 (Resume): 已綁定 ExternalId 的 work1 應直接跳過、不再重打 API，work2 仍靜默略過
        fakeClient.SearchQueries.Clear();
        var summary2 = await svc.RunLibraryAutoTagFetchAsync(tracker, confidenceThreshold: 80);

        Assert.False(summary2.Cancelled);
        Assert.Equal(2, summary2.Total);
        Assert.Equal(1, summary2.Skipped);
        Assert.Equal(0, summary2.Applied);
        Assert.Equal(1, summary2.Unmatched);
        Assert.DoesNotContain(fakeClient.SearchQueries, q => q.Contains("ヨルヤハギ5"));
    }

    [Fact]
    public void BuildSearchQuery_StripsBilingualPipeSubtitle_AndResolutionSuffix()
    {
        var q1 = BatchExternalFetchService.BuildSearchQuery(
            "[ベコ太郎] あおのままで｜停留在青澀一刻 [無修正] [DL版].zip");
        Assert.Contains("あおのままで", q1);
        Assert.DoesNotContain("停留在青澀一刻", q1);

        var t1 = BatchExternalFetchService.BuildTitleOnlyQuery(
            "[無修正] [大鸟可不敢乱转汉化] [ae] 月と向日葵 (COMIC BAVEL 2022年12月号) [DL版]｜[ae] Moon and Sunflower｜Tsuki to Himawari.zip");
        Assert.Equal("月と向日葵", t1);

        var q2 = BatchExternalFetchService.BuildSearchQuery("[Author] Title -1280x.zip");
        Assert.Equal("Author Title", q2);
    }

    [Fact]
    public async Task ProcessOneSearchAsync_SingleHitVolumeMismatch_ReturnsNeedsReview()
    {
        var work3 = @"C:\lib\[360 (コナ)] 燕嵐閨中顧話・後伝3 [中国翻訳].zip";
        var id3 = SeedAsset(work3);
        _store.UpsertMetadata(new AssetMetadata { AssetId = id3, Title = "燕嵐閨中顧話・後伝3", ParsedAt = 1 });

        var workBase = @"C:\lib\[360 (コナ)] 燕嵐閨中顧話 [中国翻訳].zip";
        var idBase = SeedAsset(workBase);
        _store.UpsertMetadata(new AssetMetadata { AssetId = idBase, Title = "燕嵐閨中顧話", ParsedAt = 1 });

        var work5 = @"C:\lib\[360 (コナ)] 燕嵐閨中顧話・後伝5 [中国翻訳].zip";
        var id5 = SeedAsset(work5);
        _store.UpsertMetadata(new AssetMetadata { AssetId = id5, Title = "燕嵐閨中顧話・後伝5", ParsedAt = 1 });

        var fakeClient = new FakeMetadataClient
        {
            SearchHandler = (_, _) => new List<MetadataSearchHit>
            {
                new()
                {
                    ExternalId = "595001",
                    TitleJapanese = "[360 (コナ)] 燕嵐閨中顧話・後伝5 [中国翻訳]",
                    TitleEnglish = "[360 (Kona)] Enran Keichuu Kowa Kouden 5 [Chinese]",
                },
            },
            LookupHandler = extId => extId == "595001"
                ? new MetadataLookupResult
                {
                    Source = "nhentai",
                    ExternalId = "595001",
                    TitlePretty = "Enran Keichuu Kowa Kouden 5",
                    TitleJapanese = "[360 (コナ)] 燕嵐閨中顧話・後伝5 [中国翻訳]",
                    NumPages = 24,
                    RawJson = "{\"id\":595001}",
                }
                : null,
        };

        var svc = new BatchExternalFetchService(_store, fakeClient);
        var strategy = BatchSyncStrategy.FromPreset(OverwritePolicyPreset.OnlyTagsAndLanguage);

        var r3 = await svc.ProcessOneSearchAsync(work3, strategy, BatchExternalFetchService.BuildSearchQuery(Path.GetFileName(work3)), 80);
        var rBase = await svc.ProcessOneSearchAsync(workBase, strategy, BatchExternalFetchService.BuildSearchQuery(Path.GetFileName(workBase)), 80);
        var r5 = await svc.ProcessOneSearchAsync(work5, strategy, BatchExternalFetchService.BuildSearchQuery(Path.GetFileName(work5)), 80);

        Assert.Equal(BatchItemStatus.NeedsReview, r3.Status);
        Assert.Equal(BatchItemStatus.NeedsReview, rBase.Status);
        Assert.Equal(BatchItemStatus.Applied, r5.Status);
        Assert.Equal("595001", r5.ExternalId);
    }

    private sealed class FakeMetadataClient : IMetadataAPIClient
    {
        public string SourceName => "nhentai";
        public List<string> SearchQueries { get; } = new();
        public Func<string, int, IReadOnlyList<MetadataSearchHit>>? SearchHandler { get; set; }
        public Func<string, MetadataLookupResult?>? LookupHandler { get; set; }

        public Task<MetadataLookupResult?> GetByExternalIdAsync(string externalId, CancellationToken ct = default)
            => Task.FromResult(LookupHandler?.Invoke(externalId));

        public Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(string query, int page = 1, CancellationToken ct = default)
        {
            SearchQueries.Add(query);
            return Task.FromResult(SearchHandler?.Invoke(query, page) ?? Array.Empty<MetadataSearchHit>());
        }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true, "tester", null));
    }
}
