using System.Diagnostics;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

/// <summary>
/// 比較同一資料夾內 zip 抓封面 vs 子目錄抓封面的速度。
/// 印 per-item 耗時與 type 平均，方便評估 GetFolderCoverRecursive
/// 的「先 zip 後 subdir」優先級是否合理。
/// </summary>
public class FolderCoverPerfTest
{
    private readonly ITestOutputHelper _output;
    private const string TestRoot = @"D:\Project\ComicOrganize\TestData\Library\flugel";
    private const int Iterations = 10;
    private const int Warmup = 3;
    private const int TargetWidth = 330;

    public FolderCoverPerfTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Compare_Zip_vs_Subdir_CoverExtraction()
    {
        if (!Directory.Exists(TestRoot))
        {
            _output.WriteLine($"Test data missing: {TestRoot} — skip");
            return;
        }

        var manager = new CoverManager(new AppConfig { MaxScanDepth = 5 });

        var zips = Directory.GetFiles(TestRoot)
            .Where(f => Path.GetExtension(f).ToLower() is ".zip" or ".cbz")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var subdirs = Directory.GetDirectories(TestRoot)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _output.WriteLine($"=== Test data ({Path.GetFileName(TestRoot)}) ===");
        _output.WriteLine($"Zips ({zips.Count}):");
        foreach (var z in zips)
        {
            long mb = new FileInfo(z).Length / 1024 / 1024;
            _output.WriteLine($"  {Path.GetFileName(z),-65} {mb,4} MB");
        }
        _output.WriteLine($"Subdirs ({subdirs.Count}):");
        foreach (var d in subdirs)
        {
            int n = Directory.GetFiles(d).Length;
            _output.WriteLine($"  {Path.GetFileName(d),-65} {n,4} files");
        }
        _output.WriteLine("");

        // Warmup（暖 OS file cache，避免冷快取偏差）
        for (int w = 0; w < Warmup; w++)
        {
            foreach (var z in zips) (await manager.GetCoverAsync(z, TargetWidth))?.Dispose();
            foreach (var d in subdirs) (await manager.GetCoverAsync(d, TargetWidth))?.Dispose();
        }

        // 個別計時 (warm cache)
        _output.WriteLine($"=== ZIP cover extraction (warm cache, x{Iterations} iter) ===");
        var zipTimes = new List<double>();
        foreach (var z in zips)
        {
            double avg = await MeasureMs(() => manager.GetCoverAsync(z, TargetWidth));
            zipTimes.Add(avg);
            _output.WriteLine($"  {Path.GetFileName(z),-65} {avg,7:F2} ms");
        }
        _output.WriteLine($"  ─ avg: {(zipTimes.Count > 0 ? zipTimes.Average() : 0),7:F2} ms");
        _output.WriteLine("");

        _output.WriteLine($"=== SUBDIR cover extraction (warm cache, x{Iterations} iter) ===");
        var subdirTimes = new List<double>();
        foreach (var d in subdirs)
        {
            double avg = await MeasureMs(() => manager.GetCoverAsync(d, TargetWidth));
            subdirTimes.Add(avg);
            _output.WriteLine($"  {Path.GetFileName(d),-65} {avg,7:F2} ms");
        }
        _output.WriteLine($"  ─ avg: {(subdirTimes.Count > 0 ? subdirTimes.Average() : 0),7:F2} ms");
        _output.WriteLine("");

        // 結論摘要
        if (zipTimes.Count > 0 && subdirTimes.Count > 0)
        {
            double zipAvg = zipTimes.Average();
            double subAvg = subdirTimes.Average();
            string winner = zipAvg < subAvg ? "ZIP" : "SUBDIR";
            double ratio = Math.Max(zipAvg, subAvg) / Math.Min(zipAvg, subAvg);
            _output.WriteLine($"=== Verdict ===");
            _output.WriteLine($"Zip avg     : {zipAvg,7:F2} ms");
            _output.WriteLine($"Subdir avg  : {subAvg,7:F2} ms");
            _output.WriteLine($"Winner: {winner} (faster by {ratio:F2}x)");
        }
    }

    private async Task<double> MeasureMs(Func<Task<System.Drawing.Image?>> factory)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            using var img = await factory();
        }
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds / Iterations;
    }
}
