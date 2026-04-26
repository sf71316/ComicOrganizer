using ComicOrganize.App.Models;

namespace ComicOrganize.App.Core;

public class CoverManager
{
    private readonly AppConfig _config;

    public CoverManager(AppConfig config)
    {
        _config = config;
    }

    public void RenameImagesToCover()
    {
        // TODO: 實作重命名邏輯 (對應原功能 1)
    }

    public void ExtractCovers()
    {
        // TODO: 實作提取封面邏輯 (對應原功能 2)
    }
}
