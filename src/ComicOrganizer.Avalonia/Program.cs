using System.Runtime.InteropServices;
using Avalonia;
using ComicOrganizer.Avalonia.Services;

namespace ComicOrganizer.Avalonia;

internal sealed class Program
{
    public static string[] StartupArgs { get; private set; } = [];

    [STAThread]
    public static int Main(string[] args)
    {
        ConfigureNativeLibraryProbing();

        if (args.Any(a => string.Equals(a, VisualIndexHostService.WorkerArg, StringComparison.OrdinalIgnoreCase)))
        {
            return VisualIndexHostService.RunWorkerEntryAsync(args).GetAwaiter().GetResult();
        }

        StartupArgs = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static void ConfigureNativeLibraryProbing()
    {
        var libDir = Path.Combine(AppContext.BaseDirectory, "lib");
        if (!Directory.Exists(libDir)) return;

        // 讓所有原生 DLL（SkiaSharp、Angle/GLES、DirectML、ONNX Runtime、USearch、SQLite、PDFium）可從 lib\ 載入
        try
        {
            SetDllDirectoryW(libDir);
        }
        catch
        {
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!path.Contains(libDir, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", $"{libDir};{path}");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectoryW(string lpPathName);

    /// <summary>Skia GPU 資源快取上限（官方效能文件建議桌面 256~512 MB）。</summary>
    private const long GpuResourceCacheBytes = 256L * 1024 * 1024;

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // Skia GPU 資源快取預設約 28 MB，超過的圖片每幀重新上傳 GPU。
            // 探索牆 4K 一畫面約 150 張封面（330x468，解碼後各約 600 KB），實測調到 256 MB 後捲動 15.9 → 23.8 FPS。
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = GpuResourceCacheBytes })
            .LogToTrace();
    }
}
