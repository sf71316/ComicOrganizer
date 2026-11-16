using System.Drawing;

namespace ComicOrganizer.App.Models;

/// <summary>
/// 主視窗位置/大小防呆驗證、側邊欄分割距離夾擠與分頁循環切換索引計算輔助類別。
/// </summary>
public static class WindowLayoutHelper
{
    public const int MinWindowWidth = 800;
    public const int MinWindowHeight = 540;
    public const int MinSidebarWidth = 140;
    public const int MinGalleryContentWidth = 360;
    public const int DefaultSidebarWidth = 271;

    /// <summary>
    /// 驗證並夾擠上次儲存的視窗矩形：
    /// - <paramref name="width"/> / <paramref name="height"/> 缺失或 &lt;= 0 時回傳 null。
    /// - 依與 <paramref name="workingAreas"/> 交集面積挑選最佳螢幕（至少 Width &gt;= 80 且 Height &gt;= 60）；
    ///   若無相交螢幕（例如外接螢幕已拔除）或缺少 X/Y，改置中落於 <paramref name="primaryWorkingArea"/>。
    /// - 依目標工作區夾擠寬高與座標，確保視窗完全位於可用螢幕範圍內。
    /// </summary>
    public static Rectangle? ValidateWindowBounds(
        int? x,
        int? y,
        int? width,
        int? height,
        IReadOnlyList<Rectangle> workingAreas,
        Rectangle primaryWorkingArea,
        int minWidth = MinWindowWidth,
        int minHeight = MinWindowHeight)
    {
        if (!width.HasValue || !height.HasValue || width.Value <= 0 || height.Value <= 0)
            return null;

        Rectangle fallbackPrimary = primaryWorkingArea;
        if (fallbackPrimary.Width <= 0 || fallbackPrimary.Height <= 0)
        {
            fallbackPrimary = workingAreas?.FirstOrDefault(r => r.Width > 0 && r.Height > 0) ?? Rectangle.Empty;
            if (fallbackPrimary.Width <= 0 || fallbackPrimary.Height <= 0)
                return null;
        }

        Rectangle targetArea = fallbackPrimary;
        bool hasValidPosition = false;

        if (x.HasValue && y.HasValue && workingAreas != null && workingAreas.Count > 0)
        {
            var candidate = new Rectangle(
                x.Value,
                y.Value,
                Math.Max(minWidth, width.Value),
                Math.Max(minHeight, height.Value));

            long bestIntersectionArea = 0;
            foreach (var area in workingAreas)
            {
                if (area.Width <= 0 || area.Height <= 0) continue;
                var inter = Rectangle.Intersect(candidate, area);
                if (inter.Width >= 80 && inter.Height >= 60)
                {
                    long interArea = (long)inter.Width * inter.Height;
                    if (interArea > bestIntersectionArea)
                    {
                        bestIntersectionArea = interArea;
                        targetArea = area;
                        hasValidPosition = true;
                    }
                }
            }
        }

        int effectiveMinW = Math.Min(minWidth, targetArea.Width);
        int effectiveMinH = Math.Min(minHeight, targetArea.Height);
        int finalW = Math.Clamp(width.Value, effectiveMinW, targetArea.Width);
        int finalH = Math.Clamp(height.Value, effectiveMinH, targetArea.Height);

        int finalX;
        int finalY;
        if (hasValidPosition)
        {
            int maxX = targetArea.Right - finalW;
            int maxY = targetArea.Bottom - finalH;
            finalX = Math.Clamp(x!.Value, targetArea.Left, maxX);
            finalY = Math.Clamp(y!.Value, targetArea.Top, maxY);
        }
        else
        {
            finalX = targetArea.Left + (targetArea.Width - finalW) / 2;
            finalY = targetArea.Top + (targetArea.Height - finalH) / 2;
        }

        return new Rectangle(finalX, finalY, finalW, finalH);
    }

    /// <summary>
    /// 依容器寬度與兩側最小保留寬度夾擠左側導覽欄 SplitterDistance。
    /// </summary>
    public static int ClampSplitterDistance(
        int? savedDistance,
        int containerWidth,
        int panel1MinSize = MinSidebarWidth,
        int panel2MinSize = MinGalleryContentWidth,
        int defaultDistance = DefaultSidebarWidth)
    {
        int target = savedDistance is > 0 ? savedDistance.Value : defaultDistance;
        int maxDistance = Math.Max(panel1MinSize, containerWidth - panel2MinSize);
        return Math.Clamp(target, panel1MinSize, maxDistance);
    }

    /// <summary>
    /// 計算 Ctrl+Tab / Ctrl+Shift+Tab 循環切換的下一個分頁索引；分頁數 &lt;= 1 時回傳 -1。
    /// </summary>
    public static int GetNextTabIndex(int currentIndex, int tabCount, bool reverse)
    {
        if (tabCount <= 1) return -1;
        if (currentIndex < 0 || currentIndex >= tabCount)
            return reverse ? tabCount - 1 : 0;

        return reverse
            ? (currentIndex - 1 + tabCount) % tabCount
            : (currentIndex + 1) % tabCount;
    }
}
