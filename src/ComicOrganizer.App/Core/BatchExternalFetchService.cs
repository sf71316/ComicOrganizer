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
    private readonly IMetadataAPIClient? _apiClient;
    private readonly TagTranslator? _translator;

    public BatchExternalFetchService(IAssetMetaStore store, IMetadataAPIClient? apiClient = null, TagTranslator? translator = null)
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

        if (_apiClient == null) return BatchItemResult.Skipped(path, "service 未注入 API client");

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

    /// <summary>智慧搜尋批次 per-work：先試 id-fetch，沒命中改用標題搜尋取首筆 + confidence threshold 檢查。
    /// 結果：
    ///   - Applied：高信心自動套用
    ///   - NeedsReview：搜尋結果模糊或 0 筆，caller 應跳 picker / dialog 給 user 處理
    ///   - Skipped：manually_edited / 已綁定 external_id / 資料缺失
    /// </summary>
    public async Task<BatchItemResult> ProcessOneSearchAsync(
        string path, BatchSyncStrategy strategy, string searchQuery,
        int confidenceThreshold = 80, CancellationToken ct = default)
    {
        var assetId = _store.GetAssetId(path);
        if (assetId == null) return BatchItemResult.Skipped(path, "asset 不存在");

        var detail = _store.GetWorkDetail(path);
        if (detail == null) return BatchItemResult.Skipped(path, "GetWorkDetail 失敗");

        if (detail.ManuallyEdited) return BatchItemResult.Skipped(path, "manually_edited=1");
        if (!string.IsNullOrEmpty(detail.ExternalId)) return BatchItemResult.Skipped(path, "已綁定 external_id");
        if (_apiClient == null) return BatchItemResult.Skipped(path, "service 未注入 API client");

        // Step 1: 有 id 直接 fetch
        var externalId = ExtractExternalId(path);
        MetadataLookupResult? result = null;
        if (!string.IsNullOrEmpty(externalId))
        {
            try { result = await _apiClient.GetByExternalIdAsync(externalId, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return BatchItemResult.Error(path, $"id-fetch 失敗：{ex.Message}"); }
            if (result != null)
            {
                var merge = ApplyStrategy(detail, result, strategy);
                return BatchItemResult.Applied(path, externalId, merge);
            }
        }

        // Step 2: 用標題搜尋
        IReadOnlyList<MetadataSearchHit> hits;
        try { hits = await _apiClient.SearchAsync(searchQuery, page: 1, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return BatchItemResult.Error(path, $"search 失敗：{ex.Message}"); }

        // 0 筆 → caller 處理 (NeedsReview)
        if (hits.Count == 0)
            return BatchItemResult.NeedsReview(path, "search 0 筆", searchQuery);

        // 1 筆 → 高信心自動套用
        if (hits.Count == 1)
        {
            try { result = await _apiClient.GetByExternalIdAsync(hits[0].ExternalId, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return BatchItemResult.Error(path, ex.Message); }
            if (result == null) return BatchItemResult.NeedsReview(path, "search 結果 fetch 失敗", searchQuery);
            var merge = ApplyStrategy(detail, result, strategy);
            return BatchItemResult.Applied(path, hits[0].ExternalId, merge);
        }

        // 多筆 → fuzzy compare 首筆 title 跟 query，分數高才自動套
        var firstTitle = hits[0].TitleEnglish ?? hits[0].TitleJapanese ?? "";
        var score = FuzzySharp.Fuzz.WeightedRatio(searchQuery, firstTitle);
        if (score < confidenceThreshold)
            return BatchItemResult.NeedsReview(path,
                $"首筆相似度 {score}% 低於 {confidenceThreshold}%（{hits.Count} 筆候選）", searchQuery);

        // 高信心 → 取首筆套用
        try { result = await _apiClient.GetByExternalIdAsync(hits[0].ExternalId, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return BatchItemResult.Error(path, ex.Message); }
        if (result == null) return BatchItemResult.NeedsReview(path, "首筆 fetch 失敗", searchQuery);

        var merge2 = ApplyStrategy(detail, result, strategy);
        return BatchItemResult.Applied(path, hits[0].ExternalId, merge2);
    }

    /// <summary>把 MergeResult 寫進 DB — 從 GalleryControl 抽出來，給單本/批次/Phase I 共用。
    /// 純 store 操作，無 UI 相依，可從任何 caller 呼叫。
    /// 套完 manually_edited 永遠設為 1（鎖住避免 re-parse 反轉）。</summary>
    public void ApplyMergeResult(WorkDetail detail, MergeResult result)
    {
        // 解析作者：result.AuthorMainName 有值 → upsert；null → 保留既有
        long? authorId = detail.AuthorId;
        if (!string.IsNullOrWhiteSpace(result.AuthorMainName))
        {
            authorId = _store.UpsertAuthor(result.AuthorMainName.Trim());
            // #53 Phase 6: 主名翻譯成 CJK 後，原 slug 進 alias 留著
            if (!string.IsNullOrEmpty(result.AuthorAliasSlug)
                && !string.Equals(result.AuthorAliasSlug, result.AuthorMainName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _store.AddAuthorAlias(authorId.Value, result.AuthorAliasSlug, "en");
            }
        }

        var existing = _store.GetMetadata(detail.AssetId);
        var updated = new AssetMetadata
        {
            AssetId = detail.AssetId,
            AuthorId = authorId,
            Title = result.Title ?? existing?.Title ?? detail.Title,
            Series = result.Series ?? existing?.Series ?? detail.Series,
            ChapterNo = existing?.ChapterNo ?? detail.ChapterNo,
            Language = result.Language ?? existing?.Language ?? detail.Language,
            TranslationGroup = result.TranslationGroup ?? existing?.TranslationGroup ?? detail.TranslationGroup,
            PageCount = result.PageCount ?? existing?.PageCount ?? detail.PageCount,
            Rating = existing?.Rating ?? detail.Rating,
            ReadStatus = existing?.ReadStatus ?? ReadStatus.Unread,
            ReadCount = existing?.ReadCount ?? 0,
            LastRead = existing?.LastRead,
            LastPage = existing?.LastPage,
            ExternalId = result.ExternalSourceRecord?.SourceId ?? existing?.ExternalId,
            ParsedAt = existing?.ParsedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ManuallyEdited = true,
        };
        _store.UpsertMetadata(updated);

        if (result.Tags != null) _store.SetAssetTags(detail.AssetId, result.Tags);
        if (result.ExternalSourceRecord != null) _store.UpsertExternalSource(result.ExternalSourceRecord);
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

/// <summary>單本批次處理結果 — 給 caller 決定 UI 提示 + 是否寫 DB。
/// Query 欄位用於 NeedsReview 時，caller 開 picker 用同一個 search query。</summary>
public record BatchItemResult(string Path, BatchItemStatus Status, string? Message, string? ExternalId, MergeResult? Merge, string? Query = null)
{
    public static BatchItemResult Applied(string path, string externalId, MergeResult merge)
        => new(path, BatchItemStatus.Applied, null, externalId, merge);
    public static BatchItemResult Skipped(string path, string reason)
        => new(path, BatchItemStatus.Skipped, reason, null, null);
    public static BatchItemResult NotFound(string path, string externalId)
        => new(path, BatchItemStatus.NotFound, null, externalId, null);
    public static BatchItemResult Error(string path, string message)
        => new(path, BatchItemStatus.Error, message, null, null);
    /// <summary>智慧搜尋批次的「需 user 介入」狀態 — caller 應開 picker 給 user 處理。</summary>
    public static BatchItemResult NeedsReview(string path, string reason, string query)
        => new(path, BatchItemStatus.NeedsReview, reason, null, null, query);
}

public enum BatchItemStatus { Applied, Skipped, NotFound, Error, NeedsReview }
