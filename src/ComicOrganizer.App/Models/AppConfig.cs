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

    /// <summary>
    /// CoverManager 選擇 FIRST_IMAGE / FIRST_ZIP entry 時的最小檔案大小 (bytes)。
    /// 跳過小於此值的檔案（縮圖、banner、小圖示）。預設 50KB。
    /// 600x800 JPEG 壓縮約 80~120KB 屬正常頁面；50KB 以下幾乎都是 banner/icon。
    /// 注意：cover.* 命名檔不受此限制（使用者明確指定）。
    /// </summary>
    public long MinCoverFileSizeBytes { get; set; } = 50 * 1024;

    /// <summary>
    /// CoverManager 選擇 FIRST_IMAGE / FIRST_ZIP entry 時的最大檔案大小 (bytes)。
    /// 跳過大於此值的檔案（拚版、超解析掃圖、插畫合輯）— 既慢又通常不是真正的封面。
    /// 預設 5MB。注意：cover.* 命名檔不受此限制。
    /// </summary>
    public long MaxCoverFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Thumbnail cache 容量 soft limit (bytes)。超過會 LRU 淘汰最舊的縮圖。
    /// 預設 1GB。設 0 視為不限制。
    /// </summary>
    public long ThumbCacheMaxBytes { get; set; } = 1024L * 1024 * 1024;

    /// <summary>
    /// 閱讀記錄延遲門檻（秒）— 內部設定不出 UI。
    /// 在 reader 內停留超過此值或翻頁達 ReadCountThresholdPages 才算一次有效閱讀。
    /// </summary>
    public int ReadCountThresholdSeconds { get; set; } = 30;

    /// <summary>
    /// 閱讀記錄翻頁門檻 — 內部設定不出 UI。
    /// 內建 reader 內翻過此頁數才算一次有效閱讀（與秒數任一達成即可）。
    /// </summary>
    public int ReadCountThresholdPages { get; set; } = 2;

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

    // ===== 外部 metadata 整合 (#52) =====

    /// <summary>啟用外部 metadata 整合（nhentai API 等）。預設關閉。</summary>
    public bool EnableExternalMetadata { get; set; } = false;

    /// <summary>nhentai API key — DPAPI 加密 base64 字串。
    /// 直接讀寫請走 AppConfigExtensions.GetNhentaiApiKey / SetNhentaiApiKey 而非此屬性。</summary>
    public string NhentaiApiKeyProtected { get; set; } = "";

    // ===== 翻譯庫線上更新 (#53 Phase 5) =====

    /// <summary>上次成功檢查 EhTag 翻譯庫更新的時間（unix ms，0 = 從未）。
    /// 啟動背景檢查若距上次 &lt; 24h 就 skip，避免每次啟動都打 GitHub API。</summary>
    public long LastTranslationDbCheckTime { get; set; }
}
