using System.Runtime.InteropServices;

namespace ComicOrganizer.App.Core;

/// <summary>
/// Windows Explorer 風格的自然排序比較器：把字串中的數字片段當數值比，
/// 例如 ["1.jpg", "2.jpg", "10.jpg"] 排出來是 1, 2, 10 而不是字典序的 1, 10, 2。
/// 走 shlwapi.dll 的 StrCmpLogicalW，與檔案總管行為一致；不可用時 fallback 字典序。
/// </summary>
public class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        try { return StrCmpLogicalW(x, y); }
        catch { return string.Compare(x, y, StringComparison.OrdinalIgnoreCase); }
    }
}
