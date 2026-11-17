using System.Drawing;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.Models;

/// <summary>閱讀器同作品延伸探索維度。</summary>
public enum ReaderExploreKind
{
    Author,
    Series,
    TranslationGroup,
}

/// <summary>閱讀器延伸探索選項（含維度、顯示值與安全搜尋 DSL）。</summary>
public sealed record ReaderExploreOption(ReaderExploreKind Kind, string Value, string Dsl);

/// <summary>
/// 內建閱讀器操作輔助類別：頁碼跳轉解析、同目錄／同清單相鄰作品解析、
/// 檢視／縮放模式正規化與閱讀器視窗尺寸防呆夾擠。
/// </summary>
public static class ReaderNavigationHelper
{
    /// <summary>
    /// 解析使用者輸入的 1-based 頁碼字串並轉換為 0-based 頁索引；
    /// 空白、非整數、&lt; 1 或 &gt; <paramref name="pageCount"/> 回傳 false。
    /// </summary>
    public static bool TryParseGoToPageInput(string? input, int pageCount, out int zeroBasedIndex)
    {
        zeroBasedIndex = 0;
        if (pageCount <= 0 || string.IsNullOrWhiteSpace(input))
            return false;

        if (!int.TryParse(input.Trim(), out int oneBased))
            return false;

        if (oneBased < 1 || oneBased > pageCount)
            return false;

        zeroBasedIndex = oneBased - 1;
        return true;
    }

    /// <summary>
    /// 解析目前作品所屬的同級作品清單：
    /// 1. 若有提供 <paramref name="candidatePaths"/> 且經去重後包含 <paramref name="currentPath"/>，直接保留原順序回傳；
    /// 2. 否則枚舉同目錄下的非隱藏子資料夾與支援之歸檔／PDF（且必定含 <paramref name="currentPath"/> 若實際存在），依自然排序回傳。
    /// </summary>
    public static IReadOnlyList<string> ResolveSiblingWorks(
        string currentPath,
        IEnumerable<string>? candidatePaths = null)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
            return Array.Empty<string>();

        string currentKey = DelayedLoadingTracker.NormalizePathKey(currentPath);

        if (candidatePaths != null)
        {
            var deduped = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool containsCurrent = false;

            foreach (var raw in candidatePaths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var key = DelayedLoadingTracker.NormalizePathKey(raw);
                if (string.IsNullOrEmpty(key) || !seen.Add(key)) continue;

                deduped.Add(raw);
                if (string.Equals(key, currentKey, StringComparison.OrdinalIgnoreCase))
                    containsCurrent = true;
            }

            if (containsCurrent && deduped.Count > 0)
                return deduped;
        }

        string? parentDir = Path.GetDirectoryName(currentKey);
        bool currentExists = Directory.Exists(currentPath) || File.Exists(currentPath);

        if (string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir))
            return currentExists ? new[] { currentPath } : Array.Empty<string>();

        var collected = new List<string>();
        var collectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parentDir))
            {
                try
                {
                    var attrs = File.GetAttributes(dir);
                    if ((attrs & FileAttributes.Hidden) != 0) continue;
                }
                catch
                {
                    continue;
                }

                var key = DelayedLoadingTracker.NormalizePathKey(dir);
                if (!string.IsNullOrEmpty(key) && collectedKeys.Add(key))
                    collected.Add(dir);
            }

            foreach (var file in Directory.EnumerateFiles(parentDir))
            {
                string ext = Path.GetExtension(file);
                if (!SupportedExtensions.Archive.Contains(ext) && !SupportedExtensions.Pdf.Contains(ext))
                    continue;

                var key = DelayedLoadingTracker.NormalizePathKey(file);
                if (!string.IsNullOrEmpty(key) && collectedKeys.Add(key))
                    collected.Add(file);
            }
        }
        catch
        {
            // 目錄讀取失敗時仍保留目前作品（若存在）
        }

        if (currentExists && !string.IsNullOrEmpty(currentKey) && collectedKeys.Add(currentKey))
        {
            collected.Add(currentPath);
        }

        collected.Sort((a, b) =>
        {
            string nameA = Path.GetFileName(DelayedLoadingTracker.NormalizePathKey(a));
            string nameB = Path.GetFileName(DelayedLoadingTracker.NormalizePathKey(b));
            int cmp = NaturalStringComparer.Instance.Compare(nameA, nameB);
            return cmp != 0 ? cmp : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });

        return collected;
    }

    /// <summary>
    /// 依 <paramref name="delta"/>（-1 上一部、+1 下一部）從 <paramref name="siblingPaths"/> 取得相鄰作品路徑；
    /// 找不到目前作品或已達首尾邊界時回傳 null。
    /// </summary>
    public static string? GetAdjacentWorkPath(IReadOnlyList<string> siblingPaths, string currentPath, int delta)
    {
        if (siblingPaths == null || siblingPaths.Count == 0 || string.IsNullOrWhiteSpace(currentPath) || delta == 0)
            return null;

        string currentKey = DelayedLoadingTracker.NormalizePathKey(currentPath);
        int currentIndex = -1;
        for (int i = 0; i < siblingPaths.Count; i++)
        {
            if (string.Equals(DelayedLoadingTracker.NormalizePathKey(siblingPaths[i]), currentKey, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0) return null;

        int targetIndex = currentIndex + delta;
        if (targetIndex < 0 || targetIndex >= siblingPaths.Count)
            return null;

        return siblingPaths[targetIndex];
    }

    /// <summary>正規化閱讀器檢視模式名稱，無效值一律退回 <c>"SinglePage"</c>。</summary>
    public static string NormalizeReaderViewMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "SinglePage";
        return value.Trim().ToLowerInvariant() switch
        {
            "singlepage" or "single" => "SinglePage",
            "dualpage" or "dual" => "DualPage",
            "continuousscroll" or "continuous" => "ContinuousScroll",
            _ => "SinglePage",
        };
    }

    /// <summary>正規化閱讀器縮放模式名稱，無效值一律退回 <c>"FitPage"</c>。</summary>
    public static string NormalizeReaderZoomMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "FitPage";
        return value.Trim().ToLowerInvariant() switch
        {
            "fitpage" => "FitPage",
            "fitwidth" => "FitWidth",
            "actual" => "Actual",
            _ => "FitPage",
        };
    }

    /// <summary>依目標螢幕工作區夾擠閱讀器視窗初始／還原尺寸。</summary>
    public static Size ClampReaderWindowSize(
        int? width,
        int? height,
        Rectangle workingArea,
        int defaultWidth = 1024,
        int defaultHeight = 768,
        int minWidth = 720,
        int minHeight = 520)
    {
        int targetW = width is > 0 ? width.Value : defaultWidth;
        int targetH = height is > 0 ? height.Value : defaultHeight;

        if (workingArea.Width <= 0 || workingArea.Height <= 0)
            return new Size(Math.Max(minWidth, targetW), Math.Max(minHeight, targetH));

        int effectiveMinW = Math.Min(minWidth, workingArea.Width);
        int effectiveMinH = Math.Min(minHeight, workingArea.Height);

        int clampedW = Math.Clamp(targetW, effectiveMinW, workingArea.Width);
        int clampedH = Math.Clamp(targetH, effectiveMinH, workingArea.Height);
        return new Size(clampedW, clampedH);
    }

    /// <summary>
    /// 依作品的作者／系列／翻譯組建立探索搜尋選項（以 <see cref="SearchQueryParser.BuildFieldToken"/> 安全組句）。
    /// 空白或無法組成有效 DSL 的欄位會自動略過。
    /// </summary>
    public static IReadOnlyList<ReaderExploreOption> BuildExploreOptions(
        string? authorName,
        string? series,
        string? translationGroup)
    {
        var options = new List<ReaderExploreOption>(3);
        AddExploreOption(options, ReaderExploreKind.Author, "artist", authorName);
        AddExploreOption(options, ReaderExploreKind.Series, "series", series);
        AddExploreOption(options, ReaderExploreKind.TranslationGroup, "group", translationGroup);
        return options;
    }

    private static void AddExploreOption(
        List<ReaderExploreOption> options,
        ReaderExploreKind kind,
        string fieldName,
        string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return;
        var dsl = SearchQueryParser.BuildFieldToken(fieldName, rawValue);
        if (string.IsNullOrEmpty(dsl)) return;
        options.Add(new ReaderExploreOption(kind, rawValue.Trim(), dsl));
    }

    /// <summary>判斷目前單／雙頁瀏覽位置是否已抵達本篇尾頁（含最後跨頁）。</summary>
    public static bool IsLastSpreadReached(int currentIndex, int spreadSize, int pageCount)
        => pageCount > 0 && currentIndex + Math.Max(1, spreadSize) >= pageCount;
}
