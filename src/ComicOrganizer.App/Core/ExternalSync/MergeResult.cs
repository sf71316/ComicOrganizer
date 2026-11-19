namespace ComicOrganizer.App.Core;

/// <summary>
/// ExternalMetadataMergeDialog 的輸出 — 使用者選定的合併結果。
/// caller 把這個套用到 asset_meta + asset_tag + asset_external_source。
///
/// 各欄位語意：
/// - null = 不變動本地（使用者選擇保留本地）
/// - non-null = 套用此值（使用者選擇外部、或自訂）
/// 例外：Tags 是「使用者選的 tag 名最終列表」，會 SetAssetTags 整批替換。
/// </summary>
public class MergeResult
{
    public string? Title { get; set; }
    public string? AuthorMainName { get; set; }

    /// <summary>nhentai 上的 artist slug（英文 url-friendly key）。AuthorMainName 翻譯成 CJK 後，原 slug
    /// 進 author_alias 留著供搜尋 / API 對應用。null = 沒外部 slug 來源（使用者選保留本地或自訂）。</summary>
    public string? AuthorAliasSlug { get; set; }

    public string? Series { get; set; }
    public string? Language { get; set; }
    public string? TranslationGroup { get; set; }
    public int? PageCount { get; set; }

    /// <summary>使用者選定的最終 tag 名清單（直接套用 SetAssetTags）。null = 不動 tag。</summary>
    public List<string>? Tags { get; set; }

    /// <summary>保留分類 (tag / parody / character / artist / group) 的完整標籤清單；非 null 時優先於 Tags 寫入。</summary>
    public List<TagEntry>? TagEntries { get; set; }

    /// <summary>外部來源 metadata 紀錄（外部 source 永遠覆蓋當前 source 的紀錄）。</summary>
    public AssetExternalSource? ExternalSourceRecord { get; set; }

    /// <summary>批次模式中此選擇是否套用到剩餘 N-1 本（策略樣板）。</summary>
    public bool ApplyToRemaining { get; set; }

    /// <summary>當 ApplyToRemaining=true 時，由 dialog 從本次選擇生成的策略樣板，
    /// 給 BatchExternalFetchService 套用到剩餘作品（自動套用，不再 prompt user）。</summary>
    public BatchSyncStrategy? Strategy { get; set; }
}
