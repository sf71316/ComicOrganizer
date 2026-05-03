namespace ComicOrganizer.App.Models;

/// <summary>
/// 使用者偏好設定 — UI / UX 行為調整，不涉及檔案路徑或掃描參數。
/// 跟 AppConfig 分檔儲存（preferences.json），允許獨立備份 / 重置。
/// </summary>
public class UserPreferences
{
    /// <summary>主題：Light / Dark / System。</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>UI 語言代碼。例："zh-TW" / "en-US"。</summary>
    public string Language { get; set; } = "zh-TW";

    // 預留：#49 內建 reader 偏好（先放 default 值，UI 還沒實作）

    /// <summary>是否使用內建 reader 取代外部 viewer。預設 off，#49 完成後改 default true。</summary>
    public bool UseBuiltInReader { get; set; } = false;

    /// <summary>預設閱讀方向："rtl"（manga）/ "ltr"（西漫）。可被 asset_meta.reading_direction 覆寫。</summary>
    public string DefaultReadingDirection { get; set; } = "rtl";

    /// <summary>雙頁模式封面是否獨立顯示。false = 1+2 併排；true = 第 1 頁獨立、2+3 併排。</summary>
    public bool DualPageSpreadFromCover { get; set; } = false;

    /// <summary>是否啟用閱讀記錄功能（read_status 自動升級 / read_count++ / last_read 更新）。
    /// false 時 reader / 外部 viewer 都不寫這幾個欄位；last_page 仍會記錄（resume 用）。</summary>
    public bool TrackReadingRecord { get; set; } = true;
}
