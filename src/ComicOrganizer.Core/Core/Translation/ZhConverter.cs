using System.Collections.Concurrent;
using OpenCCZh = OpenCCNET.ZhConverter;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 簡↔繁中文轉換（OpenCC.NET 後端）。
///
/// EhTagTranslation/Database 是中國社群維護，翻譯為「簡體中文」；
/// 用 OpenCC HansToTW(useTaiwanPhrase:true) 同時做：
/// - 字形轉換（妈妈→媽媽 / 学校→學校）
/// - 用語級台灣化（内存→記憶體 / 软件→軟體）
///
/// 第一次呼叫 lazy 觸發 OpenCC.NET 載入字典 + jieba 分詞資源
///（~3 MB，從 nuget content folder 複製到輸出目錄）。
/// 結果走 ConcurrentDictionary 快取避免重複分詞。
/// </summary>
public static class ZhConverter
{
    private static readonly ConcurrentDictionary<string, string> _s2tCache = new();
    private static readonly ConcurrentDictionary<string, string> _t2sCache = new();
    private static readonly object _initLock = new();
    private static bool _initialized;

    private static void EnsureInit()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            try
            {
                // dictionaries / jieba resources 由 NuGet content 自動複製到 bin
                OpenCCZh.Initialize();
                _initialized = true;
            }
            catch
            {
                // 初始化失敗（dict 缺檔）— 後續轉換都退化為原樣回傳
                _initialized = true;
            }
        }
    }

    /// <summary>簡體 → 繁體（台灣，含用語轉換）。失敗 / 空字串 → 原樣回傳。</summary>
    public static string ToTraditional(string? source)
    {
        if (string.IsNullOrEmpty(source)) return source ?? "";
        return _s2tCache.GetOrAdd(source, s =>
        {
            EnsureInit();
            try { return OpenCCZh.HansToTW(s, true); }
            catch { return s; }
        });
    }

    /// <summary>繁體 → 簡體。失敗 / 空字串 → 原樣回傳。</summary>
    public static string ToSimplified(string? source)
    {
        if (string.IsNullOrEmpty(source)) return source ?? "";
        return _t2sCache.GetOrAdd(source, s =>
        {
            EnsureInit();
            try { return OpenCCZh.HantToHans(s); }
            catch { return s; }
        });
    }

    /// <summary>清快取（測試用）。</summary>
    public static void ClearCache()
    {
        _s2tCache.Clear();
        _t2sCache.Clear();
    }
}
