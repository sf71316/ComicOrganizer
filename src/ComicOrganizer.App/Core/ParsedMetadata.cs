namespace ComicOrganizer.App.Core;

/// <summary>
/// FilenameMetadataParser 的輸出 — 從檔名/資料夾名抽出的所有 metadata 候選欄位。
/// 後續由 orchestrator 寫入 author / asset_meta / tag 表。
/// </summary>
public class ParsedMetadata
{
    /// <summary>主作者名（顯示用）。例：「Flugel」、「Digital Lover」</summary>
    public string? AuthorMainName { get; set; }

    /// <summary>作者別名（雙語名 / 圈內藝術家）。例：「Tiger Roman」、「なかじまゆか」</summary>
    public string? AuthorAlias { get; set; }

    /// <summary>作品名（去除 author / event / 標籤後剩下的部分）。</summary>
    public string? Title { get; set; }

    /// <summary>章節編號。例：「Vol.3」、「第5話」、「Ch.10」</summary>
    public string? ChapterNo { get; set; }

    /// <summary>語言代碼。例："zh-TW" / "zh-CN" / "en" / "ja"。</summary>
    public string? Language { get; set; }

    /// <summary>翻譯 / 漢化組。例：「無邪気漢化組」</summary>
    public string? TranslationGroup { get; set; }

    /// <summary>其他標籤（含 event / parody / generic 等，由 Category 分流）。</summary>
    public List<TagEntry> Tags { get; set; } = new();
}

/// <summary>標籤輸出。Category 為自由字串：event / parody / language / 或 null（未分類）。</summary>
public record TagEntry(string Name, string? Category);
