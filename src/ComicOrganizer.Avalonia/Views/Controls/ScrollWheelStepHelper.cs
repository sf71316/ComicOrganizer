using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ComicOrganizer.Avalonia.Views.Controls;

/// <summary>
/// 為虛擬化大卡片清單（畫廊與探索牆）調整滑鼠滾輪單格捲動距離：
/// Avalonia ScrollViewer 預設每格僅捲動 ~48 DIP，對於高度 320–380 DIP 的卡片顯得太慢。
/// 以 Tunnel 階段攔截垂直滾輪事件（未按修飾鍵時），依設定的 DIP 步幅平滑推進 Offset.Y。
/// </summary>
public static class ScrollWheelStepHelper
{
    public const int DefaultWheelScrollDip = 200;
    public const int MinWheelScrollDip = 100;
    public const int MaxWheelScrollDip = 400;

    public static int ClampStepDip(int dip)
        => Math.Clamp(dip, MinWheelScrollDip, MaxWheelScrollDip);

    /// <summary>將設定值正規化為有效滾輪距離（未設定或 &lt;= 0 時回傳預設值 200 DIP）。</summary>
    public static int ClampOrDefault(int dip)
        => ClampStepDip(dip > 0 ? dip : DefaultWheelScrollDip);

    /// <summary>
    /// 計算套用滾輪步幅後的目標垂直捲動位置；若條件不適用或已在邊界上無法再捲動，回傳 <c>false</c> 讓系統預設行為處理。
    /// </summary>
    public static bool TryComputeNextVerticalOffset(
        double currentY,
        double deltaY,
        double extentHeight,
        double viewportHeight,
        double stepDip,
        KeyModifiers modifiers,
        out double nextY)
    {
        nextY = currentY;
        if (modifiers != KeyModifiers.None
            || stepDip <= 0
            || Math.Abs(deltaY) < 1e-6)
        {
            return false;
        }

        double maxY = Math.Max(0d, extentHeight - viewportHeight);
        if (maxY <= 0d)
        {
            return false;
        }

        double clampedStep = Math.Clamp(stepDip, MinWheelScrollDip, MaxWheelScrollDip);
        double candidate = Math.Clamp(currentY - deltaY * clampedStep, 0d, maxY);
        if (Math.Abs(candidate - currentY) < 0.1d)
        {
            return false;
        }

        nextY = candidate;
        return true;
    }

    public static void Attach(ScrollViewer? scrollViewer, Func<double> getStepDip)
    {
        if (scrollViewer is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(getStepDip);

        scrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            (_, e) =>
            {
                if (e.Handled)
                {
                    return;
                }

                // 僅攔截純垂直（或垂直為主）滾輪；保留 Horizontal / Ctrl+Wheel 給預設行為
                if (Math.Abs(e.Delta.Y) < Math.Abs(e.Delta.X))
                {
                    return;
                }

                if (TryComputeNextVerticalOffset(
                        scrollViewer.Offset.Y,
                        e.Delta.Y,
                        scrollViewer.Extent.Height,
                        scrollViewer.Viewport.Height,
                        getStepDip(),
                        e.KeyModifiers,
                        out double nextY))
                {
                    scrollViewer.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(scrollViewer.Offset.X, nextY));
                    e.Handled = true;
                }
            },
            RoutingStrategies.Tunnel);
    }
}
