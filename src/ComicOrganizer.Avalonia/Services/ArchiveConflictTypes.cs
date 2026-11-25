using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ComicOrganizer.Avalonia.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ComicOrganizer.Avalonia.Services;

public enum ArchiveConflictDecision
{
    Skip = 0,
    Overwrite = 1,
    CancelRemaining = 2
}

public sealed record ArchiveConflictInfo(
    string SourceName,
    string SourceFullPath,
    string TargetFolderName,
    string DestinationFullPath,
    string SourceSummaryText,
    string DestinationSummaryText)
{
    public static ArchiveConflictInfo FromPaths(string sourcePath, string targetFolderName, string destinationFullPath)
    {
        string cleanSource = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string sourceName = Path.GetFileName(cleanSource);
        return new ArchiveConflictInfo(
            SourceName: string.IsNullOrEmpty(sourceName) ? cleanSource : sourceName,
            SourceFullPath: cleanSource,
            TargetFolderName: targetFolderName,
            DestinationFullPath: destinationFullPath,
            SourceSummaryText: DescribeEntry(cleanSource),
            DestinationSummaryText: DescribeEntry(destinationFullPath));
    }

    private static string DescribeEntry(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                var files = di.GetFiles("*", SearchOption.AllDirectories);
                long totalBytes = files.Sum(f => f.Length);
                return $"資料夾 · {files.Length} 個檔案 · {FormatBytes(totalBytes)} · 修改：{di.LastWriteTime:yyyy/MM/dd HH:mm}";
            }

            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                return $"檔案 · {FormatBytes(fi.Length)} · 修改：{fi.LastWriteTime:yyyy/MM/dd HH:mm}";
            }
        }
        catch
        {
            // ignore metadata inspection errors
        }

        return "項目資訊無法讀取";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F1} MB";
        return $"{mb / 1024.0:F2} GB";
    }
}

public interface IArchiveConflictPrompt
{
    Task<ArchiveConflictDecision> ConfirmOverwriteAsync(ArchiveConflictInfo conflict);
}

public sealed partial class ArchiveConflictConfirmViewModel : ObservableObject
{
    public ArchiveConflictConfirmViewModel(ArchiveConflictInfo conflict)
    {
        Conflict = conflict ?? throw new ArgumentNullException(nameof(conflict));
    }

    public ArchiveConflictInfo Conflict { get; }

    public string HeaderSubtitle => $"目標資料夾「{Conflict.TargetFolderName}」已存在同名項目，請確認要覆蓋或略過此作品。";

    public event Action<ArchiveConflictDecision>? RequestClose;

    [RelayCommand]
    private void Overwrite() => RequestClose?.Invoke(ArchiveConflictDecision.Overwrite);

    [RelayCommand]
    private void Skip() => RequestClose?.Invoke(ArchiveConflictDecision.Skip);

    [RelayCommand]
    private void CancelRemaining() => RequestClose?.Invoke(ArchiveConflictDecision.CancelRemaining);
}

public sealed class AvaloniaArchiveConflictPrompt : IArchiveConflictPrompt
{
    public Task<ArchiveConflictDecision> ConfirmOverwriteAsync(ArchiveConflictInfo conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        if (Dispatcher.UIThread == null)
        {
            return Task.FromResult(ArchiveConflictDecision.Skip);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowDialogOnUiAsync(conflict);
        }

        return Dispatcher.UIThread.InvokeAsync(() => ShowDialogOnUiAsync(conflict));
    }

    private static async Task<ArchiveConflictDecision> ShowDialogOnUiAsync(ArchiveConflictInfo conflict)
    {
        var vm = new ArchiveConflictConfirmViewModel(conflict);
        var win = new ArchiveConflictConfirmWindow
        {
            DataContext = vm,
        };

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner != null && owner.IsVisible)
        {
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return await win.ShowDialog<ArchiveConflictDecision>(owner);
        }

        win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var tcs = new TaskCompletionSource<ArchiveConflictDecision>();
        void OnClosed(object? s, EventArgs e)
        {
            win.Closed -= OnClosed;
            tcs.TrySetResult(win.SelectedDecision);
        }

        win.Closed += OnClosed;
        win.Show();
        return await tcs.Task;
    }
}
