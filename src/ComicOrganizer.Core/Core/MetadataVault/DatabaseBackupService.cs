using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace ComicOrganizer.App.Core;

/// <summary>
/// Phase 5 W5.4 SQLite 安全熱備份與還原服務：
/// - 危險操作（匯入快照、還原備份、清理孤兒、重解析、目錄對帳、作者/標籤合併）前自動備份至 backups/
/// - 透過 <see cref="SqliteAssetMetaStore.BackupToFile"/> 使用 SQLite 線上備份 API，避免 WAL 檔未刷入造成毀損
/// - 一併配對備份 mappings.json，自動維護保留最近 <see cref="DefaultRetainCount"/> 份備份
/// </summary>
public sealed class DatabaseBackupService
{
    public const int DefaultRetainCount = 10;

    private static readonly Regex UnsafeReasonChars = new(@"[^a-zA-Z0-9_\-]+", RegexOptions.Compiled);

    private readonly string _backupsDirectory;
    private readonly string _liveDbPath;
    private readonly string _liveMappingsPath;

    public DatabaseBackupService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _backupsDirectory = Path.Combine(dataDirectory, "backups");
        _liveDbPath = Path.Combine(dataDirectory, "cache.db");
        _liveMappingsPath = Path.Combine(dataDirectory, "mappings.json");
    }

    public string BackupsDirectory => _backupsDirectory;

    public DatabaseBackupEntry CreateSafetyBackup(
        IAssetMetaStore store,
        string reasonTag,
        int retainLatestCount = DefaultRetainCount)
    {
        ArgumentNullException.ThrowIfNull(store);
        Directory.CreateDirectory(_backupsDirectory);

        string safeReason = SanitizeReason(reasonTag);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        string baseName = string.IsNullOrEmpty(safeReason)
            ? $"cache_{timestamp}"
            : $"cache_{timestamp}_{safeReason}";

        string backupDbPath = Path.Combine(_backupsDirectory, baseName + ".db");
        string? mappingsCopyPath = null;

        store.BackupToFile(backupDbPath);

        if (File.Exists(_liveMappingsPath))
        {
            mappingsCopyPath = Path.Combine(_backupsDirectory, baseName + ".mappings.json");
            File.Copy(_liveMappingsPath, mappingsCopyPath, overwrite: true);
        }

        PruneOldBackups(Math.Max(1, retainLatestCount));

        var info = new FileInfo(backupDbPath);
        return new DatabaseBackupEntry(
            FileName: info.Name,
            FullPath: info.FullName,
            MappingsBackupPath: mappingsCopyPath,
            Reason: string.IsNullOrEmpty(safeReason) ? "manual" : safeReason,
            CreatedAtUtc: info.CreationTimeUtc,
            FileSizeBytes: info.Exists ? info.Length : 0);
    }

    public IReadOnlyList<DatabaseBackupEntry> ListBackups()
    {
        if (!Directory.Exists(_backupsDirectory))
        {
            return Array.Empty<DatabaseBackupEntry>();
        }

        var results = new List<DatabaseBackupEntry>();
        foreach (var dbFile in Directory.GetFiles(_backupsDirectory, "cache_*.db"))
        {
            var fi = new FileInfo(dbFile);
            string baseNoExt = Path.GetFileNameWithoutExtension(fi.Name);
            string companionMappings = Path.Combine(_backupsDirectory, baseNoExt + ".mappings.json");
            results.Add(new DatabaseBackupEntry(
                FileName: fi.Name,
                FullPath: fi.FullName,
                MappingsBackupPath: File.Exists(companionMappings) ? companionMappings : null,
                Reason: ExtractReasonFromFileName(baseNoExt),
                CreatedAtUtc: fi.LastWriteTimeUtc,
                FileSizeBytes: fi.Length));
        }

        return results
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void RestoreBackupFilesAfterDispose(string backupDbPath, string? mappingsBackupPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDbPath);
        if (!File.Exists(backupDbPath))
        {
            throw new FileNotFoundException("找不到指定的備份檔案。", backupDbPath);
        }

        SqliteConnection.ClearAllPools();

        // 清除殘留的 WAL / SHM，避免重開後套用舊異動日誌
        TryDeleteFile(_liveDbPath + "-wal");
        TryDeleteFile(_liveDbPath + "-shm");

        File.Copy(backupDbPath, _liveDbPath, overwrite: true);

        string resolvedMappings = mappingsBackupPath
            ?? Path.Combine(
                Path.GetDirectoryName(backupDbPath) ?? _backupsDirectory,
                Path.GetFileNameWithoutExtension(backupDbPath) + ".mappings.json");
        if (File.Exists(resolvedMappings))
        {
            File.Copy(resolvedMappings, _liveMappingsPath, overwrite: true);
        }
    }

    private void PruneOldBackups(int retainLatestCount)
    {
        var all = ListBackups();
        if (all.Count <= retainLatestCount)
        {
            return;
        }

        foreach (var old in all.Skip(retainLatestCount))
        {
            TryDeleteFile(old.FullPath);
            if (!string.IsNullOrWhiteSpace(old.MappingsBackupPath))
            {
                TryDeleteFile(old.MappingsBackupPath);
            }
        }
    }

    private static string SanitizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "backup";
        string cleaned = UnsafeReasonChars.Replace(reason.Trim(), "_").Trim('_');
        return cleaned.Length > 32 ? cleaned[..32] : cleaned;
    }

    private static string ExtractReasonFromFileName(string baseNoExt)
    {
        // cache_20261126_220500_123_before_import -> before_import
        var parts = baseNoExt.Split('_', 5);
        if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
        {
            return parts[4];
        }
        return "backup";
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
        }
    }
}
