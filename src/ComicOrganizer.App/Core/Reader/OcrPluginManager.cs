using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace ComicOrganizer.App.Core;

public static class OcrPluginManager
{
    private static readonly Dictionary<int, IOcrPlugin> _registeredPlugins = new();
    private static readonly object _lock = new();
    private static bool _initialized = false;

    public static string PluginsDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");

    static OcrPluginManager()
    {
        Initialize();
    }

    public static void Initialize(bool forceReload = false)
    {
        lock (_lock)
        {
            if (_initialized && !forceReload) return;

            _registeredPlugins.Clear();
            DiscoverPlugins();
            _initialized = true;
        }
    }

    private static void DiscoverPlugins()
    {
        string pluginsDir = PluginsDirectory;
        if (!Directory.Exists(pluginsDir))
        {
            try
            {
                Directory.CreateDirectory(pluginsDir);
            }
            catch
            {
                // ignore
            }
            return;
        }

        // 搜尋 plugins 目錄及子目錄下的所有 DLL
        var dllFiles = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories);

        foreach (var dllPath in dllFiles)
        {
            try
            {
                string fileName = Path.GetFileName(dllPath);
                // 優先載入外掛 DLL，略過已知的依賴庫以提升掃描效率
                if (fileName.StartsWith("paddle_", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("mkl", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("opencv_", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("DirectML", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var context = new PluginAssemblyLoadContext(dllPath);
                var asm = context.LoadFromAssemblyPath(dllPath);

                var pluginTypes = asm.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface && typeof(IOcrPlugin).IsAssignableFrom(t));
                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IOcrPlugin plugin)
                    {
                        _registeredPlugins[plugin.EngineType] = plugin;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OcrPluginManager] 載入外掛失敗 {dllPath}: {ex.Message}");
            }
        }
    }

    public static bool IsEngineAvailable(int engineType)
    {
        Initialize();
        if (engineType == 0)
        {
            // Windows OCR 是內建的
            return true;
        }

        lock (_lock)
        {
            if (_registeredPlugins.TryGetValue(engineType, out var plugin))
            {
                return plugin.IsAvailable;
            }
        }

        return false;
    }

    public static bool IsPaddleOcrAvailable()
    {
        return IsEngineAvailable(1);
    }

    public static IOcrService CreateService(int engineType, string? customModelDir = null)
    {
        Initialize();

        if (engineType == 1)
        {
            lock (_lock)
            {
                if (_registeredPlugins.TryGetValue(1, out var plugin) && plugin.IsAvailable)
                {
                    try
                    {
                        return plugin.CreateService(customModelDir);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OcrPluginManager] 初始化 PaddleOCR 失敗，降級為 Windows OCR: {ex.Message}");
                    }
                }
            }
        }

        // 預設或 fallback 為 Windows 原生 OCR
        return new WindowsOcrService();
    }

    public static List<OcrEngineInfo> GetAvailableEngines()
    {
        Initialize();

        var list = new List<OcrEngineInfo>
        {
            new OcrEngineInfo
            {
                EngineType = 0,
                Name = "Windows 原生 OCR",
                Description = "Windows 10/11 內建 OCR 引擎，免額外下載模型，資源佔用低",
                IsAvailable = true,
                StatusMessage = "內建就緒"
            }
        };

        bool paddleAvailable = false;
        string paddleStatus = "未安裝外掛 (可將外掛放置於 plugins/PaddleOCR 資料夾)";

        lock (_lock)
        {
            if (_registeredPlugins.TryGetValue(1, out var plugin))
            {
                paddleAvailable = plugin.IsAvailable;
                paddleStatus = plugin.StatusMessage ?? (paddleAvailable ? "外掛已就緒" : "模型檔不完整");
            }
        }

        list.Add(new OcrEngineInfo
        {
            EngineType = 1,
            Name = "PaddleOCR 離線引擎 (PP-OCRv6)",
            Description = "高精準度本機 C++ 深度學習 OCR，支援日文/中文手繪與氣泡辨識",
            IsAvailable = paddleAvailable,
            StatusMessage = paddleStatus
        });

        return list;
    }

    public static OcrPluginDiagnostics GetPaddleOcrDiagnostics()
    {
        Initialize();

        var diag = new OcrPluginDiagnostics
        {
            EngineType = 1,
            PluginName = "PaddleOCR 離線引擎 (PP-OCRv6)",
            PluginDirectory = Path.Combine(PluginsDirectory, "PaddleOCR")
        };

        string paddlePluginDir = Path.Combine(PluginsDirectory, "PaddleOCR");
        string mainPluginDll = Path.Combine(paddlePluginDir, "ComicOrganizer.Plugin.PaddleOcr.dll");

        // 若不存在於 plugins/PaddleOCR，也檢查 plugins/ 根目錄
        if (!File.Exists(mainPluginDll))
        {
            mainPluginDll = Path.Combine(PluginsDirectory, "ComicOrganizer.Plugin.PaddleOcr.dll");
            if (File.Exists(mainPluginDll))
            {
                paddlePluginDir = PluginsDirectory;
                diag.PluginDirectory = paddlePluginDir;
            }
        }

        if (!File.Exists(mainPluginDll))
        {
            diag.IsInstalled = false;
            diag.IsHealthy = false;
            diag.Summary = "未安裝外掛";
            diag.MissingFiles.Add("ComicOrganizer.Plugin.PaddleOcr.dll (外掛主程式)");
            diag.MissingFiles.Add("paddle_inference.dll (Paddle C++ 核心)");
            diag.MissingFiles.Add("mklml.dll (Intel MKL 加速庫)");
            diag.MissingFiles.Add("opencv_world470.dll (OpenCV 影像庫)");
            diag.MissingFiles.Add("inference/ (模型目錄)");
            return diag;
        }

        diag.IsInstalled = true;

        // 檢查 Native DLL
        string[] requiredDlls = new[]
        {
            "paddle_inference.dll",
            "mklml.dll",
            "opencv_world470.dll",
            "paddle_yt_phi.dll",
            "mkldnn.dll"
        };

        foreach (var dll in requiredDlls)
        {
            if (!File.Exists(Path.Combine(paddlePluginDir, dll)) &&
                !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dll)))
            {
                diag.MissingFiles.Add(dll);
            }
        }

        // 檢查模型檔案
        string inferDir = Path.Combine(paddlePluginDir, "inference");
        if (!Directory.Exists(inferDir))
        {
            inferDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inference");
        }

        if (!Directory.Exists(inferDir))
        {
            diag.MissingFiles.Add("inference/ (模型目錄缺失)");
        }
        else
        {
            string[] requiredModelItems = new[]
            {
                "PP-OCRv6_small_det_infer",
                "PP-OCRv6_small_rec_infer",
                "ppocr_keys.txt"
            };

            foreach (var item in requiredModelItems)
            {
                string p = Path.Combine(inferDir, item);
                if (!File.Exists(p) && !Directory.Exists(p))
                {
                    diag.MissingFiles.Add($"inference/{item}");
                }
            }
        }

        if (diag.MissingFiles.Count == 0)
        {
            diag.IsHealthy = true;
            diag.Summary = "外掛與模型已就緒 (PP-OCRv6)";
        }
        else
        {
            diag.IsHealthy = false;
            diag.Summary = $"缺少 {diag.MissingFiles.Count} 個必要元件";
        }

        return diag;
    }

    private class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginAssemblyLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == "ComicOrganizer.App" || assemblyName.Name == typeof(IOcrPlugin).Assembly.GetName().Name)
            {
                return null;
            }
            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }
            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }
            return IntPtr.Zero;
        }
    }
}
