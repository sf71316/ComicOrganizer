using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ComicOrganizer.Avalonia.ViewModels;
using ComicOrganizer.Avalonia.Views;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>暫存待挑書籃中的作品快照資訊（僅存在記憶體中，不寫入資料庫）。</summary>
public sealed record WorkBasketEntry(
    string Path,
    string Title,
    string AuthorName,
    string? CoverThumbKey = null,
    int? PageCount = null,
    string? Language = null,
    bool IsFolder = false)
{
    public static string NormalizePathKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }
}

/// <summary>書籃儲存書單時的目標選項（null 表示建立新書單；有值表示併入既有 VirtualLibrary）。</summary>
public sealed record VirtualLibraryTargetOption(long? LibraryId, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public enum WorkPreviewSampleMode
{
    FrontPages = 0,
    UniformSample = 1,
}

public static class WorkPreviewPageSampler
{
    private static readonly double[] UniformRatios = [0.04, 0.22, 0.40, 0.58, 0.76, 0.92];

    public static IReadOnlyList<int> ComputeSamplePageIndices(
        int totalPages,
        WorkPreviewSampleMode mode,
        int maxSamples = 6)
    {
        if (totalPages <= 0 || maxSamples <= 0)
        {
            return Array.Empty<int>();
        }

        int count = Math.Min(totalPages, maxSamples);
        if (mode == WorkPreviewSampleMode.FrontPages || totalPages <= maxSamples)
        {
            var front = new int[count];
            for (int i = 0; i < count; i++)
            {
                front[i] = i;
            }
            return front;
        }

        var selected = new SortedSet<int>();
        for (int i = 0; i < count && i < UniformRatios.Length; i++)
        {
            int idx = Math.Clamp((int)Math.Round((totalPages - 1) * UniformRatios[i]), 0, totalPages - 1);
            selected.Add(idx);
        }

        // 若因頁數偏少造成四捨五入重複，線性均勻補齊至 count 張
        for (int i = 0; selected.Count < count && i < totalPages; i++)
        {
            int fallback = (int)Math.Floor((double)i * totalPages / count);
            selected.Add(Math.Clamp(fallback, 0, totalPages - 1));
            for (int p = fallback; selected.Count < count && p < totalPages; p++)
            {
                if (selected.Add(p))
                {
                    break;
                }
            }
        }

        return selected.ToList();
    }
}

public interface IWorkPreviewLauncher
{
    bool OpenPreview(
        WorkBasketEntry entry,
        WorkBasketViewModel basket,
        IReaderWindowLauncher readerLauncher,
        out string? errorMessage);
}

public sealed class AvaloniaWorkPreviewLauncher : IWorkPreviewLauncher
{
    private WorkPreviewWindow? _activeWindow;

    public bool OpenPreview(
        WorkBasketEntry entry,
        WorkBasketViewModel basket,
        IReaderWindowLauncher readerLauncher,
        out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(entry.Path))
        {
            errorMessage = "作品路徑為空。";
            return false;
        }

        if (!File.Exists(entry.Path) && !Directory.Exists(entry.Path))
        {
            errorMessage = $"找不到目標檔案或資料夾：{entry.Path}";
            return false;
        }

        try
        {
            if (_activeWindow != null)
            {
                try
                {
                    _activeWindow.Close();
                }
                catch
                {
                }
                _activeWindow = null;
            }

            var vm = new WorkPreviewViewModel(entry, basket, readerLauncher);
            var win = new WorkPreviewWindow
            {
                DataContext = vm,
            };
            win.Closed += (_, _) =>
            {
                if (ReferenceEquals(_activeWindow, win))
                {
                    _activeWindow = null;
                }
            };
            _activeWindow = win;

            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner != null && owner.IsVisible)
            {
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                win.Show(owner);
            }
            else
            {
                win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                win.Show();
            }

            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"無法開啟快速預覽：{ex.Message}";
            return false;
        }
    }
}

public interface IWorkBasketSaveLauncher
{
    bool OpenSaveDialog(WorkBasketSaveViewModel dialogVm);
}

public sealed class AvaloniaWorkBasketSaveLauncher : IWorkBasketSaveLauncher
{
    public bool OpenSaveDialog(WorkBasketSaveViewModel dialogVm)
    {
        ArgumentNullException.ThrowIfNull(dialogVm);

        var win = new SaveBasketToLibraryWindow
        {
            DataContext = dialogVm,
        };

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner != null && owner.IsVisible)
        {
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _ = win.ShowDialog<bool>(owner);
        }
        else
        {
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            win.Show();
        }

        return true;
    }
}

