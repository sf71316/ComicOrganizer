using PDFtoImage;

namespace ComicOrganizer.App.Core;

/// <summary>
/// PDF 圖檔來源（#51 Phase 2-A）：用 PDFtoImage（PDFium + SkiaSharp）即時 render 每一頁成 PNG。
///
/// 與 Folder/Archive 不同 — PDF 沒有「預先存好的 image bytes」，每頁都要 render。
/// 進入時把整份 PDF 讀進 byte[] 常駐記憶體，OpenPage(i) 用 SavePng 直接寫進 MemoryStream。
///
/// PDFium native 非執行緒安全 — 多頁 preload 會並發 OpenPage，內部 lock 序列化。
/// </summary>
public class PdfReaderSource : IReaderSource
{
    private readonly byte[] _bytes;
    private readonly int _pageCount;
    private readonly RenderOptions _renderOptions;
    private readonly object _lock = new();

    public string DisplayName { get; }
    public string RootPath { get; }
    public int PageCount => _pageCount;

    public PdfReaderSource(string pdfPath, int dpi = 200)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"PDF not found: {pdfPath}");

        RootPath = pdfPath;
        DisplayName = Path.GetFileName(pdfPath);
        _bytes = File.ReadAllBytes(pdfPath);
        _pageCount = Conversion.GetPageCount(_bytes);
        _renderOptions = new RenderOptions(Dpi: dpi);
    }

    public Stream OpenPage(int index)
    {
        if (index < 0 || index >= _pageCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        var ms = new MemoryStream();
        lock (_lock)
        {
            Conversion.SavePng(ms, _bytes, page: index, options: _renderOptions);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>render 結果一律是 PNG，給 ReaderImageLoader.Decode 走 GDI+ Image.FromStream 路徑。</summary>
    public string GetPageExtension(int index) => ".png";

    public string GetPageName(int index) => $"Page {index + 1}";

    public void Dispose() { /* byte[] 由 GC 回收，無 native handle 需釋放 */ }
}
