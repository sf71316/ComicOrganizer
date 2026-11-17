using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>單頁輕量 metadata（供跳頁清單延遲讀取，避免預先解壓）。</summary>
public readonly record struct ReaderPageMetadata(
    int PageIndex,
    string Name,
    long? SizeBytes,
    DateTime? LastModified);

/// <summary>
/// 支援直接產出 <see cref="SKBitmap"/> 的閱讀器來源（例如 PDF）。
/// 可省略先編碼成 PNG 串流再重新解碼的往返開銷；caller 負責 Dispose 回傳的 <see cref="SKBitmap"/>。
/// </summary>
public interface ISkiaBitmapSource
{
    /// <summary>直接產出第 index 頁的 <see cref="SKBitmap"/>（失敗回 null；caller 負責 Dispose）。</summary>
    SKBitmap? OpenPageBitmap(int index);
}

/// <summary>
/// 內建 reader 的圖檔來源抽象。FolderReaderSource / ArchiveReaderSource / (未來) PdfReaderSource
/// 都實作此介面，ReaderForm 不在意背後是哪種來源。
/// 頁面索引從 0 開始，總頁數 PageCount。
/// </summary>
public interface IReaderSource : IDisposable
{
    /// <summary>來源的顯示名稱（給 reader title bar 用）。例如資料夾名 / 壓縮檔名。</summary>
    string DisplayName { get; }

    /// <summary>底層路徑（asset_meta.path 對應的 key，給 RecordRead / last_page 寫入用）。</summary>
    string RootPath { get; }

    /// <summary>總頁數。</summary>
    int PageCount { get; }

    /// <summary>取出第 index 頁的原始 byte stream（caller 負責 Dispose）。</summary>
    Stream OpenPage(int index);

    /// <summary>第 index 頁的副檔名（".jpg" / ".png" / ".webp"…），decoder 選擇用。</summary>
    string GetPageExtension(int index);

    /// <summary>第 index 頁的顯示名稱（檔名或 archive entry name）。</summary>
    string GetPageName(int index);

    /// <summary>取得第 index 頁的輕量 metadata（預設僅回傳檔名）。</summary>
    ReaderPageMetadata GetPageMetadata(int index) => new(index, GetPageName(index), null, null);
}
