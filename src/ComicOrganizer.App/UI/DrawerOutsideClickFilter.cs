namespace ComicOrganizer.App.UI;

/// <summary>
/// IMessageFilter：偵測滑鼠按鍵事件決定是否關閉 drawer。
/// AntdUI Drawer 是 layered window，不走標準 Form focus 模型 — 無法用 Form.Deactivate
/// 偵測「點到外面」，改在 application message loop 攔截 WM_LBUTTONDOWN 自己判斷。
///
/// 判定邏輯：依 click 目標所屬的 Form 區分 ——
///   點到 drawer 自身（含內部子控件） → 不關
///   點到 drawer 的 Owner Form（主視窗 tree/tab/卡片）→ 關
///   點到其他 popup form（AntdUI.Modal / ContextMenu / Tooltip）→ 不關
///   （之前用 cursor 位置 vs drawer.Bounds 判定會誤把 Modal 上的點擊當成「外部」造成 drawer 關閉）
/// </summary>
internal class DrawerOutsideClickFilter : System.Windows.Forms.IMessageFilter
{
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_NCLBUTTONDOWN = 0x00A1;

    private readonly Form _drawer;

    public DrawerOutsideClickFilter(Form drawer) { _drawer = drawer; }

    public bool PreFilterMessage(ref System.Windows.Forms.Message m)
    {
        if (m.Msg != WM_LBUTTONDOWN && m.Msg != WM_RBUTTONDOWN && m.Msg != WM_NCLBUTTONDOWN)
            return false;
        if (_drawer == null || _drawer.IsDisposed) return false;

        // m.HWnd 可能是任一子控件 handle；用 FromChildHandle 解析才能跨 native window 邊界
        var ctrl = Control.FromChildHandle(m.HWnd) ?? Control.FromHandle(m.HWnd);
        var clickForm = ctrl?.FindForm();
        if (clickForm == _drawer) return false;            // 點 drawer 內 — 不關
        if (clickForm == _drawer.Owner)                    // 點主視窗 — 關
        {
            _drawer.Close();   // OnClose callback 會清 ref + remove filter
        }
        // 其他 form（Modal / ContextMenu / Tooltip 等 popup）— 不關
        return false;   // 不吞訊息，讓點擊正常傳遞給目標控件
    }
}
