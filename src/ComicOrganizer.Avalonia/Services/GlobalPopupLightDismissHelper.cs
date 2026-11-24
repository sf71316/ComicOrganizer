using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>
/// 全域 Popup 輕量關閉輔助器：
/// 修補 Avalonia 12 + SukiUI 7.0.1（SukiWindow 僅自行關閉標題列 Flyout）下，
/// 頁面內 ComboBox 下拉選單、右鍵 ContextMenu 與輕量浮層在點擊視窗其他位置時未能穩定自動關閉的問題。
/// </summary>
public static class GlobalPopupLightDismissHelper
{
    private static readonly HashSet<Popup> OpenPopups = [];
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>(OnPopupIsOpenChanged);
        InputElement.PointerPressedEvent.AddClassHandler<Window>(
            OnWindowTunnelPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Window.IsActiveProperty.Changed.AddClassHandler<Window>(OnWindowIsActiveChanged);
    }

    internal static bool ShouldTrackPopup(Popup popup)
        => popup.IsLightDismissEnabled
           || popup.TemplatedParent is ComboBox or ContextMenu
           || popup.Child is ContextMenu;

    internal static bool ShouldDismissPopupOnPointerPress(Popup popup, object? eventSource)
    {
        if (!popup.IsOpen)
        {
            return false;
        }

        return ShouldDismissPopupCore(
            popup.Child,
            popup.TemplatedParent,
            popup.PlacementTarget as Visual,
            eventSource);
    }

    internal static bool ShouldDismissPopupCore(
        object? child,
        object? templatedParent,
        Visual? placementTarget,
        object? eventSource)
    {
        if (child is Visual childVisual && IsDescendantOrSelf(eventSource, childVisual))
        {
            return false;
        }

        // 點擊觸發 ComboBox 本體交由 ComboBox 自身切換開關，避免剛關掉又瞬間打開
        if (templatedParent is ComboBox combo && IsDescendantOrSelf(eventSource, combo))
        {
            return false;
        }

        if (templatedParent is ContextMenu menu && IsDescendantOrSelf(eventSource, menu))
        {
            return false;
        }

        // DslSearchBox 自動完成浮層等：點擊觸發目標控制項本身（如輸入框）時保持開啟
        if (templatedParent is not ComboBox
            && templatedParent is not ContextMenu
            && placementTarget is not null
            && IsDescendantOrSelf(eventSource, placementTarget))
        {
            return false;
        }

        return true;
    }

    internal static void DismissPopup(Popup popup)
    {
        if (popup.TemplatedParent is ComboBox combo)
        {
            combo.IsDropDownOpen = false;
        }
        else if (popup.TemplatedParent is ContextMenu menu)
        {
            menu.Close();
        }
        else if (popup.Child is ContextMenu childMenu)
        {
            childMenu.Close();
        }
        else
        {
            popup.Close();
        }
    }

    private static void OnPopupIsOpenChanged(Popup popup, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            if (ShouldTrackPopup(popup))
            {
                OpenPopups.Add(popup);
            }
        }
        else
        {
            OpenPopups.Remove(popup);
        }
    }

    private static void OnWindowTunnelPointerPressed(Window window, PointerPressedEventArgs e)
    {
        if (OpenPopups.Count == 0)
        {
            return;
        }

        var snapshot = OpenPopups.ToList();
        foreach (var popup in snapshot)
        {
            if (!popup.IsOpen)
            {
                OpenPopups.Remove(popup);
                continue;
            }

            if (!IsPopupOwnedByWindow(popup, window))
            {
                continue;
            }

            if (ShouldDismissPopupOnPointerPress(popup, e.Source))
            {
                DismissPopup(popup);
            }
        }
    }

    private static void OnWindowIsActiveChanged(Window window, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true || OpenPopups.Count == 0)
        {
            return;
        }

        var snapshot = OpenPopups.ToList();
        foreach (var popup in snapshot)
        {
            if (!popup.IsOpen)
            {
                OpenPopups.Remove(popup);
                continue;
            }

            if (!IsPopupOwnedByWindow(popup, window))
            {
                continue;
            }

            if (popup.Child is InputElement { IsPointerOver: true })
            {
                continue;
            }

            DismissPopup(popup);
        }
    }

    private static bool IsPopupOwnedByWindow(Popup popup, Window window)
    {
        Visual? anchor = popup.PlacementTarget
                         ?? (popup.TemplatedParent as Visual)
                         ?? (popup as Visual);
        if (anchor != null && TopLevel.GetTopLevel(anchor) is TopLevel tl)
        {
            return ReferenceEquals(tl, window);
        }

        return false;
    }

    private static bool IsDescendantOrSelf(object? source, object target)
    {
        if (source == null)
        {
            return false;
        }

        for (Visual? cur = source as Visual; cur != null; cur = cur.GetVisualParent())
        {
            if (ReferenceEquals(cur, target))
            {
                return true;
            }
        }

        for (ILogical? cur = source as ILogical; cur != null; cur = cur.LogicalParent)
        {
            if (ReferenceEquals(cur, target))
            {
                return true;
            }
        }

        return false;
    }
}
