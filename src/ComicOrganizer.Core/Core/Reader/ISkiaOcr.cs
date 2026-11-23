using System.Reflection;
using System.Runtime.Loader;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 跨平台（無 System.Drawing 依賴）之漫畫 OCR 服務介面。
/// 接受已編碼影像位元組（PNG / JPEG / WebP），產出 <see cref="OcrBlock"/> 並支援區塊合併。
/// </summary>
public interface ISkiaOcrService
{
    bool IsLanguageSupported(string langCode);
    List<string> GetSupportedLanguages();
    Task<List<OcrBlock>> RecognizeEncodedAsync(byte[] encodedImageBytes, string langCode = "ja-JP");
    List<OcrBlock> MergeBlocks(List<OcrBlock> blocks, bool isVerticalFlow = true, float thresholdMultiplier = 1.0f);
}

/// <summary>
/// 跨平台 OCR 外掛介面（供 Avalonia 與純 net8.0 核心動態載入）。
/// </summary>
public interface ISkiaOcrPlugin
{
    string PluginId { get; }
    string DisplayName { get; }
    string Description { get; }
    int EngineType { get; }
    bool IsAvailable { get; }
    string? StatusMessage { get; }
    ISkiaOcrService CreateSkiaService(string? modelDir = null);
}

public sealed class SkiaOcrEngineInfo
{
    public int EngineType { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsAvailable { get; set; }
    public string StatusMessage { get; set; } = "";

    public override string ToString() => Name;
}

/// <summary>
/// 管理跨平台 OCR 外掛（自動探索本程式目錄下與上層共用目錄之 plugins/PaddleOCR）。
/// 並支援由宿主應用程式（例如 Win10/11 Avalonia 端）註冊系統原生 Windows OCR 實作。
/// </summary>
public static class SkiaOcrPluginManager
{
    private static readonly Dictionary<int, ISkiaOcrPlugin> RegisteredPlugins = new();
    private static readonly object SyncLock = new();
    private static bool _initialized;
    private static Func<ISkiaOcrService?>? _nativeFallbackFactory;
    private static string _nativeFallbackName = "Windows 原生 OCR";
    private static string _nativeFallbackDesc = "Windows 10/11 內建 OCR 引擎，免額外下載模型，資源佔用低";

    /// <summary>由宿主平台註冊內建原生 OCR（EngineType = 0）。</summary>
    public static void RegisterNativeFallback(Func<ISkiaOcrService?> factory, string? name = null, string? description = null)
    {
        lock (SyncLock)
        {
            _nativeFallbackFactory = factory;
            if (!string.IsNullOrWhiteSpace(name)) _nativeFallbackName = name;
            if (!string.IsNullOrWhiteSpace(description)) _nativeFallbackDesc = description;
        }
    }

    public static IReadOnlyList<string> GetCandidatePluginDirectories()
    {
        var dirs = new List<string>();
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            dirs.Add(Path.Combine(baseDir, "plugins"));
            var parent = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;
            if (!string.IsNullOrEmpty(parent))
            {
                dirs.Add(Path.Combine(parent, "plugins"));
            }
        }
        return dirs
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void Initialize(bool forceReload = false)
    {
        lock (SyncLock)
        {
            if (_initialized && !forceReload) return;
            RegisteredPlugins.Clear();
            DiscoverPlugins();
            _initialized = true;
        }
    }

    private static void DiscoverPlugins()
    {
        foreach (var pluginsDir in GetCandidatePluginDirectories())
        {
            string[] dllFiles;
            try
            {
                dllFiles = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var dllPath in dllFiles)
            {
                try
                {
                    string fileName = Path.GetFileName(dllPath);
                    if (fileName.StartsWith("paddle_", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("mkl", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("opencv_", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("DirectML", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var context = new SkiaPluginAssemblyLoadContext(dllPath);
                    var asm = context.LoadFromAssemblyPath(dllPath);

                    Type?[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        // 例如同一個外掛 DLL 也含依賴 WinForms App 的舊介面類別；只取可成功載入的 ISkiaOcrPlugin 型別
                        types = rtle.Types;
                    }

                    foreach (var type in types)
                    {
                        if (type == null || type.IsAbstract || type.IsInterface) continue;
                        if (!typeof(ISkiaOcrPlugin).IsAssignableFrom(type)) continue;
                        if (Activator.CreateInstance(type) is ISkiaOcrPlugin plugin &&
                            !RegisteredPlugins.ContainsKey(plugin.EngineType))
                        {
                            RegisteredPlugins[plugin.EngineType] = plugin;
                        }
                    }
                }
                catch
                {
                    // 忽略無法載入之原生/第三方 DLL
                }
            }
        }
    }

    public static bool IsEngineAvailable(int engineType)
    {
        Initialize();
        if (engineType == 0)
            return _nativeFallbackFactory != null;

        lock (SyncLock)
        {
            return RegisteredPlugins.TryGetValue(engineType, out var plugin) && plugin.IsAvailable;
        }
    }

    public static ISkiaOcrService? CreateService(int engineType, string? customModelDir = null)
    {
        Initialize();

        if (engineType != 0)
        {
            lock (SyncLock)
            {
                if (RegisteredPlugins.TryGetValue(engineType, out var plugin) && plugin.IsAvailable)
                {
                    try
                    {
                        return plugin.CreateSkiaService(customModelDir);
                    }
                    catch
                    {
                        // 降級為原生 OCR
                    }
                }
            }
        }

        return _nativeFallbackFactory?.Invoke();
    }

    public static List<SkiaOcrEngineInfo> GetAvailableEngines()
    {
        Initialize();
        var list = new List<SkiaOcrEngineInfo>
        {
            new SkiaOcrEngineInfo
            {
                EngineType = 0,
                Name = _nativeFallbackName,
                Description = _nativeFallbackDesc,
                IsAvailable = _nativeFallbackFactory != null,
                StatusMessage = _nativeFallbackFactory != null ? "內建就緒" : "此平台不支援原生 Windows OCR"
            }
        };

        bool paddleAvailable = false;
        string paddleStatus = "未安裝外掛 (找不到 plugins/PaddleOCR)";

        lock (SyncLock)
        {
            if (RegisteredPlugins.TryGetValue(1, out var plugin))
            {
                paddleAvailable = plugin.IsAvailable;
                paddleStatus = plugin.StatusMessage ?? (paddleAvailable ? "外掛已就緒" : "模型檔不完整");
            }
        }

        list.Add(new SkiaOcrEngineInfo
        {
            EngineType = 1,
            Name = "PaddleOCR 離線引擎 (PP-OCRv6)",
            Description = "高精準度本機深度學習 OCR，支援日文/中文氣泡與豎排辨識",
            IsAvailable = paddleAvailable,
            StatusMessage = paddleStatus
        });

        return list;
    }

    private sealed class SkiaPluginAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public SkiaPluginAssemblyLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, typeof(ISkiaOcrPlugin).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            // VC++ 執行期 DLL 若 System32 已有新版則優先使用系統版本，避免外掛附帶的舊版（如 14.28）鎖死主程式較新的原生套件
            string bareName = Path.GetFileName(unmanagedDllName);
            if ((bareName.StartsWith("msvcp140", StringComparison.OrdinalIgnoreCase) ||
                 bareName.StartsWith("vcruntime140", StringComparison.OrdinalIgnoreCase) ||
                 bareName.StartsWith("concrt140", StringComparison.OrdinalIgnoreCase)) &&
                OperatingSystem.IsWindows())
            {
                string dllFile = bareName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? bareName : bareName + ".dll";
                string sysPath = Path.Combine(Environment.SystemDirectory, dllFile);
                if (File.Exists(sysPath))
                {
                    return LoadUnmanagedDllFromPath(sysPath);
                }
            }

            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
        }
    }
}
