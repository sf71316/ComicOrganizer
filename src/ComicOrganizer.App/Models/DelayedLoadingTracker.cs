namespace ComicOrganizer.App.Models;

/// <summary>
/// 延遲載入指示器狀態機（以 generation token 防止過期計時器或舊非同步作業誤切換顯示狀態）。
/// 亦提供共用的路徑正規化鍵值計算（<see cref="NormalizePathKey"/>）。
/// </summary>
public sealed class DelayedLoadingTracker
{
    /// <summary>預設延遲顯示載入動畫的時間門檻（毫秒）。</summary>
    public const int DefaultDelayMs = 380;

    private readonly object _lock = new();
    private bool _isBusy;
    private bool _isIndicatorVisible;
    private long _generation;

    /// <summary>目前是否有進行中的載入作業。</summary>
    public bool IsBusy
    {
        get { lock (_lock) return _isBusy; }
    }

    /// <summary>目前是否已超過延遲門檻並應顯示載入指示器。</summary>
    public bool IsIndicatorVisible
    {
        get { lock (_lock) return _isIndicatorVisible; }
    }

    /// <summary>目前載入世代代號（每次 <see cref="Begin"/> 遞增）。</summary>
    public long Generation
    {
        get { lock (_lock) return _generation; }
    }

    /// <summary>開始一次新的載入作業並回傳該次世代 token。</summary>
    public long Begin()
    {
        lock (_lock)
        {
            _generation++;
            _isBusy = true;
            _isIndicatorVisible = false;
            return _generation;
        }
    }

    /// <summary>
    /// 延遲計時器到期時呼叫；僅當 <paramref name="token"/> 仍為最新世代且處於忙碌且尚未顯示時，
    /// 將 <see cref="IsIndicatorVisible"/> 設為 true 並回傳 true。
    /// </summary>
    public bool MarkDelayElapsed(long token)
    {
        lock (_lock)
        {
            if (!_isBusy || token != _generation || _isIndicatorVisible)
                return false;

            _isIndicatorVisible = true;
            return true;
        }
    }

    /// <summary>
    /// 完成指定世代的載入作業；僅當 <paramref name="token"/> 為最新世代且目前仍在忙碌或顯示中時，
    /// 清除狀態並回傳 true。
    /// </summary>
    public bool Complete(long token)
    {
        lock (_lock)
        {
            if (token != _generation || (!_isBusy && !_isIndicatorVisible))
                return false;

            _isBusy = false;
            _isIndicatorVisible = false;
            return true;
        }
    }

    /// <summary>無條件重置忙碌與顯示狀態；若原先有任一旗標為 true 則回傳 true。</summary>
    public bool Reset()
    {
        lock (_lock)
        {
            bool changed = _isBusy || _isIndicatorVisible;
            _isBusy = false;
            _isIndicatorVisible = false;
            return changed;
        }
    }

    /// <summary>
    /// 正規化完整路徑並移除結尾斜線（保留如 <c>C:\</c> 的磁碟根目錄），
    /// 供防重複開窗雜湊／字典鍵值（搭配 <see cref="StringComparer.OrdinalIgnoreCase"/>）共用。
    /// </summary>
    public static string NormalizePathKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim();
        try
        {
            trimmed = Path.GetFullPath(trimmed);
        }
        catch
        {
            // 若含無效字元則保留 trim 後字串繼續去除結尾分隔符
        }

        return Path.TrimEndingDirectorySeparator(trimmed);
    }
}
