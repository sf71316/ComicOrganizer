namespace ComicOrganizer.App.Core;

/// <summary>
/// 縮圖檔案儲存介面。production 實作見 FileSystemThumbStore。
/// thumbKey 是 hex 字串（通常 SHA1 前 40 字元）。
/// </summary>
public interface IThumbStore
{
    Stream? OpenRead(string thumbKey);
    void Write(string thumbKey, byte[] jpegBytes);
    void Delete(string thumbKey);

    /// <summary>計算 thumbs/ 整個目錄的總大小（bytes）。</summary>
    long TotalSizeBytes();

    /// <summary>清空所有 thumb 檔。完整重建用。</summary>
    void Clear();
}
