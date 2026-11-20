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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
