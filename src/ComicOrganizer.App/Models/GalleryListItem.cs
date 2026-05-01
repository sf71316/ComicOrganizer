using System.ComponentModel;

namespace ComicOrganizer.App.Models;

public class GalleryListItem : INotifyPropertyChanged
{
    private AntdUI.CellImage? _thumbnail;
    public AntdUI.CellImage? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(nameof(Thumbnail)); }
    }

    public string Name { get; set; } = "";
    public string Size { get; set; } = "";
    public string CreatedAt { get; set; } = "";

    // folder row 顯示「進入下一層」icon button（id="OpenFolder"），file row 為 null
    public AntdUI.CellLink[]? Action { get; set; }

    // 互動用 — 不顯示成欄位
    public bool IsFolder { get; set; }
    public string FullPath { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
