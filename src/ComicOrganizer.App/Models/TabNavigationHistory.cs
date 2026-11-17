namespace ComicOrganizer.App.Models;

/// <summary>分頁導航歷史紀錄的目標類型。</summary>
public enum TabNavigationEntryKind
{
    Folder,
    VirtualWorks,
}

/// <summary>單筆分頁導航歷史項目，表示一個資料夾或虛擬作品清單目的地。</summary>
public sealed class TabNavigationEntry
{
    /// <summary>目的地類型（Folder 或 VirtualWorks）。</summary>
    public TabNavigationEntryKind Kind { get; }

    /// <summary>分頁顯示標題。</summary>
    public string Title { get; private set; }

    /// <summary>資料夾實體路徑（僅 <see cref="TabNavigationEntryKind.Folder"/> 有值）。</summary>
    public string? FolderPath { get; }

    /// <summary>虛擬作品清單路徑集合（僅 <see cref="TabNavigationEntryKind.VirtualWorks"/> 有值）。</summary>
    public IReadOnlyList<string> VirtualPaths { get; }

    /// <summary>分頁圖示名稱（AntdUI iconSvg）。</summary>
    public string IconSvg { get; }

    /// <summary>虛擬分頁重整時重新計算作品路徑的委派（可為 null）。</summary>
    public Func<IReadOnlyList<string>>? RefreshResolver { get; private set; }

    /// <summary>若對應自訂虛擬庫，記錄其資料庫 ID（可為 null）。</summary>
    public long? VirtualLibraryId { get; }

    /// <summary>若此虛擬分頁來自搜尋 DSL，記錄原始查詢字串（供歷史切換還原「存為清單」與重整用；可為 null）。</summary>
    public string? SearchDsl { get; private set; }

    private TabNavigationEntry(
        TabNavigationEntryKind kind,
        string title,
        string? folderPath,
        IReadOnlyList<string> virtualPaths,
        string iconSvg,
        Func<IReadOnlyList<string>>? refreshResolver,
        long? virtualLibraryId,
        string? searchDsl)
    {
        Kind = kind;
        Title = title;
        FolderPath = folderPath;
        VirtualPaths = virtualPaths;
        IconSvg = iconSvg;
        RefreshResolver = refreshResolver;
        VirtualLibraryId = virtualLibraryId;
        SearchDsl = string.IsNullOrWhiteSpace(searchDsl) ? null : searchDsl;
    }

    /// <summary>建立資料夾目的地歷史項目。</summary>
    public static TabNavigationEntry ForFolder(string folderPath, string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (string.IsNullOrWhiteSpace(title))
        {
            var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            title = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(title)) title = folderPath;
        }

        return new TabNavigationEntry(
            TabNavigationEntryKind.Folder,
            title,
            folderPath,
            Array.Empty<string>(),
            "FolderOutlined",
            refreshResolver: null,
            virtualLibraryId: null,
            searchDsl: null);
    }

    /// <summary>建立虛擬作品清單目的地歷史項目。</summary>
    public static TabNavigationEntry ForVirtualWorks(
        string title,
        IReadOnlyList<string> paths,
        string iconSvg = "TagOutlined",
        Func<IReadOnlyList<string>>? refreshResolver = null,
        long? virtualLibraryId = null,
        string? searchDsl = null)
    {
        var snapshot = paths == null ? Array.Empty<string>() : paths.ToArray();
        return new TabNavigationEntry(
            TabNavigationEntryKind.VirtualWorks,
            title ?? string.Empty,
            folderPath: null,
            snapshot,
            string.IsNullOrEmpty(iconSvg) ? "TagOutlined" : iconSvg,
            refreshResolver,
            virtualLibraryId,
            searchDsl);
    }

    /// <summary>判斷兩個導航項目是否指向同一個目的地。</summary>
    public bool IsSameDestination(TabNavigationEntry? other)
    {
        if (other == null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Kind != other.Kind) return false;

        if (Kind == TabNavigationEntryKind.Folder)
        {
            var a = NormalizeFolderPath(FolderPath);
            var b = NormalizeFolderPath(other.FolderPath);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        return VirtualLibraryId == other.VirtualLibraryId
            && string.Equals(Title, other.Title, StringComparison.Ordinal)
            && string.Equals(IconSvg, other.IconSvg, StringComparison.Ordinal)
            && VirtualPaths.Count == other.VirtualPaths.Count
            && VirtualPaths.SequenceEqual(other.VirtualPaths, StringComparer.OrdinalIgnoreCase);
    }

    internal void UpdateFrom(TabNavigationEntry source)
    {
        Title = source.Title;
        RefreshResolver = source.RefreshResolver;
        SearchDsl = source.SearchDsl;
    }

    private static string? NormalizeFolderPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        return Path.TrimEndingDirectorySeparator(path);
    }
}

/// <summary>單一分頁的瀏覽歷史堆疊（支援上一頁、下一頁、容量上限與滑鼠側鍵解析）。</summary>
public sealed class TabNavigationHistory
{
    /// <summary>預設最大歷史容量。</summary>
    public const int DefaultMaxCapacity = 50;

    private readonly int _maxCapacity;
    private readonly List<TabNavigationEntry> _entries = new();
    private int _currentIndex = -1;

    /// <summary>建立分頁導航歷史堆疊。</summary>
    public TabNavigationHistory(int maxCapacity = DefaultMaxCapacity)
    {
        if (maxCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCapacity), "maxCapacity must be positive.");
        _maxCapacity = maxCapacity;
    }

    /// <summary>是否可回到上一頁。</summary>
    public bool CanGoBack => _currentIndex > 0;

    /// <summary>是否可前進至下一頁。</summary>
    public bool CanGoForward => _currentIndex >= 0 && _currentIndex < _entries.Count - 1;

    /// <summary>目前歷史紀錄總筆數。</summary>
    public int Count => _entries.Count;

    /// <summary>目前指標索引（尚無紀錄時為 -1）。</summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>目前所在歷史項目（尚無紀錄時為 null）。</summary>
    public TabNavigationEntry? Current =>
        _currentIndex >= 0 && _currentIndex < _entries.Count ? _entries[_currentIndex] : null;

    /// <summary>
    /// 推入新導航目的地。
    /// 若與目前目的地相同則原地更新 RefreshResolver、Title 與 SearchDsl 並回傳 false；
    /// 若目前位於歷史中間（有 forward 紀錄）會先截斷後方歷史再附加，並於超出容量上限時移除最舊項。
    /// </summary>
    public bool Push(TabNavigationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (Current != null && Current.IsSameDestination(entry))
        {
            Current.UpdateFrom(entry);
            return false;
        }

        if (CanGoForward)
        {
            _entries.RemoveRange(_currentIndex + 1, _entries.Count - (_currentIndex + 1));
        }

        _entries.Add(entry);
        if (_entries.Count > _maxCapacity)
        {
            int overflow = _entries.Count - _maxCapacity;
            _entries.RemoveRange(0, overflow);
        }

        _currentIndex = _entries.Count - 1;
        return true;
    }

    /// <summary>嘗試回到上一頁。</summary>
    public bool TryGoBack(out TabNavigationEntry? entry)
    {
        if (!CanGoBack)
        {
            entry = null;
            return false;
        }

        _currentIndex--;
        entry = _entries[_currentIndex];
        return true;
    }

    /// <summary>嘗試前進至下一頁。</summary>
    public bool TryGoForward(out TabNavigationEntry? entry)
    {
        if (!CanGoForward)
        {
            entry = null;
            return false;
        }

        _currentIndex++;
        entry = _entries[_currentIndex];
        return true;
    }

    /// <summary>
    /// 解析 Win32 WM_XBUTTONUP (0x020C) 的 wParam 高位字組（HIWORD）：
    /// XBUTTON1 (1) → -1（上一頁）、XBUTTON2 (2) → +1（下一頁）、其他 → 0。
    /// </summary>
    public static int ParseXButtonAction(long wParam)
    {
        long xButton = (wParam >> 16) & 0xFFFFL;
        return xButton switch
        {
            1 => -1,
            2 => 1,
            _ => 0,
        };
    }
}
