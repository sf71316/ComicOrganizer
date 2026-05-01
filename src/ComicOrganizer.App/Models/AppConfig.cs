namespace ComicOrganizer.App.Models;

public class AppConfig
{
    public string UnorganizedDirectory { get; set; } = string.Empty;
    public string RootDirectory { get; set; } = string.Empty;
    public string ImageViewerPath { get; set; } = string.Empty;

    /// <summary>
    /// 外部圖片瀏覽器啟動參數模板。佔位符 {path} 會被替換成實際檔案/資料夾路徑。
    /// 預設 "{path}"（含引號）；遞迴載入子目錄改 -r "{path}"，依 viewer 而定。
    /// </summary>
    public string ImageViewerArgs { get; set; } = "\"{path}\"";
    public List<string> ExcludedDirectories { get; set; } = new();
    public bool DebugMode { get; set; } = false;

    /// <summary>
    /// CoverManager 遞迴搜尋封面圖檔的最大層數。
    /// 父資料夾沒有直接圖檔時，遞迴往子資料夾找第一張圖當封面。
    /// 預設 3 — 對應 作家/作品/章節/圖檔 結構的「作家」層往下找 3 層即可命中圖檔。
    /// </summary>
    public int MaxScanDepth { get; set; } = 3;

    // UI 偏好設定
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string Language { get; set; } = "zh-TW";

    // 動態黑名單 (使用者可編輯)
    public List<string> Blacklist { get; set; } = new() 
    { 
        "中国翻訳", "中譯", "DL版", "Digital", "AI Generated", "無修正", "個人漢化", "繁中機翻",
        "pixiv", "fanbox", "fantia", "upscaled", "high res"
    };

    // 模式黑名單 (Regex)
    public List<string> BlacklistPatterns { get; set; } = new()
    {
        @"C\d{2,3}",          // Comic Market 編號 (C103, C99)
        @"\d+x\d+",           // 解析度 (1280x720)
        @"\d{6,8}"            // 日期格式 (20240907)
    };
}

public enum AppTheme
{
    Light,
    Dark,
    System
}
