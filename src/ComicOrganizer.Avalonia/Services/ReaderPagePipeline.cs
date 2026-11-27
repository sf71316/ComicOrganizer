using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.App.UI;
using SkiaSharp;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>
/// 單頁顯示影像取得結果（統一管理 <see cref="SkiaImageLease"/> 與獨立合成圖的生命週期）。
/// </summary>
public readonly struct DisplayPageImage : IDisposable
{
    private readonly SkiaImageLease? _lease;
    public SKImage? Image { get; }
    public bool IsSuperRes { get; }
    public bool IsTranslated { get; }

    public DisplayPageImage(SkiaImageLease? lease, SKImage? directImage, bool isSuperRes, bool isTranslated)
    {
        _lease = lease;
        Image = lease?.Image ?? directImage;
        IsSuperRes = isSuperRes;
        IsTranslated = isTranslated;
    }

    public void Dispose()
    {
        _lease?.Dispose();
    }
}

/// <summary>
/// Avalonia 閱讀器頁面解碼、預載、單頁 AI 超解析度重繪與 OCR + LLM 翻譯管線。
/// 所有快取操作皆具備執行緒安全鎖定，並在關閉時確實釋放所有 <see cref="SKImage"/> 與原生引擎資源。
/// </summary>
public sealed class ReaderPagePipeline : IDisposable
{
    private const int TranslatedCacheCap = 14;
    private const int SuperResCacheCap = 8;

    private readonly IReaderSource _source;
    private readonly SkiaPageRenderer _renderer;
    private readonly LlmTranslationService _translationService;
    private readonly ISkiaTextMaskProvider _textMaskProvider;
    private readonly object _sync = new();

    private readonly Dictionary<int, SKImage> _superResImages = new();
    private readonly LinkedList<int> _superResLru = new();

    private readonly Dictionary<int, SKImage> _translatedImages = new();
    private readonly LinkedList<int> _translatedLru = new();

    private readonly Dictionary<int, List<OcrBlock>> _pageBlocks = new();
    private readonly Dictionary<int, string> _pageErrors = new();
    private readonly HashSet<int> _ocrAttemptedPages = new();
    private readonly HashSet<int> _translatingPages = new();
    private readonly HashSet<int> _superResBusyPages = new();

    private ISkiaOcrService? _ocrService;
    private int _ocrEngineType = 1;
    private int _maskEngineType;
    private string _ocrSourceLanguage = "ja-JP";
    private string _targetLanguage = "zh-Hant";
    private bool _disposed;

    public ReaderPagePipeline(
        IReaderSource source,
        AppConfig config,
        int cacheCap = 12,
        string? dataDirectory = null,
        ISkiaTextMaskProvider? textMaskProvider = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentNullException.ThrowIfNull(config);

        _translationService = new LlmTranslationService(new HttpClient { Timeout = TimeSpan.FromSeconds(45) });
        _textMaskProvider = textMaskProvider ?? new SkiaComicTextDetector(dataDirectory: dataDirectory);
        ConfigureFromAppConfig(config);

        _renderer = new SkiaPageRenderer(
            source.PageCount,
            idx => SkiaPageRenderer.DecodeFromSource(_source, idx),
            cacheCap: Math.Max(6, cacheCap));

        _renderer.PageReady += idx => PageUpdated?.Invoke(idx);
    }

    public IReaderSource Source => _source;
    public int PageCount => _source.PageCount;
    public int OcrEngineType => _ocrEngineType;
    public int MaskEngineType => _maskEngineType;

    /// <summary>當某頁背景解碼、超解析重繪或翻譯合成完成時觸發（傳入頁碼索引）。</summary>
    public event Action<int>? PageUpdated;

    /// <summary>當 OCR / 翻譯狀態文字更新時觸發。</summary>
    public event Action<int, string?>? TranslationStatusChanged;

    public void ConfigureFromAppConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var apiType = config.TranslationApiType == 1 ? LlmApiType.OllamaNative : LlmApiType.OpenAiCompatible;
        _translationService.Configure(config.TranslationEndpoint, config.TranslationModelName, apiType, config.GetTranslationApiKey());
        _ocrSourceLanguage = "ja-JP";
        _targetLanguage = "zh-Hant";
        SetOcrEngine(config.OcrEngineType);
        SetMaskEngine(config.MaskEngineType);
    }

    public void SetOcrEngine(int engineType)
    {
        lock (_sync)
        {
            if (_ocrEngineType == engineType && _ocrService != null) return;
            _ocrEngineType = engineType;
            (_ocrService as IDisposable)?.Dispose();
            _ocrService = null;
        }
    }

    public void SetMaskEngine(int maskEngineType)
    {
        int normalized = maskEngineType == 2 ? 2 : 0;
        lock (_sync)
        {
            if (_maskEngineType == normalized) return;
            _maskEngineType = normalized;
            foreach (var img in _translatedImages.Values) img.Dispose();
            _translatedImages.Clear();
            _translatedLru.Clear();
            _ocrAttemptedPages.Clear();
        }
    }

    public void SetCurrentPageAndPreload(int pageIndex, ReaderViewMode viewMode, int preloadRadius = 2)
    {
        if (_disposed || PageCount == 0) return;
        pageIndex = Math.Clamp(pageIndex, 0, PageCount - 1);
        _renderer.Current = pageIndex;

        if (viewMode == ReaderViewMode.DualPage)
        {
            _renderer.SetProtectedRange(pageIndex, Math.Min(PageCount - 1, pageIndex + 1));
            _renderer.RequestSpreadAndAround(pageIndex, 2, preloadRadius);
        }
        else
        {
            _renderer.SetProtectedRange(pageIndex, pageIndex);
            _renderer.RequestAround(pageIndex, preloadRadius);
        }
    }

    public void SetContinuousVisibleRange(int firstPage, int lastPage)
    {
        if (_disposed || PageCount == 0) return;
        firstPage = Math.Clamp(firstPage, 0, PageCount - 1);
        lastPage = Math.Clamp(lastPage, firstPage, PageCount - 1);
        _renderer.SetProtectedRange(firstPage, lastPage);
        _renderer.RequestRange(Math.Max(0, firstPage - 1), Math.Min(PageCount - 1, lastPage + 2));
    }

    /// <summary>
    /// 取得第 <paramref name="index"/> 頁供繪製的影像（依優先順序：譯文合成圖 > 單頁超解析圖 > LRU 原始解碼頁）。
    /// 若尚未解碼則排入前景優先解碼佇列並回傳空結構。
    /// </summary>
    public DisplayPageImage AcquireForDisplay(int index, bool translationEnabled, bool compareOriginal)
    {
        if (_disposed || index < 0 || index >= PageCount)
            return default;

        lock (_sync)
        {
            if (translationEnabled && !compareOriginal && _translatedImages.TryGetValue(index, out var transImg))
            {
                TouchLru(_translatedLru, index);
                return new DisplayPageImage(null, transImg, _superResImages.ContainsKey(index), true);
            }

            if (!translationEnabled || compareOriginal || !_translatedImages.ContainsKey(index))
            {
                if (_superResImages.TryGetValue(index, out var srImg))
                {
                    TouchLru(_superResLru, index);
                    return new DisplayPageImage(null, srImg, true, false);
                }
            }
        }

        var lease = _renderer.AcquirePeek(index);
        if (lease != null)
        {
            if (translationEnabled && !compareOriginal)
            {
                bool alreadyAttempted;
                lock (_sync) alreadyAttempted = _ocrAttemptedPages.Contains(index);
                if (!alreadyAttempted)
                    _ = EnsurePageTranslatedAsync(index);
            }
            return new DisplayPageImage(lease, null, false, false);
        }

        _renderer.RequestPriority(index);
        if (translationEnabled && !compareOriginal)
        {
            bool alreadyAttempted;
            lock (_sync) alreadyAttempted = _ocrAttemptedPages.Contains(index);
            if (!alreadyAttempted)
                _ = EnsurePageTranslatedAsync(index);
        }
        return default;
    }

    public bool TryGetPageDimensions(int index, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (_disposed || index < 0 || index >= PageCount) return false;

        lock (_sync)
        {
            if (_superResImages.TryGetValue(index, out var sr))
            {
                width = sr.Width;
                height = sr.Height;
                return true;
            }
        }

        using var lease = _renderer.AcquirePeek(index);
        if (lease != null)
        {
            width = lease.Image.Width;
            height = lease.Image.Height;
            return true;
        }

        return false;
    }

    public bool IsPageSuperRes(int index)
    {
        lock (_sync) return _superResImages.ContainsKey(index);
    }

    public bool IsPageSuperResBusy(int index)
    {
        lock (_sync) return _superResBusyPages.Contains(index);
    }

    public bool IsPageTranslating(int index)
    {
        lock (_sync) return _translatingPages.Contains(index);
    }

    public IReadOnlyList<OcrBlock> GetPageBlocks(int index)
    {
        lock (_sync)
        {
            return _pageBlocks.TryGetValue(index, out var list)
                ? list.ToList()
                : Array.Empty<OcrBlock>();
        }
    }

    public string? GetPageError(int index)
    {
        lock (_sync)
        {
            return _pageErrors.TryGetValue(index, out var err) ? err : null;
        }
    }

    /// <summary>清除指定頁的單頁超解析快取。</summary>
    public void ClearSinglePageSuperRes(int pageIndex)
    {
        lock (_sync)
        {
            if (_superResImages.Remove(pageIndex, out var old))
            {
                _superResLru.Remove(pageIndex);
                old.Dispose();
            }
        }
        PageUpdated?.Invoke(pageIndex);
    }

    /// <summary>使用指定超解析插件（例如 Anime4K 漫畫線條重繪）在背景對單頁進行深度重繪。</summary>
    public async Task<bool> RunSinglePageSuperResAsync(int pageIndex, IImageEnhancerPlugin plugin, CancellationToken ct = default)
    {
        if (_disposed || pageIndex < 0 || pageIndex >= PageCount) return false;
        ArgumentNullException.ThrowIfNull(plugin);

        lock (_sync)
        {
            if (!_superResBusyPages.Add(pageIndex))
                return false;
        }

        try
        {
            using var lease = await Task.Run(() => _renderer.AcquireOrProduce(pageIndex), ct).ConfigureAwait(false);
            if (lease == null || _disposed) return false;

            var enhanced = await plugin.EnhancePageAsync(lease.Image, ct).ConfigureAwait(false);
            if (enhanced == null || _disposed)
            {
                enhanced?.Dispose();
                return false;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    enhanced.Dispose();
                    return false;
                }

                if (_superResImages.Remove(pageIndex, out var prev))
                    prev.Dispose();
                _superResImages[pageIndex] = enhanced;
                TouchLru(_superResLru, pageIndex);

                while (_superResImages.Count > SuperResCacheCap && _superResLru.Last is { } last)
                {
                    int evict = last.Value;
                    _superResLru.RemoveLast();
                    if (_superResImages.Remove(evict, out var oldImg))
                        oldImg.Dispose();
                }

                // 若已有翻譯資料，清除舊的譯文合成圖以便下次以高解析底圖重新合成
                if (_translatedImages.Remove(pageIndex, out var oldTrans))
                {
                    _translatedLru.Remove(pageIndex);
                    oldTrans.Dispose();
                }
            }

            PageUpdated?.Invoke(pageIndex);
            return true;
        }
        finally
        {
            lock (_sync)
            {
                _superResBusyPages.Remove(pageIndex);
            }
        }
    }

    /// <summary>對指定頁執行 OCR 辨識、氣泡合併與 LLM 繁中翻譯並快取合成結果。</summary>
    public async Task EnsurePageTranslatedAsync(int pageIndex, bool forceReRun = false, CancellationToken ct = default)
    {
        if (_disposed || pageIndex < 0 || pageIndex >= PageCount) return;

        lock (_sync)
        {
            if (!forceReRun && (_translatedImages.ContainsKey(pageIndex) || _ocrAttemptedPages.Contains(pageIndex)))
                return;
            if (!_translatingPages.Add(pageIndex))
                return;
            if (forceReRun)
            {
                _ocrAttemptedPages.Remove(pageIndex);
                _pageBlocks.Remove(pageIndex);
                if (_translatedImages.Remove(pageIndex, out var old))
                {
                    _translatedLru.Remove(pageIndex);
                    old.Dispose();
                }
            }
            _pageErrors.Remove(pageIndex);
        }

        TranslationStatusChanged?.Invoke(pageIndex, "正在辨識對話氣泡 (OCR)...");

        try
        {
            using var lease = await Task.Run(() => _renderer.AcquireOrProduce(pageIndex), ct).ConfigureAwait(false);
            if (lease == null || _disposed)
            {
                TranslationStatusChanged?.Invoke(pageIndex, "頁面解碼失敗");
                return;
            }

            ISkiaOcrService? ocr;
            lock (_sync)
            {
                _ocrService ??= SkiaOcrPluginManager.CreateService(_ocrEngineType);
                ocr = _ocrService;
            }

            if (ocr == null)
            {
                SetPageError(pageIndex, "找不到可用的 OCR 引擎");
                TranslationStatusChanged?.Invoke(pageIndex, "找不到可用的 OCR 引擎");
                return;
            }

            List<OcrBlock> mergedBlocks;
            lock (_sync)
            {
                _pageBlocks.TryGetValue(pageIndex, out mergedBlocks!);
            }

            if (mergedBlocks == null || forceReRun)
            {
                byte[] encodedPng = await Task.Run(() =>
                {
                    using var data = lease.Image.Encode(SKEncodedImageFormat.Png, 90);
                    return data?.ToArray() ?? Array.Empty<byte>();
                }, ct).ConfigureAwait(false);

                var rawBlocks = await ocr.RecognizeEncodedAsync(encodedPng, _ocrSourceLanguage).ConfigureAwait(false);
                mergedBlocks = ocr.MergeBlocks(rawBlocks, isVerticalFlow: true, thresholdMultiplier: 1.0f);

                if (mergedBlocks.Count > 0)
                {
                    TranslationStatusChanged?.Invoke(pageIndex, $"偵測到 {mergedBlocks.Count} 個對話區塊，AI 翻譯中...");
                    var srcTexts = mergedBlocks.Select(b => b.OriginalText).ToList();
                    var translatedTexts = await _translationService
                        .TranslateBatchAsync(srcTexts, fromLanguage: "ja", toLanguage: _targetLanguage)
                        .ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(_translationService.LastError))
                    {
                        SetPageError(pageIndex, _translationService.LastError);
                    }

                    for (int i = 0; i < mergedBlocks.Count && i < translatedTexts.Count; i++)
                    {
                        mergedBlocks[i].Text = translatedTexts[i];
                    }
                }
            }

            // 以目前畫質最高之底圖（超解析優先，其次原始解碼圖）合成譯文氣泡圖
            SKImage baseForComposite = lease.Image;
            int maskEngineType;
            lock (_sync)
            {
                if (_superResImages.TryGetValue(pageIndex, out var srImg))
                    baseForComposite = srImg;
                maskEngineType = _maskEngineType;
            }

            bool hasTranslatedText = mergedBlocks.Count > 0 && mergedBlocks.Any(b => !string.IsNullOrWhiteSpace(b.Text));
            bool usePreciseMask = hasTranslatedText && maskEngineType == 2 && _textMaskProvider.IsAvailable;
            if (usePreciseMask)
            {
                TranslationStatusChanged?.Invoke(pageIndex, "正在偵測原文筆畫遮罩 (ONNX)...");
            }

            var composited = await Task.Run(() =>
            {
                if (!hasTranslatedText)
                {
                    return SkiaTranslationRenderer.RenderTranslatedPage(baseForComposite, Array.Empty<OcrBlock>());
                }

                byte[]? textMask = null;
                if (usePreciseMask)
                {
                    textMask = _textMaskProvider.GetTextMask(baseForComposite, mergedBlocks);
                }

                return SkiaTranslationRenderer.RenderTranslatedPage(baseForComposite, mergedBlocks, textMask);
            }, ct).ConfigureAwait(false);

            lock (_sync)
            {
                if (_disposed)
                {
                    if (!ReferenceEquals(composited, baseForComposite))
                        composited.Dispose();
                    return;
                }

                _ocrAttemptedPages.Add(pageIndex);
                _pageBlocks[pageIndex] = mergedBlocks;
                if (!ReferenceEquals(composited, baseForComposite))
                {
                    if (_translatedImages.Remove(pageIndex, out var oldImg))
                        oldImg.Dispose();
                    _translatedImages[pageIndex] = composited;
                    TouchLru(_translatedLru, pageIndex);

                    while (_translatedImages.Count > TranslatedCacheCap && _translatedLru.Last is { } last)
                    {
                        int evict = last.Value;
                        _translatedLru.RemoveLast();
                        if (_translatedImages.Remove(evict, out var oldTrans))
                            oldTrans.Dispose();
                    }
                }
            }

            int activeCount = mergedBlocks.Count(b => !string.IsNullOrWhiteSpace(b.Text));
            string summary = GetPageError(pageIndex) is { } err
                ? $"翻譯警告：{err}"
                : $"已完成翻譯（{activeCount}/{mergedBlocks.Count} 對話氣泡）";
            TranslationStatusChanged?.Invoke(pageIndex, summary);
            PageUpdated?.Invoke(pageIndex);
        }
        catch (OperationCanceledException)
        {
            TranslationStatusChanged?.Invoke(pageIndex, "翻譯已取消");
        }
        catch (Exception ex)
        {
            SetPageError(pageIndex, ex.Message);
            TranslationStatusChanged?.Invoke(pageIndex, $"翻譯失敗：{ex.Message}");
        }
        finally
        {
            lock (_sync)
            {
                _translatingPages.Remove(pageIndex);
            }
        }
    }

    private void SetPageError(int pageIndex, string message)
    {
        lock (_sync)
        {
            _pageErrors[pageIndex] = message;
        }
    }

    private static void TouchLru(LinkedList<int> lru, int pageIndex)
    {
        var node = lru.Find(pageIndex);
        if (node != null)
        {
            lru.Remove(node);
            lru.AddFirst(node);
        }
        else
        {
            lru.AddFirst(pageIndex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _renderer.Dispose();
        lock (_sync)
        {
            foreach (var img in _superResImages.Values) img.Dispose();
            _superResImages.Clear();
            _superResLru.Clear();

            foreach (var img in _translatedImages.Values) img.Dispose();
            _translatedImages.Clear();
            _translatedLru.Clear();

            (_ocrService as IDisposable)?.Dispose();
            _ocrService = null;

            (_textMaskProvider as IDisposable)?.Dispose();
        }
    }
}
