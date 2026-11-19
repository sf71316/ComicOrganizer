namespace ComicOrganizer.App.Core;

public class RebuildProgress
{
    public int Done { get; set; }
    public int Total { get; set; }
    public string CurrentPath { get; set; } = "";
}
