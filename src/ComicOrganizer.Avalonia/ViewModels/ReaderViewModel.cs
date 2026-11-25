using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.App.UI;
using ComicOrganizer.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace ComicOrganizer.Avalonia.ViewModels;

public enum ReaderDrawerTab
{
    Thumbnails = 0,
    Bookmarks = 1,
    OcrLlm = 2,
    Enhancement = 3
}

public sealed partial class ReaderThumbnailItemVm : ObservableObject, IDisposable
{
    private readonly Action<int> _onSelect;
    private readonly Func<Action, Task> _runOnUiAsync;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isBookmarked;

    public Thickness ItemBorderThickness => IsCurrent ? new Thickness(2) : new Thickness(0);

    partial void OnIsCurrentChanged(bool value) => OnPropertyChanged(nameof(ItemBorderThickness));

    public ReaderThumbnailItemVm(
        int pageIndex,
        string pageName,
        Action<int> onSelect,
        Func<Action, Task> runOnUiAsync)
    {
        PageIndex = pageIndex;
        PageNumberOneBased = pageIndex + 1;
        PageName = string.IsNullOrWhiteSpace(pageName) ? $"第 {PageNumberOneBased} 頁" : pageName;
        _onSelect = onSelect;
        _runOnUiAsync = runOnUiAsync;
    }

    public int PageIndex { get; }
    public int PageNumberOneBased { get; }
    public string PageName { get; }
    public bool ThumbnailLoaded { get; private set; }

    public async Task SetThumbnailAsync(Bitmap? bmp)
    {
        ThumbnailLoaded = true;
        await _runOnUiAsync(() =>
        {
            var old = Thumbnail;
            Thumbnail = bmp;
            old?.Dispose();
        });
    }

    [RelayCommand]
    private void Select() => _onSelect(PageIndex);

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}

public sealed partial class ReaderBookmarkItemVm : ObservableObject
{
    private readonly Action<int> _onJump;
    private readonly Action<ReaderBookmarkItemVm> _onRemove;

    public ReaderBookmarkItemVm(
        int pageIndex,
        string label,
        long? bookmarkId,
        Action<int> onJump,
        Action<ReaderBookmarkItemVm> onRemove)
    {
        PageIndex = pageIndex;
        PageNumberOneBased = pageIndex + 1;
        _label = label;
        BookmarkId = bookmarkId;
        _onJump = onJump;
        _onRemove = onRemove;
    }

    public int PageIndex { get; }
    public int PageNumberOneBased { get; }
    public long? BookmarkId { get; }

    [ObservableProperty]
    private string _label;

    public string DisplayPageLabel => $"P.{PageNumberOneBased}";

    [RelayCommand]
    private void Jump() => _onJump(PageIndex);

    [RelayCommand]
    private void Remove() => _onRemove(this);
}

public sealed class ReaderOcrBlockItemVm
{
    public ReaderOcrBlockItemVm(int index, string originalText, string translatedText)
    {
        IndexLabel = $"#{index + 1}";
        OriginalText = string.IsNullOrWhiteSpace(originalText) ? "(空白)" : originalText;
        TranslatedText = string.IsNullOrWhiteSpace(translatedText) ? "（狀聲詞 / 已過濾留白）" : translatedText;
        IsFilteredNoise = string.IsNullOrWhiteSpace(translatedText);
    }

    public string IndexLabel { get; }
    public string OriginalText { get; }
    public string TranslatedText { get; }
    public bool IsFilteredNoise { get; }
}

public sealed record OcrEngineOptionVm(int EngineType, string DisplayName, bool IsAvailable, string StatusMessage)
{
    public string DisplayLabel => IsAvailable ? DisplayName : $"{DisplayName} (未就緒)";
    public override string ToString() => DisplayLabel;
}

/// <summary>
/// 獨立漫畫閱讀器視窗 ViewModel：
/// 管理頁面切換、檢視/縮放模式、連續長條頁距（標準/韓漫無縫）、OCR/LLM 翻譯狀態、
/// 組合 B 畫質優化參數微調、單頁超解析、側欄（縮圖/書籤/OCR/畫質＋底部翻頁滑桿）及 1.A 唯讀資料庫保護。
/// </summary>
public sealed partial class ReaderViewModel : ViewModelBase, IDisposable
{
    private readonly AppDataContext _dataContext;
    private readonly IReaderSource _source;
    private readonly ReaderPagePipeline _pipeline;
    private readonly long? _assetId;
    private readonly HashSet<int> _visitedPages = new();
    private readonly DateTime _sessionStartedAt = DateTime.UtcNow;
    private readonly SemaphoreSlim _thumbThrottle = new(2, 2);
    private readonly Func<Action, Task> _runOnUiAsync;

    private bool _readRecorded;
    private bool _suppressSliderSync;
    private bool _suppressOptionSync;
    private bool _disposed;

    public ReaderViewModel(
        AppDataContext dataContext,
        string sourcePath,
        string? displayTitleOverride = null,
        string? authorOverride = null,
        Func<Action, Task>? runOnUiAsync = null,
        int? initialPageZeroBasedOverride = null,
        (float X, float Y, float W, float H)? initialHighlightBox = null)
    {
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        _runOnUiAsync = runOnUiAsync ?? (action =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return Dispatcher.UIThread.InvokeAsync(action).GetTask();
        });

        _source = ReaderSourceFactory.Open(sourcePath, out int initialPageIndex);
        _pipeline = new ReaderPagePipeline(_source, _dataContext.Config);
        _pipeline.PageUpdated += OnPipelinePageUpdated;
        _pipeline.TranslationStatusChanged += OnTranslationStatusChanged;
        _dataContext.SettingsChanged += OnDataContextSettingsChanged;

        SourceRootPath = _source.RootPath;
        PageCount = _source.PageCount;
        MaxPageOneBased = Math.Max(1, PageCount);

        var metaView = TryLoadWorkDetail(SourceRootPath);
        _assetId = metaView?.AssetId ?? TryGetAssetId(SourceRootPath);

        WorkTitle = !string.IsNullOrWhiteSpace(displayTitleOverride)
            ? displayTitleOverride
            : (!string.IsNullOrWhiteSpace(metaView?.Title) ? metaView.Title : _source.DisplayName);

        var authorPart = !string.IsNullOrWhiteSpace(authorOverride)
            ? authorOverride
            : metaView?.AuthorMainName;
        var formatPart = Path.GetExtension(SourceRootPath);
        if (string.IsNullOrEmpty(formatPart)) formatPart = "資料夾";
        else formatPart = formatPart.TrimStart('.').ToUpperInvariant();

        WorkSubtitle = string.IsNullOrWhiteSpace(authorPart)
            ? $"{formatPart} • {PageCount} 頁"
            : $"{authorPart} • {formatPart} • {PageCount} 頁";

        IsReadOnlyDatabase = _dataContext.IsReadOnlyDatabase;

        // 恢復上次閱讀頁（若由單一圖片或圖搜漫指定頁碼進入則優先採用指定頁碼）
        if (initialPageZeroBasedOverride.HasValue && PageCount > 0)
        {
            initialPageIndex = initialPageZeroBasedOverride.Value;
        }
        else if (initialPageIndex == 0 && PageCount > 0)
        {
            int? savedPage = TryGetLastPage(SourceRootPath);
            if (savedPage is >= 0 && savedPage.Value < PageCount)
            {
                initialPageIndex = savedPage.Value;
            }
        }

        _currentPageIndex = PageCount > 0 ? Math.Clamp(initialPageIndex, 0, PageCount - 1) : 0;
        if (initialHighlightBox is { } hb)
        {
            InitialHighlightBox = (_currentPageIndex, hb.X, hb.Y, hb.W, hb.H);
        }
        _visitedPages.Add(_currentPageIndex);
        _sliderPageOneBased = PageCount > 0 ? _currentPageIndex + 1 : 1;

        ApplyUserPreferencesDefaults();
        InitializeOcrEngineOptions();
        LoadInitialBookmarks();
        BuildThumbnailList();
        SyncThumbnailSelection();
        RefreshBookmarkFlagForCurrentPage();
        RefreshOcrInspectorForCurrentPage();
        ApplyPresetInternal(ReaderImageEnhancementPreset.Off, syncOptions: true);
    }

    private void ApplyUserPreferencesDefaults()
    {
        var prefs = _dataContext.Preferences;
        IsRightToLeft = !string.Equals(prefs.DefaultReadingDirection, "ltr", StringComparison.OrdinalIgnoreCase);

        if (Enum.TryParse<ComicOrganizer.App.UI.ReaderViewMode>(prefs.DefaultReaderViewMode, ignoreCase: true, out var viewMode))
        {
            ViewMode = viewMode;
        }

        if (Enum.TryParse<ComicOrganizer.App.UI.ReaderZoomMode>(prefs.DefaultReaderZoomMode, ignoreCase: true, out var zoomMode))
        {
            ZoomMode = zoomMode;
        }
    }

    public AppDataContext DataContext => _dataContext;
    public ReaderPagePipeline Pipeline => _pipeline;
    public string SourceRootPath { get; }
    public int PageCount { get; }
    public int MaxPageOneBased { get; }
    public bool IsReadOnlyDatabase { get; }
    public (int PageIndex, float X, float Y, float W, float H)? InitialHighlightBox { get; }

    public string WorkTitle { get; }
    public string WorkSubtitle { get; }

    public string ReadOnlyBadgeText => IsReadOnlyDatabase ? "🔒 共用庫唯讀（進度/書籤僅存本機工作階段）" : "";
    public string LlmSummaryText =>
        $"LLM：{_dataContext.Config.TranslationModelName} ({_dataContext.Config.TranslationEndpoint})";
    public string OcrEngineSummaryText =>
        $"OCR：{SelectedOcrEngine?.DisplayLabel ?? "預設"}（可至系統設定調整）";

    public ObservableCollection<ReaderThumbnailItemVm> Thumbnails { get; } = new();
    public ObservableCollection<ReaderBookmarkItemVm> Bookmarks { get; } = new();
    public ObservableCollection<ReaderOcrBlockItemVm> CurrentPageOcrBlocks { get; } = new();
    public ObservableCollection<OcrEngineOptionVm> OcrEngineOptions { get; } = new();

    [ObservableProperty]
    private int _currentPageIndex;

    [ObservableProperty]
    private int _sliderPageOneBased = 1;

    [ObservableProperty]
    private ReaderViewMode _viewMode = ReaderViewMode.SinglePage;

    [ObservableProperty]
    private ReaderZoomMode _zoomMode = ReaderZoomMode.FitPage;

    [ObservableProperty]
    private int _zoomPercent = 100;

    [ObservableProperty]
    private bool _isRightToLeft = true;

    [ObservableProperty]
    private bool _isGaplessWebtoon;

    [ObservableProperty]
    private bool _isTranslationEnabled;

    [ObservableProperty]
    private bool _isComparingOriginal;

    [ObservableProperty]
    private OcrEngineOptionVm? _selectedOcrEngine;

    [ObservableProperty]
    private bool _isDrawerOpen;

    [ObservableProperty]
    private ReaderDrawerTab _activeDrawerTab = ReaderDrawerTab.Thumbnails;

    [ObservableProperty]
    private bool _isHudVisible = true;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _isCurrentPageBookmarked;

    [ObservableProperty]
    private string _translationStatusText = "AI 翻譯待機中（按 T 開啟）";

    [ObservableProperty]
    private ReaderImageEnhancementOptions _enhancementOptions =
        ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.Off);

    // 組合 B 畫質優化微調綁定欄位
    [ObservableProperty]
    private ReaderImageEnhancementPreset _activeEnhancementPreset = ReaderImageEnhancementPreset.Off;

    [ObservableProperty]
    private bool _highQualitySampling = true;

    [ObservableProperty]
    private double _sharpenAmount;

    [ObservableProperty]
    private double _lineCrispAmount;

    [ObservableProperty]
    private double _blackPoint;

    [ObservableProperty]
    private double _whitePoint = 1.0;

    [ObservableProperty]
    private double _contrast;

    [ObservableProperty]
    private double _gamma = 1.0;

    [ObservableProperty]
    private double _saturation = 1.0;

    [ObservableProperty]
    private double _brightnessCap = 1.0;

    [ObservableProperty]
    private double _warmPaperTint;

    public string PageCounterText => PageCount == 0
        ? "0 / 0"
        : (ViewMode == ReaderViewMode.DualPage && CurrentPageIndex + 1 < PageCount
            ? $"{CurrentPageIndex + 1}-{CurrentPageIndex + 2} / {PageCount}"
            : $"{CurrentPageIndex + 1} / {PageCount}");

    public string ZoomPercentText => $"{ZoomPercent}%";
    public string DirectionToggleLabel => IsRightToLeft ? "右翻 (RTL)" : "左翻 (LTR)";
    public string DirectionToggleTooltip => IsRightToLeft
        ? "目前：日漫右翻 (RTL) — 點擊切換美漫左翻 (LTR)"
        : "目前：美漫左翻 (LTR) — 點擊切換日漫右翻 (RTL)";
    public string ContinuousGapLabel => IsGaplessWebtoon ? "韓漫無縫 (0px)" : "頁距：標準 (8px)";
    public string ContinuousGapTooltip => IsGaplessWebtoon
        ? "目前：韓漫無縫 (0px) — 點擊切換標準頁距 (8px)"
        : "目前：標準頁距 (8px) — 點擊切換韓漫無縫緊貼 (0px)";
    public int ContinuousGapPx => IsGaplessWebtoon ? 0 : ContinuousLayout.PageGap;

    public bool IsSingleMode => ViewMode == ReaderViewMode.SinglePage;
    public bool IsDoubleMode => ViewMode == ReaderViewMode.DualPage;
    public bool IsContinuousMode => ViewMode == ReaderViewMode.ContinuousScroll;
    public bool ShowDirectionToggle => ViewMode != ReaderViewMode.ContinuousScroll;
    public bool ShowContinuousGapToggle => ViewMode == ReaderViewMode.ContinuousScroll;

    public bool IsFitPageMode => ZoomMode == ReaderZoomMode.FitPage;
    public bool IsFitWidthMode => ZoomMode == ReaderZoomMode.FitWidth;
    public bool IsActualZoomMode => ZoomMode == ReaderZoomMode.Actual;

    public bool IsThumbnailsTab => ActiveDrawerTab == ReaderDrawerTab.Thumbnails;
    public bool IsBookmarksTab => ActiveDrawerTab == ReaderDrawerTab.Bookmarks;
    public bool IsOcrTab => ActiveDrawerTab == ReaderDrawerTab.OcrLlm;
    public bool IsEnhancementTab => ActiveDrawerTab == ReaderDrawerTab.Enhancement;

    public int ActiveDrawerTabIndex
    {
        get => (int)ActiveDrawerTab;
        set
        {
            var next = (ReaderDrawerTab)Math.Clamp(value, 0, 3);
            if (ActiveDrawerTab != next)
                ActiveDrawerTab = next;
        }
    }

    public bool IsPresetOff => ActiveEnhancementPreset == ReaderImageEnhancementPreset.Off;
    public bool IsPresetMangaCrisp => ActiveEnhancementPreset == ReaderImageEnhancementPreset.MangaCrisp;
    public bool IsPresetVintageBleach => ActiveEnhancementPreset == ReaderImageEnhancementPreset.VintageBleach;
    public bool IsPresetColorVivid => ActiveEnhancementPreset == ReaderImageEnhancementPreset.ColorVivid;
    public bool IsPresetNightEyeCare => ActiveEnhancementPreset == ReaderImageEnhancementPreset.NightEyeCare;

    public string ActivePresetBadgeText => ReaderImageEnhancementOptions.GetPresetDisplayName(ActiveEnhancementPreset);
    public string BookmarkButtonText => IsCurrentPageBookmarked ? "🔖 已書籤" : "🔖 書籤";

    public event Action<int>? NavigationRequested;
    public event Action<ReaderViewMode>? ViewModeChanged;
    public event Action<ReaderZoomMode>? ZoomModeResetRequested;
    public event Action<float>? ZoomStepRequested;
    public event Action<bool>? FullscreenChanged;

    partial void OnCurrentPageIndexChanged(int value)
    {
        if (!_suppressSliderSync)
        {
            _suppressSliderSync = true;
            SliderPageOneBased = PageCount > 0 ? Math.Clamp(value + 1, 1, PageCount) : 1;
            _suppressSliderSync = false;
        }

        OnPropertyChanged(nameof(PageCounterText));
        SyncThumbnailSelection();
        RefreshBookmarkFlagForCurrentPage();
        RefreshOcrInspectorForCurrentPage();
        TrackPageVisit(value);
        EnsureNearbyThumbnailsLoaded(value);
    }

    partial void OnSliderPageOneBasedChanged(int value)
    {
        if (_suppressSliderSync || PageCount == 0) return;
        int idx = Math.Clamp(value - 1, 0, PageCount - 1);
        if (idx != CurrentPageIndex)
        {
            NavigationRequested?.Invoke(idx);
        }
    }

    partial void OnViewModeChanged(ReaderViewMode value)
    {
        OnPropertyChanged(nameof(IsSingleMode));
        OnPropertyChanged(nameof(IsDoubleMode));
        OnPropertyChanged(nameof(IsContinuousMode));
        OnPropertyChanged(nameof(ShowDirectionToggle));
        OnPropertyChanged(nameof(ShowContinuousGapToggle));
        OnPropertyChanged(nameof(PageCounterText));
        ViewModeChanged?.Invoke(value);
    }

    partial void OnZoomModeChanged(ReaderZoomMode value)
    {
        OnPropertyChanged(nameof(IsFitPageMode));
        OnPropertyChanged(nameof(IsFitWidthMode));
        OnPropertyChanged(nameof(IsActualZoomMode));
    }

    partial void OnZoomPercentChanged(int value) => OnPropertyChanged(nameof(ZoomPercentText));

    partial void OnIsRightToLeftChanged(bool value)
    {
        OnPropertyChanged(nameof(DirectionToggleLabel));
        OnPropertyChanged(nameof(DirectionToggleTooltip));
    }

    partial void OnIsGaplessWebtoonChanged(bool value)
    {
        OnPropertyChanged(nameof(ContinuousGapLabel));
        OnPropertyChanged(nameof(ContinuousGapTooltip));
        OnPropertyChanged(nameof(ContinuousGapPx));
    }

    partial void OnIsTranslationEnabledChanged(bool value)
    {
        if (value)
        {
            TranslationStatusText = "AI 翻譯已開啟，正在檢查當前頁面…";
            _ = _pipeline.EnsurePageTranslatedAsync(CurrentPageIndex);
        }
        else
        {
            TranslationStatusText = "AI 翻譯已關閉（僅顯示原圖）";
            IsComparingOriginal = false;
        }
    }

    partial void OnSelectedOcrEngineChanged(OcrEngineOptionVm? value)
    {
        if (value == null) return;
        _pipeline.SetOcrEngine(value.EngineType);
        TranslationStatusText = $"已切換 OCR 引擎：{value.DisplayName}";
        OnPropertyChanged(nameof(OcrEngineSummaryText));
    }

    partial void OnActiveDrawerTabChanged(ReaderDrawerTab value)
    {
        OnPropertyChanged(nameof(ActiveDrawerTabIndex));
        OnPropertyChanged(nameof(IsThumbnailsTab));
        OnPropertyChanged(nameof(IsBookmarksTab));
        OnPropertyChanged(nameof(IsOcrTab));
        OnPropertyChanged(nameof(IsEnhancementTab));

        if (value == ReaderDrawerTab.Thumbnails)
        {
            EnsureNearbyThumbnailsLoaded(CurrentPageIndex);
        }
        else if (value == ReaderDrawerTab.OcrLlm)
        {
            RefreshOcrInspectorForCurrentPage();
        }
    }

    partial void OnIsDrawerOpenChanged(bool value)
    {
        if (value && ActiveDrawerTab == ReaderDrawerTab.Thumbnails)
        {
            EnsureNearbyThumbnailsLoaded(CurrentPageIndex);
        }
    }

    partial void OnIsFullscreenChanged(bool value) => FullscreenChanged?.Invoke(value);

    partial void OnIsCurrentPageBookmarkedChanged(bool value) => OnPropertyChanged(nameof(BookmarkButtonText));

    partial void OnHighQualitySamplingChanged(bool value) => PushCustomEnhancementIfReady();
    partial void OnSharpenAmountChanged(double value) => PushCustomEnhancementIfReady();
    partial void OnLineCrispAmountChanged(double value) => PushCustomEnhancementIfReady();
    partial void OnBlackPointChanged(double value) => PushCustomEnhancementIfReady();
    partial void OnWhitePointChanged(double value) => PushCustomEnhancementIfReady();
    partial void OnContrastChanged(double value) => PushCustomEnhancementIfReady();
    partial void OnGammaChanged(double value) => PushCustomEnhancementIfReady();
    partial void OnSaturationChanged(double value) => PushCustomEnhancementIfReady();
    partial void OnBrightnessCapChanged(double value) => PushCustomEnhancementIfReady();
    partial void OnWarmPaperTintChanged(double value) => PushCustomEnhancementIfReady();

    public void NotifyViewportPageChanged(int pageIndex)
    {
        if (PageCount == 0) return;
        int clamped = Math.Clamp(pageIndex, 0, PageCount - 1);
        CurrentPageIndex = clamped;
    }

    public void NotifyViewportZoomChanged(int percent)
    {
        ZoomPercent = percent;
    }

    public void SyncEnhancementFromViewport(ReaderImageEnhancementOptions options)
    {
        if (options == null || _suppressOptionSync) return;
        _suppressOptionSync = true;
        try
        {
            EnhancementOptions = options;
            ActiveEnhancementPreset = options.Preset;
            HighQualitySampling = options.HighQualitySampling;
            SharpenAmount = Math.Round(options.SharpenAmount, 2);
            LineCrispAmount = Math.Round(options.LineCrispAmount, 2);
            BlackPoint = Math.Round(options.BlackPoint, 2);
            WhitePoint = Math.Round(options.WhitePoint, 2);
            Contrast = Math.Round(options.Contrast, 2);
            Gamma = Math.Round(options.Gamma, 2);
            Saturation = Math.Round(options.Saturation, 2);
            BrightnessCap = Math.Round(options.BrightnessCap, 2);
            WarmPaperTint = Math.Round(options.WarmPaperTint, 2);
            NotifyPresetBadgesChanged();
        }
        finally
        {
            _suppressOptionSync = false;
        }
    }

    public void OpenEnhancementTabInDrawer()
    {
        ActiveDrawerTab = ReaderDrawerTab.Enhancement;
        IsDrawerOpen = true;
    }

    public void PersistSessionProgressIfAllowed()
    {
        if (IsReadOnlyDatabase || PageCount == 0) return;
        try
        {
            _dataContext.MetaStore.SetLastPage(SourceRootPath, CurrentPageIndex);
            TryRecordReadIfThresholdReached(forceTimeCheck: true);
        }
        catch
        {
            // 不因單次寫入失敗影響閱讀器關閉
        }
    }

    [RelayCommand]
    private void SetViewMode(string mode)
    {
        ViewMode = mode switch
        {
            "DualPage" or "Double" => ReaderViewMode.DualPage,
            "ContinuousScroll" or "Continuous" => ReaderViewMode.ContinuousScroll,
            _ => ReaderViewMode.SinglePage
        };
    }

    [RelayCommand]
    private void ToggleDirection() => IsRightToLeft = !IsRightToLeft;

    [RelayCommand]
    private void ToggleContinuousGap() => IsGaplessWebtoon = !IsGaplessWebtoon;

    [RelayCommand]
    private void SetZoomMode(string mode)
    {
        ZoomMode = mode switch
        {
            "FitWidth" => ReaderZoomMode.FitWidth,
            "Actual" => ReaderZoomMode.Actual,
            _ => ReaderZoomMode.FitPage
        };
        ZoomModeResetRequested?.Invoke(ZoomMode);
    }

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomMode = ReaderZoomMode.Custom;
        ZoomStepRequested?.Invoke(0.15f);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomMode = ReaderZoomMode.Custom;
        ZoomStepRequested?.Invoke(-0.15f);
    }

    [RelayCommand]
    private void ToggleTranslation() => IsTranslationEnabled = !IsTranslationEnabled;

    [RelayCommand]
    private void ToggleCompareOriginal()
    {
        IsComparingOriginal = !IsComparingOriginal;
    }

    [RelayCommand]
    private void ForceRetranslateCurrentPage()
    {
        if (!IsTranslationEnabled)
            IsTranslationEnabled = true;
        _ = _pipeline.EnsurePageTranslatedAsync(CurrentPageIndex, forceReRun: true);
    }

    [RelayCommand]
    private void ToggleCurrentBookmark()
    {
        if (PageCount == 0) return;

        var existing = Bookmarks.FirstOrDefault(b => b.PageIndex == CurrentPageIndex);
        if (existing != null)
        {
            RemoveBookmarkInternal(existing);
        }
        else
        {
            long? newId = null;
            string label = $"第 {CurrentPageIndex + 1} 頁書籤";
            if (!IsReadOnlyDatabase && _assetId.HasValue)
            {
                try
                {
                    newId = _dataContext.MetaStore.AddBookmark(_assetId.Value, CurrentPageIndex, label);
                }
                catch
                {
                    newId = null;
                }
            }

            var vm = new ReaderBookmarkItemVm(
                CurrentPageIndex,
                label,
                newId,
                page => NavigationRequested?.Invoke(page),
                RemoveBookmarkInternal);

            InsertBookmarkSorted(vm);
            RefreshBookmarkFlagForCurrentPage();
        }
    }

    [RelayCommand]
    private void ToggleDrawer() => IsDrawerOpen = !IsDrawerOpen;

    [RelayCommand]
    private void ToggleDrawerOrThumbnails()
    {
        if (!IsDrawerOpen)
        {
            ActiveDrawerTab = ReaderDrawerTab.Thumbnails;
            IsDrawerOpen = true;
        }
        else
        {
            IsDrawerOpen = false;
        }
    }

    [RelayCommand]
    private void SelectDrawerTab(string tab)
    {
        ActiveDrawerTab = tab switch
        {
            "Bookmarks" => ReaderDrawerTab.Bookmarks,
            "OcrLlm" => ReaderDrawerTab.OcrLlm,
            "Enhancement" => ReaderDrawerTab.Enhancement,
            _ => ReaderDrawerTab.Thumbnails
        };
    }

    [RelayCommand]
    private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

    [RelayCommand]
    private void ToggleHud() => IsHudVisible = !IsHudVisible;

    [RelayCommand]
    private void ApplyEnhancementPreset(string presetName)
    {
        var preset = presetName switch
        {
            "MangaCrisp" => ReaderImageEnhancementPreset.MangaCrisp,
            "VintageBleach" => ReaderImageEnhancementPreset.VintageBleach,
            "ColorVivid" => ReaderImageEnhancementPreset.ColorVivid,
            "NightEyeCare" => ReaderImageEnhancementPreset.NightEyeCare,
            _ => ReaderImageEnhancementPreset.Off
        };
        ApplyPresetInternal(preset, syncOptions: true);
    }

    [RelayCommand]
    private void ResetEnhancement() => ApplyPresetInternal(ReaderImageEnhancementPreset.Off, syncOptions: true);

    [RelayCommand]
    private async Task RunSinglePageSuperResAsync()
    {
        var plugin = ImageEnhancerPluginManager.GetPlugins().FirstOrDefault(p => p.IsAvailable);
        if (plugin == null)
        {
            TranslationStatusText = "找不到可用的單頁超解析插件";
            return;
        }

        int page = CurrentPageIndex;
        TranslationStatusText = $"正在對第 {page + 1} 頁執行 {plugin.DisplayName}…";
        bool ok = await _pipeline.RunSinglePageSuperResAsync(page, plugin);
        TranslationStatusText = ok
            ? $"第 {page + 1} 頁已完成 Anime4K 超解析重繪"
            : $"第 {page + 1} 頁超解析重繪失敗或已在執行中";
    }

    [RelayCommand]
    private void ClearSinglePageSuperRes()
    {
        _pipeline.ClearSinglePageSuperRes(CurrentPageIndex);
        TranslationStatusText = $"已清除第 {CurrentPageIndex + 1} 頁之超解析重繪";
    }

    private void ApplyPresetInternal(ReaderImageEnhancementPreset preset, bool syncOptions)
    {
        var opts = ReaderImageEnhancementOptions.FromPreset(preset, HighQualitySampling);
        _suppressOptionSync = true;
        try
        {
            ActiveEnhancementPreset = preset;
            SharpenAmount = Math.Round(opts.SharpenAmount, 2);
            LineCrispAmount = Math.Round(opts.LineCrispAmount, 2);
            BlackPoint = Math.Round(opts.BlackPoint, 2);
            WhitePoint = Math.Round(opts.WhitePoint, 2);
            Contrast = Math.Round(opts.Contrast, 2);
            Gamma = Math.Round(opts.Gamma, 2);
            Saturation = Math.Round(opts.Saturation, 2);
            BrightnessCap = Math.Round(opts.BrightnessCap, 2);
            WarmPaperTint = Math.Round(opts.WarmPaperTint, 2);
            if (syncOptions)
            {
                EnhancementOptions = opts;
            }
            NotifyPresetBadgesChanged();
        }
        finally
        {
            _suppressOptionSync = false;
        }
    }

    private void PushCustomEnhancementIfReady()
    {
        if (_suppressOptionSync) return;

        var custom = new ReaderImageEnhancementOptions
        {
            Preset = ReaderImageEnhancementPreset.Custom,
            HighQualitySampling = HighQualitySampling,
            SharpenAmount = (float)SharpenAmount,
            LineCrispAmount = (float)LineCrispAmount,
            BlackPoint = (float)BlackPoint,
            WhitePoint = Math.Max((float)BlackPoint + 0.08f, (float)WhitePoint),
            Contrast = (float)Contrast,
            Gamma = (float)Gamma,
            Saturation = (float)Saturation,
            BrightnessCap = (float)BrightnessCap,
            WarmPaperTint = (float)WarmPaperTint
        };

        _suppressOptionSync = true;
        try
        {
            ActiveEnhancementPreset = ReaderImageEnhancementPreset.Custom;
            EnhancementOptions = custom;
            NotifyPresetBadgesChanged();
        }
        finally
        {
            _suppressOptionSync = false;
        }
    }

    private void NotifyPresetBadgesChanged()
    {
        OnPropertyChanged(nameof(IsPresetOff));
        OnPropertyChanged(nameof(IsPresetMangaCrisp));
        OnPropertyChanged(nameof(IsPresetVintageBleach));
        OnPropertyChanged(nameof(IsPresetColorVivid));
        OnPropertyChanged(nameof(IsPresetNightEyeCare));
        OnPropertyChanged(nameof(ActivePresetBadgeText));
    }

    private void InitializeOcrEngineOptions()
    {
        var engines = SkiaOcrPluginManager.GetAvailableEngines();
        OcrEngineOptions.Clear();
        foreach (var e in engines)
        {
            OcrEngineOptions.Add(new OcrEngineOptionVm(e.EngineType, e.Name, e.IsAvailable, e.StatusMessage));
        }

        int preferred = _dataContext.Config.OcrEngineType;
        var match = OcrEngineOptions.FirstOrDefault(o => o.EngineType == preferred && o.IsAvailable)
                    ?? OcrEngineOptions.FirstOrDefault(o => o.IsAvailable)
                    ?? OcrEngineOptions.FirstOrDefault();
        SelectedOcrEngine = match;
    }

    private void SyncOcrEngineFromConfig()
    {
        InitializeOcrEngineOptions();
        if (SelectedOcrEngine == null)
        {
            _pipeline.SetOcrEngine(_dataContext.Config.OcrEngineType);
        }

        OnPropertyChanged(nameof(OcrEngineSummaryText));
    }

    private void OnDataContextSettingsChanged()
    {
        if (_disposed)
        {
            return;
        }

        _ = _runOnUiAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            SyncOcrEngineFromConfig();
            OnPropertyChanged(nameof(LlmSummaryText));
        });
    }

    private void LoadInitialBookmarks()
    {
        Bookmarks.Clear();
        if (!_assetId.HasValue) return;

        try
        {
            var items = _dataContext.MetaStore.ListBookmarks(_assetId.Value);
            foreach (var b in items.OrderBy(x => x.PageIndex))
            {
                Bookmarks.Add(new ReaderBookmarkItemVm(
                    b.PageIndex,
                    string.IsNullOrWhiteSpace(b.Label) ? $"第 {b.PageIndex + 1} 頁書籤" : b.Label!,
                    b.Id,
                    page => NavigationRequested?.Invoke(page),
                    RemoveBookmarkInternal));
            }
        }
        catch
        {
            // 唯讀資料庫或找不到書籤時維持空白
        }
    }

    private void InsertBookmarkSorted(ReaderBookmarkItemVm item)
    {
        int insertAt = 0;
        while (insertAt < Bookmarks.Count && Bookmarks[insertAt].PageIndex < item.PageIndex)
            insertAt++;
        Bookmarks.Insert(insertAt, item);
    }

    private void RemoveBookmarkInternal(ReaderBookmarkItemVm item)
    {
        Bookmarks.Remove(item);
        if (!IsReadOnlyDatabase && _assetId.HasValue)
        {
            try
            {
                if (item.BookmarkId.HasValue)
                    _dataContext.MetaStore.DeleteBookmark(item.BookmarkId.Value);
                else
                    _dataContext.MetaStore.DeleteBookmarkByPage(_assetId.Value, item.PageIndex);
            }
            catch
            {
                // 忽略書籤刪除失敗
            }
        }
        RefreshBookmarkFlagForCurrentPage();
    }

    private void RefreshBookmarkFlagForCurrentPage()
    {
        IsCurrentPageBookmarked = Bookmarks.Any(b => b.PageIndex == CurrentPageIndex);
        foreach (var t in Thumbnails)
        {
            t.IsBookmarked = Bookmarks.Any(b => b.PageIndex == t.PageIndex);
        }
    }

    private void BuildThumbnailList()
    {
        foreach (var old in Thumbnails) old.Dispose();
        Thumbnails.Clear();

        for (int i = 0; i < PageCount; i++)
        {
            Thumbnails.Add(new ReaderThumbnailItemVm(
                i,
                _source.GetPageName(i),
                page => NavigationRequested?.Invoke(page),
                _runOnUiAsync));
        }
    }

    private void SyncThumbnailSelection()
    {
        foreach (var item in Thumbnails)
        {
            item.IsCurrent = item.PageIndex == CurrentPageIndex;
        }
    }

    private void EnsureNearbyThumbnailsLoaded(int centerIndex)
    {
        if (_disposed || !IsDrawerOpen || ActiveDrawerTab != ReaderDrawerTab.Thumbnails || PageCount == 0)
            return;

        int start = Math.Max(0, centerIndex - 6);
        int end = Math.Min(PageCount - 1, centerIndex + 10);

        for (int i = start; i <= end; i++)
        {
            var item = Thumbnails[i];
            if (item.ThumbnailLoaded) continue;
            _ = LoadThumbnailAsync(item);
        }
    }

    private async Task LoadThumbnailAsync(ReaderThumbnailItemVm item)
    {
        try
        {
            await _thumbThrottle.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed || item.ThumbnailLoaded) return;

            var bmp = await Task.Run(() =>
            {
                if (_disposed) return null;
                try
                {
                    using var decoded = SkiaPageRenderer.DecodeFromSource(_source, item.PageIndex);
                    if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0) return null;

                    const int maxW = 140;
                    const int maxH = 200;
                    float scale = Math.Min(1f, Math.Min((float)maxW / decoded.Width, (float)maxH / decoded.Height));
                    int outW = Math.Max(1, (int)Math.Round(decoded.Width * scale));
                    int outH = Math.Max(1, (int)Math.Round(decoded.Height * scale));

                    var info = new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using var surface = SKSurface.Create(info);
                    if (surface == null) return null;

                    surface.Canvas.Clear(SKColors.Transparent);
                    surface.Canvas.DrawImage(
                        decoded,
                        new SKRect(0, 0, outW, outH),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    surface.Canvas.Flush();

                    using var snap = surface.Snapshot();
                    using var data = snap.Encode(SKEncodedImageFormat.Jpeg, 82);
                    if (data == null) return null;

                    using var ms = new MemoryStream(data.ToArray(), writable: false);
                    return new Bitmap(ms);
                }
                catch
                {
                    return null;
                }
            }).ConfigureAwait(false);

            await item.SetThumbnailAsync(bmp).ConfigureAwait(false);
        }
        finally
        {
            try { _thumbThrottle.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private void OnPipelinePageUpdated(int pageIndex)
    {
        if (pageIndex == CurrentPageIndex)
        {
            _ = _runOnUiAsync(RefreshOcrInspectorForCurrentPage);
        }
    }

    private void OnTranslationStatusChanged(int pageIndex, string? status)
    {
        if (pageIndex != CurrentPageIndex || string.IsNullOrWhiteSpace(status)) return;
        _ = _runOnUiAsync(() =>
        {
            TranslationStatusText = status;
            RefreshOcrInspectorForCurrentPage();
        });
    }

    private void RefreshOcrInspectorForCurrentPage()
    {
        CurrentPageOcrBlocks.Clear();
        var blocks = _pipeline.GetPageBlocks(CurrentPageIndex);
        for (int i = 0; i < blocks.Count; i++)
        {
            CurrentPageOcrBlocks.Add(new ReaderOcrBlockItemVm(i, blocks[i].OriginalText, blocks[i].Text));
        }

        if (_pipeline.IsPageTranslating(CurrentPageIndex))
        {
            TranslationStatusText = $"第 {CurrentPageIndex + 1} 頁正在辨識與翻譯中…";
        }
        else if (_pipeline.GetPageError(CurrentPageIndex) is { } err)
        {
            TranslationStatusText = $"警告：{err}";
        }
        else if (blocks.Count > 0)
        {
            int active = blocks.Count(b => !string.IsNullOrWhiteSpace(b.Text));
            TranslationStatusText = $"第 {CurrentPageIndex + 1} 頁已辨識 {blocks.Count} 區塊（{active} 句翻譯）";
        }
    }

    private void TrackPageVisit(int pageIndex)
    {
        _visitedPages.Add(pageIndex);
        TryRecordReadIfThresholdReached(forceTimeCheck: false);
    }

    private void TryRecordReadIfThresholdReached(bool forceTimeCheck)
    {
        if (_readRecorded || IsReadOnlyDatabase) return;

        int pageThreshold = Math.Max(1, _dataContext.Config.ReadCountThresholdPages);
        int secThreshold = Math.Max(1, _dataContext.Config.ReadCountThresholdSeconds);
        bool reachedPages = _visitedPages.Count >= pageThreshold;
        bool reachedSeconds = forceTimeCheck && (DateTime.UtcNow - _sessionStartedAt).TotalSeconds >= secThreshold;

        if (!reachedPages && !reachedSeconds) return;

        _readRecorded = true;
        try
        {
            _dataContext.MetaStore.RecordRead(SourceRootPath);
        }
        catch
        {
            // 不中斷閱讀體驗
        }
    }

    private WorkDetail? TryLoadWorkDetail(string path)
    {
        try { return _dataContext.MetaStore.GetWorkDetail(path); }
        catch { return null; }
    }

    private long? TryGetAssetId(string path)
    {
        try { return _dataContext.MetaStore.GetAssetId(path); }
        catch { return null; }
    }

    private int? TryGetLastPage(string path)
    {
        try { return _dataContext.MetaStore.GetLastPage(path); }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        PersistSessionProgressIfAllowed();

        _dataContext.SettingsChanged -= OnDataContextSettingsChanged;
        _pipeline.PageUpdated -= OnPipelinePageUpdated;
        _pipeline.TranslationStatusChanged -= OnTranslationStatusChanged;
        _pipeline.Dispose();

        foreach (var t in Thumbnails)
            t.Dispose();
        Thumbnails.Clear();

        _source.Dispose();
        _thumbThrottle.Dispose();
    }
}
