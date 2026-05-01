using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 重建 cache 服務：
/// - FullRebuild: Clear DB + thumbs/ → 從 RootDirectory 遞迴掃描，逐項 GetCoverAsync (走 cache miss 路徑寫 cache)
/// - PartialRebuild: DeleteSubtree → 只掃指定子目錄
///
/// 每處理一筆 asset 呼叫 progress.Report，UI 端顯示進度。
/// 支援 CancellationToken 取消。
/// </summary>
public class CacheRebuildService
{
    private readonly AppConfig _config;
    private readonly CoverManager _coverManager;
    private readonly IAssetMetaStore _metaStore;
    private readonly IThumbStore _thumbStore;
    private readonly RebuildProgressTracker _tracker;
    private static readonly string[] AllowedExtensions =
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".zip", ".cbz", ".pdf" };

    public class Progress
    {
        public int Done { get; set; }
        public int Total { get; set; }
        public string CurrentPath { get; set; } = "";
    }

    public CacheRebuildService(AppConfig config, CoverManager coverManager,
        IAssetMetaStore metaStore, IThumbStore thumbStore, RebuildProgressTracker tracker)
    {
        _config = config;
        _coverManager = coverManager;
        _metaStore = metaStore;
        _thumbStore = thumbStore;
        _tracker = tracker;
    }

    /// <summary>完整重建：清空 DB + thumbs/ → 從 RootDirectory 重新掃描。可被取消，
    /// 取消後 cache 部分已寫入；下次呼叫 ResumeScanAsync 可繼續未完成的部分。</summary>
    public Task FullRebuildAsync()
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory))
            return Task.CompletedTask;

        var ct = _tracker.Start("完整重建中");
        return Task.Run(async () =>
        {
            try
            {
                _metaStore.Clear();
                _thumbStore.Clear();
                await ScanAsync(_config.RootDirectory, ct);
                _tracker.NotifyEnd(cancelled: false);
            }
            catch (OperationCanceledException)
            {
                _tracker.NotifyEnd(cancelled: true);
            }
        });
    }

    /// <summary>部分重建：清空指定 subtree → 重新掃描該子樹。</summary>
    public Task PartialRebuildAsync(string subtreePath)
    {
        if (string.IsNullOrEmpty(subtreePath) || !Directory.Exists(subtreePath))
            return Task.CompletedTask;

        var ct = _tracker.Start("部分重建中");
        return Task.Run(async () =>
        {
            try
            {
                _metaStore.DeleteSubtree(subtreePath);
                await ScanAsync(subtreePath, ct);
                _tracker.NotifyEnd(cancelled: false);
            }
            catch (OperationCanceledException)
            {
                _tracker.NotifyEnd(cancelled: true);
            }
        });
    }

    /// <summary>掃描更新：不清空 DB / thumbs，從 RootDirectory 掃描；已 cache 的會 hit 直接 skip
    /// 未 cache 的會寫入。適合「繼續未完成的重建」與一般增量同步。</summary>
    public Task ResumeScanAsync()
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory))
            return Task.CompletedTask;

        var ct = _tracker.Start("掃描更新中");
        return Task.Run(async () =>
        {
            try
            {
                await ScanAsync(_config.RootDirectory, ct);
                _tracker.NotifyEnd(cancelled: false);
            }
            catch (OperationCanceledException)
            {
                _tracker.NotifyEnd(cancelled: true);
            }
        });
    }

    private async Task ScanAsync(string root, CancellationToken ct)
    {
        // Phase 1: 列出所有候選 path（folder + 支援副檔名）
        var paths = new List<string>();
        try
        {
            CollectPaths(root, paths, ct);
        }
        catch (OperationCanceledException) { throw; }

        var p = new Progress { Total = paths.Count };
        _tracker.Update(p);

        // Phase 2: 逐項呼叫 GetCoverAsync (cache miss → 寫 cache)
        for (int i = 0; i < paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = paths[i];
            try
            {
                using var img = await _coverManager.GetCoverAsync(path, CoverManager.ThumbWidth);
            }
            catch { /* 單筆失敗不中斷整體 */ }

            p.Done = i + 1;
            p.CurrentPath = path;
            _tracker.Update(p);
        }
    }

    private static void CollectPaths(string folder, List<string> output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            // 子目錄一定收
            foreach (var sub in Directory.EnumerateDirectories(folder))
            {
                output.Add(sub);
                CollectPaths(sub, output, ct);
            }
            // 檔案：只收支援副檔名
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (AllowedExtensions.Contains(ext)) output.Add(f);
            }
        }
        catch (UnauthorizedAccessException) { /* 跳過存取拒絕的目錄 */ }
        catch (DirectoryNotFoundException) { /* 已被刪除 */ }
    }
}
