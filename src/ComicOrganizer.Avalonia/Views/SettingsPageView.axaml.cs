using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ComicOrganizer.Avalonia.ViewModels;
using SukiUI.Controls;

namespace ComicOrganizer.Avalonia.Views;

public partial class SettingsPageView : UserControl
{
    private readonly Func<string, string?, Task<string?>> _pickFolderHandler;
    private readonly Func<string, string?, string, IReadOnlyList<string>, Task<string?>> _pickFileHandler;
    private readonly Func<string, string, string, string, IReadOnlyList<string>, Task<string?>> _saveFileHandler;
    private readonly Func<string, string, Task<bool>> _confirmActionHandler;
    private SettingsPageViewModel? _boundViewModel;

    public SettingsPageView()
    {
        InitializeComponent();
        _pickFolderHandler = PickFolderAsync;
        _pickFileHandler = PickFileAsync;
        _saveFileHandler = PickSaveFileAsync;
        _confirmActionHandler = ShowConfirmDialogAsync;
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
            vm.SaveFileAsync = _saveFileHandler;
            vm.ConfirmActionAsync = _confirmActionHandler;
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
        if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.SaveFileAsync, _saveFileHandler))
        {
            _boundViewModel.SaveFileAsync = null;
        }
        if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.ConfirmActionAsync, _confirmActionHandler))
        {
            _boundViewModel.ConfirmActionAsync = null;
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

    private async Task<string?> PickSaveFileAsync(
        string title,
        string suggestedFileName,
        string defaultExtension,
        string filterName,
        IReadOnlyList<string> patterns)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null || !storageProvider.CanSave)
        {
            return null;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = defaultExtension,
            FileTypeChoices =
            [
                new FilePickerFileType(filterName) { Patterns = patterns.ToArray() }
            ],
        });

        return file?.TryGetLocalPath() ?? file?.Path.LocalPath;
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        bool confirmed = false;

        var dialog = new SukiWindow
        {
            Title = title,
            Width = 460,
            MinHeight = 200,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            IsTitleBarVisible = true
        };

        var cancelBtn = new Button
        {
            Content = "取消",
            Padding = new Thickness(16, 7),
            Classes = { "Outlined" }
        };
        var okBtn = new Button
        {
            Content = "確認還原",
            Padding = new Thickness(18, 7),
            Classes = { "Flat" }
        };

        cancelBtn.Click += (_, _) =>
        {
            confirmed = false;
            dialog.Close();
        };
        okBtn.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };

        var rootPanel = new StackPanel
        {
            Margin = new Thickness(20, 14, 20, 18),
            Spacing = 14
        };
        rootPanel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        buttonRow.Children.Add(cancelBtn);
        buttonRow.Children.Add(okBtn);
        rootPanel.Children.Add(buttonRow);

        dialog.Content = rootPanel;

        if (owner != null && owner.IsVisible)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            dialog.Closed += (_, _) => tcs.TrySetResult(confirmed);
            dialog.Show();
            return await tcs.Task;
        }

        return confirmed;
    }
}
