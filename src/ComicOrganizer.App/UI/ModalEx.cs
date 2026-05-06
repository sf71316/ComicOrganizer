namespace ComicOrganizer.App.UI;

/// <summary>
/// AntdUI.Modal 的 wrapper — 主要功能：把 AntdUI 預設的簡體「确定」改成繁體「確定」。
/// AntdUI.Modal.Config.OkText 預設值是 "确定"（簡體）；若 caller 沒顯式設，
/// 結尾呼叫 AntdUI.Modal.open 前 patch 一下。已自訂 OkText 的 callsite 不動。
///
/// 用法：把所有 `AntdUI.Modal.open(...)` 改成 `ModalEx.Open(...)`。
/// </summary>
public static class ModalEx
{
    public static void Open(AntdUI.Modal.Config config)
    {
        FixSimplifiedDefaults(config);
        AntdUI.Modal.open(config);
    }

    private static void FixSimplifiedDefaults(AntdUI.Modal.Config c)
    {
        if (c.OkText == "确定") c.OkText = "確定";
        // 其他預設值（CancelText="取消"）已經是兩岸通用，不動
    }
}
