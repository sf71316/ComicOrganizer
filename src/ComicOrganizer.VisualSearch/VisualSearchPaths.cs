namespace ComicOrganizer.VisualSearch;

public sealed record VisualModelStatus(
    string DisplayName,
    string FileName,
    string FullPath,
    bool Exists,
    long SizeBytes,
    string ExpectedSizeHint);

/// <summary>
/// 集中管理視覺搜尋模組的目錄結構、ONNX 模型狀態與下載／匯入工具。
/// 所有檔案皆置於 &lt;DataDirectory&gt;\visual_search\，與主程式 cache.db 完全解耦。
/// </summary>
public sealed class VisualSearchPaths
{
    public const string PanelModelFileName = "panel_detector.onnx";
    public const string EmbedModelFileName = "sscd_disc_mixup.onnx";

    public VisualSearchPaths(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("dataDirectory 不可為空。", nameof(dataDirectory));

        VisualSearchRoot = Path.Combine(dataDirectory, "visual_search");
        ModelsDirectory = Path.Combine(VisualSearchRoot, "models");
        ShardsDirectory = Path.Combine(VisualSearchRoot, "shards");
        DatabasePath = Path.Combine(VisualSearchRoot, "visual_index.db");
    }

    public string VisualSearchRoot { get; }
    public string ModelsDirectory { get; }
    public string ShardsDirectory { get; }
    public string DatabasePath { get; }

    public string PanelModelPath => Path.Combine(ModelsDirectory, PanelModelFileName);
    public string EmbedModelPath => Path.Combine(ModelsDirectory, EmbedModelFileName);

    public string GetShardPath(int shardId) => Path.Combine(ShardsDirectory, $"panels_{shardId:D3}.usearch");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(VisualSearchRoot);
        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(ShardsDirectory);
    }

    public bool AreModelsReady()
        => File.Exists(PanelModelPath) && new FileInfo(PanelModelPath).Length > 1024
        && File.Exists(EmbedModelPath) && new FileInfo(EmbedModelPath).Length > 1024;

    public IReadOnlyList<VisualModelStatus> GetModelStatuses()
    {
        return
        [
            BuildStatus("分鏡偵測模型 (YOLO26n)", PanelModelFileName, PanelModelPath, "~9.3 MB"),
            BuildStatus("特徵提取模型 (Meta SSCD)", EmbedModelFileName, EmbedModelPath, "~93.6 MB"),
        ];
    }

    public void ImportLocalModel(string sourceFilePath, string targetModelFileName)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("找不到來源模型檔。", sourceFilePath);

        if (!string.Equals(targetModelFileName, PanelModelFileName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(targetModelFileName, EmbedModelFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"不支援的模型目標檔名：{targetModelFileName}", nameof(targetModelFileName));
        }

        EnsureDirectories();
        var dest = Path.Combine(ModelsDirectory, targetModelFileName);
        var temp = dest + ".tmp";
        File.Copy(sourceFilePath, temp, overwrite: true);
        File.Move(temp, dest, overwrite: true);
    }

    public async Task DownloadModelAsync(
        string url,
        string targetModelFileName,
        IProgress<(long DownloadedBytes, long? TotalBytes)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("請先設定有效的模型下載網址。", nameof(url));

        EnsureDirectories();
        var dest = Path.Combine(ModelsDirectory, targetModelFileName);
        var temp = dest + ".downloading";

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                downloaded += read;
                progress?.Report((downloaded, totalBytes));
            }
        }

        File.Move(temp, dest, overwrite: true);
    }

    public long GetTotalDiskUsageBytes()
    {
        long total = 0;
        if (File.Exists(DatabasePath)) total += new FileInfo(DatabasePath).Length;
        if (Directory.Exists(ShardsDirectory))
        {
            foreach (var f in Directory.GetFiles(ShardsDirectory, "*.usearch"))
            {
                total += new FileInfo(f).Length;
            }
        }
        return total;
    }

    private static VisualModelStatus BuildStatus(string displayName, string fileName, string fullPath, string hint)
    {
        bool exists = File.Exists(fullPath);
        long len = exists ? new FileInfo(fullPath).Length : 0;
        return new VisualModelStatus(displayName, fileName, fullPath, exists && len > 1024, len, hint);
    }
}
