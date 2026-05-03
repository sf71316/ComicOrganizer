namespace ComicOrganizer.App.UI;

/// <summary>
/// IMessageFilter：偵測滑鼠按鍵事件，若 cursor 不在 drawer Bounds 內則關閉 drawer。
/// AntdUI Drawer 是 layered window，不走標準 Form focus 模型 — 無法用 Form.Deactivate
/// 偵測「點到外面」，改在 application message loop 攔截 WM_LBUTTONDOWN 自己判斷。
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
        // Cursor.Position 是 screen coords，Form.Bounds 也是 screen coords
        if (!_drawer.Bounds.Contains(Cursor.Position))
        {
            _drawer.Close();   // OnClose callback 會清 ref + remove filter
        }
        return false;   // 不吞訊息，讓點擊正常傳遞給目標控件
    }
}
