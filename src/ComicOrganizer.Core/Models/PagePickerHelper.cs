using System.Globalization;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.Models;

/// <summary>跳頁選擇清單單筆項目摘要。</summary>
public sealed record PagePickerEntry(
    int PageIndex,
    string Name,
    long? SizeBytes,
    DateTime? LastModified);

/// <summary>
/// 跳頁選擇視窗（<c>ReaderPagePickerDialog</c>）純邏輯輔助類別：
/// 名稱關鍵字篩選與排序索引建置、檔案大小／修改時間格式化、上方縮圖導覽條窗口計算。
/// </summary>
public static class PagePickerHelper
{
    /// <summary>
    /// 依關鍵字（不分大小寫）過濾並依頁序或自然名稱（<see cref="NaturalStringComparer"/>）排序，
    /// 回傳對應真實 0-based 頁碼索引清單。
    /// </summary>
    public static IReadOnlyList<int> BuildDisplayIndices(
        IReadOnlyList<string> names,
        string? filterText,
        bool sortByName)
    {
        if (names == null || names.Count == 0)
            return Array.Empty<int>();

        string? keyword = string.IsNullOrWhiteSpace(filterText) ? null : filterText.Trim();
        var indices = new List<int>(names.Count);

        for (int i = 0; i < names.Count; i++)
        {
            if (keyword == null || (names[i]?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                indices.Add(i);
            }
        }

        if (sortByName && indices.Count > 1)
        {
            indices.Sort((a, b) =>
            {
                int cmp = NaturalStringComparer.Instance.Compare(names[a], names[b]);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });
        }

        return indices;
    }

    /// <summary>將位元組大小格式化為人類可讀字串（null 或負數回傳「—」）。</summary>
    public static string FormatFileSize(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0) return "—";
        if (bytes.Value < 1024) return $"{bytes.Value} B";
        if (bytes.Value < 1024L * 1024) return $"{bytes.Value / 1024.0:F1} KB";
        return $"{bytes.Value / (1024.0 * 1024.0):F1} MB";
    }

    /// <summary>將修改時間格式化為 <c>yyyy/M/d HH:mm</c>（null 回傳「—」）。</summary>
    public static string FormatModifiedTime(DateTime? dt)
    {
        if (!dt.HasValue) return "—";
        return dt.Value.ToString("yyyy/M/d HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 計算以 <paramref name="selectedPos"/> 為中心的縮圖導覽條顯示位置索引集合
    /// （過濾超出 <c>0 .. totalCount - 1</c> 的無效位置）。
    /// </summary>
    public static IReadOnlyList<int> GetStripWindowPositions(int selectedPos, int totalCount, int halfRadius = 2)
    {
        if (totalCount <= 0 || selectedPos < 0 || selectedPos >= totalCount)
            return Array.Empty<int>();

        int r = Math.Max(0, halfRadius);
        int start = Math.Max(0, selectedPos - r);
        int end = Math.Min(totalCount - 1, selectedPos + r);

        var result = new List<int>(end - start + 1);
        for (int i = start; i <= end; i++)
            result.Add(i);

        return result;
    }
}
