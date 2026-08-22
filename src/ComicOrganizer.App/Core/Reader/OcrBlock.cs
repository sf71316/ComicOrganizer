using System.Collections.Generic;

namespace ComicOrganizer.App.Core;

public class OcrBlock
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public string Text { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public List<OcrBlock> Children { get; set; } = new();
}
