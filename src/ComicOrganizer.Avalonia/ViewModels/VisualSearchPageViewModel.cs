using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.VisualSearch;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace ComicOrganizer.Avalonia.ViewModels;

public sealed partial class VisualSearchHitCardVm : ObservableObject, IDisposable
{
    private readonly Action<VisualSearchHitCardVm> _onOpenInReader;
    private readonly Action<VisualSearchHitCardVm> _onRevealInExplorer;

    public VisualSearchHitCardVm(
        int rank,
        VisualSearchHit hit,
        string displayTitle,
        string? authorName,
        Action<VisualSearchHitCardVm> onOpenInReader,
        Action<VisualSearchHitCardVm> onRevealInExplorer)
    {
        Rank = rank;
        Similarity = hit.Similarity;
        Meta = hit.Meta;
        DisplayTitle = displayTitle;
        AuthorName = string.IsNullOrWhiteSpace(authorName) ? "未登錄作者" : authorName;
        _onOpenInReader = onOpenInReader;
        _onRevealInExplorer = onRevealInExplorer;
    }

    public int Rank { get; }
    public float Similarity { get; }
    public VisualVectorMeta Meta { get; }
    public string DisplayTitle { get; }
    public string AuthorName { get; }

    public string SimilarityText => $"{Math.Clamp(Similarity * 100f, 0f, 100f):F1}%";

    public IBrush SimilarityBadgeBrush => Similarity switch
    {
        >= 0.85f => new SolidColorBrush(Color.Parse("#16A34A")),
        >= 0.70f => new SolidColorBrush(Color.Parse("#0284C7")),
        _ => new SolidColorBrush(Color.Parse("#D97706")),
    };

    public string MatchKindBadge => Meta.RegionKind switch
    {
        VisualRegionKind.Head => "👤 角色頭像命中",
        VisualRegionKind.Panel => "📐 分鏡裁切命中",
        _ => "🖼 整頁原圖",
    };
    public string PageBadgeText => $"第 {Meta.PageNo} 頁 · {Meta.EntryName}";

    [ObservableProperty]
    private Bitmap? _previewBitmap;

    public void LoadPreviewOnBackground()
    {
        _ = Task.Run(() =>
        {
            var jpeg = VisualSearchEngine.RenderHitPreviewJpeg(Meta, maxWidth: 340);
            if (jpeg is { Length: > 0 })
            {
                using var ms = new MemoryStream(jpeg, writable: false);
                var bmp = new Bitmap(ms);
                ExploreCardVmBase.DefaultRunOnUiAsync(() =>
                {
                    PreviewBitmap?.Dispose();
                    PreviewBitmap = bmp;
                });
            }
        });
    }

    [RelayCommand]
    private void OpenInReader() => _onOpenInReader(this);

    [RelayCommand]
    private void RevealInExplorer() => _onRevealInExplorer(this);

    public void Dispose()
    {
        PreviewBitmap?.Dispose();
        PreviewBitmap = null;
    }
}

/// <summary>
/// 「🔎 圖搜漫」頁面 ViewModel：
/// - 支援 Ctrl+V 貼上截圖、拖曳圖片檔與選檔查詢（純 CPU + MMap 執行，0 VRAM）。
/// - 透過獨立 --visual-index-worker 子行程進行 GPU 建庫與增量同步（含新作者發現、搬移零重算、刪除清理、中斷續建）。
/// </summary>
public sealed partial class VisualSearchPageViewModel : ViewModelBase, IDisposable
{
    private readonly AppDataContext _dataContext;
    private readonly VisualIndexHostService _hostService;
    private readonly IExternalLauncher _externalLauncher;
    private readonly IReaderWindowLauncher _readerLauncher;
    private readonly Func<Action, Task> _runOnUiAsync;
    private readonly SemaphoreSlim _searchGate = new(1, 1);

    private VisualSearchEngine? _searchEngine;
    private bool _disposed;

    public VisualSearchPageViewModel(
        AppDataContext dataContext,
        IExternalLauncher? externalLauncher = null,
        IReaderWindowLauncher? readerLauncher = null,
        Func<Action, Task>? runOnUiAsync = null)
    {
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        _hostService = dataContext.VisualIndexHost;
        _externalLauncher = externalLauncher ?? new SystemExternalLauncher();
        _readerLauncher = readerLauncher ?? new AvaloniaReaderWindowLauncher(dataContext);
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        _enableMultiCropRerank = dataContext.Config.VisualSearchEnableMultiCropRerank;

        _hostService.ProgressReceived += OnHostProgress;
        _hostService.Completed += OnHostCompleted;
        _hostService.ErrorReceived += OnHostError;
        _hostService.RunningStateChanged += OnHostRunningChanged;

        RefreshStatus();
    }

    public ObservableCollection<VisualSearchHitCardVm> SearchHits { get; } = new();

    public event Action? OpenSettingsRequested;

    [ObservableProperty]
    private bool _isFeatureEnabled;

    [ObservableProperty]
    private bool _areModelsReady;

    [ObservableProperty]
    private string _modelsBannerText = string.Empty;

    [ObservableProperty]
    private string _indexSummaryText = "尚未建立視覺索引";

    [ObservableProperty]
    private string _shardsSummaryText = string.Empty;

    [ObservableProperty]
    private bool _isIndexing;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressStatusText = "待機中（0 VRAM）";

    [ObservableProperty]
    private bool _needsCompactionHint;

    [ObservableProperty]
    private bool _confirmRebuildPending;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _enableMultiCropRerank = true;

    [ObservableProperty]
    private Bitmap? _queryPreviewBitmap;

    [ObservableProperty]
    private bool _hasQueryImage;

    [ObservableProperty]
    private string _querySourceInfo = "按 Ctrl+V 貼上截圖、拖曳圖片至此，或點擊上方選擇截圖";

    [ObservableProperty]
    private string _queryMetricsText = "等待查詢截圖…";

    [ObservableProperty]
    private bool _hasSearchResults;

    public string SyncButtonText => IsIndexing ? "⏹ 停止建庫" : "⚡ 增量更新索引";
    public string RebuildButtonText => ConfirmRebuildPending ? "⚠️ 再次點擊確認清空重建" : "🗑️ 全部重建";

    partial void OnIsIndexingChanged(bool value) => OnPropertyChanged(nameof(SyncButtonText));
    partial void OnConfirmRebuildPendingChanged(bool value) => OnPropertyChanged(nameof(RebuildButtonText));

    [RelayCommand]
    public void RefreshStatus()
    {
        var cfg = _dataContext.Config;
        IsFeatureEnabled = cfg.EnableVisualSearch;
        EnableMultiCropRerank = cfg.VisualSearchEnableMultiCropRerank;

        var paths = new VisualSearchPaths(_dataContext.DataDirectory);
        AreModelsReady = paths.AreModelsReady();
        ModelsBannerText = AreModelsReady
            ? $"ONNX 模型已就緒：{paths.ModelsDirectory}"
            : $"⚠️ 尚未安裝 ONNX 模型（需下載／匯入 {VisualSearchPaths.PanelModelFileName} 與 {VisualSearchPaths.EmbedModelFileName}）。";

        ConfirmRebuildPending = false;
        RefreshIndexOverview(paths);
    }

    private void RefreshIndexOverview(VisualSearchPaths paths)
    {
        if (!File.Exists(paths.DatabasePath))
        {
            IndexSummaryText = "尚未建立視覺索引（點擊右側「⚡ 增量更新索引」開始首次掃描）";
            ShardsSummaryText = $"目標漫畫庫：{(_dataContext.Config.RootDirectory is { Length: > 0 } r ? r : "尚未設定")}";
            NeedsCompactionHint = false;
            return;
        }

        try
        {
            using var db = new VisualIndexDatabase(paths.DatabasePath, readOnly: true);
            var ov = db.GetOverview();
            long diskBytes = paths.GetTotalDiskUsageBytes();
            double diskMb = diskBytes / (1024.0 * 1024.0);
            string updatedText = ov.LastUpdatedUnixSec > 0
                ? DateTimeOffset.FromUnixTimeSeconds(ov.LastUpdatedUnixSec).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : "-";

            IndexSummaryText =
                $"已索引 {ov.MangaCount:N0} 本 · {ov.TotalPages:N0} 頁 · {ov.ActiveVectorCount:N0} 筆向量" +
                (ov.HeadVectorCount > 0 ? $"（含頭像 {ov.HeadVectorCount:N0}）" : "") +
                $" · 佔用 {diskMb:F1} MB（更新：{updatedText}）";

            int pendingHeadBackfill = _dataContext.Config.VisualSearchEnableHeadIndex && paths.IsHeadModelReady()
                ? Math.Max(0, ov.MangaCount - ov.HeadIndexedMangaCount)
                : 0;
            string headHint = pendingHeadBackfill > 0
                ? $" · 尚有 {pendingHeadBackfill:N0} 本待增量補建頭像索引"
                : (_dataContext.Config.VisualSearchEnableHeadIndex && !paths.IsHeadModelReady()
                    ? " · 已開啟頭像索引（尚缺 head_detector.onnx）"
                    : "");

            ShardsSummaryText = (ov.Shards.Count == 0
                ? "尚無向量分片"
                : $"向量分片：{ov.Shards.Count} 個 · 待回收刪除紀錄：{ov.DeletedVectorCount:N0} 筆") + headHint;

            NeedsCompactionHint = ov.Shards.Any(s => s.DeletedCount > 0 && s.DeletedRatio >= 0.15);
        }
        catch (Exception ex)
        {
            IndexSummaryText = $"讀取視覺索引狀態失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private void GoToSettings() => OpenSettingsRequested?.Invoke();

    // 允許建庫執行期間再次點擊同顆按鈕以觸發「⏹ 停止建庫」（底層 _hostService 已有 _runGate 防重入）
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task StartOrStopIncrementalSyncAsync()
    {
        if (IsIndexing)
        {
            ProgressStatusText = "正在安全停止建庫並保存已完成作品…";
            _hostService.RequestStopGracefully();
            return;
        }

        await RunWorkerSyncAsync(rebuild: false);
    }

    [RelayCommand]
    public async Task RebuildIndexAsync()
    {
        if (IsIndexing) return;
        if (!ConfirmRebuildPending)
        {
            ConfirmRebuildPending = true;
            _dataContext.TryShowToast("確認全部重建", "再次點擊「全部重建」將清空現有視覺索引並從頭掃描。", NotificationType.Warning);
            return;
        }

        ConfirmRebuildPending = false;
        await RunWorkerSyncAsync(rebuild: true);
    }

    private async Task RunWorkerSyncAsync(bool rebuild)
    {
        var cfg = _dataContext.Config;
        if (string.IsNullOrWhiteSpace(cfg.RootDirectory) || !Directory.Exists(cfg.RootDirectory))
        {
            _dataContext.TryShowToast("找不到漫畫庫目錄", "請先至系統設定配置有效的漫畫庫根目錄 (RootDirectory)。", NotificationType.Warning);
            return;
        }

        var paths = new VisualSearchPaths(_dataContext.DataDirectory);
        if (!paths.AreModelsReady())
        {
            _dataContext.TryShowToast("模型尚未就緒", "請先至系統設定下載或匯入 ONNX 模型。", NotificationType.Warning);
            return;
        }

        bool enableHeadIndex = cfg.VisualSearchEnableHeadIndex;
        if (enableHeadIndex && !paths.IsHeadModelReady())
        {
            _dataContext.TryShowToast(
                "尚未匯入頭像偵測模型",
                $"已開啟角色頭像索引但找不到 {VisualSearchPaths.HeadModelFileName}，本次僅同步整頁與分鏡。",
                NotificationType.Warning,
                5);
        }

        // 先釋放查詢 MMap 句柄，防止 Windows 鎖檔衝突
        DisposeSearchEngine();
        ProgressPercent = 0;
        ProgressStatusText = rebuild ? "準備清空並全部重建索引…" : "正在啟動 GPU 增量建庫子行程…";

        await _hostService.RunSyncWorkerAsync(
            cfg.RootDirectory,
            specificPaths: null,
            rebuild: rebuild,
            enableHeadIndex: enableHeadIndex).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task CompactShardsAsync()
    {
        if (IsIndexing) return;
        var paths = new VisualSearchPaths(_dataContext.DataDirectory);
        if (!File.Exists(paths.DatabasePath)) return;

        DisposeSearchEngine();
        ProgressStatusText = "正在壓縮向量分片與回收空間…";
        try
        {
            int count = await Task.Run(() => VisualIndexingPipeline.CompactShards(paths, minDeletedRatio: 0.01)).ConfigureAwait(false);
            await _runOnUiAsync(() =>
            {
                RefreshIndexOverview(paths);
                ProgressStatusText = $"分片重整完成（已壓縮 {count} 個分片，0 VRAM）";
                _dataContext.TryShowToast("空間已回收", $"已完成 {count} 個向量分片壓縮重整。", NotificationType.Success);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() =>
            {
                ProgressStatusText = $"重整失敗：{ex.Message}";
                _dataContext.TryShowToast("重整分片失敗", ex.Message, NotificationType.Error, 4);
            }).ConfigureAwait(false);
        }
    }

    public async Task SearchFromImageBytesAsync(byte[] encodedBytes, string sourceLabel)
    {
        SKBitmap? bmp = null;
        try
        {
            bmp = SKBitmap.Decode(encodedBytes);
        }
        catch { }

        if (bmp == null)
        {
            _dataContext.TryShowToast("無法讀取圖片", $"格式不支援或圖片損毀：{sourceLabel}", NotificationType.Warning);
            return;
        }

        using (bmp)
        {
            await SearchWithBitmapAsync(bmp, sourceLabel).ConfigureAwait(false);
        }
    }

    public async Task SearchWithBitmapAsync(SKBitmap queryBitmap, string sourceLabel)
    {
        var paths = new VisualSearchPaths(_dataContext.DataDirectory);
        if (!paths.AreModelsReady())
        {
            _dataContext.TryShowToast("模型尚未就緒", "請先於系統設定頁下載或匯入 ONNX 模型。", NotificationType.Warning);
            return;
        }
        if (!File.Exists(paths.DatabasePath))
        {
            _dataContext.TryShowToast("尚未建立索引", "請先點擊「⚡ 增量更新索引」建立漫畫庫視覺索引。", NotificationType.Warning);
            return;
        }

        // 先於 UI 更新查詢截圖預覽
        using var previewImg = SKImage.FromBitmap(queryBitmap);
        using var previewEncoded = previewImg.Encode(SKEncodedImageFormat.Png, 90);
        byte[] previewBytes = previewEncoded.ToArray();
        int qW = queryBitmap.Width;
        int qH = queryBitmap.Height;

        await _runOnUiAsync(() =>
        {
            using var ms = new MemoryStream(previewBytes, writable: false);
            QueryPreviewBitmap?.Dispose();
            QueryPreviewBitmap = new Bitmap(ms);
            HasQueryImage = true;
            QuerySourceInfo = $"{sourceLabel}（{qW} × {qH}）";
            IsSearching = true;
            QueryMetricsText = "正在以純 CPU + MMap 搜尋…";
        }).ConfigureAwait(false);

        using var bitmapCopy = queryBitmap.Copy();

        await _searchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var report = await Task.Run(() =>
            {
                _searchEngine ??= new VisualSearchEngine(paths);
                return _searchEngine.SearchImage(
                    bitmapCopy,
                    topK: 12,
                    trimBorders: true,
                    enableMultiCropRerank: EnableMultiCropRerank);
            }).ConfigureAwait(false);

            // 嘗試從主庫 cache.db 補齊作者與正式標題（唯讀快速查詢）
            var cards = new List<VisualSearchHitCardVm>(report.Hits.Count);
            for (int i = 0; i < report.Hits.Count; i++)
            {
                var hit = report.Hits[i];
                var (title, author) = TryResolveWorkInfoFromMainStore(hit.Meta.MangaPath, hit.Meta.MangaTitle);
                var card = new VisualSearchHitCardVm(i + 1, hit, title, author, OnOpenHitInReader, OnRevealHitInExplorer);
                card.LoadPreviewOnBackground();
                cards.Add(card);
            }

            await _runOnUiAsync(() =>
            {
                ClearHitCards();
                foreach (var c in cards) SearchHits.Add(c);
                HasSearchResults = SearchHits.Count > 0;
                QueryMetricsText =
                    $"命中 {SearchHits.Count} 筆 · 總計 {report.TotalMs:F1} ms" +
                    $"（前處理 {report.PreprocessMs:F1} + CPU推論 {report.EmbedMs:F1} + MMap {report.VectorSearchMs:F1} ms" +
                    (report.RerankTriggered ? $" + Multi-Crop精排 {report.RerankMs:F1} ms" : "") +
                    " · 0 VRAM）";
                IsSearching = false;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            string detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            await _runOnUiAsync(() =>
            {
                IsSearching = false;
                QueryMetricsText = $"搜尋失敗：{detail}";
                _dataContext.TryShowToast("圖搜漫失敗", detail, NotificationType.Error, 5);
            }).ConfigureAwait(false);
        }
        finally
        {
            _searchGate.Release();
        }
    }

    private (string Title, string? Author) TryResolveWorkInfoFromMainStore(string mangaPath, string fallbackTitle)
    {
        try
        {
            var view = _dataContext.MetaStore.GetWorkDetail(mangaPath);
            if (view != null)
            {
                return (
                    string.IsNullOrWhiteSpace(view.Title) ? fallbackTitle : view.Title,
                    view.AuthorMainName);
            }
        }
        catch
        {
        }
        return (fallbackTitle, null);
    }

    [RelayCommand]
    public void ClearQuery()
    {
        QueryPreviewBitmap?.Dispose();
        QueryPreviewBitmap = null;
        HasQueryImage = false;
        QuerySourceInfo = "按 Ctrl+V 貼上截圖、拖曳圖片至此，或點擊上方選擇截圖";
        QueryMetricsText = "等待查詢截圖…";
        ClearHitCards();
        HasSearchResults = false;
    }

    private void ClearHitCards()
    {
        foreach (var card in SearchHits) card.Dispose();
        SearchHits.Clear();
    }

    private void OnOpenHitInReader(VisualSearchHitCardVm card)
    {
        (float X, float Y, float W, float H)? highlight = card.Meta.IsFullPage
            ? null
            : (card.Meta.X, card.Meta.Y, card.Meta.W, card.Meta.H);

        if (!_readerLauncher.OpenInReaderAtPage(
                card.Meta.MangaPath,
                Math.Max(0, card.Meta.PageNo - 1),
                highlight,
                card.DisplayTitle,
                card.AuthorName == "未登錄作者" ? null : card.AuthorName,
                onReaderClosed: null,
                out var error))
        {
            _dataContext.TryShowToast("無法開啟閱讀器", error ?? card.Meta.MangaPath, NotificationType.Warning);
        }
    }

    private void OnRevealHitInExplorer(VisualSearchHitCardVm card)
    {
        if (!_externalLauncher.RevealInFileExplorer(card.Meta.MangaPath, out var error))
        {
            _dataContext.TryShowToast("無法開啟檔案總管", error ?? card.Meta.MangaPath, NotificationType.Warning);
        }
    }

    private void OnHostProgress(VisualIndexProgress prog)
    {
        _ = _runOnUiAsync(() =>
        {
            double pct = prog.TotalComics > 0
                ? Math.Clamp(100.0 * prog.DoneComics / prog.TotalComics, 0, 100)
                : 0;
            ProgressPercent = pct;
            ProgressStatusText = prog.TotalComics > 0
                ? $"{prog.Phase} {prog.DoneComics}/{prog.TotalComics} 本（{prog.DonePages:N0} 頁 / +{prog.AddedVectors:N0} 向量）· {prog.CurrentTitle}"
                : prog.Phase;
        });
    }

    private void OnHostCompleted(VisualSyncSummary summary)
    {
        _ = _runOnUiAsync(() =>
        {
            RefreshIndexOverview(new VisualSearchPaths(_dataContext.DataDirectory));
            ProgressPercent = 100;
            string statusPrefix = summary.WasCancelled ? "已安全中斷續建" : "增量同步完成";
            string headPart = summary.HeadBackfilledComics > 0
                ? $" · 補建頭像 {summary.HeadBackfilledComics} 本"
                : "";
            ProgressStatusText =
                $"{statusPrefix}：新增 {summary.IndexedComics} 本{headPart}（{summary.TotalPages:N0} 頁 / +{summary.TotalVectorsAdded:N0} 向量）· " +
                $"搬移 {summary.MovedComics} · 清理 {summary.DeletedComics} · 略過 {summary.SkippedUnchanged} · {summary.ElapsedSeconds:F1}s（VRAM 已歸零）";

            _dataContext.TryShowToast(
                statusPrefix,
                ProgressStatusText,
                summary.WasCancelled ? NotificationType.Warning : NotificationType.Success);
        });
    }

    private void OnHostError(string error)
    {
        _ = _runOnUiAsync(() =>
        {
            ProgressStatusText = $"⚠️ {error}";
            _dataContext.TryShowToast("視覺索引作業訊息", error, NotificationType.Error, 5);
        });
    }

    private void OnHostRunningChanged(bool isRunning)
    {
        _ = _runOnUiAsync(() => IsIndexing = isRunning);
    }

    public void DisposeSearchEngine()
    {
        _searchGate.Wait();
        try
        {
            _searchEngine?.Dispose();
            _searchEngine = null;
        }
        finally
        {
            _searchGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hostService.ProgressReceived -= OnHostProgress;
        _hostService.Completed -= OnHostCompleted;
        _hostService.ErrorReceived -= OnHostError;
        _hostService.RunningStateChanged -= OnHostRunningChanged;
        DisposeSearchEngine();
        ClearHitCards();
        QueryPreviewBitmap?.Dispose();
        QueryPreviewBitmap = null;
        _searchGate.Dispose();
    }
}
