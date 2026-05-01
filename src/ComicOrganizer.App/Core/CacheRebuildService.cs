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
    private static readonly string[] AllowedExtensions =
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".zip", ".cbz", ".pdf" };

    public class Progress
    {
        public int Done { get; set; }
        public int Total { get; set; }
        public string CurrentPath { get; set; } = "";
    }

    public CacheRebuildService(AppConfig config, CoverManager coverManager,
        IAssetMetaStore metaStore, IThumbStore thumbStore)
    {
        _config = config;
        _coverManager = coverManager;
        _metaStore = metaStore;
        _thumbStore = thumbStore;
    }

    public Task FullRebuildAsync(IProgress<Progress>? progress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory))
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            _metaStore.Clear();
            _thumbStore.Clear();
            await ScanAsync(_config.RootDirectory, progress, ct);
        }, ct);
    }

    public Task PartialRebuildAsync(string subtreePath, IProgress<Progress>? progress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(subtreePath) || !Directory.Exists(subtreePath))
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            _metaStore.DeleteSubtree(subtreePath);
            // ThumbStore 不掃 subtree（需 path map），留給 LRU 自然淘汰
            await ScanAsync(subtreePath, progress, ct);
        }, ct);
    }

    private async Task ScanAsync(string root, IProgress<Progress>? progress, CancellationToken ct)
    {
        // Phase 1: 列出所有候選 path（folder + 支援副檔名）
        var paths = new List<string>();
        try
        {
            CollectPaths(root, paths, ct);
        }
        catch (OperationCanceledException) { return; }

        var p = new Progress { Total = paths.Count };
        progress?.Report(p);

        // Phase 2: 逐項呼叫 GetCoverAsync (cache miss → 寫 cache)
        for (int i = 0; i < paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = paths[i];
            try
            {
                using var img = await _coverManager.GetCoverAsync(path, CoverManager.ThumbWidth);
                // img 立即釋放，CacheWriter 已經有 jpeg bytes 在 queue 內
            }
            catch { /* 單筆失敗不中斷整體 */ }

            p.Done = i + 1;
            p.CurrentPath = path;
            progress?.Report(p);
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
