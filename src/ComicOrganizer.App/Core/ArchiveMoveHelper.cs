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

        // 衝突預檢（不靜默覆蓋）
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            throw new IOException($"目標已存在同名項目：{Path.GetFileName(targetPath)}（已安全略過）");
        }

        // 先確定目標目錄存在
        Directory.CreateDirectory(destDir);

        string sourceRoot = Path.GetPathRoot(Path.GetFullPath(cleanSourcePath)) ?? "";
        string destRoot = Path.GetPathRoot(Path.GetFullPath(destDir)) ?? "";
        bool sameRoot = string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);

        if (isDir)
        {
            if (sameRoot)
            {
                Directory.Move(cleanSourcePath, targetPath);
            }
            else
            {
                try
                {
                    CopyDirectory(cleanSourcePath, targetPath);
                    Directory.Delete(cleanSourcePath, true);
                }
                catch
                {
                    if (Directory.Exists(targetPath))
                    {
                        try { Directory.Delete(targetPath, true); } catch { /* ignore cleanup failure */ }
                    }
                    throw;
                }
            }
        }
        else // isFile
        {
            if (sameRoot)
            {
                File.Move(cleanSourcePath, targetPath);
            }
            else
            {
                try
                {
                    File.Copy(cleanSourcePath, targetPath, overwrite: false);
                    File.Delete(cleanSourcePath);
                }
                catch
                {
                    if (File.Exists(targetPath))
                    {
                        try { File.Delete(targetPath); } catch { /* ignore cleanup failure */ }
                    }
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// 安全地搬移檔案或資料夾至目標目錄並回傳目標完整路徑。
    /// </summary>
    public static string MoveItemSafely(string sourcePath, string destDir)
    {
        MoveItemSafely(sourcePath, destDir, out string targetPath);
        return targetPath;
    }

    /// <summary>
    /// 遞迴複製目錄。絕不覆蓋已存在的檔案。
    /// </summary>
    public static void CopyDirectory(string sourceDir, string destDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"來源目錄不存在: {sourceDir}");

        Directory.CreateDirectory(destDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destDir, file.Name);
            file.CopyTo(targetFilePath, overwrite: false);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string targetSubDirPath = Path.Combine(destDir, subDir.Name);
            CopyDirectory(subDir.FullName, targetSubDirPath);
        }
    }
}
