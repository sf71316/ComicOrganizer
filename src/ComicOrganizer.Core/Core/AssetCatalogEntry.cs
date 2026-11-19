namespace ComicOrganizer.App.Core;

/// <summary>
/// asset 表精簡 row（含 id）— 給需要 id 的批次操作（reconcile / re-parse 等）使用。
/// AssetMeta 不含 id 是歷史包袱；新增 API 走這個 DTO。
/// </summary>
public class AssetCatalogEntry
{
    public long Id { get; set; }
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long? FileSize { get; set; }
    public long Mtime { get; set; }
}
