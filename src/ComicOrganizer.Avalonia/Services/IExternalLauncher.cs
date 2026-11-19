using System.Diagnostics;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>
/// 外部程式與系統檔案總管啟動抽象（方便無 UI 單元測試抽換）。
/// </summary>
public interface IExternalLauncher
{
    bool OpenWithDefaultApp(string path, out string? errorMessage);
    bool RevealInFileExplorer(string path, out string? errorMessage);
}

public sealed class SystemExternalLauncher : IExternalLauncher
{
    public bool OpenWithDefaultApp(string path, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "作品路徑為空。";
            return false;
        }

        var normalized = NormalizePath(path);
        if (!File.Exists(normalized) && !Directory.Exists(normalized))
        {
            errorMessage = $"找不到目標檔案或資料夾：{normalized}";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(normalized)
            {
                UseShellExecute = true,
            });
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"無法開啟檔案：{ex.Message}";
            return false;
        }
    }

    public bool RevealInFileExplorer(string path, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "作品路徑為空。";
            return false;
        }

        var normalized = NormalizePath(path);
        bool isDirectory = Directory.Exists(normalized);
        if (!isDirectory && !File.Exists(normalized))
        {
            errorMessage = $"找不到目標檔案或資料夾：{normalized}";
            return false;
        }

        try
        {
            var args = BuildExplorerArguments(normalized, isDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", args)
            {
                UseShellExecute = true,
            });
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"無法開啟檔案總管：{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 組合 explorer.exe 啟動參數：資料夾直接開啟該目錄內容；檔案則於父目錄選取該檔案。
    /// </summary>
    public static string BuildExplorerArguments(string path, bool isDirectory)
    {
        var normalized = NormalizePath(path);
        return isDirectory ? $"\"{normalized}\"" : $"/select,\"{normalized}\"";
    }

    private static string NormalizePath(string path)
        => path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
