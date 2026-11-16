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

    /// <summary>預設畫廊檢視模式："Small" / "Medium" / "Large" / "List"。</summary>
    public string DefaultGalleryViewMode { get; set; } = "Medium";

    /// <summary>預設畫廊排序欄位。</summary>
    public GallerySortField DefaultGallerySortField { get; set; } = GallerySortField.Name;

    /// <summary>預設畫廊是否降冪排序。</summary>
    public bool DefaultGallerySortDescending { get; set; } = false;

    /// <summary>詳情側邊抽屜（Drawer）寬度；null 表示使用預設寬度。</summary>
    public int? DrawerWidth { get; set; }

    /// <summary>主視窗上次位置 X；null 表示置中或由系統決定。</summary>
    public int? WindowX { get; set; }

    /// <summary>主視窗上次位置 Y；null 表示置中或由系統決定。</summary>
    public int? WindowY { get; set; }

    /// <summary>主視窗上次寬度；null 表示使用設計期預設寬度。</summary>
    public int? WindowWidth { get; set; }

    /// <summary>主視窗上次高度；null 表示使用設計期預設高度。</summary>
    public int? WindowHeight { get; set; }

    /// <summary>主視窗上次關閉時是否為最大化狀態。</summary>
    public bool WindowMaximized { get; set; } = false;

    /// <summary>左側導覽樹側邊欄寬度（SplitContainer.SplitterDistance）；null 表示使用預設寬度。</summary>
    public int? SidebarSplitterWidth { get; set; }

    /// <summary>內建閱讀器上次視窗寬度；null 表示使用預設寬度。</summary>
    public int? ReaderWindowWidth { get; set; }

    /// <summary>內建閱讀器上次視窗高度；null 表示使用預設高度。</summary>
    public int? ReaderWindowHeight { get; set; }

    /// <summary>內建閱讀器上次關閉時是否為最大化狀態。</summary>
    public bool ReaderWindowMaximized { get; set; } = false;

    /// <summary>預設閱讀器檢視模式："SinglePage" / "DualPage" / "ContinuousScroll"。</summary>
    public string DefaultReaderViewMode { get; set; } = "SinglePage";

    /// <summary>預設閱讀器縮放模式："FitPage" / "FitWidth" / "Actual"。</summary>
    public string DefaultReaderZoomMode { get; set; } = "FitPage";

    /// <summary>跳頁選擇視窗是否隱藏上方縮圖導覽條。</summary>
    public bool PagePickerHideThumbnails { get; set; } = false;
}
