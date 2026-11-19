namespace ComicOrganizer.App.Core;

/// <summary>
/// 用檔案系統儲存縮圖：thumbs/<2-char-prefix>/<key>.jpg
/// 寫入走 tmp + rename 確保原子性，避免半檔。
/// </summary>
public class FileSystemThumbStore : IThumbStore
{
    private readonly string _rootDir;

    public FileSystemThumbStore(string rootDir)
    {
        _rootDir = rootDir;
        Directory.CreateDirectory(_rootDir);
    }

    private string PathFor(string thumbKey)
    {
        // 前 2 字元當子目錄，分散到 256 子目錄避免 NTFS 單目錄爆量
        if (thumbKey.Length < 2) throw new ArgumentException("thumbKey 太短", nameof(thumbKey));
        string prefix = thumbKey.Substring(0, 2);
        return Path.Combine(_rootDir, prefix, thumbKey + ".jpg");
    }

    public Stream? OpenRead(string thumbKey)
    {
        var path = PathFor(thumbKey);
        if (!File.Exists(path)) return null;
        try
        {
            return File.OpenRead(path);
        }
        catch
        {
            return null;
        }
    }

    public void Write(string thumbKey, byte[] jpegBytes)
    {
        var path = PathFor(thumbKey);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        // 原子寫：先寫 .tmp，再 rename 蓋過去（避免半檔被讀到）
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, jpegBytes);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    public void Delete(string thumbKey)
    {
        var path = PathFor(thumbKey);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* 忽略 lock 等罕見錯誤 */ }
        }
    }

    public IEnumerable<string> EnumerateAllKeys()
    {
        if (!Directory.Exists(_rootDir)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_rootDir, "*.jpg", SearchOption.AllDirectories);
        }
        catch { yield break; }

        foreach (var f in files)
        {
            // 檔名 = thumbKey + ".jpg"
            var key = Path.GetFileNameWithoutExtension(f);
            if (!string.IsNullOrEmpty(key)) yield return key;
        }
    }

    public long TotalSizeBytes()
    {
        if (!Directory.Exists(_rootDir)) return 0;
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(_rootDir, "*.jpg", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { /* 檔案被刪等 race，忽略 */ }
            }
        }
        catch { }
        return total;
    }

    public void Clear()
    {
        if (!Directory.Exists(_rootDir)) return;
        try
        {
            // 刪掉整個 thumbs 目錄重建
            Directory.Delete(_rootDir, recursive: true);
        }
        catch { /* 個別檔被 lock 時可能失敗，但極少見 */ }
        Directory.CreateDirectory(_rootDir);
    }
}
