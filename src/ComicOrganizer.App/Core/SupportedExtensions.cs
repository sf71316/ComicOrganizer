namespace ComicOrganizer.App.Core;

/// <summary>
/// 全 app 支援的檔案副檔名集合 — 集中定義避免散落不一致。
/// 之前 cb7 / tiff 等格式因各檔自帶清單造成行為不同（cover 抽得到但 gallery 不顯示等）。
/// 新增/移除支援格式時只動這裡。
/// 集合一律 OrdinalIgnoreCase，比對前不必額外 ToLower。
/// </summary>
public static class SupportedExtensions
{
    /// <summary>圖片格式 — cover 抽取、reader page 解碼共用。
    /// 故意不收 .tiff/.tif：GDI+ tiff 支援不一致、過去只在 gallery 一處出現造成 reader/cover 失敗。</summary>
    public static readonly HashSet<string> Image = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif",
    };

    /// <summary>歸檔格式 — SharpCompress 支援的範圍。
    /// .cb7 是「Comic Book 7-Zip」，跟 .cbz/.cbr 同類但較少見。</summary>
    public static readonly HashSet<string> Archive = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".cbz", ".rar", ".cbr", ".7z", ".cb7",
    };

    public static readonly HashSet<string> Pdf = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
    };

    /// <summary>Gallery 卡片可顯示的檔案 = Image ∪ Archive ∪ Pdf（資料夾另外處理）。</summary>
    public static readonly HashSet<string> Gallery = new(
        Image.Concat(Archive).Concat(Pdf), StringComparer.OrdinalIgnoreCase);

    /// <summary>內建 reader 可開的歸檔/PDF（單檔圖檔走 FolderReaderSource，不在此集合）。</summary>
    public static readonly HashSet<string> BuiltInReader = new(
        Archive.Concat(Pdf), StringComparer.OrdinalIgnoreCase);

    public static bool IsImage(string path) => Image.Contains(Path.GetExtension(path));
    public static bool IsArchive(string path) => Archive.Contains(Path.GetExtension(path));
    public static bool IsPdf(string path) => Pdf.Contains(Path.GetExtension(path));
}
