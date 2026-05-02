namespace ComicOrganizer.App.Core;

/// <summary>
/// 漫畫 metadata。對應 asset_meta 表一筆 row（與 asset 表 1:1，PK = asset_id）。
/// 由 parser（檔名解析、folder 階層、ComicInfo.xml 等）填入；可被使用者手動修正。
/// </summary>
public class AssetMetadata
{
    public long AssetId { get; set; }

    /// <summary>主作者 → author.id。可空（解析失敗）。</summary>
    public long? AuthorId { get; set; }

    /// <summary>作品名（parser 抽出）。例：「D.L.action 45」</summary>
    public string? Title { get; set; }

    /// <summary>系列名。例：「NIGHTMARE MAKER」（多冊作品的共同系列）</summary>
    public string? Series { get; set; }

    /// <summary>章節編號。例：「Vol.3」、「第5話」。自由字串，不強制格式。</summary>
    public string? ChapterNo { get; set; }

    /// <summary>語言代碼。例："ja" / "en" / "zh-TW"。</summary>
    public string? Language { get; set; }

    /// <summary>翻譯 / 漢化組。例：「無邪気漢化組」</summary>
    public string? TranslationGroup { get; set; }

    /// <summary>頁數（zip 內 image entry 數，或目錄內 image 數）。</summary>
    public int? PageCount { get; set; }

    /// <summary>個人評分 1-5。</summary>
    public int? Rating { get; set; }

    public ReadStatus ReadStatus { get; set; } = ReadStatus.Unread;

    /// <summary>累計閱讀次數。</summary>
    public int ReadCount { get; set; }

    /// <summary>上次閱讀時間（Unix ts）。可空。</summary>
    public long? LastRead { get; set; }

    /// <summary>外部來源 ID（nhentai / e-hentai 等）。預留欄位，未來功能使用。</summary>
    public string? ExternalId { get; set; }

    /// <summary>parser 跑的時間（Unix ts）。重 parse 時更新。</summary>
    public long ParsedAt { get; set; }
}
