namespace ComicOrganizer.App.Core;

/// <summary>
/// 外部來源 metadata 紀錄 — 對應 asset_external_source 表。
/// 一個 asset 可有多筆（不同 source 各一筆）。
/// </summary>
public class AssetExternalSource
{
    public long AssetId { get; set; }

    /// <summary>來源名（"nhentai" / 未來 "ehentai" / "dlsite" 等）。</summary>
    public string Source { get; set; } = "";

    /// <summary>對方平台的 gallery id（nhentai gallery id 是整數但統一用 TEXT，相容字母 id）。</summary>
    public string SourceId { get; set; } = "";

    /// <summary>nhentai 的 media_id（拼 CDN url 用）。其他 source 可空。</summary>
    public string? MediaId { get; set; }

    /// <summary>對方平台上的 upload date（Unix ts）。可空。</summary>
    public long? UploadAt { get; set; }

    /// <summary>完整 metadata JSON 文字（剝 pages / related 後 archive）。
    /// 未來重 import / 換語言 / debug 都從這裡讀，不必重打 API。</summary>
    public string RawJson { get; set; } = "";

    /// <summary>本機抓到此筆資料的 Unix ts。</summary>
    public long FetchedAt { get; set; }
}
