using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia.Views;

public partial class SettingsPageView : UserControl
{
    private readonly Func<string, string?, Task<string?>> _pickFolderHandler;
    private SettingsPageViewModel? _boundViewModel;

    public SettingsPageView()
    {
        InitializeComponent();
        _pickFolderHandler = PickFolderAsync;
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
        }
    }

    private void DetachPickerCallback()
    {
        if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.PickFolderAsync, _pickFolderHandler))
        {
            _boundViewModel.PickFolderAsync = null;
        }

        _boundViewModel = null;
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
