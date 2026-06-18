using System.Collections.Generic;
using System.Globalization;

namespace ComicOrganizer.App.Core;

/// <summary>
/// AntdUI 內建字串（文字框右鍵選單等）的在地化。AntdUI 預設為簡體中文；
/// 此 provider 對已知 key 回傳繁中（英文介面時回英文），未知 key 回 null → AntdUI 沿用其預設。
/// key 為 AntdUI ContextMenuStripItem 的 ID（見 Input.OnOpenContextMenu 的 SetID）。
/// 即時讀取 <see cref="CultureInfo.CurrentUICulture"/>，故跟著 LocalizationManager 切換語言。
/// 於 Program.Main 註冊到 AntdUI.Localization.Provider。
/// </summary>
public sealed class AntdLocalizationProvider : AntdUI.ILocalization
{
    // AntdUI key(ID) → (繁中, 英文)；採 Windows 台灣慣用譯名
    private static readonly Dictionary<string, (string tw, string en)> Map = new()
    {
        ["Cut"] = ("剪下", "Cut"),
        ["Copy"] = ("複製", "Copy"),
        ["Paste"] = ("貼上", "Paste"),
        ["Delete"] = ("刪除", "Delete"),
        ["SelectAll"] = ("全選", "Select All"),
        ["Undo"] = ("復原", "Undo"),
        ["Redo"] = ("重做", "Redo"),
    };

    public string? GetLocalizedString(string key)
    {
        if (key != null && Map.TryGetValue(key, out var v))
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? v.en : v.tw;
        return null;   // 未知 key → AntdUI 用預設值
    }
}
