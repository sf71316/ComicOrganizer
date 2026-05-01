namespace ComicOrganizer.App.Core;

/// <summary>
/// 資源（檔案 / 資料夾）在 cache 庫中的 metadata 記錄。
/// 對應 SQLite asset 表一筆 row。
/// </summary>
public class AssetMeta
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long? FileSize { get; set; }

    /// <summary>檔案 mtime（Unix ts，秒）。Cache invalidation 依據。</summary>
    public long Mtime { get; set; }

    /// <summary>cover 來源（cover.jpg / 01.jpg / inside zip 'a.zip!01.jpg' 等），主要用於除錯。</summary>
    public string? CoverOrigin { get; set; }

    /// <summary>thumb 檔的 hash 名（SHA1(absolutePath)[0:40]）。</summary>
    public string? CoverThumbKey { get; set; }

    public int? CoverWidth { get; set; }
    public int? CoverHeight { get; set; }

    /// <summary>上次 cache miss 寫入的時間（Unix ts）。</summary>
    public long LastScanned { get; set; }

    /// <summary>上次讀取時間（Unix ts）。LRU eviction 依據。</summary>
    public long LastAccessed { get; set; }
}
