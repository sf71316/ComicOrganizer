namespace ComicOrganizer.App.Core;

/// <summary>
/// 標籤。對應 tag 表一筆 row。booru-style 多對多，asset_tag 表做關聯。
/// name UNIQUE。
/// </summary>
public class Tag
{
    public long Id { get; set; }

    /// <summary>標籤名（UNIQUE）。範例：「中文」、「DL版」、「無修正」、「校園」</summary>
    public string Name { get; set; } = "";

    /// <summary>分類（自由字串）。常用：character / artist / parody / genre / language / meta。可空。</summary>
    public string? Category { get; set; }
}
