namespace ComicOrganizer.App.Models;

/// <summary>
/// 詳情側邊抽屜（DetailDrawerHost）寬度限制與內部文字折行寬度計算輔助類別。
/// </summary>
public static class DrawerWidthHelper
{
    /// <summary>未指定偏好時的預設抽屜寬度（px）。</summary>
    public const int DefaultDrawerWidth = 420;

    /// <summary>抽屜最小寬度（px）。</summary>
    public const int MinDrawerWidth = 340;

    /// <summary>宿主視窗左側保留給主畫廊的最小邊距（px）。</summary>
    public const int OwnerReservedMargin = 60;

    /// <summary>
    /// 依宿主視窗客戶區寬度夾住請求的抽屜寬度。
    /// 當 <paramref name="requestedWidth"/> 為 null 或 &lt;= 0 時採用 <see cref="DefaultDrawerWidth"/>，
    /// 上限為 <c>Math.Max(MinDrawerWidth, ownerClientWidth - OwnerReservedMargin)</c>。
    /// </summary>
    public static int ClampDrawerWidth(int? requestedWidth, int ownerClientWidth)
    {
        int target = requestedWidth is > 0 ? requestedWidth.Value : DefaultDrawerWidth;
        int maxWidth = Math.Max(MinDrawerWidth, ownerClientWidth - OwnerReservedMargin);
        return Math.Clamp(target, MinDrawerWidth, maxWidth);
    }

    /// <summary>
    /// 計算面板內部可折行文字的最大寬度。
    /// </summary>
    public static int ComputeContentWrapWidth(
        int containerClientWidth,
        int horizontalOverhead,
        int verticalScrollbarWidth = 0,
        int minWidth = 180)
    {
        return Math.Max(minWidth, containerClientWidth - horizontalOverhead - verticalScrollbarWidth);
    }
}
