using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ComicOrganizer.Avalonia.ViewModels;

public sealed record PlaceholderMetricItem(string Label, string Value, string Hint);

public sealed record PlaceholderSnapshot(IReadOnlyList<PlaceholderMetricItem> Metrics, string Footnote);

/// <summary>
/// 尚未遷移功能模組的 SukiUI 佔位頁 ViewModel，非同步載入 SQLite / 設定的即時統計數據。
/// </summary>
public sealed partial class PlaceholderPageViewModel : ViewModelBase
{
    private readonly Func<PlaceholderSnapshot> _snapshotLoader;

    public PlaceholderPageViewModel(
        string iconGlyph,
        string title,
        string phaseBadge,
        string description,
        IReadOnlyList<string> plannedFeatures,
        Func<PlaceholderSnapshot> snapshotLoader)
    {
        IconGlyph = iconGlyph;
        Title = title;
        PhaseBadge = phaseBadge;
        Description = description;
        PlannedFeatures = new ObservableCollection<string>(plannedFeatures);
        _snapshotLoader = snapshotLoader;

        _ = RefreshAsync();
    }

    public string IconGlyph { get; }

    public string Title { get; }

    public string PhaseBadge { get; }

    public string Description { get; }

    public ObservableCollection<PlaceholderMetricItem> Metrics { get; } = [];

    public ObservableCollection<string> PlannedFeatures { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusFootnote = "正在讀取即時統計資料...";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var snapshot = await Task.Run(_snapshotLoader);
            await ApplySnapshotAsync(snapshot);
        }
        catch (Exception ex)
        {
            await ApplySnapshotAsync(new PlaceholderSnapshot(
                [new PlaceholderMetricItem("資料讀取狀態", "無法載入", ex.Message)],
                $"統計讀取失敗：{ex.Message}"));
        }
        finally
        {
            await SetLoadingFalseAsync();
        }
    }

    private Task ApplySnapshotAsync(PlaceholderSnapshot snapshot)
    {
        void Apply()
        {
            Metrics.Clear();
            foreach (var item in snapshot.Metrics)
            {
                Metrics.Add(item);
            }

            StatusFootnote = snapshot.Footnote;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(Apply).GetTask();
    }

    private Task SetLoadingFalseAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsLoading = false;
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(() => IsLoading = false).GetTask();
    }
}
