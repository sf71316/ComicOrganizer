namespace ComicOrganizer.App.Core;

/// <summary>
/// 整庫 parse 狀態統計（給「詮釋資料管理 / 工具」面板顯示用）。
/// </summary>
public class ParseStats
{
    /// <summary>asset 表總筆數。</summary>
    public int TotalAssets { get; set; }

    /// <summary>asset_meta 有紀錄的筆數（已 parse 過的）。</summary>
    public int ParsedCount { get; set; }

    /// <summary>asset_meta.manually_edited = 1 的筆數（re-parse 會跳過）。</summary>
    public int ManuallyEditedCount { get; set; }

    public int UnparsedCount => TotalAssets - ParsedCount;
    public int AutoParsedCount => ParsedCount - ManuallyEditedCount;

    public double ParsedPercent => TotalAssets > 0 ? ParsedCount * 100.0 / TotalAssets : 0;
}
