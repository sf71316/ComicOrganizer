using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.UI;

/// <summary>
/// GalleryControl — 外部 metadata 同步區（#52 Phase E/F/I）。
/// 從 GalleryControl.cs 抽出 — nhentai fetch / batch sync / merge 套用。
/// </summary>
public partial class GalleryControl
{
    /// <summary>「從 nhentai 拉取」事件 handler — 抽 id（沒就 search 提示）→ API 抓 → merge dialog → 寫 DB。</summary>
    private async Task FetchExternalMetadata(string path)
    {
        if (_metaStore == null || _config == null) return;
        var form = this.FindForm();
        if (form == null) return;

        var apiKey = _config.GetNhentaiApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            AntdUI.Message.warn(form, LocalizationManager.GetString("FetchExtErrNoKey") ?? "請先在「設定 / 外部資料來源」設定 API key");
            return;
        }

        var assetId = _metaStore.GetAssetId(path);
        if (assetId == null) return;
        var detail = _metaStore.GetWorkDetail(path);
        if (detail == null) return;

        // 抽 id：先試從檔名（不含副檔名 + parent dir name）
        var name = Path.GetFileName(path);
        var externalId = NhentaiIdExtractor.Extract(name) ?? NhentaiIdExtractor.Extract(Path.GetFileName(Path.GetDirectoryName(path) ?? ""));

        try
        {
            using var client = new NhentaiApiClient(apiKey);

            MetadataLookupResult? result = null;
            if (!string.IsNullOrEmpty(externalId))
            {
                result = await client.GetByExternalIdAsync(externalId);
                if (result == null)
                {
                    AntdUI.Message.warn(form, string.Format(
                        LocalizationManager.GetString("FetchExtErrNotFoundFmt") ?? "id={0} 在 nhentai 上找不到（可能已下架）",
                        externalId));
                    return;
                }
            }
            else
            {
                // 沒 id → 開 search picker fallback
                var initialQuery = BuildSearchQueryFromFilename(name);
                using var picker = new NhentaiSearchPickerDialog(name, initialQuery,
                    q => client.SearchAsync(q));
                if (picker.ShowDialog(form) != DialogResult.OK || picker.SelectedHit == null) return;
                result = await client.GetByExternalIdAsync(picker.SelectedHit.ExternalId);
                if (result == null)
                {
                    AntdUI.Message.warn(form, string.Format(
                        LocalizationManager.GetString("FetchExtErrNotFoundFmt") ?? "id={0} 在 nhentai 上找不到（可能已下架）",
                        picker.SelectedHit.ExternalId));
                    return;
                }
            }

            // 開 merge dialog（傳 translator 即時顯示中文）
            using var dlg = new ExternalMetadataMergeDialog(detail, result, batchMode: false, translator: _tagTranslator);
            if (dlg.ShowDialog(form) != DialogResult.OK || dlg.Result == null) return;

            ApplyMergeResult(detail, dlg.Result);
            ShowWorkDetail(path);
            RefreshCardReadStatusByPath(path);

            AntdUI.Message.success(form,
                LocalizationManager.GetString("FetchExtDone") ?? "metadata 已套用");
        }
        catch (Exception ex)
        {
            AntdUI.Message.error(form, string.Format(
                LocalizationManager.GetString("FetchExtErrFmt") ?? "拉取失敗：{0}", ex.Message));
        }
    }

    /// <summary>批次同步此作者所有作品（#52 Phase F）。</summary>
    private Task BatchSyncAllForAuthor(long authorId) => BatchSyncForAuthor(authorId, null);

    /// <summary>對單本作品跑跟 Phase E 一樣的單本拉取流程 — 給 AuthorWorksListPanel per-row sync icon 用。</summary>
    public async Task SyncOneWork(string path)
    {
        await FetchExternalMetadata(path);
    }

    /// <summary>Phase I 入口：使用者在「詮釋資料管理 / 作者」勾選作品 → host 委派到此。
    /// paths != null 時用該子集；null 表示「全部」(Phase F 流程)。</summary>
    public async Task BatchSyncForAuthor(long authorId, IReadOnlyList<string>? paths)
    {
        if (_metaStore == null || _config == null) return;
        var form = this.FindForm();
        if (form == null) return;

        var apiKey = _config.GetNhentaiApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            AntdUI.Message.warn(form, LocalizationManager.GetString("FetchExtErrNoKey")
                ?? "請先在「設定 / 外部資料來源」設定 API key");
            return;
        }

        var info = _metaStore.GetAuthorInfo(authorId);
        if (info == null)
        {
            AntdUI.Message.info(form, LocalizationManager.GetString("BatchSyncNoWorks")
                ?? "此作者沒有作品可同步");
            return;
        }

        // 來源 paths：Phase F 用 AllWorks；Phase I 用使用者勾選的子集
        var sourcePaths = paths != null
            ? paths.ToList()
            : info.AllWorks.Select(w => w.Path).ToList();
        if (sourcePaths.Count == 0)
        {
            AntdUI.Message.info(form, LocalizationManager.GetString("BatchSyncNoWorks")
                ?? "此作者沒有作品可同步");
            return;
        }

        // ── 1+2: 篩選 ─────────────────────────────────────────────
        // 沒 id 的不再 skip，會走 search picker fallback（第一本）或在剩餘 batch 中個別 skip
        var eligible = new List<string>();
        var skippedReasons = new Dictionary<string, int>();   // reason → count
        foreach (var path in sourcePaths)
        {
            var detail = _metaStore.GetWorkDetail(path);
            if (detail == null) { Bump(skippedReasons, "資料缺失"); continue; }
            if (detail.ManuallyEdited) { Bump(skippedReasons, "已手動編輯"); continue; }
            if (!string.IsNullOrEmpty(detail.ExternalId)) { Bump(skippedReasons, "已綁定 external_id"); continue; }
            eligible.Add(path);
        }

        if (eligible.Count == 0)
        {
            var detail = string.Join("、", skippedReasons.Select(kv => $"{kv.Key}({kv.Value})"));
            UI.ModalEx.Open(new AntdUI.Modal.Config(form,
                LocalizationManager.GetString("BatchSyncNothingTitle") ?? "沒有可同步的作品",
                $"{sourcePaths.Count} 本全部跳過：{detail}",
                AntdUI.TType.Info));
            return;
        }

        // ── 3: 模式選擇 dialog（取代舊預檢 Modal） ────────────────────────
        BatchSyncMode mode;
        OverwritePolicyPreset policy;
        using (var modeDlg = new BatchSyncModeDialog(info.Author.MainName, sourcePaths.Count, eligible.Count, skippedReasons))
        {
            if (modeDlg.ShowDialog(form) != DialogResult.OK) return;
            mode = modeDlg.SelectedMode;
            policy = modeDlg.SelectedPolicy;
        }

        // 共用的 progress dialog
        var progressDlg = new BatchProgressDialog(eligible.Count);
        progressDlg.Show(form);
        var ct = progressDlg.CancellationToken;

        try
        {
            using var client = new NhentaiApiClient(apiKey);
            if (mode == BatchSyncMode.PerWorkConfirm)
                await BatchSyncPerWorkConfirm(eligible, client, progressDlg, form, ct);
            else
                await BatchSyncSmartBatch(eligible, client, policy, progressDlg, form, ct);
        }
        catch (OperationCanceledException) { /* 取消 — 正常結束 */ }
        finally
        {
            progressDlg.MarkComplete();
        }
    }

    /// <summary>逐一確認模式：每本作品 → fetch (id 或 picker) → MergeDialog → ApplyMergeResult。</summary>
    private async Task BatchSyncPerWorkConfirm(List<string> eligible, NhentaiApiClient client,
        BatchProgressDialog progressDlg, Form form, CancellationToken ct)
    {
        if (_metaStore == null) return;

        foreach (var path in eligible)
        {
            if (ct.IsCancellationRequested) break;
            progressDlg.SetCurrent(Path.GetFileName(path));

            var detail = _metaStore.GetWorkDetail(path);
            if (detail == null)
            {
                progressDlg.AppendResult(BatchItemResult.Skipped(path, "資料缺失"));
                continue;
            }

            MetadataLookupResult? result = null;
            var extId = BatchExternalFetchService.ExtractExternalId(path);

            // 有 id 試 fetch
            if (!string.IsNullOrEmpty(extId))
            {
                try { result = await client.GetByExternalIdAsync(extId, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    progressDlg.AppendResult(BatchItemResult.Error(path, ex.Message));
                    continue;
                }
            }

            // 沒 id 或 fetch 回 null → search picker
            if (result == null)
            {
                var initialQuery = BuildSearchQueryFromFilename(Path.GetFileName(path));
                using var picker = new NhentaiSearchPickerDialog(Path.GetFileName(path), initialQuery,
                    q => client.SearchAsync(q));
                if (picker.ShowDialog(form) != DialogResult.OK || picker.SelectedHit == null)
                {
                    progressDlg.AppendResult(BatchItemResult.Skipped(path, "user 取消 search picker"));
                    continue;
                }
                try { result = await client.GetByExternalIdAsync(picker.SelectedHit.ExternalId, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    progressDlg.AppendResult(BatchItemResult.Error(path, ex.Message));
                    continue;
                }
                if (result == null)
                {
                    progressDlg.AppendResult(BatchItemResult.NotFound(path, picker.SelectedHit.ExternalId));
                    continue;
                }
            }

            // MergeDialog（單本模式 — 無 cbApplyToRemaining）
            using var dlg = new ExternalMetadataMergeDialog(detail, result, batchMode: false, translator: _tagTranslator);
            if (dlg.ShowDialog(form) != DialogResult.OK || dlg.Result == null)
            {
                progressDlg.AppendResult(BatchItemResult.Skipped(path, "user 取消 MergeDialog"));
                continue;
            }
            ApplyMergeResult(detail, dlg.Result);
            RefreshCardReadStatusByPath(path);
            progressDlg.AppendResult(BatchItemResult.Applied(path, result.ExternalId, dlg.Result));
        }
    }

    /// <summary>智慧搜尋批次模式：ProcessOneSearchAsync 自動處理；NeedsReview 時開 picker fallback。</summary>
    private async Task BatchSyncSmartBatch(List<string> eligible, NhentaiApiClient client,
        OverwritePolicyPreset policy, BatchProgressDialog progressDlg, Form form, CancellationToken ct)
    {
        if (_metaStore == null) return;

        var strategy = BatchSyncStrategy.FromPreset(policy);
        var service = new BatchExternalFetchService(_metaStore, client, _tagTranslator);

        foreach (var path in eligible)
        {
            if (ct.IsCancellationRequested) break;
            progressDlg.SetCurrent(Path.GetFileName(path));

            var query = BuildSearchQueryFromFilename(Path.GetFileName(path));
            var res = await service.ProcessOneSearchAsync(path, strategy, query, confidenceThreshold: 80, ct);

            if (res.Status == BatchItemStatus.Applied && res.Merge != null)
            {
                var d = _metaStore.GetWorkDetail(path);
                if (d != null) ApplyMergeResult(d, res.Merge);
                RefreshCardReadStatusByPath(path);
                progressDlg.AppendResult(res);
                continue;
            }

            // NeedsReview → 開 picker 給 user 處理
            if (res.Status == BatchItemStatus.NeedsReview)
            {
                var detail = _metaStore.GetWorkDetail(path);
                if (detail == null) { progressDlg.AppendResult(BatchItemResult.Skipped(path, "資料缺失")); continue; }

                using var picker = new NhentaiSearchPickerDialog(Path.GetFileName(path), res.Query ?? query,
                    q => client.SearchAsync(q));
                if (picker.ShowDialog(form) != DialogResult.OK || picker.SelectedHit == null)
                {
                    progressDlg.AppendResult(BatchItemResult.Skipped(path, $"NeedsReview ({res.Message}) → user 取消"));
                    continue;
                }
                try
                {
                    var fullResult = await client.GetByExternalIdAsync(picker.SelectedHit.ExternalId, ct);
                    if (fullResult == null)
                    {
                        progressDlg.AppendResult(BatchItemResult.NotFound(path, picker.SelectedHit.ExternalId));
                        continue;
                    }
                    var merge = service.ApplyStrategy(detail, fullResult, strategy);
                    ApplyMergeResult(detail, merge);
                    RefreshCardReadStatusByPath(path);
                    progressDlg.AppendResult(BatchItemResult.Applied(path, picker.SelectedHit.ExternalId, merge));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { progressDlg.AppendResult(BatchItemResult.Error(path, ex.Message)); }
                continue;
            }

            // 其他 status (Skipped / NotFound / Error) 直接記
            progressDlg.AppendResult(res);
        }
    }

    private static void Bump(Dictionary<string, int> counter, string key)
    {
        counter.TryGetValue(key, out var v);
        counter[key] = v + 1;
    }

    /// <summary>從檔名建構初始搜尋字串：委派給 BatchExternalFetchService.BuildSearchQuery（避免誤對日文作者加 artist:）。</summary>
    private string BuildSearchQueryFromFilename(string filename)
        => BatchExternalFetchService.BuildSearchQuery(filename, _tagTranslator);

    /// <summary>把 MergeDialog 的結果寫進 DB（asset_meta + asset_tag + asset_external_source）。</summary>
    private void ApplyMergeResult(WorkDetail detail, MergeResult result)
    {
        if (_metaStore == null) return;
        // 委派給 BatchExternalFetchService — 純 store 操作，給單本/批次/Phase I 共用
        var service = new BatchExternalFetchService(_metaStore, translator: _tagTranslator);
        service.ApplyMergeResult(detail, result);
    }
}
