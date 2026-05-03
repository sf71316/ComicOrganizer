namespace ComicOrganizer.App.Core;

/// <summary>
/// 連續捲動模式的版面計算器（#49 Phase 1.2c）。
/// 紀錄每頁 y/h，支援「lazy fill」— 未量測的頁用估計高度，量測過的頁用 viewport 寬縮放後的真實高。
/// 純計算 + 無 UI 依賴，可單測。
///
/// 使用流程：
///   var layout = new ContinuousLayout(pageCount, viewportW);
///   while decode 完成 page i: layout.SetMeasured(i, natW, natH);
///   scroll 變動: layout.FindPageAtY(scrollY) → 取當前頁
///   resize: layout.SetViewportWidth(newW);
///   user zoom: layout.SetZoom(0.75f);
/// </summary>
public class ContinuousLayout
{
    /// <summary>未量測頁的估計高 = ContentWidth × 此比例。漫畫頁常見 1.4。</summary>
    public const float DefaultAspect = 1.4f;

    /// <summary>頁與頁之間的預設間距 (px)。"無隔間" 模式時切到 0。</summary>
    public const int PageGap = 8;

    public const float MinZoom =0.2f;
    public const float MaxZoom = 1.2f;

    private readonly int _pageCount;
    private int _viewportW;
    private float _zoom = 1.0f;
    private int _gap = PageGap;

    private readonly int[] _yOffset;
    private readonly int[] _height;
    private readonly int[] _natW;        // 量測過的自然寬
    private readonly int[] _natH;        // 量測過的自然高
    private readonly bool[] _measured;

    public ContinuousLayout(int pageCount, int viewportW)
    {
        _pageCount = Math.Max(0, pageCount);
        _viewportW = Math.Max(1, viewportW);
        _yOffset = new int[_pageCount];
        _height = new int[_pageCount];
        _natW = new int[_pageCount];
        _natH = new int[_pageCount];
        _measured = new bool[_pageCount];
        Recalc();
    }

    public int PageCount => _pageCount;
    public int ViewportWidth => _viewportW;
    public float Zoom => _zoom;
    public int ContentWidth => Math.Max(1, (int)(_viewportW * _zoom));
    public int TotalHeight { get; private set; }

    /// <summary>頁間距 (px)。0 = 無隔間（圖片緊貼）。設值會 Recalc。</summary>
    public int Gap
    {
        get => _gap;
        set
        {
            var v = Math.Max(0, value);
            if (v == _gap) return;
            _gap = v;
            Recalc();
        }
    }

    public int GetPageY(int idx) =>
        idx < 0 || idx >= _pageCount ? 0 : _yOffset[idx];

    public int GetPageHeight(int idx) =>
        idx < 0 || idx >= _pageCount ? 0 : _height[idx];

    public bool IsMeasured(int idx) =>
        idx >= 0 && idx < _pageCount && _measured[idx];

    /// <summary>提供某頁的自然像素尺寸（decode 完成後呼叫）。觸發整體 Recalc。</summary>
    public void SetMeasured(int idx, int naturalW, int naturalH)
    {
        if (idx < 0 || idx >= _pageCount) return;
        if (naturalW <= 0 || naturalH <= 0) return;
        _natW[idx] = naturalW;
        _natH[idx] = naturalH;
        _measured[idx] = true;
        Recalc();
    }

    public void SetViewportWidth(int w)
    {
        w = Math.Max(1, w);
        if (w == _viewportW) return;
        _viewportW = w;
        Recalc();
    }

    public void SetZoom(float zoom)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(zoom - _zoom) < 0.001f) return;
        _zoom = zoom;
        Recalc();
    }

    /// <summary>找出哪一頁包含指定 y（用於決定捲動位置對應的當前頁）。</summary>
    public int FindPageAtY(int y)
    {
        if (_pageCount == 0) return -1;
        if (y <= 0) return 0;
        if (y >= TotalHeight) return _pageCount - 1;
        // binary search
        int lo = 0, hi = _pageCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            int pageEnd = _yOffset[mid] + _height[mid];
            if (y < pageEnd) hi = mid;
            else lo = mid + 1;
        }
        return lo;
    }

    /// <summary>給定可視範圍 [viewTop, viewBottom)，回傳涵蓋的第一頁與最後一頁 index。</summary>
    public (int first, int last) GetVisibleRange(int viewTop, int viewBottom)
    {
        if (_pageCount == 0) return (-1, -1);
        int first = FindPageAtY(viewTop);
        int last = FindPageAtY(Math.Max(viewTop, viewBottom - 1));
        return (first, last);
    }

    /// <summary>找下一頁的頂部 y（嚴格大於 currentY 的最近頁邊界）。
    /// 沒有下一頁回 TotalHeight。Smart paging 用。</summary>
    public int NextPageBoundary(int currentY)
    {
        for (int i = 0; i < _pageCount; i++)
        {
            if (_yOffset[i] > currentY) return _yOffset[i];
        }
        return TotalHeight;
    }

    /// <summary>找上一頁的頂部 y（嚴格小於 currentY 的最近頁邊界）。
    /// 沒有上一頁回 0。Smart paging 用。</summary>
    public int PrevPageBoundary(int currentY)
    {
        int prev = 0;
        for (int i = 0; i < _pageCount; i++)
        {
            if (_yOffset[i] >= currentY) return prev;
            prev = _yOffset[i];
        }
        return prev;
    }

    private void Recalc()
    {
        int y = 0;
        for (int i = 0; i < _pageCount; i++)
        {
            _yOffset[i] = y;
            int h;
            if (_measured[i] && _natW[i] > 0)
            {
                float scale = (float)ContentWidth / _natW[i];
                h = Math.Max(1, (int)(_natH[i] * scale));
            }
            else
            {
                h = Math.Max(1, (int)(ContentWidth * DefaultAspect));
            }
            _height[i] = h;
            y += h + _gap;
        }
        // 最後一頁不算 gap
        TotalHeight = _pageCount > 0 ? y - _gap : 0;
    }
}
