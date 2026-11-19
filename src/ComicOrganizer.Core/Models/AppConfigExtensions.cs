using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.Models;

/// <summary>
/// AppConfig 上的便利擴充方法。
/// 把加密 / 解密細節隱藏起來，caller 直接拿 plain text。
/// </summary>
public static class AppConfigExtensions
{
    /// <summary>取出解密後的 nhentai API key（plain text）。沒設或解不開回 null。</summary>
    public static string? GetNhentaiApiKey(this AppConfig cfg)
        => string.IsNullOrEmpty(cfg.NhentaiApiKeyProtected)
            ? null
            : SecureStringStore.TryUnprotect(cfg.NhentaiApiKeyProtected);

    /// <summary>設定 nhentai API key — 自動加密後寫進 NhentaiApiKeyProtected。
    /// 傳 null / 空字串 → 清空（後續 GetNhentaiApiKey 回 null）。</summary>
    public static void SetNhentaiApiKey(this AppConfig cfg, string? plain)
        => cfg.NhentaiApiKeyProtected = string.IsNullOrEmpty(plain)
            ? ""
            : SecureStringStore.Protect(plain);

    /// <summary>取出解密後的 AI 翻譯 API key（plain text）。沒設或解不開回 null。</summary>
    public static string? GetTranslationApiKey(this AppConfig cfg)
        => string.IsNullOrEmpty(cfg.TranslationApiKeyProtected)
            ? null
            : SecureStringStore.TryUnprotect(cfg.TranslationApiKeyProtected);

    /// <summary>設定 AI 翻譯 API key — 自動加密後寫進 TranslationApiKeyProtected。
    /// 傳 null / 空字串 → 清空。</summary>
    public static void SetTranslationApiKey(this AppConfig cfg, string? plain)
        => cfg.TranslationApiKeyProtected = string.IsNullOrEmpty(plain)
            ? ""
            : SecureStringStore.Protect(plain);
}
