using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.UI;
using ComicOrganizer.Avalonia.Services;
using SkiaSharp;

namespace ComicOrganizer.Avalonia.Views.Controls;

/// <summary>
/// 高效能 GPU/Skia 漫畫閱讀畫布：
/// - 支援單頁、雙頁（RTL 右翻 / LTR 左翻）與連續長條捲動（標準 8px / 韓漫無縫 0px）。
/// - 支援適高、適寬、原圖 100% 及 Ctrl+滾輪任意縮放、超框拖曳平移。
/// - 整合組合 B 畫質優化引擎（Catmull-Rom 重取樣 + SkSL 即時濾鏡 + 右鍵選單預設/單頁超解析重繪）。
/// - 先合成翻譯氣泡遮罩再統一套用濾鏡（夜間護眼與原圖紙色零色差）。
/// </summary>
public sealed class SkiaComicViewport : Control
{
    private ReaderPagePipeline? _pipeline;
    private ContinuousLayout? _continuousLayout;

    private ReaderViewMode _viewMode = ReaderViewMode.SinglePage;
    private ReaderZoomMode _zoomMode = ReaderZoomMode.FitPage;
    private float? _manualZoomScale;
    private int _currentPageIndex;
    private bool _isRightToLeft = true;
    private int _continuousGapPx = ContinuousLayout.PageGap;
    private bool _isTranslationEnabled = true;
    private bool _isComparingOriginal;
    private ReaderImageEnhancementOptions _enhancementOptions =
        ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.Off);

    // 單/雙頁平移偏移（螢幕邏輯座標）與連續模式捲動偏移（DIP）
    private float _panX;
    private float _panY;
    private float _continuousScrollY;
    private int _effectiveZoomPercent = 100;

    // 滑鼠互動狀態
    private bool _isDragging;
    private Point _dragStartPoint;
    private Point _lastDragPoint;
    private float _dragMovedDistance;
    private double _wheelAccum;
    private DateTime _lastWheelTime = DateTime.MinValue;

    public SkiaComicViewport()
    {
        ClipToBounds = true;
        Focusable = true;
        BuildContextMenu();
    }

    public event Action<int>? CurrentPageChanged;
    public event Action<int>? ZoomPercentChanged;
    public event Action<ReaderImageEnhancementOptions>? EnhancementOptionsChanged;
    public event Action? TranslationToggledRequested;
    public event Action? CompareOriginalToggledRequested;
    public event Action? ForceRetranslateCurrentPageRequested;
    public event Action? CustomEnhancementDialogRequested;
    public event Action<string, string>? StatusNotificationRequested;

    public ReaderPagePipeline? Pipeline
    {
        get => _pipeline;
        set
        {
            if (ReferenceEquals(_pipeline, value)) return;
            if (_pipeline != null)
                _pipeline.PageUpdated -= OnPipelinePageUpdated;

            _pipeline = value;
            _continuousLayout = null;
            _panX = 0;
            _panY = 0;
            _continuousScrollY = 0;

            if (_pipeline != null)
            {
                _pipeline.PageUpdated += OnPipelinePageUpdated;
                EnsureContinuousLayout();
                _pipeline.SetCurrentPageAndPreload(_currentPageIndex, _viewMode);
            }

            InvalidateVisual();
        }
    }

    public int CurrentPageIndex => _currentPageIndex;
    public int EffectiveZoomPercent => _effectiveZoomPercent;
    public float? ManualZoomScale => _manualZoomScale;

    public ReaderViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value) return;
            _viewMode = value;
            _panX = 0;
            _panY = 0;

            if (_viewMode == ReaderViewMode.ContinuousScroll)
            {
                EnsureContinuousLayout();
                if (_continuousLayout != null && _pipeline != null)
                {
                    _continuousScrollY = _continuousLayout.GetPageY(_currentPageIndex);
                }
            }
            else if (_pipeline != null)
            {
                _pipeline.SetCurrentPageAndPreload(_currentPageIndex, _viewMode);
            }

            InvalidateVisual();
        }
    }

    public ReaderZoomMode ZoomMode
    {
        get => _zoomMode;
        set
        {
            _zoomMode = value;
            _manualZoomScale = null;
            _panX = 0;
            _panY = 0;
            InvalidateVisual();
        }
    }

    public bool IsRightToLeft
    {
        get => _isRightToLeft;
        set
        {
            if (_isRightToLeft == value) return;
            _isRightToLeft = value;
            InvalidateVisual();
        }
    }

    /// <summary>連續長條頁距（標準 8px 或韓漫無縫 0px）。</summary>
    public int ContinuousGapPx
    {
        get => _continuousGapPx;
        set
        {
            int normalized = Math.Max(0, value);
            if (_continuousGapPx == normalized) return;
            _continuousGapPx = normalized;
            if (_continuousLayout != null)
            {
                _continuousLayout.Gap = normalized;
                if (_viewMode == ReaderViewMode.ContinuousScroll)
                    _continuousScrollY = _continuousLayout.GetPageY(_currentPageIndex);
            }
            InvalidateVisual();
        }
    }

    public bool IsTranslationEnabled
    {
        get => _isTranslationEnabled;
        set
        {
            if (_isTranslationEnabled == value) return;
            _isTranslationEnabled = value;
            InvalidateVisual();
        }
    }

    public bool IsComparingOriginal
    {
        get => _isComparingOriginal;
        set
        {
            if (_isComparingOriginal == value) return;
            _isComparingOriginal = value;
            InvalidateVisual();
        }
    }

    public ReaderImageEnhancementOptions EnhancementOptions
    {
        get => _enhancementOptions;
        set
        {
            var next = value ?? ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.Off);
            if (_enhancementOptions == next) return;
            _enhancementOptions = next;
            EnhancementOptionsChanged?.Invoke(_enhancementOptions);
            InvalidateVisual();
        }
    }

    public void SetManualZoom(float scale)
    {
        _zoomMode = ReaderZoomMode.Custom;
        _manualZoomScale = Math.Clamp(scale, 0.25f, 4.0f);
        if (_viewMode == ReaderViewMode.ContinuousScroll && _continuousLayout != null)
        {
            _continuousLayout.SetZoom(Math.Clamp(_manualZoomScale.Value, ContinuousLayout.MinZoom, ContinuousLayout.MaxZoom));
        }
        UpdateZoomPercent((int)Math.Round(_manualZoomScale.Value * 100f));
        InvalidateVisual();
    }

    public void AdjustZoomByStep(float deltaScale)
    {
        float current = _manualZoomScale ?? (_effectiveZoomPercent / 100f);
        SetManualZoom(current + deltaScale);
    }

    public void ResetAutoZoom(ReaderZoomMode zoomMode)
    {
        _zoomMode = zoomMode == ReaderZoomMode.Custom ? ReaderZoomMode.FitPage : zoomMode;
        _manualZoomScale = null;
        _panX = 0;
        _panY = 0;
        if (_viewMode == ReaderViewMode.ContinuousScroll && _continuousLayout != null)
        {
            _continuousLayout.SetZoom(1.0f);
        }
        InvalidateVisual();
    }

    public void GoToPage(int pageIndex, bool resetPan = true)
    {
        if (_pipeline == null || _pipeline.PageCount == 0) return;
        int clamped = Math.Clamp(pageIndex, 0, _pipeline.PageCount - 1);

        if (_viewMode == ReaderViewMode.DualPage && clamped > 0 && clamped >= _pipeline.PageCount)
            clamped = _pipeline.PageCount - 1;

        bool changed = clamped != _currentPageIndex;
        _currentPageIndex = clamped;
        if (resetPan)
        {
            _panX = 0;
            _panY = 0;
        }

        if (_viewMode == ReaderViewMode.ContinuousScroll)
        {
            EnsureContinuousLayout();
            if (_continuousLayout != null)
            {
                _continuousScrollY = _continuousLayout.GetPageY(clamped);
            }
        }
        else
        {
            _pipeline.SetCurrentPageAndPreload(clamped, _viewMode);
        }

        if (changed)
            CurrentPageChanged?.Invoke(clamped);

        InvalidateVisual();
    }

    public void NextSpreadOrPage()
    {
        if (_pipeline == null || _pipeline.PageCount == 0) return;
        int step = _viewMode == ReaderViewMode.DualPage ? 2 : 1;
        int target = Math.Min(_pipeline.PageCount - 1, _currentPageIndex + step);
        if (target != _currentPageIndex) GoToPage(target);
    }

    public void PrevSpreadOrPage()
    {
        if (_pipeline == null || _pipeline.PageCount == 0) return;
        int step = _viewMode == ReaderViewMode.DualPage ? 2 : 1;
        int target = Math.Max(0, _currentPageIndex - step);
        if (target != _currentPageIndex) GoToPage(target);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (_continuousLayout != null && arranged.Width > 1)
        {
            _continuousLayout.SetViewportWidth((int)Math.Max(100, arranged.Width - 32));
        }
        return arranged;
    }

    private void OnPipelinePageUpdated(int pageIndex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_pipeline != null && _continuousLayout != null &&
                _pipeline.TryGetPageDimensions(pageIndex, out int w, out int h))
            {
                _continuousLayout.SetMeasured(pageIndex, w, h);
            }
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    private void EnsureContinuousLayout()
    {
        if (_pipeline == null) return;
        int viewW = Math.Max(320, (int)(Bounds.Width > 40 ? Bounds.Width - 32 : 820));
        if (_continuousLayout == null || _continuousLayout.PageCount != _pipeline.PageCount)
        {
            _continuousLayout = new ContinuousLayout(_pipeline.PageCount, viewW)
            {
                Gap = _continuousGapPx
            };
        }
        else
        {
            _continuousLayout.SetViewportWidth(viewW);
            _continuousLayout.Gap = _continuousGapPx;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        context.Custom(new ViewportDrawOperation(this, bounds));
    }

    private void RenderSkia(SKCanvas canvas, Rect bounds)
    {
        // 背景依主題或夜間模式自然襯托，漫畫本身色彩由 ReaderImageEnhancer 控制
        canvas.Clear(new SKColor(18, 19, 24));

        if (_pipeline == null || _pipeline.PageCount == 0)
        {
            DrawCenteredNotice(canvas, bounds, "沒有可顯示的漫畫頁面");
            return;
        }

        if (_viewMode == ReaderViewMode.ContinuousScroll)
        {
            RenderContinuous(canvas, bounds);
        }
        else if (_viewMode == ReaderViewMode.DualPage)
        {
            RenderDoubleSpread(canvas, bounds);
        }
        else
        {
            RenderSinglePage(canvas, bounds);
        }
    }

    private void RenderSinglePage(SKCanvas canvas, Rect bounds)
    {
        if (_pipeline == null) return;

        using var page = _pipeline.AcquireForDisplay(_currentPageIndex, _isTranslationEnabled, _isComparingOriginal);
        if (page.Image == null)
        {
            DrawCenteredNotice(canvas, bounds, $"載入第 {_currentPageIndex + 1} 頁中…");
            return;
        }

        var img = page.Image;
        float scale = ComputeScale(img.Width, img.Height, (float)bounds.Width, (float)bounds.Height);
        UpdateZoomPercent((int)Math.Round(scale * 100f));

        float drawW = img.Width * scale;
        float drawH = img.Height * scale;
        ClampPan(drawW, drawH, (float)bounds.Width, (float)bounds.Height);

        float left = ((float)bounds.Width - drawW) / 2f + _panX;
        float top = ((float)bounds.Height - drawH) / 2f + _panY;

        var dest = new SKRect(left, top, left + drawW, top + drawH);
        ReaderImageEnhancer.DrawEnhancedImage(canvas, img, dest, _enhancementOptions);
        DrawPageOverlayStatus(canvas, dest, page.IsSuperRes, _pipeline.IsPageTranslating(_currentPageIndex));
    }

    private void RenderDoubleSpread(SKCanvas canvas, Rect bounds)
    {
        if (_pipeline == null) return;

        int p1 = _currentPageIndex;
        int p2 = _currentPageIndex + 1;
        bool hasSecond = p2 < _pipeline.PageCount;

        using var firstPage = _pipeline.AcquireForDisplay(p1, _isTranslationEnabled, _isComparingOriginal);
        using var secondPage = hasSecond
            ? _pipeline.AcquireForDisplay(p2, _isTranslationEnabled, _isComparingOriginal)
            : default;

        if (firstPage.Image == null && secondPage.Image == null)
        {
            DrawCenteredNotice(canvas, bounds, $"載入第 {p1 + 1} 頁中…");
            return;
        }

        var imgA = firstPage.Image;
        var imgB = secondPage.Image;
        if (imgA != null && imgB != null)
        {
            const float spreadGap = 6f;
            float baseH = Math.Max(imgA.Height, imgB.Height);
            float scaleAtoBase = baseH / imgA.Height;
            float scaleBtoBase = baseH / imgB.Height;
            float totalBaseW = imgA.Width * scaleAtoBase + imgB.Width * scaleBtoBase + spreadGap;

            float scale = ComputeScale(totalBaseW, baseH, (float)bounds.Width, (float)bounds.Height);
            UpdateZoomPercent((int)Math.Round(scale * 100f));

            float wA = imgA.Width * scaleAtoBase * scale;
            float hA = imgA.Height * scaleAtoBase * scale;
            float wB = imgB.Width * scaleBtoBase * scale;
            float hB = imgB.Height * scaleBtoBase * scale;
            float totalW = wA + wB + spreadGap;
            float totalH = Math.Max(hA, hB);

            ClampPan(totalW, totalH, (float)bounds.Width, (float)bounds.Height);
            float startLeft = ((float)bounds.Width - totalW) / 2f + _panX;
            float centerY = ((float)bounds.Height - totalH) / 2f + _panY;

            // 日漫右翻 RTL：[p2][p1]；美漫左翻 LTR：[p1][p2]
            SKRect rectA, rectB;
            if (_isRightToLeft)
            {
                rectB = new SKRect(startLeft, centerY + (totalH - hB) / 2f, startLeft + wB, centerY + (totalH + hB) / 2f);
                rectA = new SKRect(rectB.Right + spreadGap, centerY + (totalH - hA) / 2f, rectB.Right + spreadGap + wA, centerY + (totalH + hA) / 2f);
            }
            else
            {
                rectA = new SKRect(startLeft, centerY + (totalH - hA) / 2f, startLeft + wA, centerY + (totalH + hA) / 2f);
                rectB = new SKRect(rectA.Right + spreadGap, centerY + (totalH - hB) / 2f, rectA.Right + spreadGap + wB, centerY + (totalH + hB) / 2f);
            }

            ReaderImageEnhancer.DrawEnhancedImage(canvas, imgA, rectA, _enhancementOptions);
            ReaderImageEnhancer.DrawEnhancedImage(canvas, imgB, rectB, _enhancementOptions);
            DrawPageOverlayStatus(canvas, rectA, firstPage.IsSuperRes, _pipeline.IsPageTranslating(p1));
            DrawPageOverlayStatus(canvas, rectB, secondPage.IsSuperRes, _pipeline.IsPageTranslating(p2));
        }
        else if (imgA != null)
        {
            float scale = ComputeScale(imgA.Width, imgA.Height, (float)bounds.Width, (float)bounds.Height);
            UpdateZoomPercent((int)Math.Round(scale * 100f));
            float drawW = imgA.Width * scale;
            float drawH = imgA.Height * scale;
            ClampPan(drawW, drawH, (float)bounds.Width, (float)bounds.Height);
            float left = ((float)bounds.Width - drawW) / 2f + _panX;
            float top = ((float)bounds.Height - drawH) / 2f + _panY;
            var dest = new SKRect(left, top, left + drawW, top + drawH);
            ReaderImageEnhancer.DrawEnhancedImage(canvas, imgA, dest, _enhancementOptions);
            DrawPageOverlayStatus(canvas, dest, firstPage.IsSuperRes, _pipeline.IsPageTranslating(p1));
        }
    }

    private void RenderContinuous(SKCanvas canvas, Rect bounds)
    {
        if (_pipeline == null) return;
        EnsureContinuousLayout();
        if (_continuousLayout == null) return;

        float viewH = (float)bounds.Height;
        float maxScroll = Math.Max(0f, _continuousLayout.TotalHeight - viewH + 40f);
        _continuousScrollY = Math.Clamp(_continuousScrollY, 0f, maxScroll);

        var (first, last) = _continuousLayout.GetVisibleRange((int)_continuousScrollY, (int)(_continuousScrollY + viewH));
        if (first < 0 || last < first) return;

        _pipeline.SetContinuousVisibleRange(first, last);
        UpdateZoomPercent((int)Math.Round(_continuousLayout.Zoom * 100f));

        float contentW = _continuousLayout.ContentWidth;
        float left = ((float)bounds.Width - contentW) / 2f;

        for (int i = first; i <= last; i++)
        {
            float pageY = _continuousLayout.GetPageY(i) - _continuousScrollY + 20f;
            float pageH = _continuousLayout.GetPageHeight(i);
            var dest = new SKRect(left, pageY, left + contentW, pageY + pageH);

            using var page = _pipeline.AcquireForDisplay(i, _isTranslationEnabled, _isComparingOriginal);
            if (page.Image != null)
            {
                if (!_continuousLayout.IsMeasured(i))
                {
                    _continuousLayout.SetMeasured(i, page.Image.Width, page.Image.Height);
                }

                ReaderImageEnhancer.DrawEnhancedImage(canvas, page.Image, dest, _enhancementOptions);
                DrawPageOverlayStatus(canvas, dest, page.IsSuperRes, _pipeline.IsPageTranslating(i));
            }
            else
            {
                using var placeholderPaint = new SKPaint { Color = new SKColor(32, 34, 42) };
                canvas.DrawRect(dest, placeholderPaint);
                DrawCenteredNotice(canvas, new Rect(dest.Left, dest.Top, dest.Width, dest.Height), $"載入第 {i + 1} 頁…");
            }
        }
    }

    private float ComputeScale(float contentW, float contentH, float viewW, float viewH)
    {
        if (contentW <= 0 || contentH <= 0) return 1f;
        if (_manualZoomScale.HasValue) return _manualZoomScale.Value;

        const float pad = 16f;
        float availW = Math.Max(40f, viewW - pad);
        float availH = Math.Max(40f, viewH - pad);

        return _zoomMode switch
        {
            ReaderZoomMode.FitWidth => availW / contentW,
            ReaderZoomMode.Actual => 1.0f,
            _ => Math.Min(availW / contentW, availH / contentH)
        };
    }

    private void ClampPan(float drawW, float drawH, float viewW, float viewH)
    {
        float maxPanX = Math.Max(0f, (drawW - viewW) / 2f + 24f);
        float maxPanY = Math.Max(0f, (drawH - viewH) / 2f + 24f);
        _panX = Math.Clamp(_panX, -maxPanX, maxPanX);
        _panY = Math.Clamp(_panY, -maxPanY, maxPanY);
    }

    private void UpdateZoomPercent(int nextPercent)
    {
        nextPercent = Math.Clamp(nextPercent, 10, 500);
        if (_effectiveZoomPercent == nextPercent) return;
        _effectiveZoomPercent = nextPercent;
        Dispatcher.UIThread.Post(() => ZoomPercentChanged?.Invoke(nextPercent), DispatcherPriority.Background);
    }

    private static void DrawCenteredNotice(SKCanvas canvas, Rect rect, string text)
    {
        using var font = new SKFont(SkiaTranslationRenderer.TranslationTypeface, 15f);
        using var paint = new SKPaint { Color = new SKColor(180, 185, 200), IsAntialias = true };
        float cx = (float)(rect.X + rect.Width / 2);
        float cy = (float)(rect.Y + rect.Height / 2);
        canvas.DrawText(text, cx, cy, SKTextAlign.Center, font, paint);
    }

    private static void DrawPageOverlayStatus(SKCanvas canvas, SKRect dest, bool isSuperRes, bool isTranslating)
    {
        if (!isSuperRes && !isTranslating) return;

        string badge = isTranslating
            ? "⏳ 正在 OCR + AI 翻譯…"
            : "⚡ Anime4K 超解析重繪";

        using var bg = new SKPaint { Color = new SKColor(15, 23, 42, 205), IsAntialias = true };
        using var font = new SKFont(SkiaTranslationRenderer.TranslationTypeface, 12f);
        using var fg = new SKPaint { Color = isTranslating ? new SKColor(147, 197, 253) : new SKColor(110, 231, 183), IsAntialias = true };

        float textW = font.MeasureText(badge);
        var pill = new SKRect(dest.Left + 10, dest.Top + 10, dest.Left + 26 + textW, dest.Top + 34);
        canvas.DrawRoundRect(pill, 12, 12, bg);
        canvas.DrawText(badge, pill.Left + 8, pill.Bottom - 7, SKTextAlign.Left, font, fg);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_pipeline == null || _pipeline.PageCount == 0) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl)
        {
            float factor = e.Delta.Y > 0 ? 1.12f : 1f / 1.12f;
            float baseScale = _manualZoomScale ?? (_effectiveZoomPercent / 100f);
            SetManualZoom(baseScale * factor);
            e.Handled = true;
            return;
        }

        if (_viewMode == ReaderViewMode.ContinuousScroll)
        {
            EnsureContinuousLayout();
            if (_continuousLayout != null)
            {
                float maxScroll = Math.Max(0f, _continuousLayout.TotalHeight - (float)Bounds.Height + 40f);
                _continuousScrollY = Math.Clamp(_continuousScrollY - (float)(e.Delta.Y * 110.0), 0f, maxScroll);

                int centerPage = _continuousLayout.FindPageAtY((int)(_continuousScrollY + Bounds.Height * 0.35));
                if (centerPage >= 0 && centerPage != _currentPageIndex)
                {
                    _currentPageIndex = centerPage;
                    CurrentPageChanged?.Invoke(centerPage);
                }
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        // 單頁/雙頁模式：若頁面放大超過視窗高度，先滾動垂直位置；抵達頂/底邊界或適配畫面時觸發翻頁
        if (TryVerticalScrollSingleSpread((float)(e.Delta.Y * 110.0)))
        {
            e.Handled = true;
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastWheelTime).TotalMilliseconds > 450 || Math.Sign(e.Delta.Y) != Math.Sign(_wheelAccum))
            _wheelAccum = 0;

        _lastWheelTime = now;
        _wheelAccum += e.Delta.Y;

        if (_wheelAccum <= -0.8)
        {
            _wheelAccum = 0;
            NextSpreadOrPage();
        }
        else if (_wheelAccum >= 0.8)
        {
            _wheelAccum = 0;
            PrevSpreadOrPage();
        }

        e.Handled = true;
    }

    private bool TryVerticalScrollSingleSpread(float deltaY)
    {
        if (_pipeline == null) return false;
        using var page = _pipeline.AcquireForDisplay(_currentPageIndex, _isTranslationEnabled, _isComparingOriginal);
        if (page.Image == null) return false;

        float scale = ComputeScale(page.Image.Width, page.Image.Height, (float)Bounds.Width, (float)Bounds.Height);
        float drawH = page.Image.Height * scale;
        float viewH = (float)Bounds.Height;
        if (drawH <= viewH + 4f) return false;

        float maxPanY = (drawH - viewH) / 2f + 24f;
        float nextPanY = Math.Clamp(_panY + deltaY, -maxPanY, maxPanY);
        if (Math.Abs(nextPanY - _panY) > 0.5f)
        {
            _panY = nextPanY;
            InvalidateVisual();
            return true;
        }

        return false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed || props.IsMiddleButtonPressed)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this);
            _lastDragPoint = _dragStartPoint;
            _dragMovedDistance = 0;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;

        var pt = e.GetPosition(this);
        float dx = (float)(pt.X - _lastDragPoint.X);
        float dy = (float)(pt.Y - _lastDragPoint.Y);
        _lastDragPoint = pt;
        _dragMovedDistance += MathF.Sqrt(dx * dx + dy * dy);

        if (_viewMode == ReaderViewMode.ContinuousScroll && _continuousLayout != null)
        {
            float maxScroll = Math.Max(0f, _continuousLayout.TotalHeight - (float)Bounds.Height + 40f);
            _continuousScrollY = Math.Clamp(_continuousScrollY - dy, 0f, maxScroll);
            int centerPage = _continuousLayout.FindPageAtY((int)(_continuousScrollY + Bounds.Height * 0.35));
            if (centerPage >= 0 && centerPage != _currentPageIndex)
            {
                _currentPageIndex = centerPage;
                CurrentPageChanged?.Invoke(centerPage);
            }
            InvalidateVisual();
        }
        else
        {
            _panX += dx;
            _panY += dy;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging) return;
        _isDragging = false;

        // 純點擊（無拖曳）時於單頁/雙頁左右邊緣點擊快速翻頁（依 RTL/LTR 方向）
        if (_dragMovedDistance < 6f &&
            e.InitialPressMouseButton == MouseButton.Left &&
            _viewMode != ReaderViewMode.ContinuousScroll &&
            Bounds.Width > 0)
        {
            var pt = e.GetPosition(this);
            double ratio = pt.X / Bounds.Width;
            if (ratio <= 0.22)
            {
                if (_isRightToLeft) NextSpreadOrPage();
                else PrevSpreadOrPage();
            }
            else if (ratio >= 0.78)
            {
                if (_isRightToLeft) PrevSpreadOrPage();
                else NextSpreadOrPage();
            }
        }
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) => PopulateContextMenu(menu);
        ContextMenu = menu;
    }

    private void PopulateContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();

        var enhanceHeader = new MenuItem { Header = "✨ 漫畫畫質優化預設 (組合 B)", IsEnabled = false };
        menu.Items.Add(enhanceHeader);

        AddPresetItem(menu, ReaderImageEnhancementPreset.Off, "原圖關閉（僅高品質取樣）");
        AddPresetItem(menu, ReaderImageEnhancementPreset.MangaCrisp, "✨ 漫畫清晰增強（銳化＋墨線壓實）");
        AddPresetItem(menu, ReaderImageEnhancementPreset.VintageBleach, "📜 老舊掃描漂白（強去泛黃＋對比）");
        AddPresetItem(menu, ReaderImageEnhancementPreset.ColorVivid, "🎨 彩漫生動增艷（鮮明增色＋清晰）");
        AddPresetItem(menu, ReaderImageEnhancementPreset.NightEyeCare, "🌙 夜間護眼降亮（暖色柔光＋零氣泡色差）");

        var hqItem = new MenuItem
        {
            Header = "高品質重取樣 (Catmull-Rom 抗摩爾紋)",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _enhancementOptions.HighQualitySampling
        };
        hqItem.Click += (_, _) =>
        {
            EnhancementOptions = _enhancementOptions with
            {
                HighQualitySampling = !_enhancementOptions.HighQualitySampling
            };
        };
        menu.Items.Add(hqItem);

        var customItem = new MenuItem { Header = "🎛️ 自訂畫質參數微調（銳化 / 黑白點 / Gamma）…" };
        customItem.Click += (_, _) => CustomEnhancementDialogRequested?.Invoke();
        menu.Items.Add(customItem);

        menu.Items.Add(new Separator());

        // 插件式 AI / 單頁深度超解析度重繪
        var plugins = ImageEnhancerPluginManager.GetPlugins();
        bool hasSr = _pipeline?.IsPageSuperRes(_currentPageIndex) ?? false;
        foreach (var plugin in plugins)
        {
            var srItem = new MenuItem
            {
                Header = $"{plugin.DisplayName}（第 {_currentPageIndex + 1} 頁）",
                IsEnabled = plugin.IsAvailable && _pipeline != null && !_pipeline.IsPageSuperResBusy(_currentPageIndex)
            };
            srItem.Click += async (_, _) =>
            {
                if (_pipeline == null) return;
                int targetPage = _currentPageIndex;
                StatusNotificationRequested?.Invoke("超解析重繪", $"正在為第 {targetPage + 1} 頁執行 {plugin.DisplayName}…");
                bool ok = await _pipeline.RunSinglePageSuperResAsync(targetPage, plugin);
                StatusNotificationRequested?.Invoke(
                    "超解析重繪",
                    ok ? $"第 {targetPage + 1} 頁已完成深度線條超解析重繪。" : $"第 {targetPage + 1} 頁超解析重繪未執行或失敗。");
            };
            menu.Items.Add(srItem);
        }

        if (hasSr)
        {
            var clearSrItem = new MenuItem { Header = $"🗑️ 清除本頁（第 {_currentPageIndex + 1} 頁）超解析重繪" };
            clearSrItem.Click += (_, _) => _pipeline?.ClearSinglePageSuperRes(_currentPageIndex);
            menu.Items.Add(clearSrItem);
        }

        menu.Items.Add(new Separator());

        var toggleTrans = new MenuItem
        {
            Header = "🌐 AI 漫畫翻譯 (T)",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _isTranslationEnabled
        };
        toggleTrans.Click += (_, _) => TranslationToggledRequested?.Invoke();
        menu.Items.Add(toggleTrans);

        var compareItem = new MenuItem
        {
            Header = "👁️ 暫時顯示原文對照 (Space)",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _isComparingOriginal
        };
        compareItem.Click += (_, _) => CompareOriginalToggledRequested?.Invoke();
        menu.Items.Add(compareItem);

        var retranslateItem = new MenuItem { Header = $"🔄 重新辨識並翻譯本頁（第 {_currentPageIndex + 1} 頁）" };
        retranslateItem.Click += (_, _) => ForceRetranslateCurrentPageRequested?.Invoke();
        menu.Items.Add(retranslateItem);
    }

    private void AddPresetItem(ContextMenu menu, ReaderImageEnhancementPreset preset, string label)
    {
        var item = new MenuItem
        {
            Header = label,
            ToggleType = MenuItemToggleType.Radio,
            GroupName = "EnhancementPresets",
            IsChecked = _enhancementOptions.Preset == preset
        };
        item.Click += (_, _) =>
        {
            EnhancementOptions = ReaderImageEnhancementOptions.FromPreset(preset, _enhancementOptions.HighQualitySampling);
        };
        menu.Items.Add(item);
    }

    private sealed class ViewportDrawOperation : ICustomDrawOperation
    {
        private readonly SkiaComicViewport _owner;
        public Rect Bounds { get; }

        public ViewportDrawOperation(SkiaComicViewport owner, Rect bounds)
        {
            _owner = owner;
            Bounds = bounds;
        }

        public void Dispose() { }

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            _owner.RenderSkia(canvas, Bounds);
            canvas.Restore();
        }
    }
}
