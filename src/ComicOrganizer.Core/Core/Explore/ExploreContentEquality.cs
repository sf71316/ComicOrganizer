namespace ComicOrganizer.App.Core;

/// <summary>
/// 探索牆模型的內容比對（含清單欄位逐項比對）。
/// record 內建的 Equals 對 <see cref="IReadOnlyList{T}"/> 欄位只比參考，每次查詢都是新清單而永遠不相等；
/// 重整時要判斷卡片內容是否真的變了，才能沿用畫面上已有的卡片（保留封面、不重建）。
/// </summary>
public static class ExploreContentEquality
{
    public static bool SameWork(ExploreWorkItem? a, ExploreWorkItem? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.AssetId == b.AssetId
            && string.Equals(a.Path, b.Path, StringComparison.Ordinal)
            && string.Equals(a.FileName, b.FileName, StringComparison.Ordinal)
            && string.Equals(a.Title, b.Title, StringComparison.Ordinal)
            && a.AuthorId == b.AuthorId
            && string.Equals(a.AuthorName, b.AuthorName, StringComparison.Ordinal)
            && string.Equals(a.Language, b.Language, StringComparison.Ordinal)
            && string.Equals(a.TranslationGroup, b.TranslationGroup, StringComparison.Ordinal)
            && a.PageCount == b.PageCount
            && a.ReadStatus == b.ReadStatus
            && a.ReadCount == b.ReadCount
            && a.LastRead == b.LastRead
            && a.AddedAt == b.AddedAt
            && string.Equals(a.ExternalId, b.ExternalId, StringComparison.Ordinal)
            && string.Equals(a.CoverThumbKey, b.CoverThumbKey, StringComparison.Ordinal)
            && SameSequence(a.Tags, b.Tags, static (x, y) => x == y);
    }

    public static bool SameAuthorCard(ExploreAuthorCard? a, ExploreAuthorCard? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.AuthorId == b.AuthorId
            && string.Equals(a.MainName, b.MainName, StringComparison.Ordinal)
            && string.Equals(a.FolderPath, b.FolderPath, StringComparison.Ordinal)
            && a.TotalWorks == b.TotalWorks
            && a.AllLibraryWorks == b.AllLibraryWorks
            && a.UnreadWorks == b.UnreadWorks
            && a.ReadingWorks == b.ReadingWorks
            && a.CompletedWorks == b.CompletedWorks
            && a.DistinctTagCount == b.DistinctTagCount
            && SameSequence(a.Aliases, b.Aliases, static (x, y) => string.Equals(x, y, StringComparison.Ordinal))
            && SameSequence(a.TopTags, b.TopTags, static (x, y) => x == y)
            && SameSequence(a.RepresentativeWorks, b.RepresentativeWorks, SameWork);
    }

    public static bool SameThemeCard(ExploreThemeCard? a, ExploreThemeCard? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.TagId == b.TagId
            && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
            && string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal)
            && string.Equals(a.Category, b.Category, StringComparison.Ordinal)
            && a.WorkCount == b.WorkCount
            && a.UnreadWorks == b.UnreadWorks
            && SameSequence(a.RepresentativeWorks, b.RepresentativeWorks, SameWork);
    }

    public static bool SameComboCard(ExploreComboCard? a, ExploreComboCard? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.FirstTag == b.FirstTag
            && a.SecondTag == b.SecondTag
            && a.WorkCount == b.WorkCount
            && a.UnreadWorks == b.UnreadWorks
            && SameSequence(a.RepresentativeWorks, b.RepresentativeWorks, SameWork);
    }

    private static bool SameSequence<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b, Func<T, T, bool> equals)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }
}
