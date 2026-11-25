using System;
using System.IO;

namespace ComicOrganizer.App.Core;

public static class ArchiveMoveHelper
{
    /// <summary>
    /// 安全地搬移檔案或資料夾至目標目錄。
    /// </summary>
    /// <param name="sourcePath">來源檔案或資料夾完整路徑</param>
    /// <param name="destDir">目標存放資料夾路徑</param>
    /// <param name="targetPath">搬移後的目標完整路徑</param>
    /// <exception cref="InvalidOperationException">當目標路徑為空或來源與目標路徑相同時拋出</exception>
    /// <exception cref="FileNotFoundException">當來源路徑不存在時拋出</exception>
    /// <exception cref="IOException">當目標已存在同名項目或搬移衝突時拋出</exception>
    public static void MoveItemSafely(string sourcePath, string destDir, out string targetPath)
        => MoveItemSafely(sourcePath, destDir, out targetPath, overwrite: false, progressCallback: null);

    /// <summary>
    /// 安全地搬移檔案或資料夾至目標目錄（支援選擇性安全覆蓋與搬移進度回報）。
    /// </summary>
    public static void MoveItemSafely(
        string sourcePath,
        string destDir,
        out string targetPath,
        bool overwrite,
        Action<double>? progressCallback = null)
    {
        if (string.IsNullOrWhiteSpace(destDir))
        {
            throw new InvalidOperationException("目標路徑不可為空");
        }

        string cleanSourcePath = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        bool isDir = Directory.Exists(cleanSourcePath);
        bool isFile = File.Exists(cleanSourcePath);

        if (!isDir && !isFile)
        {
            throw new FileNotFoundException($"來源路徑不存在: {sourcePath}", sourcePath);
        }

        string fileName = Path.GetFileName(cleanSourcePath);
        if (string.IsNullOrEmpty(fileName))
        {
            throw new InvalidOperationException($"無效的來源路徑: {sourcePath}");
        }

        targetPath = Path.Combine(destDir, fileName);

        if (string.Equals(Path.GetFullPath(cleanSourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("來源路徑與目標路徑相同");
        }

        bool destFileExists = File.Exists(targetPath);
        bool destDirExists = Directory.Exists(targetPath);
        bool destExists = destFileExists || destDirExists;

        // 衝突預檢（未指定覆蓋時絕不靜默覆蓋）
        if (destExists && !overwrite)
        {
            throw new IOException($"目標已存在同名項目：{Path.GetFileName(targetPath)}（已安全略過）");
        }

        Directory.CreateDirectory(destDir);
        progressCallback?.Invoke(10.0);

        // 覆蓋時先將既有目標移至暫存備份，新項目完全搬妥後再移除舊檔，避免中途失敗兩頭空
        string? backupPath = null;
        if (destExists)
        {
            backupPath = Path.Combine(destDir, $"{fileName}.__overwrite_bak_{Guid.NewGuid():N}");
            if (destDirExists)
            {
                Directory.Move(targetPath, backupPath);
            }
            else
            {
                File.Move(targetPath, backupPath);
            }
        }

        string sourceRoot = Path.GetPathRoot(Path.GetFullPath(cleanSourcePath)) ?? "";
        string destRoot = Path.GetPathRoot(Path.GetFullPath(destDir)) ?? "";
        bool sameRoot = string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isDir)
            {
                if (sameRoot)
                {
                    progressCallback?.Invoke(35.0);
                    Directory.Move(cleanSourcePath, targetPath);
                    progressCallback?.Invoke(95.0);
                }
                else
                {
                    try
                    {
                        CopyDirectory(cleanSourcePath, targetPath, p =>
                            progressCallback?.Invoke(10.0 + p * 0.8));
                        progressCallback?.Invoke(92.0);
                        Directory.Delete(cleanSourcePath, true);
                    }
                    catch
                    {
                        TryDeleteDirectory(targetPath);
                        throw;
                    }
                }
            }
            else
            {
                if (sameRoot)
                {
                    progressCallback?.Invoke(35.0);
                    File.Move(cleanSourcePath, targetPath);
                    progressCallback?.Invoke(95.0);
                }
                else
                {
                    try
                    {
                        CopyFileWithProgress(cleanSourcePath, targetPath, p =>
                            progressCallback?.Invoke(10.0 + p * 0.8));
                        progressCallback?.Invoke(92.0);
                        File.Delete(cleanSourcePath);
                    }
                    catch
                    {
                        TryDeleteFile(targetPath);
                        throw;
                    }
                }
            }

            // 新目標已就位，安全清除舊目標備份
            if (backupPath != null)
            {
                TryDeleteDirectoryOrFile(backupPath);
            }

            progressCallback?.Invoke(100.0);
        }
        catch
        {
            // 若覆蓋中途失敗，嘗試把舊目標還原回原位
            if (backupPath != null && !File.Exists(targetPath) && !Directory.Exists(targetPath))
            {
                try
                {
                    if (Directory.Exists(backupPath))
                    {
                        Directory.Move(backupPath, targetPath);
                    }
                    else if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, targetPath);
                    }
                }
                catch
                {
                    // 還原失敗時保留 backupPath 以供手動搶救
                }
            }

            throw;
        }
    }

    /// <summary>
    /// 安全地搬移檔案或資料夾至目標目錄並回傳目標完整路徑。
    /// </summary>
    public static string MoveItemSafely(string sourcePath, string destDir)
    {
        MoveItemSafely(sourcePath, destDir, out string targetPath, overwrite: false, progressCallback: null);
        return targetPath;
    }

    /// <summary>
    /// 安全地搬移檔案或資料夾至目標目錄（可指定是否覆蓋並回報進度）。
    /// </summary>
    public static string MoveItemSafely(
        string sourcePath,
        string destDir,
        bool overwrite,
        Action<double>? progressCallback = null)
    {
        MoveItemSafely(sourcePath, destDir, out string targetPath, overwrite, progressCallback);
        return targetPath;
    }

    /// <summary>
    /// 遞迴複製目錄。絕不覆蓋已存在的檔案。
    /// </summary>
    public static void CopyDirectory(string sourceDir, string destDir, Action<double>? progressCallback = null)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"來源目錄不存在: {sourceDir}");

        var allFiles = dir.GetFiles("*", SearchOption.AllDirectories);
        long totalBytes = 0;
        foreach (var f in allFiles)
        {
            totalBytes += Math.Max(1, f.Length);
        }

        long copiedBytes = 0;
        CopyDirectoryInternal(dir, destDir, ref copiedBytes, Math.Max(1, totalBytes), progressCallback);
    }

    private static void CopyDirectoryInternal(
        DirectoryInfo dir,
        string destDir,
        ref long copiedBytes,
        long totalBytes,
        Action<double>? progressCallback)
    {
        Directory.CreateDirectory(destDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destDir, file.Name);
            file.CopyTo(targetFilePath, overwrite: false);
            copiedBytes += Math.Max(1, file.Length);
            progressCallback?.Invoke(Math.Clamp(copiedBytes * 100.0 / totalBytes, 0.0, 100.0));
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string targetSubDirPath = Path.Combine(destDir, subDir.Name);
            CopyDirectoryInternal(subDir, targetSubDirPath, ref copiedBytes, totalBytes, progressCallback);
        }
    }

    private static void CopyFileWithProgress(string sourceFile, string destFile, Action<double>? progressCallback)
    {
        const int bufferSize = 256 * 1024;
        var info = new FileInfo(sourceFile);
        long total = Math.Max(1, info.Length);
        long copied = 0;

        using var src = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
        using var dst = new FileStream(destFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize);
        var buffer = new byte[bufferSize];
        int read;
        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            dst.Write(buffer, 0, read);
            copied += read;
            progressCallback?.Invoke(Math.Clamp(copied * 100.0 / total, 0.0, 100.0));
        }
    }

    private static void TryDeleteDirectoryOrFile(string path)
    {
        if (Directory.Exists(path))
        {
            TryDeleteDirectory(path);
        }
        else if (File.Exists(path))
        {
            TryDeleteFile(path);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // ignore cleanup failure
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore cleanup failure
        }
    }
}
