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

    /// <summary>啟用啟動時的翻譯庫自動更新檢查（預設 on）。
    /// 關掉只影響背景檢查；工具頁的「立即檢查更新」按鈕仍可主動觸發。</summary>
    public bool AutoCheckTranslationDbUpdate { get; set; } = true;

    // ===== AI 翻譯服務設定 =====
    /// <summary>AI 翻譯 API 端點</summary>
    public string TranslationEndpoint { get; set; } = "http://192.168.0.74:1234";

    /// <summary>AI 翻譯模型名稱</summary>
    public string TranslationModelName { get; set; } = "qwen3.6-35b-a3b-mtp";

    /// <summary>AI 翻譯 API 類型 (0 = OpenAiCompatible, 1 = OllamaNative)</summary>
    public int TranslationApiType { get; set; } = 0;

    /// <summary>AI 翻譯併發數量限制，預設為 1</summary>
    public int TranslationMaxConcurrency { get; set; } = 1;

    /// <summary>AI 翻譯 API 金鑰（DPAPI 密文）。雲端 OpenAI 相容端點以 Authorization: Bearer 帶上；空=免認證(本地端)。</summary>
    public string TranslationApiKeyProtected { get; set; } = "";

    /// <summary>OCR 引擎類型 (0 = WindowsNativeOcr, 1 = PaddleOcr)</summary>
    public int OcrEngineType { get; set; } = 0;

    /// <summary>譯文抹除用的文字遮罩引擎 (0 = 關閉/逐欄矩形白底, 1 = FloodFill[保留], 2 = ComicTextDetector ONNX)。
    /// 預設 2；模型檔不存在時自動 fallback 回逐欄白底。</summary>
    public int MaskEngineType { get; set; } = 2;

    /// <summary>閱讀器底圖改用 SkiaSharp/GPU(SKGLControl) 繪製(實驗)。預設 false；
    /// 開啟後單頁等非譯文模式走 GPU canvas，GL 起不來時自動退回 GDI+。需重開閱讀器生效。</summary>
    public bool UseGpuRendering { get; set; } = false;

    // ===== 圖搜漫（額外選配功能，預設關閉，需於設定頁啟用並下載 ONNX 模型） =====

    /// <summary>是否啟用「🔎 圖搜漫」功能（預設 false，關閉時不載入模型與向量索引）。</summary>
    public bool EnableVisualSearch { get; set; } = false;

    /// <summary>自動歸檔搬檔完成後，是否自動補建新歸檔漫畫的視覺索引（預設 false）。</summary>
    public bool VisualSearchAutoIndexAfterArchive { get; set; } = false;

    /// <summary>圖搜漫於低信心（Top-1 &lt; 0.72）時自動觸發純 CPU Multi-Crop 極端裁切精排（預設 true）。</summary>
    public bool VisualSearchEnableMultiCropRerank { get; set; } = true;

    /// <summary>分鏡偵測模型（panel_detector.onnx）選用自訂下載網址（空值代表使用預設或本機匯入）。</summary>
    public string VisualSearchPanelModelUrl { get; set; } = "";

    /// <summary>特徵提取模型（sscd_disc_mixup.onnx）選用自訂下載網址（空值代表使用預設或本機匯入）。</summary>
    public string VisualSearchEmbedModelUrl { get; set; } = "";

    /// <summary>是否啟用「角色頭像／小裁切偵測索引（方案 B，head_detector.onnx）」（預設 false，由使用者於設定頁手動勾選）。</summary>
    public bool VisualSearchEnableHeadIndex { get; set; } = false;

    /// <summary>角色頭像偵測模型（head_detector.onnx）選用自訂下載網址（空值代表使用預設或本機匯入）。</summary>
    public string VisualSearchHeadModelUrl { get; set; } = "";
}
