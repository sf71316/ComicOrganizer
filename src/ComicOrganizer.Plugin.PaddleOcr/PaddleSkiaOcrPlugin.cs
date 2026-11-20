using System;
using System.IO;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.Plugin.PaddleOcr;

/// <summary>
/// 跨平台（無 WinForms / System.Drawing 依賴）之 PaddleOCR 外掛進入點，供 Avalonia 與純 net8.0 核心動態載入。
/// </summary>
public sealed class PaddleSkiaOcrPlugin : ISkiaOcrPlugin
{
    public string PluginId => "PaddleOCR";
    public string DisplayName => "PaddleOCR 離線引擎 (PP-OCRv6)";
    public string Description => "高精準度本機 C++ 深度學習 OCR，支援日文/中文手繪與氣泡辨識";
    public int EngineType => 1;

    public bool IsAvailable
    {
        get
        {
            string inferDir = ResolveInferenceDirectory(null);
            return Directory.Exists(inferDir);
        }
    }

    public string? StatusMessage => IsAvailable ? "已就緒 (PP-OCRv6)" : "模型目錄 (inference/) 遺失";

    public ISkiaOcrService CreateSkiaService(string? modelDir = null)
        => new PaddleSkiaOcrService(ResolveInferenceDirectory(modelDir));

    internal static string ResolveInferenceDirectory(string? modelDir)
    {
        if (!string.IsNullOrWhiteSpace(modelDir) && Directory.Exists(modelDir))
            return modelDir;

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string? loc = typeof(PaddleSkiaOcrPlugin).Assembly.Location;
        string pluginDir = !string.IsNullOrEmpty(loc)
            ? (Path.GetDirectoryName(loc) ?? baseDir)
            : Path.Combine(baseDir, "plugins", "PaddleOCR");

        string[] candidates =
        {
            Path.Combine(pluginDir, "inference"),
            Path.Combine(baseDir, "plugins", "PaddleOCR", "inference"),
            Path.Combine(baseDir, "inference"),
            Path.Combine(Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName ?? baseDir, "plugins", "PaddleOCR", "inference")
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
                return dir;
        }
        return candidates[0];
    }
}
