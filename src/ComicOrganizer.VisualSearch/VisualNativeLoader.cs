using System.Runtime.InteropServices;

namespace ComicOrganizer.VisualSearch;

/// <summary>
/// 確保視覺搜尋所需的原生 DLL 優先從應用程式旁的 <c>lib\</c> 子目錄依完整路徑載入。
/// <para>
/// 原因：Windows 11（如 24H2+）於 <c>C:\Windows\System32\</c> 內建了舊版 <c>onnxruntime.dll</c> (1.17)；
/// 在 SingleFile + <c>lib\</c> 部署結構中，若 GUI 階段的 <c>SetDllDirectoryW</c> 被 Shell / 檔案對話框覆寫清空，
/// P/Invoke 預設探索會搶先載入 <c>System32\onnxruntime.dll</c> 導致與 1.24.4 API 不相容而拋出
/// <c>TypeInitializationException</c>。先以完整路徑載入後，Windows PE Loader 會將正確模組登記於進程中。
/// </para>
/// <para>此類別刻意不參考 OnnxRuntime 或 USearch 型別，確保於靜態建構子執行前即可安全呼叫。</para>
/// </summary>
public static class VisualNativeLoader
{
    internal static readonly string[] OrderedNativeDllNames =
    [
        "DirectML.dll",
        "onnxruntime_providers_shared.dll",
        "onnxruntime.dll",
        "numkong.dll",
        "libusearch_c.dll",
    ];

    private static int _defaultProbed;

    public static void EnsureLoaded()
    {
        if (Interlocked.Exchange(ref _defaultProbed, 1) != 0)
        {
            return;
        }

        LoadFromLibDirectory(AppContext.BaseDirectory);
    }

    /// <summary>
    /// 依序嘗試從 <paramref name="baseDirectory"/>\lib 載入既有原生 DLL；
    /// 回傳本次嘗試載入成功的檔名清單（供測試驗證）。
    /// </summary>
    internal static IReadOnlyList<string> LoadFromLibDirectory(string? baseDirectory)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Array.Empty<string>();
        }

        string libDir = Path.Combine(baseDirectory, "lib");
        if (!Directory.Exists(libDir))
        {
            return Array.Empty<string>();
        }

        var loaded = new List<string>(OrderedNativeDllNames.Length);
        foreach (string dllName in OrderedNativeDllNames)
        {
            string fullPath = Path.Combine(libDir, dllName);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            if (NativeLibrary.TryLoad(fullPath, out _))
            {
                loaded.Add(dllName);
            }
        }

        return loaded;
    }
}
