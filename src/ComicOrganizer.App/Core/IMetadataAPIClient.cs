namespace ComicOrganizer.App.Core;

/// <summary>
/// 外部 metadata 來源抽象介面（#52）。
/// 第一個實作 NhentaiApiClient；未來可加 ehentai / DLsite / Fakku 等。
/// 純讀取，不寫 DB（caller 自行決定如何 merge）。
/// </summary>
public interface IMetadataAPIClient
{
    /// <summary>來源名（"nhentai" / "ehentai" / ...）。寫進 asset_external_source.source。</summary>
    string SourceName { get; }

    /// <summary>用對方平台的 gallery id 直接抓單本完整 metadata。null = 不存在 / 已下架。</summary>
    Task<MetadataLookupResult?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);

    /// <summary>用查詢字串搜尋（標題 / 作者 / 標籤等）。回多筆候選讓 UI picker 選。</summary>
    Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(string query, int page = 1, CancellationToken ct = default);

    /// <summary>驗證 token / 連線。設定頁「測試連線」按鈕用。</summary>
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>單本完整 metadata（GetByExternalIdAsync 回傳）。</summary>
public class MetadataLookupResult
{
    public string Source { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string? MediaId { get; set; }

    /// <summary>乾淨標題（nhentai title.pretty 已去 [社團]/雜誌/[語言]/[DL版]）。</summary>
    public string? TitlePretty { get; set; }
    public string? TitleEnglish { get; set; }
    public string? TitleJapanese { get; set; }

    /// <summary>對方平台 upload date (Unix ts)。可空。</summary>
    public long? UploadAt { get; set; }
    public int? NumPages { get; set; }
    public int? NumFavorites { get; set; }
    public string? Scanlator { get; set; }

    /// <summary>結構化 tags：含 artist / group / parody / character / language / category / tag 等。</summary>
    public List<MetadataTag> Tags { get; set; } = new();

    /// <summary>完整 raw JSON（剝 pages / related 後）— 寫進 asset_external_source.raw_json archive。</summary>
    public string RawJson { get; set; } = "";
}

/// <summary>結構化 tag 單筆。</summary>
public record MetadataTag(string Type, string Name, string Slug, int? Count);

/// <summary>SearchAsync 回傳的精簡 hit（給 picker UI 顯示）。</summary>
public class MetadataSearchHit
{
    public string ExternalId { get; set; } = "";
    public string? TitleEnglish { get; set; }
    public string? TitleJapanese { get; set; }
    public int? NumPages { get; set; }
    public List<int> TagIds { get; set; } = new();
    public string? ThumbnailUrl { get; set; }
}

/// <summary>TestConnectionAsync 結果。</summary>
public record ConnectionTestResult(bool Success, string? UserName, string? ErrorMessage);
