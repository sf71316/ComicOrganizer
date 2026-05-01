using System.ComponentModel;

namespace ComicOrganizer.App.Models;

public class ArchiveItem : INotifyPropertyChanged
{
    private bool _checked;
    public bool Checked
    {
        get => _checked;
        set { _checked = value; OnPropertyChanged(nameof(Checked)); }
    }

    public string Source { get; set; } = "";
    public string FullPath { get; set; } = "";

    private string _targetFolder = "";
    public string TargetFolder
    {
        get => _targetFolder;
        set { _targetFolder = value; OnPropertyChanged(nameof(TargetFolder)); }
    }

    private string _destinationPath = "";
    public string DestinationPath
    {
        get => _destinationPath;
        set { _destinationPath = value; OnPropertyChanged(nameof(DestinationPath)); }
    }

    public string Confidence { get; set; } = "";

    private string _status = "";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); }
    }

    public AntdUI.CellLink[]? Action { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
