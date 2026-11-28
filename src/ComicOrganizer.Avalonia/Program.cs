using System.Runtime.InteropServices;
using System.Runtime.Loader;
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
        PreloadSystemVcRuntime();

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

        // 即使 Windows Shell／檔案對話框於 GUI 期間清空 SetDllDirectoryW，仍優先從 lib\ 載入完整路徑，避免誤踩 System32 舊版 DLL
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, unmanagedDllName) =>
        {
            if (string.IsNullOrWhiteSpace(unmanagedDllName))
            {
                return IntPtr.Zero;
            }

            string fileName = Path.GetFileName(unmanagedDllName);
            string exact = Path.Combine(libDir, fileName);
            if (File.Exists(exact) && NativeLibrary.TryLoad(exact, out IntPtr handle))
            {
                return handle;
            }

            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string withExt = Path.Combine(libDir, fileName + ".dll");
                if (File.Exists(withExt) && NativeLibrary.TryLoad(withExt, out IntPtr extHandle))
                {
                    return extHandle;
                }
            }

            return IntPtr.Zero;
        };
    }

    /// <summary>
    /// 優先從 System32 載入較新版的 VC++ 2015–2022 執行期，避免後續載入外掛（如 PaddleOCR 目錄內的 14.28 舊版 msvcp140.dll）
    /// 將整個進程鎖死在過舊的 C++ ABI，進而造成較新版本的 onnxruntime.dll（1.24.4）初始化失敗（0x8007045A）。
    /// </summary>
    private static void PreloadSystemVcRuntime()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string sysDir = Environment.SystemDirectory;
        if (string.IsNullOrWhiteSpace(sysDir))
        {
            return;
        }

        foreach (string dllName in new[]
        {
            "vcruntime140.dll",
            "vcruntime140_1.dll",
            "msvcp140.dll",
            "msvcp140_1.dll",
            "msvcp140_2.dll",
            "concrt140.dll",
        })
        {
            string fullPath = Path.Combine(sysDir, dllName);
            if (File.Exists(fullPath))
            {
                NativeLibrary.TryLoad(fullPath, out _);
            }
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
            // Skia GPU 資源快取預設約 28 MB，超過的圖片每幀重新上傳 GPU。
            // 探索牆 4K 一畫面約 150 張封面（330x468，解碼後各約 600 KB），實測調到 256 MB 後捲動 15.9 → 23.8 FPS。
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = GpuResourceCacheBytes })
            .LogToTrace();
    }
}
