using System;
using System.IO;
using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

public class OcrPluginTests
{
    [Fact]
    public void TestWindowsOcr_IsAlwaysAvailable()
    {
        bool available = OcrPluginManager.IsEngineAvailable(0);
        Assert.True(available);

        var service = OcrPluginManager.CreateService(0);
        Assert.NotNull(service);
        Assert.IsType<WindowsOcrService>(service);
    }

    [Fact]
    public void TestGetAvailableEngines_ListsEngines()
    {
        var engines = OcrPluginManager.GetAvailableEngines();
        Assert.NotEmpty(engines);
        var winEngine = engines.FirstOrDefault(e => e.EngineType == 0);
        var paddleEngine = engines.FirstOrDefault(e => e.EngineType == 1);
        Assert.NotNull(winEngine);
        Assert.NotNull(paddleEngine);

        // When plugin is in plugins/PaddleOCR, paddleEngine.IsAvailable should be true, and ToString() should not have "(未安裝外掛)"
        if (OcrPluginManager.GetPaddleOcrDiagnostics().IsHealthy)
        {
            Assert.True(paddleEngine.IsAvailable);
            Assert.DoesNotContain("未安裝外掛", paddleEngine.ToString());
        }
    }

    [Fact]
    public void TestCreateService_FallbackWhenNotFound()
    {
        // EngineType 999 does not exist, should safely fallback to WindowsOcrService
        var service = OcrPluginManager.CreateService(999);
        Assert.NotNull(service);
        Assert.IsType<WindowsOcrService>(service);
    }

    [Fact]
    public void TestPaddleOcrDiagnostics_ProvidesAccurateStatus()
    {
        var diag = OcrPluginManager.GetPaddleOcrDiagnostics();
        Assert.NotNull(diag);
        Assert.Equal(1, diag.EngineType);
        Assert.False(string.IsNullOrWhiteSpace(diag.PluginDirectory));
        Assert.False(string.IsNullOrWhiteSpace(diag.Summary));

        if (diag.IsHealthy)
        {
            Assert.True(diag.IsInstalled);
            Assert.Empty(diag.MissingFiles);
        }
        else
        {
            Assert.NotEmpty(diag.MissingFiles);
        }
    }
}
