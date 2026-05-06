namespace ComicOrganizer.App.Core;

/// <summary>
/// 批次同步策略樣板（#52 Phase F）— 第一本作品 user 在 MergeDialog
/// 勾「套用到剩餘」後，依當下選擇生成此策略，後續作品依此自動套用，不再彈 dialog。
///
/// 設計：每個 scalar 欄位記「使用者選了 Local 還是 External」；Tags 用合併策略
/// （AllExternal / UnionBoth / LocalOnly）。null = 沒設定該欄位（不動本地）。
/// </summary>
public class BatchSyncStrategy
{
    public FieldSource TitleSource { get; set; }
    public FieldSource AuthorSource { get; set; }
    public FieldSource LanguageSource { get; set; }
    public FieldSource TranslationGroupSource { get; set; }
    public FieldSource PageCountSource { get; set; }
    public TagsMergePolicy TagsPolicy { get; set; }
}

/// <summary>scalar 欄位來源策略 — Local 保留本地 / External 取外部。</summary>
public enum FieldSource
{
    Local,
    External,
}

/// <summary>Tags 合併策略。</summary>
public enum TagsMergePolicy
{
    /// <summary>本地不動。</summary>
    LocalOnly,
    /// <summary>外部 tags 全部覆蓋本地。</summary>
    AllExternal,
    /// <summary>本地 + 外部聯集（保守）。</summary>
    UnionBoth,
}
