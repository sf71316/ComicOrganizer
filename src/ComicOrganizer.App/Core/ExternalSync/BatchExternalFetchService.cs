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

        // Step 2: 用標題+作者搜尋（0 筆時自動以純標題重試）
        IReadOnlyList<MetadataSearchHit> hits;
        string effectiveQuery = searchQuery;
        try { hits = await _apiClient.SearchAsync(effectiveQuery, page: 1, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return BatchItemResult.Error(path, $"search 失敗：{ex.Message}"); }

        if (hits.Count == 0)
        {
            var titleOnly = BuildTitleOnlyQuery(Path.GetFileName(path));
            if (!string.IsNullOrWhiteSpace(titleOnly)
                && !string.Equals(titleOnly, effectiveQuery, StringComparison.OrdinalIgnoreCase))
            {
                effectiveQuery = titleOnly;
                try { hits = await _apiClient.SearchAsync(effectiveQuery, page: 1, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return BatchItemResult.Error(path, $"fallback search 失敗：{ex.Message}"); }
            }
        }

        // 0 筆 → caller 處理 (NeedsReview)
        if (hits.Count == 0)
            return BatchItemResult.NeedsReview(path, "search 0 筆", searchQuery);

        // 評估候選相似度（包含僅 1 筆時），避免標題無關或系列集數錯配（如 後伝3 配到 後伝5）直接誤套用
        var localFilename = Path.GetFileName(path);
        MetadataSearchHit bestHit = hits[0];
        int bestScore = -1;
        foreach (var candidate in hits.Take(5))
        {
            int s = CalculateHitConfidence(localFilename, effectiveQuery, candidate);
            if (s > bestScore)
            {
                bestScore = s;
                bestHit = candidate;
            }
        }
        if (bestScore < confidenceThreshold)
            return BatchItemResult.NeedsReview(path,
                $"最佳候選相似度 {bestScore}% 低於 {confidenceThreshold}%（{hits.Count} 筆候選）", effectiveQuery);

        // 高信心 → 取最高分候選套用
        try { result = await _apiClient.GetByExternalIdAsync(bestHit.ExternalId, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return BatchItemResult.Error(path, ex.Message); }
        if (result == null) return BatchItemResult.NeedsReview(path, "最佳候選 fetch 失敗", effectiveQuery);

        var merge2 = ApplyStrategy(detail, result, strategy);
        return BatchItemResult.Applied(path, bestHit.ExternalId, merge2);
    }

    /// <summary>從檔名建立較高命中率的 nhentai 搜尋字串：僅已知純 ASCII artist slug 才加 artist:，其餘改用關鍵字。</summary>
    public static string BuildSearchQuery(string filename, TagTranslator? translator = null)
    {
        var parsed = FilenameMetadataParser.ParseWorkName(filename);
        var parts = new List<string>();

        string? matchedArtistSlug = null;
        foreach (var candidate in new[] { parsed.AuthorAlias, parsed.AuthorMainName })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var trimmed = candidate.Trim();
            if (IsAscii(trimmed))
            {
                var slug = trimmed.ToLowerInvariant().Replace(' ', '-');
                if (translator != null && translator.IsAvailable && translator.IsKnownArtist(slug))
                {
                    matchedArtistSlug = slug;
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(matchedArtistSlug))
        {
            parts.Add($"artist:{matchedArtistSlug}");
        }
        else if (!string.IsNullOrWhiteSpace(parsed.AuthorAlias))
        {
            parts.Add(parsed.AuthorAlias.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(parsed.AuthorMainName))
        {
            parts.Add(parsed.AuthorMainName.Trim());
        }

        var searchTitle = ExtractSearchableTitle(parsed.Title);
        if (!string.IsNullOrWhiteSpace(searchTitle))
            parts.Add(searchTitle);

        if (parts.Count == 0)
        {
            var fallback = ExtractSearchableTitle(Path.GetFileNameWithoutExtension(filename));
            if (!string.IsNullOrEmpty(fallback)) parts.Add(fallback);
        }

        return string.Join(" ", parts);
    }

    /// <summary>抽出純標題（用於主搜尋 0 筆時之 fallback 查詢）。會剝除雙語 `｜`/`|` 後段譯名與解析度/版本尾碼。</summary>
    public static string BuildTitleOnlyQuery(string filename)
    {
        var parsed = FilenameMetadataParser.ParseWorkName(filename);
        var searchTitle = ExtractSearchableTitle(parsed.Title);
        if (!string.IsNullOrWhiteSpace(searchTitle)) return searchTitle;
        return ExtractSearchableTitle(Path.GetFileNameWithoutExtension(filename));
    }

    private static readonly char[] VolumePrecedingChars = { ' ', '\t', '。', '.', '-', '—', '_', '・', '（', '(' };

    /// <summary>從標題抽出適合搜尋與比對的主標題：若含雙語分隔符 `｜` / `|` 取首段原文，並剝除 `-1280x` 與 `v2` 等尾碼。</summary>
    internal static string ExtractSearchableTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var segment = title.Split(new[] { '｜', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";
        if (string.IsNullOrEmpty(segment)) return "";

        segment = System.Text.RegularExpressions.Regex.Replace(
            segment, @"-\d{3,4}x\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        segment = System.Text.RegularExpressions.Regex.Replace(
            segment, @"\s+v\d+(?:\.\d+)?\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(segment, @"\s+", " ").Trim();
    }

    /// <summary>抽出標題尾端的集數、上下篇或總集編標記，用來避免系列作不同集數誤配。</summary>
    internal static string? ExtractTrailingVolumeToken(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var sb = new System.Text.StringBuilder(title.Length);
        foreach (var ch in title.Trim())
        {
            sb.Append(ch >= '０' && ch <= '９' ? (char)('0' + (ch - '０')) : ch);
        }
        var s = sb.ToString().TrimEnd(' ', '\t', '~', '～', '.', '。', '!', '！', '?', '？', '-', '—', '_').Trim();
        if (string.IsNullOrEmpty(s)) return null;

        if (s.Contains("総集編", StringComparison.OrdinalIgnoreCase)
            || s.Contains("総集", StringComparison.OrdinalIgnoreCase)
            || s.Contains("合集", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(s, @"\bOmnibus\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return "OMNIBUS";
        }

        if (s.EndsWith("前編", StringComparison.Ordinal) || s.EndsWith("前篇", StringComparison.Ordinal) || s.EndsWith("前编", StringComparison.Ordinal))
            return "PART_1";
        if (s.EndsWith("後編", StringComparison.Ordinal) || s.EndsWith("後篇", StringComparison.Ordinal) || s.EndsWith("後编", StringComparison.Ordinal)
            || s.EndsWith("后編", StringComparison.Ordinal) || s.EndsWith("后篇", StringComparison.Ordinal) || s.EndsWith("后编", StringComparison.Ordinal))
            return "PART_2";

        char last = s[^1];
        if (last is '上' or '中' or '下')
        {
            bool validBoundary = s.Length >= 4 || (s.Length >= 2 && Array.IndexOf(VolumePrecedingChars, s[^2]) >= 0);
            if (validBoundary)
            {
                return last switch
                {
                    '上' => "PART_1",
                    '中' => "PART_M",
                    '下' => "PART_2",
                    _ => null,
                };
            }
        }

        var m = System.Text.RegularExpressions.Regex.Match(
            s,
            @"(?:Vol\.?\s*|Ch\.?\s*|Part\.?\s*|No\.?\s*|#)?(\d+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int num))
        {
            if (num >= 1900 && num <= 2099) return null;
            if (num > 999) return null;
            return num.ToString();
        }

        return null;
    }

    /// <summary>同時比對日文原題、英文題與各自剝除括號後的純標題，並檢查尾端集數/上下篇一致度，取 WeightedRatio 作為信心分數。</summary>
    public static int CalculateHitConfidence(string localFilename, string searchQuery, MetadataSearchHit hit)
    {
        var localClean = ExtractSearchableTitle(FilenameMetadataParser.ParseWorkName(localFilename).Title);
        var queryNoField = System.Text.RegularExpressions.Regex.Replace(searchQuery ?? "", @"\b\w+:[^\s]+", " ").Trim();
        var queryClean = ExtractSearchableTitle(queryNoField);

        var hitJa = hit.TitleJapanese?.Trim() ?? "";
        var hitEn = hit.TitleEnglish?.Trim() ?? "";
        var hitJaClean = string.IsNullOrEmpty(hitJa) ? "" : ExtractSearchableTitle(FilenameMetadataParser.ParseWorkName(hitJa).Title);
        var hitEnClean = string.IsNullOrEmpty(hitEn) ? "" : ExtractSearchableTitle(FilenameMetadataParser.ParseWorkName(hitEn).Title);

        var localTargets = new[] { localClean, queryClean }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hitTargets = new[] { hitJaClean, hitEnClean, hitJa, hitEn }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int maxScore = 0;
        foreach (var l in localTargets)
        {
            foreach (var h in hitTargets)
            {
                int score = FuzzySharp.Fuzz.WeightedRatio(l, h);
                if (score > maxScore) maxScore = score;
            }
        }

        var localVol = ExtractTrailingVolumeToken(localClean);
        var hitJaVol = ExtractTrailingVolumeToken(hitJaClean);
        var hitEnVol = ExtractTrailingVolumeToken(hitEnClean);

        if (localVol != null)
        {
            bool matchesVol = string.Equals(hitJaVol, localVol, StringComparison.Ordinal)
                           || string.Equals(hitEnVol, localVol, StringComparison.Ordinal);
            if (!matchesVol)
            {
                maxScore = (hitJaVol != null || hitEnVol != null)
                    ? Math.Min(maxScore, 50)
                    : Math.Min(maxScore, 65);
            }
        }
        else if (hitJaVol != null || hitEnVol != null)
        {
            maxScore = Math.Min(maxScore, 65);
        }

        return maxScore;
    }

    private static bool IsAscii(string s) => s.All(c => c <= 0x7F);

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

        if (result.TagEntries != null)
        {
            _store.SetAssetTagEntries(detail.AssetId, result.TagEntries);
        }
        else if (result.Tags != null)
        {
            _store.SetAssetTags(detail.AssetId, result.Tags);
        }
        if (result.ExternalSourceRecord != null) _store.UpsertExternalSource(result.ExternalSourceRecord);

        // schema v6：寫 tag 後同步翻譯到 tag_translation 表（user 手動的不動）
        var syncNames = result.TagEntries?.Select(t => t.Name).ToList() ?? result.Tags;
        if (syncNames != null && _translator != null && _translator.IsAvailable && syncNames.Count > 0)
        {
            var lang = System.Globalization.CultureInfo.CurrentUICulture.Name;
            // EhTag 是簡體，UI 是繁體環境統一存 zh-TW；其他語系直接用當前 lang
            if (lang.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)) lang = "zh-CN";
            else if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) lang = "zh-TW";
            var idMap = _store.GetTagIdsByNames(syncNames);
            _translator.SyncTranslationsToDb(_store, lang, idMap);
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

        var tagEntries = BuildTagEntriesPerPolicy(local, external, s.TagsPolicy);
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
            TagEntries = tagEntries,
            Tags = tagEntries?.Select(t => t.Name).ToList(),
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

    private static List<TagEntry>? BuildTagEntriesPerPolicy(WorkDetail local, MetadataLookupResult external, TagsMergePolicy policy)
    {
        if (policy == TagsMergePolicy.LocalOnly) return null;   // null = 不動 tags

        var map = new Dictionary<string, TagEntry>(StringComparer.OrdinalIgnoreCase);

        if (policy == TagsMergePolicy.UnionBoth)
        {
            foreach (var lt in local.Tags)
            {
                if (!string.IsNullOrWhiteSpace(lt.Name))
                    map.TryAdd(lt.Name.Trim(), new TagEntry(lt.Name.Trim(), lt.Category));
            }
        }

        // 外部 tag 的 Type (tag / parody / character / artist / group) 直接對應 tag.category
        foreach (var et in external.Tags.Where(t => t.Type != "language" && t.Type != "category"))
        {
            if (string.IsNullOrWhiteSpace(et.Name)) continue;
            var name = et.Name.Trim();
            var cat = string.IsNullOrWhiteSpace(et.Type) ? null : et.Type.Trim();
            map[name] = new TagEntry(name, cat);
        }

        return map.Values.ToList();
    }

    /// <summary>
    /// 全庫無人值守背景自動補齊 nhentai 標籤（不彈視窗）。
    /// 策略：保留本地 Title / Author / TranslationGroup；填補 PageCount 與外部 Tags（UnionBoth + zh-TW 譯文同步）；
    /// Language 僅在本地為空時補齊。已綁定 ExternalId 或 ManuallyEdited 者跳過；信心低於門檻 / 未命中者靜默略過。
    /// 支援透過 tracker 取消並保留進度（已綁定作品下次重跑自動跳過）。
    /// </summary>
    public async Task<LibraryAutoFetchSummary> RunLibraryAutoTagFetchAsync(
        RebuildProgressTracker tracker, int confidenceThreshold = 80)
    {
        var ct = tracker.Start("全庫自動補齊 nhentai 標籤中");
        var summary = new LibraryAutoFetchSummary();

        try
        {
            await Task.Run(async () =>
            {
                var entries = _store.ListAssetEntries()
                    .Where(e => !(e.IsFolder && _store.IsAuthorFolderPath(e.Path))
                                && !(!e.IsFolder && SupportedExtensions.IsImage(e.Path)))
                    .ToList();

                summary.Total = entries.Count;
                tracker.Update(new CacheRebuildService.Progress
                {
                    Done = 0,
                    Total = summary.Total,
                    CurrentPath = "",
                });

                for (int i = 0; i < entries.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = entries[i];

                    var detail = _store.GetWorkDetail(entry.Path);
                    if (detail == null || detail.ManuallyEdited || !string.IsNullOrEmpty(detail.ExternalId))
                    {
                        summary.Skipped++;
                        tracker.Update(new CacheRebuildService.Progress
                        {
                            Done = i + 1,
                            Total = summary.Total,
                            CurrentPath = entry.Path,
                        });
                        continue;
                    }

                    var fileName = Path.GetFileName(entry.Path);
                    var query = BuildSearchQuery(fileName, _translator);
                    if (string.IsNullOrEmpty(ExtractExternalId(entry.Path)) && string.IsNullOrWhiteSpace(query))
                    {
                        summary.Skipped++;
                        tracker.Update(new CacheRebuildService.Progress
                        {
                            Done = i + 1,
                            Total = summary.Total,
                            CurrentPath = entry.Path,
                        });
                        continue;
                    }

                    var strategy = new BatchSyncStrategy
                    {
                        TitleSource = FieldSource.Local,
                        AuthorSource = FieldSource.Local,
                        LanguageSource = string.IsNullOrWhiteSpace(detail.Language)
                            ? FieldSource.External
                            : FieldSource.Local,
                        TranslationGroupSource = FieldSource.Local,
                        PageCountSource = FieldSource.External,
                        TagsPolicy = TagsMergePolicy.UnionBoth,
                    };

                    var itemResult = await ProcessOneSearchAsync(entry.Path, strategy, query, confidenceThreshold, ct);
                    switch (itemResult.Status)
                    {
                        case BatchItemStatus.Applied when itemResult.Merge != null:
                            ApplyMergeResult(detail, itemResult.Merge);
                            summary.Applied++;
                            break;
                        case BatchItemStatus.Skipped:
                            summary.Skipped++;
                            break;
                        case BatchItemStatus.NeedsReview:
                        case BatchItemStatus.NotFound:
                            summary.Unmatched++;
                            break;
                        case BatchItemStatus.Error:
                            summary.Errors++;
                            break;
                    }

                    tracker.Update(new CacheRebuildService.Progress
                    {
                        Done = i + 1,
                        Total = summary.Total,
                        CurrentPath = entry.Path,
                    });
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            summary.Cancelled = true;
        }
        finally
        {
            tracker.NotifyEnd(summary.Cancelled);
        }

        return summary;
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

/// <summary>全庫自動補齊 nhentai 標籤執行摘要。</summary>
public class LibraryAutoFetchSummary
{
    public int Total { get; set; }
    public int Applied { get; set; }
    public int Skipped { get; set; }
    public int Unmatched { get; set; }
    public int Errors { get; set; }
    public bool Cancelled { get; set; }
}
