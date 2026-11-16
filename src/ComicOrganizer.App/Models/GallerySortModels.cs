using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.Models;

/// <summary>畫廊排序欄位。</summary>
public enum GallerySortField
{
    Name = 0,
    ModifiedTime = 1,
    Size = 2,
    LastRead = 3,
    ReadCount = 4,
}

/// <summary>畫廊就地閱讀狀態過濾。</summary>
public enum GalleryReadFilter
{
    All = 0,
    Unread = 1,
    Reading = 2,
    Completed = 3,
}

/// <summary>畫廊單筆作品的資料庫統計（批次查詢回傳用）。</summary>
public record AssetGalleryStats(string? Title, ReadStatus ReadStatus, int ReadCount, long? LastRead);

/// <summary>畫廊項目用於記憶體內就地排序與過濾的快照資料。未入庫項目預設 Unread / 0 / null。</summary>
public record GalleryItemSortMetadata(
    string Path,
    string Name,
    string? DisplayTitle,
    bool IsFolder,
    long SizeBytes,
    long ModifiedTicks,
    ReadStatus ReadStatus,
    int ReadCount,
    long? LastRead);

/// <summary>畫廊記憶體內就地排序與過濾純邏輯輔助類別。</summary>
public static class GallerySortFilterHelper
{
    /// <summary>
    /// 判斷項目是否符合狀態過濾與關鍵字過濾。
    /// </summary>
    public static bool MatchesFilter(GalleryItemSortMetadata item, GalleryReadFilter statusFilter, string? keyword)
    {
        bool statusMatched = statusFilter switch
        {
            GalleryReadFilter.All => true,
            GalleryReadFilter.Unread => item.ReadStatus == ReadStatus.Unread,
            GalleryReadFilter.Reading => item.ReadStatus == ReadStatus.Reading,
            GalleryReadFilter.Completed => item.ReadStatus == ReadStatus.Completed,
            _ => true,
        };
        if (!statusMatched) return false;

        if (string.IsNullOrWhiteSpace(keyword)) return true;

        var trimmed = keyword.Trim();
        return (item.Name?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false)
            || (!string.IsNullOrEmpty(item.DisplayTitle)
                && item.DisplayTitle.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 依指定欄位與方向比較兩筆畫廊項目。
    /// - Name：永遠維持資料夾優先，目錄內與檔案內各依自然排序升/降冪。
    /// - ModifiedTime / Size / ReadCount：依數值排序，平手以名稱升冪（資料夾優先）fallback。
    /// - LastRead：無閱讀紀錄（null 或 &lt;= 0）永遠殿後，有閱讀紀錄者依方向排序，平手以名稱升冪 fallback。
    /// </summary>
    public static int Compare(GalleryItemSortMetadata a, GalleryItemSortMetadata b, GallerySortField field, bool descending)
    {
        switch (field)
        {
            case GallerySortField.Name:
                return CompareByName(a, b, descending);

            case GallerySortField.ModifiedTime:
            {
                int cmp = descending
                    ? b.ModifiedTicks.CompareTo(a.ModifiedTicks)
                    : a.ModifiedTicks.CompareTo(b.ModifiedTicks);
                return cmp != 0 ? cmp : CompareByName(a, b, descending: false);
            }

            case GallerySortField.Size:
            {
                int cmp = descending
                    ? b.SizeBytes.CompareTo(a.SizeBytes)
                    : a.SizeBytes.CompareTo(b.SizeBytes);
                return cmp != 0 ? cmp : CompareByName(a, b, descending: false);
            }

            case GallerySortField.LastRead:
            {
                bool aHas = a.LastRead is > 0;
                bool bHas = b.LastRead is > 0;
                if (aHas != bHas) return aHas ? -1 : 1;
                if (aHas)
                {
                    int cmp = descending
                        ? b.LastRead!.Value.CompareTo(a.LastRead!.Value)
                        : a.LastRead!.Value.CompareTo(b.LastRead!.Value);
                    if (cmp != 0) return cmp;
                }
                return CompareByName(a, b, descending: false);
            }

            case GallerySortField.ReadCount:
            {
                int cmp = descending
                    ? b.ReadCount.CompareTo(a.ReadCount)
                    : a.ReadCount.CompareTo(b.ReadCount);
                return cmp != 0 ? cmp : CompareByName(a, b, descending: false);
            }

            default:
                return CompareByName(a, b, descending);
        }
    }

    private static int CompareByName(GalleryItemSortMetadata a, GalleryItemSortMetadata b, bool descending)
    {
        if (a.IsFolder != b.IsFolder)
            return a.IsFolder ? -1 : 1;

        int cmp = descending
            ? NaturalStringComparer.Instance.Compare(b.Name, a.Name)
            : NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        if (cmp != 0) return cmp;

        return descending
            ? string.Compare(b.Path, a.Path, StringComparison.OrdinalIgnoreCase)
            : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>將項目集合依指定排序規則回傳新的排序清單。</summary>
    public static List<T> SortItems<T>(
        IEnumerable<T> items,
        Func<T, GalleryItemSortMetadata> selector,
        GallerySortField field,
        bool descending)
    {
        var list = items.ToList();
        list.Sort((x, y) => Compare(selector(x), selector(y), field, descending));
        return list;
    }
}
