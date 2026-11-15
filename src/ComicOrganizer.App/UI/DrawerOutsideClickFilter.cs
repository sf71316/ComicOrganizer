namespace ComicOrganizer.App.UI;

/// <summary>外部點擊訊息的目標視窗分類。</summary>
public enum DrawerClickTarget
{
    Drawer,
    Owner,
    OtherPopup,
}

/// <summary>對單筆視窗滑鼠訊息的過濾決策。</summary>
public readonly record struct DrawerMessageDecision(
    bool Consume,
    bool ShouldTriggerOutsideClick,
    bool ShouldDetach);

/// <summary>
/// 純狀態機：評估 Drawer 外部點擊過濾器對滑鼠訊息的消費、非同步關閉觸發與延遲卸除決策。
/// </summary>
public sealed class DrawerOutsideClickEvaluator
{
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int WM_NCLBUTTONDBLCLK = 0x00A3;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_RBUTTONDBLCLK = 0x0206;

    private const long PendingUpExpireMs = 5000;

    private bool _drawerClosed;
    private bool _detached;
    private bool _closeQueued;
    private bool _swallowLButtonUp;
    private bool _swallowRButtonUp;
    private bool _swallowNCLButtonUp;
    private bool _swallowContextMenu;
    private long _swallowStartedTick;

    public bool IsDrawerClosed => _drawerClosed;

    public bool HasPendingButtonUp(long nowTickMs) =>
        !IsPendingUpExpired(nowTickMs) &&
        (_swallowLButtonUp || _swallowRButtonUp || _swallowNCLButtonUp || _swallowContextMenu);

    public void MarkDrawerClosed() => _drawerClosed = true;

    public void MarkQueuedWorkCompleted() => _closeQueued = false;

    public void Reset()
    {
        _detached = true;
        ClearPendingUpFlags();
    }

    public static bool IsTrackedMessage(int msg) =>
        IsTrackedDownMessage(msg) || IsTrackedUpOrContextMessage(msg);

    public DrawerMessageDecision ProcessMessage(
        int msg,
        bool drawerDisposed,
        Func<DrawerClickTarget> resolveTarget,
        bool isPrompting,
        long nowTickMs)
    {
        if (_detached) return default;

        bool isDown = IsTrackedDownMessage(msg);
        bool isUpOrContext = IsTrackedUpOrContextMessage(msg);
        if (!isDown && !isUpOrContext) return default;

        if (IsPendingUpExpired(nowTickMs)) ClearPendingUpFlags();

        // Drawer 已關閉時：只負責吃掉上一次因點擊 Owner 而產生的成對 UP / WM_CONTEXTMENU，避免 tree/card 的 MouseUp 誤觸發
        if (_drawerClosed || drawerDisposed)
        {
            if (!HasPendingButtonUp(nowTickMs))
            {
                _detached = true;
                return new DrawerMessageDecision(false, false, true);
            }

            if (isDown)
            {
                ClearPendingUpFlags();
                _detached = true;
                return new DrawerMessageDecision(false, false, true);
            }

            var closedTarget = resolveTarget();
            bool consumedClosedUp = TryConsumePendingUp(msg, closedTarget == DrawerClickTarget.Owner);
            bool detachAfterUp = !HasPendingButtonUp(nowTickMs);
            if (detachAfterUp) _detached = true;
            return new DrawerMessageDecision(consumedClosedUp, false, detachAfterUp);
        }

        if (isUpOrContext)
        {
            if (!HasPendingButtonUp(nowTickMs)) return default;
            var upTarget = resolveTarget();
            bool consumedUp = TryConsumePendingUp(msg, upTarget == DrawerClickTarget.Owner);
            return new DrawerMessageDecision(consumedUp, false, false);
        }

        var target = resolveTarget();
        if (target != DrawerClickTarget.Owner) return default;

        MarkPendingUpForDown(msg, nowTickMs);

        bool shouldTrigger = false;
        if (!_closeQueued && !isPrompting)
        {
            _closeQueued = true;
            shouldTrigger = true;
        }

        return new DrawerMessageDecision(Consume: true, ShouldTriggerOutsideClick: shouldTrigger, ShouldDetach: false);
    }

    private static bool IsTrackedDownMessage(int msg) =>
        msg == WM_LBUTTONDOWN || msg == WM_LBUTTONDBLCLK ||
        msg == WM_RBUTTONDOWN || msg == WM_RBUTTONDBLCLK ||
        msg == WM_NCLBUTTONDOWN || msg == WM_NCLBUTTONDBLCLK;

    private static bool IsTrackedUpOrContextMessage(int msg) =>
        msg == WM_LBUTTONUP || msg == WM_RBUTTONUP ||
        msg == WM_NCLBUTTONUP || msg == WM_CONTEXTMENU;

    private void MarkPendingUpForDown(int msg, long nowTickMs)
    {
        _swallowStartedTick = nowTickMs;
        if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONDBLCLK)
        {
            _swallowLButtonUp = true;
        }
        else if (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONDBLCLK)
        {
            _swallowRButtonUp = true;
            _swallowContextMenu = true;
        }
        else if (msg == WM_NCLBUTTONDOWN || msg == WM_NCLBUTTONDBLCLK)
        {
            _swallowNCLButtonUp = true;
        }
    }

    private bool TryConsumePendingUp(int msg, bool isOwner)
    {
        switch (msg)
        {
            case WM_LBUTTONUP when _swallowLButtonUp:
                _swallowLButtonUp = false;
                return isOwner;
            case WM_RBUTTONUP when _swallowRButtonUp:
                _swallowRButtonUp = false;
                _swallowContextMenu = false;
                return isOwner;
            case WM_NCLBUTTONUP when _swallowNCLButtonUp:
                _swallowNCLButtonUp = false;
                return isOwner;
            case WM_CONTEXTMENU when _swallowContextMenu:
                _swallowContextMenu = false;
                return isOwner;
            default:
                return false;
        }
    }

    private bool IsPendingUpExpired(long nowTickMs) =>
        (_swallowLButtonUp || _swallowRButtonUp || _swallowNCLButtonUp || _swallowContextMenu) &&
        (nowTickMs - _swallowStartedTick > PendingUpExpireMs);

    private void ClearPendingUpFlags()
    {
        _swallowLButtonUp = false;
        _swallowRButtonUp = false;
        _swallowNCLButtonUp = false;
        _swallowContextMenu = false;
    }
}

/// <summary>
/// IMessageFilter：偵測滑鼠按鍵事件決定是否關閉 drawer。
/// AntdUI Drawer 是 layered window，不走標準 Form focus 模型 — 無法用 Form.Deactivate
/// 偵測「點到外面」，改在 application message loop 攔截滑鼠按鍵自己判斷。
///
/// 判定邏輯：依 click 目標所屬的 Form 區分 ——
///   點到 drawer 自身（含內部子控件） → 不關，正常傳遞
///   點到 drawer 的 Owner Form（主視窗 tree/tab/卡片）→ 消費該點擊（防穿透並阻擋對應 UP/ContextMenu）並透過 BeginInvoke 非同步要求關閉/確認
///   點到其他 popup form（AntdUI.Modal / ContextMenu / Tooltip / Combobox dropdown）→ 不關，正常傳遞
/// </summary>
public class DrawerOutsideClickFilter : System.Windows.Forms.IMessageFilter
{
    private readonly Form _drawer;
    private readonly Form? _owner;
    private readonly Action? _onOutsideClick;
    private readonly Func<bool>? _isPrompting;
    private readonly DrawerOutsideClickEvaluator _evaluator = new();

    private bool _removedFromApp;

    public DrawerOutsideClickFilter(
        Form drawer,
        Action? onOutsideClick = null,
        Func<bool>? isPrompting = null)
    {
        _drawer = drawer ?? throw new ArgumentNullException(nameof(drawer));
        _owner = drawer.Owner;
        _onOutsideClick = onOutsideClick;
        _isPrompting = isPrompting;
    }

    /// <summary>是否仍有待消費的對應放開滑鼠按鍵/右鍵選單訊息。</summary>
    public bool HasPendingButtonUp => _evaluator.HasPendingButtonUp(Environment.TickCount64);

    /// <summary>由 Drawer OnClose 呼叫，標記 drawer 已關閉（若仍在等待 UP 則待 UP 消費後自動卸除）。</summary>
    public void MarkDrawerClosed() => _evaluator.MarkDrawerClosed();

    /// <summary>立即卸除 message filter 並清除待吞 UP 狀態。</summary>
    public void Detach()
    {
        _evaluator.Reset();
        RemoveFromApplicationFilter();
    }

    public bool PreFilterMessage(ref System.Windows.Forms.Message m)
    {
        if (!DrawerOutsideClickEvaluator.IsTrackedMessage(m.Msg))
            return false;

        IntPtr hwnd = m.HWnd;
        var decision = _evaluator.ProcessMessage(
            m.Msg,
            _drawer.IsDisposed,
            () => ResolveTarget(hwnd),
            _isPrompting?.Invoke() ?? false,
            Environment.TickCount64);

        if (decision.ShouldTriggerOutsideClick)
        {
            ScheduleOutsideClickCallback();
        }

        if (decision.ShouldDetach)
        {
            ScheduleDetachSelf();
        }

        return decision.Consume;
    }

    private DrawerClickTarget ResolveTarget(IntPtr hwnd)
    {
        var owner = _owner ?? (_drawer.IsDisposed ? null : _drawer.Owner);
        var ctrl = Control.FromChildHandle(hwnd) ?? Control.FromHandle(hwnd);
        var clickForm = ctrl?.FindForm();

        if (clickForm == _drawer) return DrawerClickTarget.Drawer;
        if (owner != null && clickForm == owner) return DrawerClickTarget.Owner;
        return DrawerClickTarget.OtherPopup;
    }

    private void ScheduleOutsideClickCallback()
    {
        var owner = _owner ?? (_drawer.IsDisposed ? null : _drawer.Owner);
        Control? invokeTarget = (_drawer.IsHandleCreated && !_drawer.IsDisposed)
            ? _drawer
            : (owner != null && owner.IsHandleCreated && !owner.IsDisposed ? owner : null);

        if (invokeTarget == null)
        {
            _evaluator.MarkQueuedWorkCompleted();
            return;
        }

        try
        {
            invokeTarget.BeginInvoke(new Action(() =>
            {
                _evaluator.MarkQueuedWorkCompleted();
                if (_evaluator.IsDrawerClosed || _drawer.IsDisposed) return;
                if (_isPrompting?.Invoke() ?? false) return;
                if (_onOutsideClick != null) _onOutsideClick();
                else _drawer.Close();
            }));
        }
        catch
        {
            _evaluator.MarkQueuedWorkCompleted();
        }
    }

    private void ScheduleDetachSelf()
    {
        var owner = _owner ?? (_drawer.IsDisposed ? null : _drawer.Owner);
        if (owner != null && owner.IsHandleCreated && !owner.IsDisposed)
        {
            try
            {
                owner.BeginInvoke(new Action(RemoveFromApplicationFilter));
                return;
            }
            catch
            {
                // fallback
            }
        }
        RemoveFromApplicationFilter();
    }

    private void RemoveFromApplicationFilter()
    {
        if (_removedFromApp) return;
        _removedFromApp = true;
        Application.RemoveMessageFilter(this);
    }
}
