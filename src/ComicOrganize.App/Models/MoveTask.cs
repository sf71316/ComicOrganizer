namespace ComicOrganize.App.Models;

public class MoveTask
{
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Moving, Completed, Failed
    public string Message { get; set; } = string.Empty;
}
