namespace ComicOrganizer.App.Core;

/// <summary>
/// 漫畫作者。對應 author 表一筆 row。
/// main_name UNIQUE — 同名 reuse 同一 author_id（誤合機率低，簡單為主）。
/// </summary>
public class Author
{
    public long Id { get; set; }

    /// <summary>主名（顯示用，UNIQUE）。範例：「Flugel」、「タイガー」、「Digital Lover」</summary>
    public string MainName { get; set; } = "";

    /// <summary>偵測來源資料夾路徑。可空。</summary>
    public string? FolderPath { get; set; }

    public long LearnedAt { get; set; }
}

/// <summary>
/// 作者別名。多對一到 Author。例：「Tiger Roman」是「タイガー」的羅馬拼音別名。
/// (author_id, name) UNIQUE — 同一作者不會有重複別名。
/// </summary>
public class AuthorAlias
{
    public long Id { get; set; }
    public long AuthorId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>語言代碼。範例："ja" / "en-romaji" / "zh-TW"。可空。</summary>
    public string? Lang { get; set; }
}
