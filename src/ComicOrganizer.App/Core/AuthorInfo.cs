namespace ComicOrganizer.App.Core;

/// <summary>
/// 作者資訊聚合視圖 — 一次撈完 author + 別名 + 閱讀統計 + top tags + 作品清單。
/// 提供給 AuthorInfoPanel UI 顯示用。實作見 SqliteAssetMetaStore.GetAuthorInfo。
/// </summary>
public class AuthorInfo
{
    public Author Author { get; set; } = null!;
    public List<AuthorAlias> Aliases { get; set; } = new();

    /// <summary>該作者的全部 work 數（asset_meta 中 author_id = 此 id 的 row 數）。</summary>
    public int TotalWorks { get; set; }

    /// <summary>read_status 統計（依 ReadStatus enum 值分組計數）。</summary>
    public int UnreadCount { get; set; }
    public int ReadingCount { get; set; }
    public int CompletedCount { get; set; }
    public int DroppedCount { get; set; }

    /// <summary>該作者全作品累計閱讀次數（SUM(asset_meta.read_count)）。</summary>
    public int TotalReadCount { get; set; }

    /// <summary>該作者作品命中最多的 top N 標籤（依 count desc）。預設 limit=10。</summary>
    public List<TagFreq> TopTags { get; set; } = new();

    /// <summary>最近閱讀過的 5 筆（last_read DESC）。空集合表示尚未開始讀過任何作品。</summary>
    public List<RecentRead> RecentlyRead { get; set; } = new();

    /// <summary>該作者全部 work，依 title 排序（NULL title 排最後）。</summary>
    public List<AssetMetadataView> AllWorks { get; set; } = new();
}

/// <summary>標籤 + 在某作者作品集中出現的次數。</summary>
public record TagFreq(string Name, int Count);

/// <summary>最近閱讀清單項。含 read_count 給 UI 顯示「讀 N 次」。</summary>
public record RecentRead(string Path, string? Title, long? LastRead, int ReadCount);
