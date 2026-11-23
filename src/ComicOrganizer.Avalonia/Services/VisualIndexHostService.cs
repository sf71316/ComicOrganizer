using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ComicOrganizer.VisualSearch;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>
/// 管理獨立的 GPU 建庫子行程（--visual-index-worker）。
/// 建庫完成或使用者按下「停止」後子行程即退出，由 Windows 100% 回收 VRAM，維持主程式待機 0 VRAM。
/// </summary>
public sealed class VisualIndexHostService : IDisposable
{
    public const string WorkerArg = "--visual-index-worker";

    private readonly string _dataDirectory;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private Process? _activeProcess;
    private bool _stopRequested;
    private bool _disposed;

    public VisualIndexHostService(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public bool IsIndexing => _activeProcess != null && !_activeProcess.HasExited;

    public event Action<VisualIndexProgress>? ProgressReceived;
    public event Action<VisualSyncSummary>? Completed;
    public event Action<string>? ErrorReceived;
    public event Action<bool>? RunningStateChanged;

    public async Task<VisualSyncSummary?> RunSyncWorkerAsync(
        string? rootDirectory,
        IReadOnlyList<string>? specificPaths = null,
        bool rebuild = false,
        bool enableHeadIndex = false)
    {
        if (_disposed) return null;
        if (!await _runGate.WaitAsync(0).ConfigureAwait(false))
            return null;

        _stopRequested = false;
        string? pathsTempFile = null;
        VisualSyncSummary? summary = null;

        try
        {
            var paths = new VisualSearchPaths(_dataDirectory);
            if (!paths.AreModelsReady())
            {
                ErrorReceived?.Invoke("尚未備齊 ONNX 模型（請至系統設定頁面下載或匯入模型）。");
                return null;
            }

            if (rebuild)
            {
                ClearVisualIndexFiles(paths);
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                ErrorReceived?.Invoke("無法取得目前執行檔路徑，無法啟動建庫子行程。");
                return null;
            }

            // 支援 dotnet test / vstest 等宿主環境：確保啟動的是獨立 worker 進程
            var psi = new ProcessStartInfo(exePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            psi.ArgumentList.Add(WorkerArg);
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(_dataDirectory);

            if (!string.IsNullOrWhiteSpace(rootDirectory))
            {
                psi.ArgumentList.Add("--source");
                psi.ArgumentList.Add(rootDirectory);
            }

            if (specificPaths is { Count: > 0 })
            {
                pathsTempFile = Path.Combine(Path.GetTempPath(), $"comicorg_vpaths_{Guid.NewGuid():N}.json");
                await File.WriteAllTextAsync(pathsTempFile, JsonSerializer.Serialize(specificPaths), Encoding.UTF8).ConfigureAwait(false);
                psi.ArgumentList.Add("--paths-file");
                psi.ArgumentList.Add(pathsTempFile);
            }

            if (enableHeadIndex)
            {
                psi.ArgumentList.Add("--enable-head-index");
            }

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                ErrorReceived?.Invoke("無法啟動視覺建庫子行程。");
                return null;
            }

            _activeProcess = proc;
            RunningStateChanged?.Invoke(true);

            var stderrTask = proc.StandardError.ReadToEndAsync();
            while (await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith("PROGRESS ", StringComparison.Ordinal))
                {
                    var json = line["PROGRESS ".Length..];
                    var prog = JsonSerializer.Deserialize<VisualIndexProgress>(json);
                    if (prog != null) ProgressReceived?.Invoke(prog);
                }
                else if (line.StartsWith("SUMMARY ", StringComparison.Ordinal))
                {
                    var json = line["SUMMARY ".Length..];
                    summary = JsonSerializer.Deserialize<VisualSyncSummary>(json);
                }
            }

            await proc.WaitForExitAsync().ConfigureAwait(false);
            var errText = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode != 0 && summary == null)
            {
                var msg = string.IsNullOrWhiteSpace(errText)
                    ? $"建庫子行程異常結束（代碼 {proc.ExitCode}）。"
                    : errText.Trim();
                ErrorReceived?.Invoke(msg);
            }
            else if (summary != null)
            {
                Completed?.Invoke(summary);
            }
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke($"視覺建庫失敗：{ex.Message}");
        }
        finally
        {
            _activeProcess = null;
            RunningStateChanged?.Invoke(false);
            if (pathsTempFile != null)
            {
                try { File.Delete(pathsTempFile); } catch { }
            }
            _runGate.Release();
        }

        return summary;
    }

    public void RequestStopGracefully()
    {
        var proc = _activeProcess;
        if (proc == null || proc.HasExited) return;

        if (!_stopRequested)
        {
            _stopRequested = true;
            try
            {
                proc.StandardInput.WriteLine("STOP");
                proc.StandardInput.Flush();
            }
            catch
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
            }
        }
        else
        {
            // 使用者連按兩次才強制終止
            try { proc.Kill(entireProcessTree: true); } catch { }
        }
    }

    private static void ClearVisualIndexFiles(VisualSearchPaths paths)
    {
        try
        {
            if (File.Exists(paths.DatabasePath)) File.Delete(paths.DatabasePath);
            if (File.Exists(paths.DatabasePath + "-wal")) File.Delete(paths.DatabasePath + "-wal");
            if (File.Exists(paths.DatabasePath + "-shm")) File.Delete(paths.DatabasePath + "-shm");
            if (Directory.Exists(paths.ShardsDirectory))
            {
                foreach (var f in Directory.GetFiles(paths.ShardsDirectory, "*.usearch*"))
                {
                    File.Delete(f);
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 獨立 Worker 子行程執行入口（不初始化 Avalonia UI）。
    /// </summary>
    public static async Task<int> RunWorkerEntryAsync(string[] args)
    {
        // OutputType=WinExe 直接從無主控台執行時可能沒有 Console handle，需容錯避免 IOException
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch
        {
        }

        string? dataDir = GetArg(args, "--data-dir");
        string? source = GetArg(args, "--source");
        string? pathsFile = GetArg(args, "--paths-file");

        if (string.IsNullOrWhiteSpace(dataDir))
        {
            await Console.Error.WriteLineAsync("缺少 --data-dir 參數。").ConfigureAwait(false);
            return 2;
        }

        List<string>? specificPaths = null;
        if (!string.IsNullOrWhiteSpace(pathsFile) && File.Exists(pathsFile))
        {
            var raw = await File.ReadAllTextAsync(pathsFile, Encoding.UTF8).ConfigureAwait(false);
            specificPaths = JsonSerializer.Deserialize<List<string>>(raw);
        }

        using var cts = new CancellationTokenSource();
        var stdinWatchTask = Task.Run(async () =>
        {
            try
            {
                while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } cmd)
                {
                    if (string.Equals(cmd.Trim(), "STOP", StringComparison.OrdinalIgnoreCase))
                    {
                        cts.Cancel();
                        break;
                    }
                }
            }
            catch
            {
            }
        });

        try
        {
            var paths = new VisualSearchPaths(dataDir);
            if (!paths.AreModelsReady())
            {
                await Console.Error.WriteLineAsync($"找不到 ONNX 模型：{paths.ModelsDirectory}").ConfigureAwait(false);
                return 3;
            }

            bool enableHeadIndex = args.Any(a => string.Equals(a, "--enable-head-index", StringComparison.OrdinalIgnoreCase));
            var pipeline = new VisualIndexingPipeline(paths, useDirectMl: true, batchSize: 32, enableHeadIndex: enableHeadIndex);
            var stdoutLock = new object();
            var progress = new Progress<VisualIndexProgress>(p =>
            {
                var line = "PROGRESS " + JsonSerializer.Serialize(p);
                lock (stdoutLock)
                {
                    Console.WriteLine(line);
                }
            });

            var summary = await pipeline.SyncAsync(
                source,
                specificPaths,
                progress: progress,
                ct: cts.Token).ConfigureAwait(false);

            var sumLine = "SUMMARY " + JsonSerializer.Serialize(summary);
            lock (stdoutLock)
            {
                Console.WriteLine(sumLine);
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        finally
        {
            cts.Cancel();
            _ = stdinWatchTask;
        }
    }

    private static string? GetArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RequestStopGracefully();
        _runGate.Dispose();
    }
}
