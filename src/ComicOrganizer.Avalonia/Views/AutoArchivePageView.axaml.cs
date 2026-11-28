using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia.Views;

public partial class AutoArchivePageView : UserControl
{
    public AutoArchivePageView()
    {
        InitializeComponent();
    }

    private void OnTargetAutoCompleteDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is AutoCompleteBox acb)
        {
            acb.IsDropDownOpen = false;
        }
    }

    private void OnTargetAutoCompleteGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is AutoCompleteBox { DataContext: AutoArchiveCandidateVm candidate } acb)
        {
            candidate.RebuildTargetSuggestions(candidate.TargetDraftText);
            if (candidate.TargetSuggestions.Count > 0)
            {
                acb.IsDropDownOpen = true;
            }
        }
    }

    private void OnTargetAutoCompleteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is AutoCompleteBox { DataContext: AutoArchiveCandidateVm candidate } acb &&
            e.AddedItems.Count > 0 &&
            e.AddedItems[0] is AutoArchiveTargetSuggestionItem item)
        {
            candidate.CommitTargetSelection(item, allowCreateNew: true);
            acb.IsDropDownOpen = false;
        }
    }

    private void OnTargetAutoCompleteKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not AutoCompleteBox { DataContext: AutoArchiveCandidateVm candidate } acb)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            candidate.CommitTargetSelection(acb.SelectedItem ?? acb.Text, allowCreateNew: true);
            acb.IsDropDownOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            candidate.RevertDraftToSelected();
            acb.IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    private void OnTargetAutoCompleteLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not AutoCompleteBox { DataContext: AutoArchiveCandidateVm candidate } acb)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!acb.IsDropDownOpen && !acb.IsKeyboardFocusWithin)
            {
                candidate.RevertDraftToSelected();
            }
        });
    }
}
