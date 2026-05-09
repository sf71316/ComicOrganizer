namespace ComicOrganizer.App.Core;

/// <summary>
/// 從資料夾讀圖：enumerate 直接子檔（不遞迴），用自然排序。
/// 若 caller 傳的是單張圖檔路徑，自動退回該檔的父資料夾並 enumerate 同層 — 仿 Honeyview 行為。
/// </summary>
public class FolderReaderSource : IReaderSource
{
    private static readonly HashSet<string> ImageExts = SupportedExtensions.Image;

    private readonly string[] _files;

    public string DisplayName { get; }
    public string RootPath { get; }
    public int PageCount => _files.Length;

    public FolderReaderSource(string path)
    {
        string folder;
        if (Directory.Exists(path))
        {
            folder = path;
        }
        else if (File.Exists(path))
        {
            // 單檔模式：用該檔父資料夾
            folder = Path.GetDirectoryName(path) ?? path;
        }
        else
        {
            throw new DirectoryNotFoundException($"Path not found: {path}");
        }

        RootPath = folder;
        DisplayName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));

        _files = Directory.EnumerateFiles(folder)
            .Where(f => ImageExts.Contains(Path.GetExtension(f)))
            .OrderBy(f => Path.GetFileName(f), NaturalStringComparer.Instance)
            .ToArray();
    }

    public Stream OpenPage(int index)
    {
        if (index < 0 || index >= _files.Length) throw new ArgumentOutOfRangeException(nameof(index));
        return File.OpenRead(_files[index]);
    }

    public string GetPageExtension(int index) => Path.GetExtension(_files[index]);
    public string GetPageName(int index) => Path.GetFileName(_files[index]);

    public void Dispose() { /* nothing to dispose */ }
}
