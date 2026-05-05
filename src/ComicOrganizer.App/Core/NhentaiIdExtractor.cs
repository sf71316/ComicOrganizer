using System.Text.RegularExpressions;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 從檔名 / 路徑抽出 nhentai gallery id（5-7 位純數字）。
/// 常見命名：[作者] 標題 (123456).zip / 標題 [567890].zip。
///
/// 太短的數字（&lt;5 位）容易誤判（檔名可能含日期/章節編號），
/// 太長（&gt;7 位）目前 nhentai id 還沒到。
/// </summary>
public static class NhentaiIdExtractor
{
    // 5-7 位純數字，前後不能緊鄰其他數字
    private static readonly Regex IdRegex = new(@"(?<!\d)(\d{5,7})(?!\d)", RegexOptions.Compiled);

    /// <summary>抽第一個 5-7 位數字組；找不到回 null。</summary>
    public static string? Extract(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return null;
        var m = IdRegex.Match(filename);
        return m.Success ? m.Groups[1].Value : null;
    }
}
