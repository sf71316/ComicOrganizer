namespace ComicOrganizer.App.Core;

public enum ExploreSortMode
{
    WorkCountDesc = 0,
    UnreadDesc = 1,
    RandomShuffle = 2,
    NameAsc = 3,
}

public enum ExploreThemeCategory
{
    ContentThemes = 0,
    ParodiesAndCharacters = 1,
    HotCombos = 2,
}

public sealed record ExploreQueryOptions
{
    public string? SearchText { get; init; }
    public IReadOnlyList<long>? SelectedTagIds { get; init; }
    public bool UnreadOnly { get; init; }
    public ExploreSortMode SortMode { get; init; } = ExploreSortMode.WorkCountDesc;
    public int RandomSeed { get; init; }
    public int MaxCards { get; init; } = 120;
}

public sealed record ExploreTagBadge(
    long TagId,
    string Name,
    string DisplayName,
    string Category,
    int WorkCount,
    bool IsStructural);

public sealed record ExploreWorkItem(
    long AssetId,
    string Path,
    string FileName,
    string Title,
    long? AuthorId,
    string AuthorName,
    string? Language,
    string? TranslationGroup,
    int? PageCount,
    ReadStatus ReadStatus,
    int ReadCount,
    long? LastRead,
    long AddedAt,
    string? ExternalId,
    string? CoverThumbKey,
    IReadOnlyList<ExploreTagBadge> Tags);

public sealed record ExploreAuthorCard(
    long AuthorId,
    string MainName,
    IReadOnlyList<string> Aliases,
    string? FolderPath,
    int TotalWorks,
    int AllLibraryWorks,
    int UnreadWorks,
    int ReadingWorks,
    int CompletedWorks,
    int DistinctTagCount,
    IReadOnlyList<ExploreTagBadge> TopTags,
    IReadOnlyList<ExploreWorkItem> RepresentativeWorks);

public sealed record ExploreThemeCard(
    long TagId,
    string Name,
    string DisplayName,
    string Category,
    int WorkCount,
    int UnreadWorks,
    IReadOnlyList<ExploreWorkItem> RepresentativeWorks);

public sealed record ExploreComboCard(
    ExploreTagBadge FirstTag,
    ExploreTagBadge SecondTag,
    int WorkCount,
    int UnreadWorks,
    IReadOnlyList<ExploreWorkItem> RepresentativeWorks);

public sealed record ExploreStatsSummary(
    int TotalWorks,
    int TotalAuthors,
    int TotalContentTags,
    int TotalParodies,
    int TotalTagsAll,
    int UnreadWorks,
    int SyncedWorks,
    int TranslatedTags);

// ===== 探索牆批次 DB 快照（供 SqliteAssetMetaStore 單趟載入，避免 N+1 查詢）=====

public sealed record ExploreWorkRow(
    long AssetId,
    string Path,
    string FileName,
    long AddedAt,
    long? AuthorId,
    string? AuthorName,
    string? Title,
    string? Language,
    string? TranslationGroup,
    int? PageCount,
    int ReadStatus,
    int ReadCount,
    long? LastRead,
    string? ExternalId,
    string? CoverThumbKey);

public sealed record ExploreAuthorRow(
    long Id,
    string MainName,
    string? FolderPath);

public sealed record ExploreAuthorAliasRow(
    long AuthorId,
    string Name);

public sealed record ExploreTagRow(
    long Id,
    string Name,
    string? Category,
    string? TranslatedName);

public sealed record ExploreAssetTagLink(
    long AssetId,
    long TagId);

public sealed record ExploreTagAliasRow(
    string AliasName,
    long TargetTagId);

public sealed record ExploreCorpusSnapshot(
    IReadOnlyList<ExploreWorkRow> Works,
    IReadOnlyList<ExploreAuthorRow> Authors,
    IReadOnlyList<ExploreAuthorAliasRow> AuthorAliases,
    IReadOnlyList<ExploreTagRow> Tags,
    IReadOnlyList<ExploreAssetTagLink> AssetTags,
    IReadOnlyList<ExploreTagAliasRow> TagAliases);
