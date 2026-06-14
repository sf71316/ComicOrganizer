using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace ComicOrganizer.App.Core;

public interface IOcrService
{
    bool IsLanguageSupported(string langCode);
    List<string> GetSupportedLanguages();
    Task<List<OcrBlock>> RecognizeAsync(Image image, string langCode = "ja-JP");
    List<OcrBlock> MergeBlocks(List<OcrBlock> blocks, bool isVerticalFlow = true, float thresholdMultiplier = 1.8f);
}
