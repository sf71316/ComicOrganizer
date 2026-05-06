namespace ComicOrganizer.App.Core;

/// <summary>
/// 批次從外部 API 拉 metadata 套用到多個作品（#52 Phase F）。
///
/// 使用流程：
///   1. caller 給 paths + apiClient + applyResult callback
///   2. 第一本：caller 自己彈 MergeDialog（拿到 user 策略）
///   3. caller 再呼叫 ProcessRemainingAsync(strategy, paths[1..]) 跑剩下的
///   4. 每本透過 progress callback 報進度
///
/// 為何不把 dialog 包進來：dialog 跟 UI 強相依（ShowDialog / 主表單 owner），
/// 服務應純後端邏輯。dialog flow 還是 caller (GalleryControl) 主導。
/// </summary>
public class BatchExternalFetchService
{
    private readonly IAssetMetaStore _store;
    private readonly IMetadataAPIClient _apiClient;
    private readonly TagTranslator? _translator;

    public BatchExternalFetchService(IAssetMetaStore store, IMetadataAPIClient apiClient, TagTranslator? translator = null)
    {
        _store = store;
        _apiClient = apiClient;
        _translator = translator;
    }

    /// <summary>單本路徑經過 NhentaiIdExtractor 抽 id。沒抽到 → null（caller 決定要不要 search fallback）。</summary>
    public static string? ExtractExternalId(string path)
    {
        var name = Path.GetFileName(path);
        return NhentaiIdExtractor.Extract(name)
            ?? NhentaiIdExtractor.Extract(Path.GetFileName(Path.GetDirectoryName(path) ?? ""));
    }

    /// <summary>套用策略到單本作品 — 自動拉 + 直接套（不彈 dialog）。
    /// 已 manually_edited 或已綁定 external_id 的作品會跳過。
    /// 沒抽到 id 也跳過（不嘗試 search，避免不確定的 match）。</summary>
    public async Task<BatchItemResult> ProcessOneAsync(string path, BatchSyncStrategy strategy, CancellationToken ct = default)
    {
        var assetId = _store.GetAssetId(path);
        if (assetId == null) return BatchItemResult.Skipped(path, "asset 不存在");

        var detail = _store.GetWorkDetail(path);
        if (detail == null) return BatchItemResult.Skipped(path, "GetWorkDetail 失敗");

        if (detail.ManuallyEdited) return BatchItemResult.Skipped(path, "manually_edited=1，跳過避免覆蓋");

        var externalId = ExtractExternalId(path);
        if (string.IsNullOrEmpty(externalId)) return BatchItemResult.Skipped(path, "檔名無 nhentai id");

        try
        {
            var result = await _apiClient.GetByExternalIdAsync(externalId, ct);
            if (result == null) return BatchItemResult.NotFound(path, externalId);

            var merge = ApplyStrategy(detail, result, strategy);
            return BatchItemResult.Applied(path, externalId, merge);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return BatchItemResult.Error(path, ex.Message);
        }
    }

    /// <summary>把外部結果依策略合併成 MergeResult。caller 用此 result 走跟單本同樣的 ApplyMergeResult 流程寫 DB。</summary>
    public MergeResult ApplyStrategy(WorkDetail local, MetadataLookupResult external, BatchSyncStrategy s)
    {
        var firstArtist = external.Tags.FirstOrDefault(t => t.Type == "artist");
        string? authorDisplay = firstArtist?.Name;
        if (firstArtist != null && _translator != null)
        {
            var cjk = _translator.TranslateArtist(firstArtist.Slug);
            if (!string.IsNullOrEmpty(cjk)) authorDisplay = cjk;
        }

        var result = new MergeResult
        {
            Title = s.TitleSource == FieldSource.External
                ? (external.TitlePretty ?? external.TitleEnglish ?? external.TitleJapanese)
                : null,
            AuthorMainName = s.AuthorSource == FieldSource.External ? authorDisplay : null,
            AuthorAliasSlug = s.AuthorSource == FieldSource.External ? firstArtist?.Slug : null,
            Language = s.LanguageSource == FieldSource.External
                ? external.Tags.FirstOrDefault(t => t.Type == "language" && t.Slug != "translated")?.Name
                : null,
            TranslationGroup = s.TranslationGroupSource == FieldSource.External ? external.Scanlator : null,
            PageCount = s.PageCountSource == FieldSource.External ? external.NumPages : null,
            Tags = BuildTagsPerPolicy(local, external, s.TagsPolicy),
            ExternalSourceRecord = new AssetExternalSource
            {
                AssetId = local.AssetId,
                Source = external.Source,
                SourceId = external.ExternalId,
                MediaId = external.MediaId,
                UploadAt = external.UploadAt,
                RawJson = external.RawJson,
                FetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            },
        };
        return result;
    }

    private static List<string>? BuildTagsPerPolicy(WorkDetail local, MetadataLookupResult external, TagsMergePolicy policy)
    {
        if (policy == TagsMergePolicy.LocalOnly) return null;   // null = 不動 tags

        var localNames = local.Tags.Select(t => t.Name);
        var externalNames = external.Tags
            .Where(t => t.Type != "language" && t.Type != "category")
            .Select(t => t.Name);

        return policy switch
        {
            TagsMergePolicy.AllExternal => externalNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            TagsMergePolicy.UnionBoth   => localNames.Union(externalNames, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => null,
        };
    }
}

/// <summary>單本批次處理結果 — 給 caller 決定 UI 提示 + 是否寫 DB。</summary>
public record BatchItemResult(string Path, BatchItemStatus Status, string? Message, string? ExternalId, MergeResult? Merge)
{
    public static BatchItemResult Applied(string path, string externalId, MergeResult merge)
        => new(path, BatchItemStatus.Applied, null, externalId, merge);
    public static BatchItemResult Skipped(string path, string reason)
        => new(path, BatchItemStatus.Skipped, reason, null, null);
    public static BatchItemResult NotFound(string path, string externalId)
        => new(path, BatchItemStatus.NotFound, null, externalId, null);
    public static BatchItemResult Error(string path, string message)
        => new(path, BatchItemStatus.Error, message, null, null);
}

public enum BatchItemStatus { Applied, Skipped, NotFound, Error }
