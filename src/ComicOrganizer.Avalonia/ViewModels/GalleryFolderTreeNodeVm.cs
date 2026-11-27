using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ComicOrganizer.Avalonia.ViewModels;

/// <summary>
/// 畫廊左側「📁 目錄」延遲展開樹狀節點 ViewModel。
/// 未展開時僅在可展開目錄放置單一哨兵節點（顯示展開箭頭），首次展開時才動態載入下一層子目錄。
/// </summary>
public sealed partial class GalleryFolderTreeNodeVm : ObservableObject
{
    private readonly Action<GalleryFolderTreeNodeVm>? _onNeedLoadChildren;
    private bool _childrenLoaded;

    public GalleryFolderTreeNodeVm(
        string fullPath,
        string name,
        bool isRoot,
        bool isAuthorFolder,
        string subText,
        Action<GalleryFolderTreeNodeVm>? onNavigate,
        Action<GalleryFolderTreeNodeVm>? onNeedLoadChildren,
        bool isPlaceholder = false)
    {
        FullPath = fullPath;
        Name = name;
        IsRoot = isRoot;
        IsAuthorFolder = isAuthorFolder;
        SubText = subText;
        IsPlaceholder = isPlaceholder;
        _onNeedLoadChildren = onNeedLoadChildren;
        _childrenLoaded = isPlaceholder;

        NavigateCommand = new RelayCommand(() =>
        {
            if (!IsPlaceholder)
            {
                onNavigate?.Invoke(this);
            }
        });
    }

    public string FullPath { get; }

    public string Name { get; }

    public bool IsRoot { get; }

    public bool IsAuthorFolder { get; }

    public bool IsPlaceholder { get; }

    public string SubText { get; }

    public bool HasSubText => !string.IsNullOrEmpty(SubText);

    public string Icon => IsPlaceholder ? string.Empty : (IsRoot ? "🏠" : (IsAuthorFolder ? "👤" : "📁"));

    public BatchObservableCollection<GalleryFolderTreeNodeVm> Children { get; } = [];

    public IRelayCommand NavigateCommand { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    internal bool ChildrenLoaded => _childrenLoaded;

    public static GalleryFolderTreeNodeVm CreatePlaceholder()
        => new(
            fullPath: string.Empty,
            name: string.Empty,
            isRoot: false,
            isAuthorFolder: false,
            subText: string.Empty,
            onNavigate: null,
            onNeedLoadChildren: null,
            isPlaceholder: true);

    /// <summary>標記此節點是否有下一層子資料夾；若有則放入單一哨兵節點以顯示展開箭頭。</summary>
    public void SetPlaceholderIfExpandable(bool canExpand)
    {
        if (IsPlaceholder || _childrenLoaded)
        {
            return;
        }

        if (canExpand)
        {
            Children.ReplaceAll([CreatePlaceholder()]);
        }
        else
        {
            _childrenLoaded = true;
            Children.Clear();
        }
    }

    /// <summary>確保子節點已從磁碟／資料庫載入（供雙向路徑展開與測試使用）。</summary>
    public void EnsureChildrenLoaded()
    {
        if (IsPlaceholder || _childrenLoaded)
        {
            return;
        }

        _onNeedLoadChildren?.Invoke(this);
    }

    /// <summary>寫入已載入的直接子節點清單並標記為已完成載入。</summary>
    public void SetLoadedChildren(IReadOnlyList<GalleryFolderTreeNodeVm> children)
    {
        _childrenLoaded = true;
        Children.ReplaceAll(children);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !IsPlaceholder && !_childrenLoaded)
        {
            _onNeedLoadChildren?.Invoke(this);
        }
    }
}
