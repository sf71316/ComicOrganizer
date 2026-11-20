using System.Numerics.Tensors;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using ComicOrganizer.VisualSearch;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// V1 視覺搜尋核心引擎（分片向量庫、增量指紋、搬移零重算、刪除清理與分片壓縮、MMap 查詢）單元測試。
/// </summary>
public sealed class VisualSearchEngineTests : IDisposable
{
    private readonly string _tempRoot;

    public VisualSearchEngineTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ComicOrg_VisualSearch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class DeterministicMockEmbedder : IVisualEmbedder
    {
        public int InputSize => 32;
        public int Dimensions => 32;
        public int BatchCallCount { get; private set; }

        public float[][] EmbedPreprocessedBatch(IReadOnlyList<float[]> chwItems)
        {
            BatchCallCount++;
            var results = new float[chwItems.Count][];
            for (int i = 0; i < chwItems.Count; i++)
            {
                var chw = chwItems[i];
                var vec = new float[Dimensions];
                int stride = Math.Max(1, chw.Length / Dimensions);
                for (int d = 0; d < Dimensions; d++)
                {
                    float sum = 0;
                    int start = d * stride;
                    int end = Math.Min(chw.Length, start + stride);
                    for (int k = start; k < end; k++) sum += chw[k];
                    vec[d] = sum / Math.Max(1, end - start);
                }
                float norm = TensorPrimitives.Norm(vec);
                if (norm > 1e-6f) TensorPrimitives.Divide(vec, norm, vec);
                else vec[0] = 1f;
                results[i] = vec;
            }
            return results;
        }

        public void Dispose() { }
    }

    private sealed class MockPanelDetector : IPanelDetector
    {
        public List<VisualPanelBox> DetectPreprocessed(PreparedYoloFrame input, float confThreshold = 0.35f, float iouThreshold = 0.45f, float minAreaRatio = 0.02f)
        {
            // 每頁固定回傳 1 個上半部、1 個下半部分鏡框
            return
            [
                new VisualPanelBox(0.05f, 0.05f, 0.90f, 0.42f, 0.95f, 0),
                new VisualPanelBox(0.05f, 0.52f, 0.90f, 0.42f, 0.92f, 0),
            ];
        }

        public void Dispose() { }
    }

    private static void WriteDistinctComicPage(string path, SKColor topColor, SKColor bottomColor)
    {
        using var bmp = new SKBitmap(160, 200, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bmp))
        {
            using var pTop = new SKPaint { Color = topColor };
            using var pBot = new SKPaint { Color = bottomColor };
            canvas.DrawRect(new SKRect(0, 0, 160, 100), pTop);
            canvas.DrawRect(new SKRect(0, 100, 160, 200), pBot);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(path, data.ToArray());
    }

    [Fact]
    public void AppConfig_VisualSearch_IsOptInByDefault()
    {
        var cfg = new AppConfig();
        Assert.False(cfg.EnableVisualSearch);
        Assert.False(cfg.VisualSearchAutoIndexAfterArchive);
        Assert.True(cfg.VisualSearchEnableMultiCropRerank);
    }

    [Fact]
    public async Task IncrementalSync_HandlesNew_Skip_MoveZeroRecompute_Delete_MultiShard_AndCompaction()
    {
        var dataDir = Path.Combine(_tempRoot, "data");
        var libDir = Path.Combine(_tempRoot, "library");
        var authorA = Path.Combine(libDir, "AuthorA", "Work1");
        var authorB = Path.Combine(libDir, "AuthorB", "Work2");
        Directory.CreateDirectory(authorA);
        Directory.CreateDirectory(authorB);

        // 建立 2 本各有 3 頁的漫畫（3 頁 × 3 向量 = 9 向量/本 => 2 本共 18 向量）
        WriteDistinctComicPage(Path.Combine(authorA, "01.png"), SKColors.Crimson, SKColors.Navy);
        WriteDistinctComicPage(Path.Combine(authorA, "02.png"), SKColors.ForestGreen, SKColors.Gold);
        WriteDistinctComicPage(Path.Combine(authorA, "03.png"), SKColors.MediumPurple, SKColors.Coral);

        WriteDistinctComicPage(Path.Combine(authorB, "01.png"), SKColors.DarkCyan, SKColors.OrangeRed);
        WriteDistinctComicPage(Path.Combine(authorB, "02.png"), SKColors.SaddleBrown, SKColors.DeepSkyBlue);
        WriteDistinctComicPage(Path.Combine(authorB, "03.png"), SKColors.Magenta, SKColors.LimeGreen);

        var paths = new VisualSearchPaths(dataDir);
        var embedder = new DeterministicMockEmbedder();
        // 設定每分片上限 6 筆，強制驗證多分片切分與跨分片搜尋
        var pipeline = new VisualIndexingPipeline(
            paths,
            () => new MockPanelDetector(),
            () => embedder,
            batchSize: 4,
            maxVectorsPerShard: 6,
            checkpointComicInterval: 5);

        // 1. 首次全庫建庫
        var s1 = await pipeline.SyncAsync(libDir);
        Assert.Equal(2, s1.ScannedComics);
        Assert.Equal(2, s1.IndexedComics);
        Assert.Equal(0, s1.SkippedUnchanged);
        Assert.Equal(6, s1.TotalPages);
        Assert.Equal(18, s1.TotalVectorsAdded);
        Assert.True(Directory.GetFiles(paths.ShardsDirectory, "panels_*.usearch").Length >= 2);

        // 2. 二次增量同步（無異動 → 全部跳過、不呼叫 embedder）
        var embedder2 = new DeterministicMockEmbedder();
        var pipeline2 = new VisualIndexingPipeline(paths, () => new MockPanelDetector(), () => embedder2, batchSize: 4, maxVectorsPerShard: 6);
        var s2 = await pipeline2.SyncAsync(libDir);
        Assert.Equal(0, s2.IndexedComics);
        Assert.Equal(2, s2.SkippedUnchanged);
        Assert.Equal(0, embedder2.BatchCallCount);

        // 3. 模擬將 AuthorA/Work1 搬移到新作者目錄 AuthorC/Work1_Moved（指紋相同 → 零重算更新路徑！）
        var authorCMoved = Path.Combine(libDir, "AuthorC", "Work1_Moved");
        Directory.CreateDirectory(Path.Combine(libDir, "AuthorC"));
        Directory.Move(authorA, authorCMoved);

        var embedder3 = new DeterministicMockEmbedder();
        var pipeline3 = new VisualIndexingPipeline(paths, () => new MockPanelDetector(), () => embedder3, batchSize: 4, maxVectorsPerShard: 6);
        var s3 = await pipeline3.SyncAsync(libDir);
        Assert.Equal(1, s3.MovedComics);
        Assert.Equal(0, s3.IndexedComics);
        Assert.Equal(0, embedder3.BatchCallCount);

        // 4. 驗證 MMap 跨分片查詢命中搬移後的新路徑，且自動去黑邊有效
        using (var engine = new VisualSearchEngine(paths, new DeterministicMockEmbedder()))
        {
            using var queryOrig = SKBitmap.Decode(Path.Combine(authorCMoved, "01.png"))!;
            using var bordered = new SKBitmap(queryOrig.Width + 60, queryOrig.Height + 60, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var c = new SKCanvas(bordered))
            {
                c.Clear(SKColors.Black);
                c.DrawBitmap(queryOrig, 30, 30);
            }

            var report = engine.SearchImage(bordered, topK: 3, trimBorders: true, enableMultiCropRerank: false);
            Assert.NotEmpty(report.Hits);
            Assert.Equal(authorCMoved, report.Hits[0].Meta.MangaPath);
            Assert.Equal(1, report.Hits[0].Meta.PageNo);
            Assert.True(report.Hits[0].Similarity > 0.95f);

            var previewJpeg = VisualSearchEngine.RenderHitPreviewJpeg(report.Hits[0].Meta);
            Assert.NotNull(previewJpeg);
            Assert.True(previewJpeg!.Length > 100);
        }

        // 5. 刪除 AuthorB/Work2 → 驗證自動清理與分片重整（CompactShards）
        Directory.Delete(authorB, recursive: true);
        var pipeline4 = new VisualIndexingPipeline(paths, () => new MockPanelDetector(), () => new DeterministicMockEmbedder(), batchSize: 4, maxVectorsPerShard: 6);
        var s4 = await pipeline4.SyncAsync(libDir);
        Assert.Equal(1, s4.DeletedComics);

        // 確認剩餘 1 本漫畫與 9 筆有效向量
        using (var db = new VisualIndexDatabase(paths.DatabasePath, readOnly: true))
        {
            var ov = db.GetOverview();
            Assert.Equal(1, ov.MangaCount);
            Assert.Equal(9, ov.ActiveVectorCount);
        }

        // 分片重整應可安全執行且不損壞有效資料
        _ = VisualIndexingPipeline.CompactShards(paths, dimensions: 32, minDeletedRatio: 0.01);
        using (var engineAfterCompact = new VisualSearchEngine(paths, new DeterministicMockEmbedder()))
        {
            using var q2 = SKBitmap.Decode(Path.Combine(authorCMoved, "02.png"))!;
            var rep2 = engineAfterCompact.SearchImage(q2, topK: 3, enableMultiCropRerank: false);
            Assert.NotEmpty(rep2.Hits);
            Assert.Equal(authorCMoved, rep2.Hits[0].Meta.MangaPath);
            Assert.Equal(2, rep2.Hits[0].Meta.PageNo);
        }
    }

    [Fact]
    public void VisualSearchPaths_DetectsMissingModels_AndImportsLocalFiles()
    {
        var dataDir = Path.Combine(_tempRoot, "models_test");
        var paths = new VisualSearchPaths(dataDir);
        Assert.False(paths.AreModelsReady());

        var dummyPanel = Path.Combine(_tempRoot, "fake_panel.onnx");
        var dummyEmbed = Path.Combine(_tempRoot, "fake_embed.onnx");
        File.WriteAllBytes(dummyPanel, new byte[4096]);
        File.WriteAllBytes(dummyEmbed, new byte[4096]);

        paths.ImportLocalModel(dummyPanel, VisualSearchPaths.PanelModelFileName);
        Assert.False(paths.AreModelsReady());

        paths.ImportLocalModel(dummyEmbed, VisualSearchPaths.EmbedModelFileName);
        Assert.True(paths.AreModelsReady());
    }

    private sealed class CapturingReaderLauncher : IReaderWindowLauncher
    {
        public string? OpenedPath { get; private set; }
        public int? OpenedPageZeroBased { get; private set; }
        public (float X, float Y, float W, float H)? OpenedHighlight { get; private set; }

        public bool OpenInReader(string path, string? displayTitle, string? authorName, Action? onReaderClosed, out string? errorMessage)
        {
            OpenedPath = path;
            OpenedPageZeroBased = null;
            OpenedHighlight = null;
            errorMessage = null;
            return true;
        }

        public bool OpenInReaderAtPage(
            string path,
            int pageIndexZeroBased,
            (float X, float Y, float W, float H)? highlightBox,
            string? displayTitle,
            string? authorName,
            Action? onReaderClosed,
            out string? errorMessage)
        {
            OpenedPath = path;
            OpenedPageZeroBased = pageIndexZeroBased;
            OpenedHighlight = highlightBox;
            errorMessage = null;
            return true;
        }
    }

    [Fact]
    public void Settings_VisualSearchToggle_ControlsMainWindowMenu_AndReaderJumpsWithHighlightBox()
    {
        var dataDir = Path.Combine(_tempRoot, "ui_data");
        var comicDir = Path.Combine(_tempRoot, "ui_comic");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(comicDir);
        WriteDistinctComicPage(Path.Combine(comicDir, "01.png"), SKColors.Blue, SKColors.White);
        WriteDistinctComicPage(Path.Combine(comicDir, "02.png"), SKColors.Red, SKColors.Black);
        WriteDistinctComicPage(Path.Combine(comicDir, "03.png"), SKColors.Green, SKColors.Yellow);

        using (var seed = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            seed.UpsertCatalogOnly(new AssetMeta { Path = comicDir, Name = "ui_comic", IsFolder = true, Mtime = 1 });
        }

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        using var mainVm = new MainWindowViewModel(ctx);
        Assert.False(mainVm.IsVisualSearchEnabled);

        // 設定頁打開開關並儲存，側選單應即時響應
        mainVm.SettingsPage.EnableVisualSearch = true;
        mainVm.SettingsPage.VisualSearchAutoIndexAfterArchive = true;
        mainVm.SettingsPage.VisualSearchEnableMultiCropRerank = false;
        mainVm.SettingsPage.VisualSearchPanelModelUrl = "https://example.com/panel.onnx";
        mainVm.SettingsPage.SaveCommand.Execute(null);

        Assert.True(ctx.Config.EnableVisualSearch);
        Assert.True(ctx.Config.VisualSearchAutoIndexAfterArchive);
        Assert.False(ctx.Config.VisualSearchEnableMultiCropRerank);
        Assert.Equal("https://example.com/panel.onnx", ctx.Config.VisualSearchPanelModelUrl);
        Assert.True(mainVm.IsVisualSearchEnabled);
        Assert.True(mainVm.VisualSearchPage.IsFeatureEnabled);

        // 驗證 GoToSettings 通知事件
        bool navigatedSettings = false;
        mainVm.NavigateToPageRequested += page => navigatedSettings = ReferenceEquals(page, mainVm.SettingsPage);
        mainVm.VisualSearchPage.GoToSettingsCommand.Execute(null);
        Assert.True(navigatedSettings);

        // 驗證搜尋結果卡點擊直跳閱讀器對應頁碼與分鏡高亮框
        var capturingLauncher = new CapturingReaderLauncher();
        using var vsVm = new VisualSearchPageViewModel(
            ctx,
            readerLauncher: capturingLauncher,
            runOnUiAsync: action => { action(); return Task.CompletedTask; });

        var hit = new VisualSearchHit(
            0.94f,
            new VisualVectorMeta(1, 0, 1, "ui_comic", comicDir, PageNo: 2, "02.png", IsFullPage: false, 0.1f, 0.2f, 0.5f, 0.4f, false));
        var card = new VisualSearchHitCardVm(
            1,
            hit,
            "ui_comic",
            null,
            c =>
            {
                (float X, float Y, float W, float H)? box = c.Meta.IsFullPage ? null : (c.Meta.X, c.Meta.Y, c.Meta.W, c.Meta.H);
                capturingLauncher.OpenInReaderAtPage(c.Meta.MangaPath, c.Meta.PageNo - 1, box, c.DisplayTitle, null, null, out _);
            },
            _ => { });
        card.OpenInReaderCommand.Execute(null);

        Assert.Equal(comicDir, capturingLauncher.OpenedPath);
        Assert.Equal(1, capturingLauncher.OpenedPageZeroBased);
        Assert.Equal((0.1f, 0.2f, 0.5f, 0.4f), capturingLauncher.OpenedHighlight);

        // 驗證 ReaderViewModel 確實依傳入頁碼與高亮框初始化
        using var readerVm = new ReaderViewModel(
            ctx,
            comicDir,
            runOnUiAsync: action => { action(); return Task.CompletedTask; },
            initialPageZeroBasedOverride: 1,
            initialHighlightBox: (0.1f, 0.2f, 0.5f, 0.4f));
        Assert.Equal(1, readerVm.CurrentPageIndex);
        Assert.Equal((1, 0.1f, 0.2f, 0.5f, 0.4f), readerVm.InitialHighlightBox);
    }

    [Fact]
    public async Task Settings_GuidesUserToBuildVisualIndexWithTimeEstimate_WhenEnabledWithoutDatabase()
    {
        var dataDir = Path.Combine(_tempRoot, "guide_data");
        var libDir = Path.Combine(_tempRoot, "guide_lib");
        var comicDir = Path.Combine(libDir, "SampleWork");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(comicDir);
        WriteDistinctComicPage(Path.Combine(comicDir, "01.png"), SKColors.Blue, SKColors.White);
        WriteDistinctComicPage(Path.Combine(comicDir, "02.png"), SKColors.Red, SKColors.Black);
        WriteDistinctComicPage(Path.Combine(comicDir, "03.png"), SKColors.Green, SKColors.Yellow);

        using (var seed = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
        {
            seed.UpsertCatalogOnly(new AssetMeta { Path = comicDir, Name = "SampleWork", IsFolder = true, Mtime = 1 });
            long id = seed.GetAssetId(comicDir)!.Value;
            seed.UpsertMetadata(new AssetMetadata { AssetId = id, Title = "SampleWork", PageCount = 120 });
        }

        // 先假定兩個 ONNX 模型已就緒，但尚無 visual_index.db
        var paths = new VisualSearchPaths(dataDir);
        paths.EnsureDirectories();
        File.WriteAllBytes(paths.PanelModelPath, new byte[4096]);
        File.WriteAllBytes(paths.EmbedModelPath, new byte[4096]);

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        var settingsVm = new SettingsPageViewModel(ctx, runOnUiAsync: action => { action(); return Task.CompletedTask; });
        settingsVm.RefreshVisualSearchModelStatus();

        // 開啟圖搜漫後檢查：應判定尚無索引庫，並含本數/頁數與時間預估
        settingsVm.EnableVisualSearch = true;
        Assert.True(settingsVm.ShowVisualIndexGuideCard);
        await settingsVm.RefreshVisualSearchIndexStatusAsync();

        Assert.False(settingsVm.VisualIndexReady);
        Assert.Contains("尚未建立「圖搜漫」索引資料庫", settingsVm.VisualIndexGuideTitle);
        Assert.Contains("120 頁", settingsVm.VisualIndexGuideDetail);
        Assert.Contains("儲存設定並前往建立索引庫", settingsVm.VisualIndexGoButtonText);

        // 驗證「儲存並前往建立索引庫」觸發儲存與跳轉
        bool goRequested = false;
        settingsVm.OpenVisualSearchRequested += () => goRequested = true;
        settingsVm.SaveAndGoToVisualSearchCommand.Execute(null);
        Assert.True(ctx.Config.EnableVisualSearch);
        Assert.True(goRequested);

        // 若建立視覺索引後再檢查，應顯示已就緒
        var pipeline = new VisualIndexingPipeline(
            paths,
            () => new MockPanelDetector(),
            () => new DeterministicMockEmbedder(),
            batchSize: 4);
        await pipeline.SyncAsync(libDir);
        await settingsVm.RefreshVisualSearchIndexStatusAsync();

        Assert.True(settingsVm.VisualIndexReady);
        Assert.Contains("已就緒", settingsVm.VisualIndexGuideTitle);
        Assert.Contains("前往「圖搜漫」頁面", settingsVm.VisualIndexGoButtonText);
    }
}
