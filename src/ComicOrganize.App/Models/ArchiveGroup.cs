using System.ComponentModel;

namespace ComicOrganize.App.Models;

public class ArchiveGroup : INotifyPropertyChanged
{
    private bool _checked = true;
    public bool Checked
    {
        get => _checked;
        set { _checked = value; OnPropertyChanged(nameof(Checked)); }
    }

    public string DestinationPath { get; set; } = "";
    public BindingList<ArchiveItem> Items { get; set; } = new();

    public string DisplayTitle => $"目標目錄:{DestinationPath}  ({Items.Count} 個檔案)";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
