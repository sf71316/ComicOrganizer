using PDFtoImage;
using SkiaSharp;

#pragma warning disable CA1416 // PDFtoImage 已跨平台實作 Windows/macOS/Linux；ComicOrganizer.Core 為 net8.0 跨平台核心

namespace ComicOrganizer.App.Core;

/// <summary>
/// PDF 圖檔來源（#51 Phase 2-A）：用 PDFtoImage（PDFium + SkiaSharp）即時 render 每一頁。
///
/// 與 Folder/Archive 不同 — PDF 沒有「預先存好的 image bytes」，每頁都要 render。
/// 進入時把整份 PDF 讀進 byte[] 常駐記憶體，支援透過 <see cref="ISkiaBitmapSource"/> 直接產出 <see cref="SKBitmap"/>
/// （免去 PNG 編解碼往返），並保留 <see cref="OpenPage"/> 相容既有串流呼叫端；
/// Dispose() 時立即釋放 _bytes 參考以回收大型 PDF 常駐記憶體。
///
/// PDFium native 非執行緒安全 — 多頁 preload 會並發存取，內部 lock 序列化。
/// </summary>
public class PdfReaderSource : IReaderSource, ISkiaBitmapSource
{
    private byte[]? _bytes;
    private readonly int _pageCount;
    private readonly RenderOptions _renderOptions;
    private readonly object _lock = new();
    private volatile bool _disposed;

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
        if (_disposed)
            throw new ObjectDisposedException(nameof(PdfReaderSource));
        if (index < 0 || index >= _pageCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        var ms = new MemoryStream();
        lock (_lock)
        {
            if (_disposed || _bytes == null)
                throw new ObjectDisposedException(nameof(PdfReaderSource));
            Conversion.SavePng(ms, _bytes, page: index, options: _renderOptions);
        }
        ms.Position = 0;
        return ms;
    }

    public SKBitmap? OpenPageBitmap(int index)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PdfReaderSource));
        if (index < 0 || index >= _pageCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        lock (_lock)
        {
            if (_disposed || _bytes == null)
                throw new ObjectDisposedException(nameof(PdfReaderSource));
            return Conversion.ToImage(_bytes, page: index, options: _renderOptions);
        }
    }

    /// <summary>render 結果一律是 PNG，給 ReaderImageLoader.Decode 走 GDI+ Image.FromStream 路徑。</summary>
    public string GetPageExtension(int index) => ".png";

    public string GetPageName(int index) => $"Page {index + 1}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _bytes = null;
        }
    }
}
