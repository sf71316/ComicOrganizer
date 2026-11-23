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
        Assert.False(cfg.VisualSearchEnableHeadIndex);
        Assert.Equal(string.Empty, cfg.VisualSearchHeadModelUrl);
    }

    [Fact]
    public void VisualSearchPaths_HeadModel_IsOptional_AndSupportsLocalImport()
    {
        var dataDir = Path.Combine(_tempRoot, "paths_test");
        var paths = new VisualSearchPaths(dataDir);
        var srcDir = Path.Combine(_tempRoot, "src_models");
        Directory.CreateDirectory(srcDir);

        var fakePanel = Path.Combine(srcDir, "p.onnx");
        var fakeEmbed = Path.Combine(srcDir, "e.onnx");
        var fakeHead = Path.Combine(srcDir, "h.onnx");
        var payload = new byte[2048];
        File.WriteAllBytes(fakePanel, payload);
        File.WriteAllBytes(fakeEmbed, payload);
        File.WriteAllBytes(fakeHead, payload);

        paths.ImportLocalModel(fakePanel, VisualSearchPaths.PanelModelFileName);
        paths.ImportLocalModel(fakeEmbed, VisualSearchPaths.EmbedModelFileName);

        // 必備模型就緒，選配頭像模型尚未匯入
        Assert.True(paths.AreModelsReady());
        Assert.False(paths.IsHeadModelReady());

        var statusesBefore = paths.GetModelStatuses();
        Assert.Equal(3, statusesBefore.Count);
        Assert.False(statusesBefore[0].IsOptional);
        Assert.False(statusesBefore[1].IsOptional);
        Assert.True(statusesBefore[2].IsOptional);
        Assert.False(statusesBefore[2].Exists);

        // 匯入選配頭像模型
        paths.ImportLocalModel(fakeHead, VisualSearchPaths.HeadModelFileName);
        Assert.True(paths.IsHeadModelReady());
        Assert.True(paths.GetModelStatuses()[2].Exists);
    }

    [Fact]
    public void VisualHeadCropHelper_ExpandsPadding_AndFiltersDuplicatePanelOverlap()
    {
        var heads = new List<VisualPanelBox>
        {
            // 一般角色頭像（遠小於分鏡）
            new(0.20f, 0.20f, 0.10f, 0.10f, 0.90f, 0),
            // 與某小分鏡幾乎完全重疊的頭像框
            new(0.60f, 0.60f, 0.20f, 0.20f, 0.88f, 0),
            // 過小雜訊（1000x1000 圖上外擴後寬度仍 < 32px）
            new(0.01f, 0.01f, 0.01f, 0.01f, 0.70f, 0),
        };
        var panels = new List<VisualPanelBox>
        {
            new(0.05f, 0.05f, 0.50f, 0.50f, 0.95f, 0),
            new(0.57f, 0.58f, 0.26f, 0.27f, 0.93f, 0),
        };

        var expanded = VisualHeadCropHelper.ExpandAndFilterHeads(heads, panels, 1000, 1000);
        Assert.Single(expanded);

        var (hx, hy, hw, hh, rect) = expanded[0];
        Assert.InRange(hx, 0.184f, 0.186f); // 0.20 - 0.10*0.15 = 0.185
        Assert.InRange(hy, 0.189f, 0.191f); // 0.20 - 0.10*0.10 = 0.190
        Assert.InRange(hw, 0.129f, 0.131f); // 0.10 * 1.30 = 0.130
        Assert.InRange(hh, 0.134f, 0.136f); // 0.10 * 1.35 = 0.135
        Assert.True(rect.Width >= 32f && rect.Height >= 32f);
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
        mainVm.SettingsPage.VisualSearchEnableHeadIndex = true;
        mainVm.SettingsPage.VisualSearchPanelModelUrl = "https://example.com/panel.onnx";
        mainVm.SettingsPage.VisualSearchHeadModelUrl = "https://example.com/head.onnx";
        mainVm.SettingsPage.SaveCommand.Execute(null);

        Assert.True(ctx.Config.EnableVisualSearch);
        Assert.True(ctx.Config.VisualSearchAutoIndexAfterArchive);
        Assert.False(ctx.Config.VisualSearchEnableMultiCropRerank);
        Assert.True(ctx.Config.VisualSearchEnableHeadIndex);
        Assert.Equal("https://example.com/panel.onnx", ctx.Config.VisualSearchPanelModelUrl);
        Assert.Equal("https://example.com/head.onnx", ctx.Config.VisualSearchHeadModelUrl);
        Assert.True(mainVm.IsVisualSearchEnabled);
        Assert.True(mainVm.VisualSearchPage.IsFeatureEnabled);

        // 驗證命中卡片三種區域徽章文字
        using var fullCard = new VisualSearchHitCardVm(1, new VisualSearchHit(0.9f, new VisualVectorMeta(1, 0, 1, "T", "P", 1, "001.jpg", true, 0, 0, 1, 1, false, VisualRegionKind.FullPage)), "T", "A", _ => { }, _ => { });
        using var panelCard = new VisualSearchHitCardVm(2, new VisualSearchHit(0.8f, new VisualVectorMeta(2, 0, 1, "T", "P", 1, "001.jpg", false, 0, 0, 0.5f, 0.5f, false, VisualRegionKind.Panel)), "T", "A", _ => { }, _ => { });
        using var headCard = new VisualSearchHitCardVm(3, new VisualSearchHit(0.95f, new VisualVectorMeta(3, 0, 1, "T", "P", 1, "001.jpg", false, 0.2f, 0.2f, 0.2f, 0.2f, false, VisualRegionKind.Head)), "T", "A", _ => { }, _ => { });
        Assert.Contains("整頁", fullCard.MatchKindBadge);
        Assert.Contains("分鏡", panelCard.MatchKindBadge);
        Assert.Contains("頭像", headCard.MatchKindBadge);

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

        // 驗證建庫進行中「⏹ 停止建庫」按鈕仍可點擊（AllowConcurrentExecutions = true）
        vsVm.IsIndexing = true;
        Assert.Equal("⏹ 停止建庫", vsVm.SyncButtonText);
        Assert.True(vsVm.StartOrStopIncrementalSyncCommand.CanExecute(null));
        vsVm.StartOrStopIncrementalSyncCommand.Execute(null);
        Assert.Contains("停止建庫", vsVm.ProgressStatusText);
        vsVm.IsIndexing = false;

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

    private sealed class CountingPanelDetector : IPanelDetector
    {
        public int CallCount { get; private set; }

        public List<VisualPanelBox> DetectPreprocessed(PreparedYoloFrame input, float confThreshold = 0.35f, float iouThreshold = 0.45f, float minAreaRatio = 0.02f)
        {
            CallCount++;
            return
            [
                new VisualPanelBox(0.05f, 0.05f, 0.90f, 0.42f, 0.95f, 0),
                new VisualPanelBox(0.05f, 0.52f, 0.90f, 0.42f, 0.92f, 0),
            ];
        }

        public void Dispose() { }
    }

    private sealed class MockHeadDetector : IPanelDetector
    {
        public int CallCount { get; private set; }

        public List<VisualPanelBox> DetectPreprocessed(PreparedYoloFrame input, float confThreshold = 0.35f, float iouThreshold = 0.45f, float minAreaRatio = 0.02f)
        {
            CallCount++;
            // 在 160x200 圖上外擴後約 52x67 px (>=32px)，且與 0.90x0.42 大分鏡 IoU 遠小於 0.70
            return
            [
                new VisualPanelBox(0.20f, 0.15f, 0.25f, 0.25f, 0.91f, 0),
            ];
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task HeadIndex_IncrementalBackfill_OnlyAddsHeadVectors_WithoutRecomputingPanelsOrFullPages()
    {
        var dataDir = Path.Combine(_tempRoot, "head_backfill_data");
        var libDir = Path.Combine(_tempRoot, "head_backfill_lib");
        var work1 = Path.Combine(libDir, "AuthorA", "Work1");
        Directory.CreateDirectory(work1);
        WriteDistinctComicPage(Path.Combine(work1, "001.png"), SKColors.Crimson, SKColors.Navy);
        WriteDistinctComicPage(Path.Combine(work1, "002.png"), SKColors.Gold, SKColors.ForestGreen);
        WriteDistinctComicPage(Path.Combine(work1, "003.png"), SKColors.Purple, SKColors.Teal);

        var paths = new VisualSearchPaths(dataDir);

        // 第一輪：未啟用頭像索引，建立整頁 + 分鏡（3 頁 × 3 = 9 向量）
        var panelDetector1 = new CountingPanelDetector();
        var pipeline1 = new VisualIndexingPipeline(
            paths,
            () => panelDetector1,
            () => new DeterministicMockEmbedder(),
            batchSize: 4,
            enableHeadIndex: false);

        var s1 = await pipeline1.SyncAsync(libDir);
        Assert.Equal(1, s1.IndexedComics);
        Assert.Equal(0, s1.HeadBackfilledComics);
        Assert.Equal(9, s1.TotalVectorsAdded);
        Assert.Equal(3, panelDetector1.CallCount);

        using (var db = new VisualIndexDatabase(paths.DatabasePath, readOnly: true))
        {
            var ov = db.GetOverview();
            Assert.Equal(1, ov.MangaCount);
            Assert.Equal(9, ov.ActiveVectorCount);
            Assert.Equal(0, ov.HeadIndexedMangaCount);
            Assert.Equal(0, ov.HeadVectorCount);
        }

        // 第二輪：開啟頭像索引，應僅增量補建人頭向量，完全不重跑分鏡模型與整頁/分鏡向量
        var panelDetector2 = new CountingPanelDetector();
        var headDetector2 = new MockHeadDetector();
        var pipeline2 = new VisualIndexingPipeline(
            paths,
            () => panelDetector2,
            () => new DeterministicMockEmbedder(),
            batchSize: 4,
            enableHeadIndex: true,
            headDetectorFactory: () => headDetector2);

        var s2 = await pipeline2.SyncAsync(libDir);
        Assert.Equal(0, s2.IndexedComics);
        Assert.Equal(1, s2.HeadBackfilledComics);
        Assert.Equal(3, s2.TotalVectorsAdded); // 3 頁 × 1 顆人頭 = 3 向量
        Assert.Equal(0, panelDetector2.CallCount); // 關鍵：完全未重跑分鏡偵測器
        Assert.Equal(3, headDetector2.CallCount);

        using (var db = new VisualIndexDatabase(paths.DatabasePath, readOnly: true))
        {
            var ov = db.GetOverview();
            Assert.Equal(1, ov.MangaCount);
            Assert.Equal(12, ov.ActiveVectorCount); // 原本 9 + 補建 3
            Assert.Equal(1, ov.HeadIndexedMangaCount);
            Assert.Equal(3, ov.HeadVectorCount);
        }

        // 第三輪：再次同步應全部跳過（零重算）
        var headDetector3 = new MockHeadDetector();
        var pipeline3 = new VisualIndexingPipeline(
            paths,
            () => new CountingPanelDetector(),
            () => new DeterministicMockEmbedder(),
            batchSize: 4,
            enableHeadIndex: true,
            headDetectorFactory: () => headDetector3);

        var s3 = await pipeline3.SyncAsync(libDir);
        Assert.Equal(0, s3.IndexedComics);
        Assert.Equal(0, s3.HeadBackfilledComics);
        Assert.Equal(1, s3.SkippedUnchanged);
        Assert.Equal(0, s3.TotalVectorsAdded);
        Assert.Equal(0, headDetector3.CallCount);
    }

    [Fact]
    public void LegacyDatabaseSchema_ReadOnlyAndUpgradeMigration_AreBackwardCompatible()
    {
        var dbPath = Path.Combine(_tempRoot, "legacy_visual.db");

        // 建立不含 has_head_index 與 region_kind 的舊版 schema
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE mangas (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT NOT NULL UNIQUE,
                    title TEXT NOT NULL,
                    fingerprint TEXT NOT NULL,
                    page_count INTEGER NOT NULL DEFAULT 0,
                    updated_at INTEGER NOT NULL
                );
                CREATE TABLE vectors (
                    vec_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    shard_id INTEGER NOT NULL DEFAULT 0,
                    manga_id INTEGER NOT NULL,
                    page_no INTEGER NOT NULL,
                    entry_name TEXT NOT NULL,
                    is_full_page INTEGER NOT NULL,
                    x REAL NOT NULL,
                    y REAL NOT NULL,
                    w REAL NOT NULL,
                    h REAL NOT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO mangas(id, path, title, fingerprint, page_count, updated_at)
                VALUES (1, 'E:\Old\Work.zip', 'OldWork', 'fp1', 10, 1000);
                INSERT INTO vectors(vec_id, shard_id, manga_id, page_no, entry_name, is_full_page, x, y, w, h, is_deleted)
                VALUES (1, 0, 1, 1, '001.jpg', 1, 0, 0, 1, 1, 0),
                       (2, 0, 1, 1, '001.jpg', 0, 0.1, 0.1, 0.4, 0.4, 0);
                """;
            cmd.ExecuteNonQuery();
        }

        // 1. 唯讀開啟舊庫不應拋錯，且應正確推導 RegionKind
        using (var ro = new VisualIndexDatabase(dbPath, readOnly: true))
        {
            var ov = ro.GetOverview();
            Assert.Equal(1, ov.MangaCount);
            Assert.Equal(2, ov.ActiveVectorCount);
            Assert.Equal(0, ov.HeadIndexedMangaCount);
            Assert.Equal(0, ov.HeadVectorCount);

            var metas = ro.GetVectorsByIds([1, 2]);
            Assert.Equal(VisualRegionKind.FullPage, metas[1].RegionKind);
            Assert.Equal(VisualRegionKind.Panel, metas[2].RegionKind);
        }

        // 2. 讀寫開啟舊庫應自動 ALTER TABLE 升級並回填 region_kind
        using (var rw = new VisualIndexDatabase(dbPath, readOnly: false))
        {
            var manga = rw.GetMangaByPath(@"E:\Old\Work.zip");
            Assert.NotNull(manga);
            Assert.False(manga!.HasHeadIndex);

            var panels = rw.GetPanelBoxesForManga(1);
            Assert.Single(panels[1]);

            var metas = rw.GetVectorsByIds([1, 2]);
            Assert.Equal(VisualRegionKind.FullPage, metas[1].RegionKind);
            Assert.Equal(VisualRegionKind.Panel, metas[2].RegionKind);
        }
    }

    [Fact]
    public void VisualNativeLoader_HandlesMissingAndValidLibDirectory_Safely()
    {
        var missingBase = Path.Combine(_tempRoot, "no_lib_dir");
        Assert.Empty(VisualNativeLoader.LoadFromLibDirectory(missingBase));

        // 傳入 bin 目錄下雖無 lib 子目錄（或空的 lib 子目錄）都不應拋例外
        var emptyBase = Path.Combine(_tempRoot, "empty_lib_dir");
        Directory.CreateDirectory(Path.Combine(emptyBase, "lib"));
        Assert.Empty(VisualNativeLoader.LoadFromLibDirectory(emptyBase));
        Assert.Contains("onnxruntime.dll", VisualNativeLoader.OrderedNativeDllNames);
        Assert.Contains("libusearch_c.dll", VisualNativeLoader.OrderedNativeDllNames);
    }

    [Fact]
    public async Task SettingsPage_AutoPopulatesExistingOrImportedHeadModelPath()
    {
        var dataDir = Path.Combine(_tempRoot, "head_path_ui");
        var paths = new VisualSearchPaths(dataDir);
        paths.EnsureDirectories();
        File.WriteAllBytes(paths.HeadModelPath, new byte[4096]);

        using var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false);
        var vm = new SettingsPageViewModel(ctx, runOnUiAsync: action => { action(); return Task.CompletedTask; });

        // 1. 磁碟已有 head_detector.onnx 且設定未手填 URL 時，應自動帶出 models\head_detector.onnx 路徑
        Assert.True(vm.IsHeadModelReady);
        Assert.Equal(paths.HeadModelPath, vm.VisualSearchHeadModelUrl);

        // 2. 透過「匯入本機」挑選新檔案時，應立即把來源路徑回填到欄位
        var pickedHead = Path.Combine(_tempRoot, "picked_head_detector.onnx");
        File.WriteAllBytes(pickedHead, new byte[4096]);
        vm.PickFileAsync = (_, _, _, _) => Task.FromResult<string?>(pickedHead);
        await vm.ImportHeadModelCommand.ExecuteAsync(null);

        Assert.Equal(pickedHead, vm.VisualSearchHeadModelUrl);
    }
}
