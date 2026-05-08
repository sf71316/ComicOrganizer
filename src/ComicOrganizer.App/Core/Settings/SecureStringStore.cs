using System.Security.Cryptography;
using System.Text;

namespace ComicOrganizer.App.Core;

/// <summary>
/// Windows DPAPI 包裝。CurrentUser scope — 只有當前 Windows 帳號能解密，
/// 跨機器拷貝 appsettings.json 也解不開（這是好事：避免 token 跟著檔案走）。
///
/// 對 ComicOrganize 用於儲存 nhentai API key 等敏感字串。
/// </summary>
public static class SecureStringStore
{
    /// <summary>plain text → DPAPI 加密 → base64 字串可存 JSON。</summary>
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>base64 字串 → DPAPI 解密 → plain text。失敗（key 來自別人 / 損壞）回 null。</summary>
    public static string? TryUnprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return null;
        try
        {
            var bytes = Convert.FromBase64String(protectedBase64);
            var plain = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }
}
