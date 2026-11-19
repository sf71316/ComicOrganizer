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

    /// <summary>5 種預設覆蓋策略 — 給「智慧搜尋批次」UI 選擇用，使用者選了就生成對應 BatchSyncStrategy。</summary>
    public static BatchSyncStrategy FromPreset(OverwritePolicyPreset preset) => preset switch
    {
        // A. 全覆蓋 — 全部用外部
        OverwritePolicyPreset.FullOverwrite => new BatchSyncStrategy
        {
            TitleSource = FieldSource.External,
            AuthorSource = FieldSource.External,
            LanguageSource = FieldSource.External,
            TranslationGroupSource = FieldSource.External,
            PageCountSource = FieldSource.External,
            TagsPolicy = TagsMergePolicy.AllExternal,
        },
        // B. 保留標題
        OverwritePolicyPreset.KeepTitle => new BatchSyncStrategy
        {
            TitleSource = FieldSource.Local,
            AuthorSource = FieldSource.External,
            LanguageSource = FieldSource.External,
            TranslationGroupSource = FieldSource.External,
            PageCountSource = FieldSource.External,
            TagsPolicy = TagsMergePolicy.AllExternal,
        },
        // C. 保留作者
        OverwritePolicyPreset.KeepAuthor => new BatchSyncStrategy
        {
            TitleSource = FieldSource.External,
            AuthorSource = FieldSource.Local,
            LanguageSource = FieldSource.External,
            TranslationGroupSource = FieldSource.External,
            PageCountSource = FieldSource.External,
            TagsPolicy = TagsMergePolicy.AllExternal,
        },
        // D. 保留標題與作者
        OverwritePolicyPreset.KeepTitleAndAuthor => new BatchSyncStrategy
        {
            TitleSource = FieldSource.Local,
            AuthorSource = FieldSource.Local,
            LanguageSource = FieldSource.External,
            TranslationGroupSource = FieldSource.External,
            PageCountSource = FieldSource.External,
            TagsPolicy = TagsMergePolicy.AllExternal,
        },
        // E. 只補 tags / language（其餘本地全保留，tags 取聯集）
        OverwritePolicyPreset.OnlyTagsAndLanguage => new BatchSyncStrategy
        {
            TitleSource = FieldSource.Local,
            AuthorSource = FieldSource.Local,
            LanguageSource = FieldSource.External,
            TranslationGroupSource = FieldSource.Local,
            PageCountSource = FieldSource.Local,
            TagsPolicy = TagsMergePolicy.UnionBoth,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };
}

/// <summary>「智慧搜尋批次」mode 的預設覆蓋策略 — UI radio 對應的 enum。</summary>
public enum OverwritePolicyPreset
{
    /// <summary>A. 全部用外部資料 (title / author / language / scanlator / pages / tags 都覆蓋)</summary>
    FullOverwrite,
    /// <summary>B. 保留本地標題，其他覆蓋</summary>
    KeepTitle,
    /// <summary>C. 保留本地作者，其他覆蓋（推薦：庫已分好作者資料夾）</summary>
    KeepAuthor,
    /// <summary>D. 保留本地標題與作者，補 language / scanlator / pages / tags</summary>
    KeepTitleAndAuthor,
    /// <summary>E. 只補 tags / language，其他本地全保留（tags 取本地+外部聯集）</summary>
    OnlyTagsAndLanguage,
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
