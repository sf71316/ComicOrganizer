using System;
using System.IO;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.Plugin.PaddleOcr;

public class PaddleOcrPlugin : IOcrPlugin
{
    public string PluginId => "PaddleOCR";
    public string DisplayName => "PaddleOCR 離線引擎 (PP-OCRv6)";
    public string Description => "高精準度本機 C++ 深度學習 OCR，支援日文/中文手繪與氣泡辨識";
    public int EngineType => 1;

    public bool IsAvailable
    {
        get
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string? loc = typeof(PaddleOcrPlugin).Assembly.Location;
            string pluginDir = !string.IsNullOrEmpty(loc) ? (Path.GetDirectoryName(loc) ?? baseDir) : Path.Combine(baseDir, "plugins", "PaddleOCR");

            string inferDir = Path.Combine(pluginDir, "inference");
            if (!Directory.Exists(inferDir))
            {
                inferDir = Path.Combine(baseDir, "plugins", "PaddleOCR", "inference");
            }
            if (!Directory.Exists(inferDir))
            {
                inferDir = Path.Combine(baseDir, "inference");
            }
            return Directory.Exists(inferDir);
        }
    }

    public string? StatusMessage => IsAvailable ? "已就緒 (PP-OCRv6)" : "模型目錄 (inference/) 遺失";

    public IOcrService CreateService(string? modelDir = null)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string? loc = typeof(PaddleOcrPlugin).Assembly.Location;
        string pluginDir = !string.IsNullOrEmpty(loc) ? (Path.GetDirectoryName(loc) ?? baseDir) : Path.Combine(baseDir, "plugins", "PaddleOCR");

        string inferDir = modelDir ?? (Directory.Exists(Path.Combine(pluginDir, "inference"))
            ? Path.Combine(pluginDir, "inference")
            : (Directory.Exists(Path.Combine(baseDir, "plugins", "PaddleOCR", "inference"))
                ? Path.Combine(baseDir, "plugins", "PaddleOCR", "inference")
                : Path.Combine(baseDir, "inference")));

        return new PaddleOcrService(inferDir);
    }
}
