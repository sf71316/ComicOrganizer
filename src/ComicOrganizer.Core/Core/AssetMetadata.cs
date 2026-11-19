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

    /// <summary>內建 reader 上次閱讀到的頁碼（0-based）。null = 從未讀過 / 從第一頁開始。</summary>
    public int? LastPage { get; set; }

    /// <summary>使用者手動編輯過 — re-parse / 自動同步流程不得覆寫此筆。</summary>
    public bool ManuallyEdited { get; set; }
}

/// <summary>
/// 顯示用的合併視圖：JOIN asset + asset_meta + author。一次查詢取齊 UI 卡片需要的欄位。
/// 沒 metadata 紀錄時回 null（caller 改 fallback 到檔名顯示）。
/// </summary>
public class AssetMetadataView
{
    public string Path { get; set; } = "";
    public string? Title { get; set; }
    public string? AuthorMainName { get; set; }
    public string? Language { get; set; }
    public string? TranslationGroup { get; set; }
    public int? PageCount { get; set; }
}

/// <summary>
/// 作品詳細資訊聚合視圖 — 一次撈完 catalog + metadata + author + tags。
/// 給 WorkDetailPanel 顯示用。
/// </summary>
public class WorkDetail
{
    public long AssetId { get; set; }
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long FileSize { get; set; }
    public long Mtime { get; set; }

    public string? Title { get; set; }
    public long? AuthorId { get; set; }
    public string? AuthorMainName { get; set; }
    public string? Series { get; set; }
    public string? ChapterNo { get; set; }
    public string? Language { get; set; }
    public string? TranslationGroup { get; set; }
    public int? PageCount { get; set; }
    public int? Rating { get; set; }
    public ReadStatus ReadStatus { get; set; } = ReadStatus.Unread;
    public int ReadCount { get; set; }
    public long? LastRead { get; set; }
    public string? ExternalId { get; set; }
    public long? ParsedAt { get; set; }
    public int? LastPage { get; set; }
    public bool ManuallyEdited { get; set; }

    public List<Tag> Tags { get; set; } = new();
}

/// <summary>Tag + 使用次數（給 Tag 列表頁排序 / 統計用）。</summary>
public class TagUsage
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public int AssetCount { get; set; }
    public int AuthorCount { get; set; }
    public int TotalCount => AssetCount + AuthorCount;
}

/// <summary>Author + 統計（給作者列表頁顯示用）。</summary>
public class AuthorUsage
{
    public Author Author { get; set; } = null!;
    public int WorkCount { get; set; }
    public int AliasCount { get; set; }
    public int TagCount { get; set; }
}

