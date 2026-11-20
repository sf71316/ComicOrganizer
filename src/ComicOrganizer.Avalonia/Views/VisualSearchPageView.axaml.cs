using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ComicOrganizer.Avalonia.ViewModels;
using SkiaSharp;

namespace ComicOrganizer.Avalonia.Views;

public partial class VisualSearchPageView : UserControl
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    public VisualSearchPageView()
    {
        InitializeComponent();

        KeyDown += OnViewKeyDown;
        if (this.FindControl<Border>("DropTargetBorder") is { } dropBorder)
        {
            dropBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            dropBorder.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    private async void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await PasteFromClipboardAsync();
        }
    }

    private async void OnPickImageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VisualSearchPageViewModel vm) return;
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null || !storageProvider.CanOpen) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "選擇要圖搜漫的截圖檔案",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("圖片檔") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"] }
            ],
        });

        var selected = files.FirstOrDefault();
        var localPath = selected?.TryGetLocalPath() ?? selected?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            var bytes = await File.ReadAllBytesAsync(localPath);
            await vm.SearchFromImageBytesAsync(bytes, Path.GetFileName(localPath));
        }
    }

    private async void OnPasteClipboardClicked(object? sender, RoutedEventArgs e)
        => await PasteFromClipboardAsync();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not VisualSearchPageViewModel vm) return;
        var file = e.DataTransfer.TryGetFile();
        var path = file?.TryGetLocalPath() ?? file?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (!ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return;

        var bytes = await File.ReadAllBytesAsync(path);
        await vm.SearchFromImageBytesAsync(bytes, Path.GetFileName(path));
    }

    private async Task PasteFromClipboardAsync()
    {
        if (DataContext is not VisualSearchPageViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;

        // 1. 優先檢查剪貼簿圖片檔案路徑
        var file = await clipboard.TryGetFileAsync();
        var filePath = file?.TryGetLocalPath() ?? file?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(filePath)
            && File.Exists(filePath)
            && ImageExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            await vm.SearchFromImageBytesAsync(fileBytes, Path.GetFileName(filePath));
            return;
        }

        // 2. 直接讀取系統截圖工具 (Win+Shift+S / PrtSc) 置於剪貼簿的點陣圖
        using var bmp = await clipboard.TryGetBitmapAsync();
        if (bmp != null)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms);
            await vm.SearchFromImageBytesAsync(ms.ToArray(), "剪貼簿截圖");
        }
    }
}
