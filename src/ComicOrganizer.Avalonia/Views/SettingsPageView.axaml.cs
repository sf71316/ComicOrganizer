using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia.Views;

public partial class SettingsPageView : UserControl
{
    private readonly Func<string, string?, Task<string?>> _pickFolderHandler;
    private readonly Func<string, string?, string, IReadOnlyList<string>, Task<string?>> _pickFileHandler;
    private SettingsPageViewModel? _boundViewModel;

    public SettingsPageView()
    {
        InitializeComponent();
        _pickFolderHandler = PickFolderAsync;
        _pickFileHandler = PickFileAsync;
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachPickerCallback();
        AttachPickerCallback();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachPickerCallback();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachPickerCallback();
    }

    private void AttachPickerCallback()
    {
        if (DataContext is SettingsPageViewModel vm)
        {
            _boundViewModel = vm;
            vm.PickFolderAsync = _pickFolderHandler;
            vm.PickFileAsync = _pickFileHandler;
        }
    }

    private void DetachPickerCallback()
    {
        if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.PickFolderAsync, _pickFolderHandler))
        {
            _boundViewModel.PickFolderAsync = null;
        }
        if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.PickFileAsync, _pickFileHandler))
        {
            _boundViewModel.PickFileAsync = null;
        }

        _boundViewModel = null;
    }

    private async Task<string?> PickFileAsync(string title, string? initialDir, string filterName, IReadOnlyList<string> patterns)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null || !storageProvider.CanOpen)
        {
            return null;
        }

        IStorageFolder? suggestedStartLocation = null;
        if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
        {
            suggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(initialDir);
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation,
            FileTypeFilter =
            [
                new FilePickerFileType(filterName) { Patterns = patterns.ToArray() }
            ],
        });

        var selected = files.FirstOrDefault();
        return selected?.TryGetLocalPath() ?? selected?.Path.LocalPath;
    }

    private async Task<string?> PickFolderAsync(string title, string? initialPath)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null || !storageProvider.CanPickFolder)
        {
            return null;
        }

        IStorageFolder? suggestedStartLocation = null;
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            suggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(initialPath);
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation,
        });

        var selected = folders.FirstOrDefault();
        return selected?.TryGetLocalPath() ?? selected?.Path.LocalPath;
    }
}
