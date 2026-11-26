namespace ComicOrganizer.App.Core;

// ===== Snapshot Corpus (Batch DB Read) =====

public sealed record SnapshotAuthorRecord(string MainName, string? FolderPath, long LearnedAt);

public sealed record SnapshotAuthorAliasRecord(string AuthorMainName, string AliasName, string? Lang);

public sealed record SnapshotAuthorTagRecord(string AuthorMainName, string TagName, string? Category);

public sealed record SnapshotTagRecord(string Name, string? Category);

public sealed record SnapshotTagTranslationRecord(string TagName, string Lang, string TranslatedName, string? Source, long? UpdatedAt);

public sealed record SnapshotTagAliasRecord(string AliasName, string TargetTagName, long CreatedAt);

public sealed record SnapshotBookmarkRecord(int PageIndex, string? Label, long CreatedAt);

public sealed record SnapshotExternalSourceRecord(
    string Source,
    string SourceId,
    string? MediaId,
    long? UploadAt,
    string RawJson,
    long FetchedAt);

public sealed record SnapshotWorkRecord(
    long AssetId,
    string FullPath,
    string FileName,
    bool IsFolder,
    long? FileSize,
    long Mtime,
    long AddedAt,
    string? AuthorMainName,
    string? Title,
    string? Series,
    string? ChapterNo,
    string? Language,
    string? TranslationGroup,
    int? PageCount,
    int? Rating,
    ReadStatus ReadStatus,
    int ReadCount,
    long? LastRead,
    int? LastPage,
    string? ExternalId,
    bool ManuallyEdited,
    long ParsedAt,
    IReadOnlyList<SnapshotTagRecord> Tags,
    IReadOnlyList<SnapshotBookmarkRecord> Bookmarks,
    IReadOnlyList<string> ManualVirtualLibraries,
    IReadOnlyList<SnapshotExternalSourceRecord> ExternalSources);

public sealed record MetadataSnapshotCorpus(
    IReadOnlyList<SnapshotTagRecord> Tags,
    IReadOnlyList<SnapshotTagTranslationRecord> TagTranslations,
    IReadOnlyList<SnapshotTagAliasRecord> TagAliases,
    IReadOnlyList<SnapshotAuthorRecord> Authors,
    IReadOnlyList<SnapshotAuthorAliasRecord> AuthorAliases,
    IReadOnlyList<SnapshotAuthorTagRecord> AuthorTags,
    IReadOnlyList<VirtualLibrary> VirtualLibraries,
    IReadOnlyList<SnapshotWorkRecord> Works);

// ===== Exported JSON Document (Schema v1) =====

public sealed class MetadataSnapshotDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset ExportedAtUtc { get; set; }
    public string SourceRootDirectory { get; set; } = string.Empty;
    public string ExportAppVersion { get; set; } = "ComicOrganizer.Avalonia";
    public bool IncludesExternalSources { get; set; }

    public List<SnapshotTagRecord> Tags { get; set; } = new();
    public List<SnapshotTagTranslationRecord> TagTranslations { get; set; } = new();
    public List<SnapshotTagAliasRecord> TagAliases { get; set; } = new();
    public List<SnapshotAuthorRecord> Authors { get; set; } = new();
    public List<SnapshotAuthorAliasRecord> AuthorAliases { get; set; } = new();
    public List<SnapshotAuthorTagRecord> AuthorTags { get; set; } = new();
    public List<SnapshotVirtualLibraryDto> VirtualLibraries { get; set; } = new();
    public Dictionary<string, string> LearnedMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SnapshotWorkDto> Works { get; set; } = new();
}

public sealed class SnapshotVirtualLibraryDto
{
    public string Name { get; set; } = string.Empty;
    public string Dsl { get; set; } = string.Empty;
    public string? IconSvg { get; set; }
    public int SortOrder { get; set; }
    public bool Pinned { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}

public sealed class SnapshotWorkDto
{
    public string FullPath { get; set; } = string.Empty;
    public string? RelativePath { get; set; }
    public string FileName { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public long? FileSize { get; set; }

    public string? AuthorMainName { get; set; }
    public string? Title { get; set; }
    public string? Series { get; set; }
    public string? ChapterNo { get; set; }
    public string? Language { get; set; }
    public string? TranslationGroup { get; set; }
    public int? PageCount { get; set; }
    public int? Rating { get; set; }
    public ReadStatus ReadStatus { get; set; }
    public int ReadCount { get; set; }
    public long? LastRead { get; set; }
    public int? LastPage { get; set; }
    public string? ExternalId { get; set; }
    public bool ManuallyEdited { get; set; }
    public long ParsedAt { get; set; }

    public List<SnapshotTagRecord> Tags { get; set; } = new();
    public List<SnapshotBookmarkRecord> Bookmarks { get; set; } = new();
    public List<string> ManualVirtualLibraries { get; set; } = new();
    public List<SnapshotExternalSourceRecord> ExternalSources { get; set; } = new();
}

// ===== Options, Import Diff Preview, Apply Result & Unmapped State =====

public sealed record MetadataSnapshotExportOptions(
    bool IncludeExternalSources = false,
    bool IncludeLearnedMappings = true);

public sealed record MetadataSnapshotExportSummary(
    string OutputPath,
    int WorkCount,
    int AuthorCount,
    int TagCount,
    int TagTranslationCount,
    int BookmarkCount,
    int VirtualLibraryCount,
    int LearnedMappingCount);

public sealed record MetadataImportOptions(
    bool OverwriteExistingMetadata = false,
    bool OverwriteReadingProgress = false,
    bool OverwriteTagTranslations = false,
    bool ImportLearnedMappings = true);

public enum WorkMatchTier
{
    Unmatched = 0,
    FullPath = 1,
    RelativePath = 2,
    FileNameAndSize = 3
}

public sealed record MatchedWorkImportPlan(
    SnapshotWorkDto SourceWork,
    long TargetAssetId,
    string TargetPath,
    WorkMatchTier MatchTier,
    bool WillModifyTarget);

public enum UnmappedWorkStatus
{
    Pending = 0,
    Skipped = 1
}

public sealed class UnmappedSnapshotWork
{
    public string ItemId { get; set; } = Guid.NewGuid().ToString("N");
    public UnmappedWorkStatus Status { get; set; } = UnmappedWorkStatus.Pending;
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public SnapshotWorkDto Work { get; set; } = new();
}

public sealed class MetadataImportAnalysisReport
{
    public MetadataSnapshotDocument Document { get; init; } = new();
    public string SourceFilePath { get; init; } = string.Empty;

    public int TagsTotal { get; init; }
    public int TagsToAdd { get; init; }
    public int TagTranslationsToAdd { get; init; }
    public int TagAliasesToAdd { get; init; }
    public int AuthorsToAdd { get; init; }
    public int AuthorAliasesToAdd { get; init; }
    public int VirtualLibrariesToAdd { get; init; }
    public int LearnedMappingsToAdd { get; init; }

    public int WorksTotal { get; init; }
    public int MatchedTier1FullPath { get; init; }
    public int MatchedTier2RelativePath { get; init; }
    public int MatchedTier3FileNameSize { get; init; }
    public int WorksWithUpdates { get; init; }
    public int BookmarksToAdd { get; init; }
    public int UnmatchedCount => UnmatchedWorks.Count;

    public List<MatchedWorkImportPlan> MatchedPlans { get; init; } = new();
    public List<UnmappedSnapshotWork> UnmatchedWorks { get; init; } = new();

    public string BuildSummaryText()
        => $"作品共 {WorksTotal:N0} 筆：精準路徑 {MatchedTier1FullPath:N0} · 相對路徑 {MatchedTier2RelativePath:N0} · 檔名大小 {MatchedTier3FileNameSize:N0} · 未對應 {UnmatchedCount:N0}\n" +
           $"預計更新作品 {WorksWithUpdates:N0} 筆 · 新增標籤 {TagsToAdd:N0} · 譯文 {TagTranslationsToAdd:N0} · 別名 {TagAliasesToAdd + AuthorAliasesToAdd:N0} · " +
           $"作者 {AuthorsToAdd:N0} · 書籤 {BookmarksToAdd:N0} · 虛擬庫 {VirtualLibrariesToAdd:N0} · 歸檔規則 {LearnedMappingsToAdd:N0}";
}

public sealed record MetadataImportApplyResult(
    int TagsAddedOrUpdated,
    int TagTranslationsAddedOrUpdated,
    int TagAliasesAdded,
    int AuthorsAdded,
    int AuthorAliasesAdded,
    int VirtualLibrariesAdded,
    int LearnedMappingsAddedOrUpdated,
    int WorksUpdated,
    int BookmarksAdded,
    int UnmatchedSavedCount);

// ===== Disk Backup Entry (W5.4) =====

public sealed record DatabaseBackupEntry(
    string FileName,
    string FullPath,
    string? MappingsBackupPath,
    string Reason,
    DateTimeOffset CreatedAtUtc,
    long FileSizeBytes)
{
    public bool HasMappingsBackup => !string.IsNullOrWhiteSpace(MappingsBackupPath) && File.Exists(MappingsBackupPath);
}
