using System;

namespace ComicOrganizer.App.Core;

public interface IOcrPlugin
{
    string PluginId { get; }
    string DisplayName { get; }
    string Description { get; }
    int EngineType { get; }
    bool IsAvailable { get; }
    string? StatusMessage { get; }
    IOcrService CreateService(string? modelDir = null);
}

public class OcrEngineInfo
{
    public int EngineType { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsAvailable { get; set; }
    public string StatusMessage { get; set; } = "";

    public override string ToString() => Name;
}

public class OcrPluginDiagnostics
{
    public int EngineType { get; set; }
    public string PluginName { get; set; } = "";
    public bool IsInstalled { get; set; }
    public bool IsHealthy { get; set; }
    public string Summary { get; set; } = "";
    public System.Collections.Generic.List<string> MissingFiles { get; set; } = new();
    public string PluginDirectory { get; set; } = "";
}
