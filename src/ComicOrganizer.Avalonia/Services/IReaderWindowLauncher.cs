using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.ViewModels;
using ComicOrganizer.Avalonia.Views;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>
/// 內建高畫質漫畫閱讀器視窗啟動抽象（便於 ViewModel 單元測試抽換）。
/// </summary>
public interface IReaderWindowLauncher
{
    bool OpenInReader(
        string path,
        string? displayTitle,
        string? authorName,
        Action? onReaderClosed,
        out string? errorMessage);

    bool OpenInReaderAtPage(
        string path,
        int pageIndexZeroBased,
        (float X, float Y, float W, float H)? highlightBox,
        string? displayTitle,
        string? authorName,
        Action? onReaderClosed,
        out string? errorMessage)
        => OpenInReader(path, displayTitle, authorName, onReaderClosed, out errorMessage);
}

/// <summary>
/// 在 Avalonia 視窗執行環境建立並顯示獨立 <see cref="ReaderWindow"/>。
/// </summary>
public sealed class AvaloniaReaderWindowLauncher : IReaderWindowLauncher
{
    private readonly AppDataContext _dataContext;

    public AvaloniaReaderWindowLauncher(AppDataContext dataContext)
    {
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
    }

    public bool OpenInReader(
        string path,
        string? displayTitle,
        string? authorName,
        Action? onReaderClosed,
        out string? errorMessage)
        => OpenInReaderInternal(path, null, null, displayTitle, authorName, onReaderClosed, out errorMessage);

    public bool OpenInReaderAtPage(
        string path,
        int pageIndexZeroBased,
        (float X, float Y, float W, float H)? highlightBox,
        string? displayTitle,
        string? authorName,
        Action? onReaderClosed,
        out string? errorMessage)
        => OpenInReaderInternal(path, pageIndexZeroBased, highlightBox, displayTitle, authorName, onReaderClosed, out errorMessage);

    private bool OpenInReaderInternal(
        string path,
        int? initialPageZeroBasedOverride,
        (float X, float Y, float W, float H)? highlightBox,
        string? displayTitle,
        string? authorName,
        Action? onReaderClosed,
        out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "作品路徑為空。";
            return false;
        }

        string normalized = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!File.Exists(normalized) && !Directory.Exists(normalized))
        {
            errorMessage = $"找不到目標檔案或資料夾：{normalized}";
            return false;
        }

        ReaderViewModel? vm = null;
        try
        {
            vm = new ReaderViewModel(
                _dataContext,
                normalized,
                displayTitle,
                authorName,
                initialPageZeroBasedOverride: initialPageZeroBasedOverride,
                initialHighlightBox: highlightBox);
            if (vm.PageCount <= 0)
            {
                vm.Dispose();
                errorMessage = $"此路徑內找不到可閱讀的圖片或壓縮頁：{normalized}";
                return false;
            }

            var win = new ReaderWindow
            {
                DataContext = vm
            };
            if (onReaderClosed != null)
            {
                win.Closed += (_, _) => onReaderClosed();
            }
            win.Show();
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            vm?.Dispose();
            errorMessage = $"無法開啟內建閱讀器：{ex.Message}";
            return false;
        }
    }
}

/// <summary>
/// 無 UI / 測試環境 fallback：轉由 <see cref="IExternalLauncher"/> 開啟外部程式，確保舊測試建構式無需變更即可運作。
/// </summary>
internal sealed class FallbackExternalReaderLauncher : IReaderWindowLauncher
{
    private readonly IExternalLauncher _externalLauncher;

    public FallbackExternalReaderLauncher(IExternalLauncher externalLauncher)
    {
        _externalLauncher = externalLauncher ?? throw new ArgumentNullException(nameof(externalLauncher));
    }

    public bool OpenInReader(
        string path,
        string? displayTitle,
        string? authorName,
        Action? onReaderClosed,
        out string? errorMessage)
    {
        return _externalLauncher.OpenWithDefaultApp(path, out errorMessage);
    }
}
