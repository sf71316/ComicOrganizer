using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 整庫重 parse metadata。對 asset 表逐筆跑 FilenameMetadataParser，
/// 寫入 asset_meta + 補 author / tag 關聯。
///
/// === 規則 ===
/// - asset_meta.manually_edited = 1 的整筆跳過（保留使用者編輯）
/// - 保留 read_status / read_count / last_read / last_page / last_page
/// - parsed_at 更新為 now
///
/// === Author 解析優先順序 ===
/// 1. parser 純檔名 (asset.name) — filename 自帶 [Author] 多半穩定
/// 2. fallback：parent dir name → 先查 author.folder_path 反查現有作者
///                              → 否則 ParseAuthorFolderName
/// 3. 都無 → asset_meta.author_id = NULL
/// </summary>
public class ReparseAllService
{
    private readonly IAssetMetaStore _metaStore;
    private readonly RebuildProgressTracker _tracker;

    public ReparseAllService(IAssetMetaStore metaStore, RebuildProgressTracker tracker)
    {
        _metaStore = metaStore;
        _tracker = tracker;
    }

    public class Summary
    {
        public int Total { get; set; }
        public int Reparsed { get; set; }
        public int Skipped { get; set; }       // manually_edited = 1
        public int Failed { get; set; }
        public bool Cancelled { get; set; }
    }

    /// <summary>整庫重 parse；走 _tracker 顯示進度，支援取消。</summary>
    public Task<Summary> RunAsync()
    {
        var ct = _tracker.Start("整庫重新解析中");
        return Task.Run(() =>
        {
            var summary = new Summary();
            try
            {
                var entries = _metaStore.ListAssetEntries();
                summary.Total = entries.Count;

                int done = 0;
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    _tracker.Update(new CacheRebuildService.Progress
                    {
                        Done = done,
                        Total = entries.Count,
                        CurrentPath = entry.Path,
                    });

                    try
                    {
                        var existing = _metaStore.GetMetadata(entry.Id);
                        if (existing != null && existing.ManuallyEdited)
                        {
                            summary.Skipped++;
                        }
                        else
                        {
                            ReparseSingle(entry, existing);
                            summary.Reparsed++;
                        }
                    }
                    catch
                    {
                        summary.Failed++;
                    }

                    done++;
                }

                _tracker.NotifyEnd(cancelled: false);
            }
            catch (OperationCanceledException)
            {
                summary.Cancelled = true;
                _tracker.NotifyEnd(cancelled: true);
            }
            return summary;
        }, ct);
    }

    private void ReparseSingle(AssetCatalogEntry entry, AssetMetadata? existing)
    {
        // Pass 1: 純檔名 parse
        var parsed = FilenameMetadataParser.ParseWorkName(entry.Name, authorFromParent: null);

        // Pass 2: filename 沒抓到 author → 用父目錄反查 / 解析
        if (string.IsNullOrEmpty(parsed.AuthorMainName))
        {
            var parentDir = Path.GetDirectoryName(entry.Path);
            if (!string.IsNullOrEmpty(parentDir))
            {
                var existingAuthorId = _metaStore.ResolveAuthorIdByPath(parentDir);
                if (existingAuthorId.HasValue)
                {
                    var existingAuthor = _metaStore.GetAuthorInfo(existingAuthorId.Value)?.Author;
                    if (existingAuthor != null)
                    {
                        parsed.AuthorMainName = existingAuthor.MainName;
                    }
                }
                else
                {
                    var (mainName, alias) = FilenameMetadataParser.ParseAuthorFolderName(
                        Path.GetFileName(parentDir));
                    if (!string.IsNullOrEmpty(mainName))
                    {
                        parsed.AuthorMainName = mainName;
                        if (!string.IsNullOrEmpty(alias) && string.IsNullOrEmpty(parsed.AuthorAlias))
                            parsed.AuthorAlias = alias;
                    }
                }
            }
        }

        // 解析作者名 → author_id（找不到就 upsert）
        long? authorId = null;
        if (!string.IsNullOrWhiteSpace(parsed.AuthorMainName))
        {
            authorId = _metaStore.UpsertAuthor(parsed.AuthorMainName.Trim());
            if (!string.IsNullOrWhiteSpace(parsed.AuthorAlias))
                _metaStore.AddAuthorAlias(authorId.Value, parsed.AuthorAlias.Trim());
        }

        // 寫 asset_meta — 保留閱讀記錄
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var meta = new AssetMetadata
        {
            AssetId = entry.Id,
            AuthorId = authorId,
            Title = parsed.Title,
            ChapterNo = parsed.ChapterNo,
            Language = parsed.Language,
            TranslationGroup = parsed.TranslationGroup,
            Series = existing?.Series,                 // parser 不抽 series，保留
            PageCount = existing?.PageCount,
            Rating = existing?.Rating,
            ReadStatus = existing?.ReadStatus ?? ReadStatus.Unread,
            ReadCount = existing?.ReadCount ?? 0,
            LastRead = existing?.LastRead,
            LastPage = existing?.LastPage,
            ExternalId = existing?.ExternalId,
            ParsedAt = now,
            ManuallyEdited = false,                    // 自動 parse 出來不可能 manually_edited
        };
        _metaStore.UpsertMetadata(meta);

        // 寫 tags — 用 SetAssetTags 整批替換（會清空舊的）
        // 只對非 language / translation 的 tag 寫進 asset_tag（language 等已是 asset_meta 欄位）
        var tagNames = parsed.Tags
            .Where(t => !string.Equals(t.Category, "language", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(t.Category, "translation_group", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .Distinct()
            .ToList();
        _metaStore.SetAssetTags(entry.Id, tagNames);
    }
}
