namespace ComicOrganize.App.Models;

public class AppConfig
{
    public string UnorganizedDirectory { get; set; } = string.Empty;
    public string RootDirectory { get; set; } = string.Empty;
    public string ImageViewerPath { get; set; } = string.Empty;
    public List<string> ExcludedDirectories { get; set; } = new();
    public bool DebugMode { get; set; } = false;

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
