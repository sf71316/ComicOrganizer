using AntdUI;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>分頁標頭批次關閉範圍。</summary>
public enum TabCloseScope
{
    Self,
    Others,
    Right,
}

/// <summary>建立作品／目錄右鍵選單所需的狀態上下文。</summary>
public sealed class WorkMenuContext
{
    public required string Path { get; init; }
    public required bool IsFolderNode { get; init; }
    public bool? IsWorkOverride { get; init; }
    public bool? CanUseBuiltInReaderOverride { get; init; }
    public ReadStatus? CurrentStatus { get; init; }
    public IReadOnlyList<VirtualLibrary>? VirtualLibraries { get; init; }
    public IReadOnlyCollection<long>? WorkLibraryIds { get; init; }
    public string? AuthorName { get; init; }
    public string? Series { get; init; }
    public string? TranslationGroup { get; init; }
}

/// <summary>作品／目錄右鍵選單各動作回呼，供卡片模式與列表模式共用。</summary>
public sealed class WorkMenuActions
{
    public Action<string>? OpenPrimary { get; init; }
    public Action<string>? OpenExternal { get; init; }
    public Action<string>? OpenNewTab { get; init; }
    public Action<string>? OpenInExplorer { get; init; }
    public Action<string>? CopyPath { get; init; }
    public Action<string>? RefreshCover { get; init; }
    public Action<string>? ShowAuthorInfo { get; init; }
    public Action<string>? ShowWorkDetail { get; init; }
    public Action<string, long>? ToggleVirtualLibrary { get; init; }
    public Action<string>? CreateVirtualLibraryAndAdd { get; init; }
    public Action<string, ReadStatus>? SetReadStatus { get; init; }
    public Action<string>? ExecuteSearchDsl { get; init; }
}

/// <summary>
/// 作品／目錄與分頁標頭右鍵選單共用 Helper（無狀態純建構 + 路徑判定 + 分頁批次關閉計算）。
/// 卡片模式與列表模式皆透過此類別建立選單並以 ID / Tag 派發動作，確保兩者行為一致。
/// </summary>
public static class WorkContextMenuHelper
{
    public const string DashboardTabTag = "DASHBOARD";

    public const string IdOpenPrimary = "open_primary";
    public const string IdOpenExternal = "open_external";
    public const string IdOpenNewTab = "open_new_tab";
    public const string IdOpenExplorer = "open_explorer";
    public const string IdCopyPath = "copy_path";
    public const string IdRefreshCover = "refresh_cover";
    public const string IdAuthorInfo = "author_info";
    public const string IdWorkDetail = "work_detail";
    public const string IdExtendedSearch = "extended_search";
    public const string IdExtendedSearchRun = "extended_search_run";
    public const string IdAddToList = "add_to_list";
    public const string IdAddToListToggle = "add_to_list_toggle";
    public const string IdAddToListCreate = "add_to_list_create";
    public const string IdStatusCompleted = "status_completed";
    public const string IdStatusReading = "status_reading";
    public const string IdStatusDropped = "status_dropped";
    public const string IdStatusUnread = "status_unread";

    public const string IdTabRefresh = "tab_refresh";
    public const string IdTabCopyPath = "tab_copy_path";
    public const string IdTabOpenExplorer = "tab_open_explorer";
    public const string IdTabCloseSelf = "tab_close_self";
    public const string IdTabCloseOthers = "tab_close_others";
    public const string IdTabCloseRight = "tab_close_right";

    /// <summary>
    /// 是否能用內建閱讀器開啟。支援壓縮檔（SupportedExtensions.Archive）/ PDF / 單張圖檔 / 含圖資料夾。
    /// </summary>
    public static bool CanUseBuiltInReader(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var f in Directory.EnumerateFiles(path))
                {
                    if (SupportedExtensions.Image.Contains(Path.GetExtension(f)))
                        return true;
                }
                return false;
            }
            if (!File.Exists(path)) return false;
            var ext = Path.GetExtension(path);
            return SupportedExtensions.Archive.Contains(ext)
                || SupportedExtensions.Pdf.Contains(ext)
                || SupportedExtensions.Image.Contains(ext);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 是否為「Work」asset（可被閱讀、可被標 read_status）。判定條件：
    /// (1) 檔案副檔名為 archive/pdf
    /// (2) 目錄無子目錄且含 ≥2 張圖檔
    /// 其他（Author folder / Library / Category / 純空目錄 / 單圖）→ false
    /// </summary>
    public static bool IsWorkAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (File.Exists(path))
            {
                var ext = Path.GetExtension(path);
                return SupportedExtensions.Archive.Contains(ext) || SupportedExtensions.Pdf.Contains(ext);
            }
            if (Directory.Exists(path))
            {
                if (Directory.EnumerateDirectories(path).Any()) return false;
                int imgCount = 0;
                foreach (var f in Directory.EnumerateFiles(path))
                {
                    if (SupportedExtensions.Image.Contains(Path.GetExtension(f)))
                    {
                        if (++imgCount >= 2) return true;
                    }
                }
                return false;
            }
        }
        catch
        {
            // 路徑被刪、權限不足等
        }
        return false;
    }

    /// <summary>依路徑上下文建立一致的右鍵選單項目清單（含三區分隔線、閱讀清單子選單、四種閱讀狀態）。</summary>
    public static AntdUI.IContextMenuStripItem[] BuildWorkMenuItems(WorkMenuContext ctx)
    {
        bool isWork = ctx.IsWorkOverride ?? IsWorkAsset(ctx.Path);
        bool canBuiltIn = ctx.CanUseBuiltInReaderOverride ?? CanUseBuiltInReader(ctx.Path);

        var items = new List<AntdUI.IContextMenuStripItem>
        {
            new AntdUI.ContextMenuStripItem(ctx.IsFolderNode ? "📂 進入目錄" : "👁 閱讀")
            {
                ID = IdOpenPrimary,
            },
            new AntdUI.ContextMenuStripItem(canBuiltIn ? "🔗 使用外部閱讀器開啟" : "🔗 使用外部程式開啟")
            {
                ID = IdOpenExternal,
            },
            new AntdUI.ContextMenuStripItem("另開頁籤")
            {
                ID = IdOpenNewTab,
            },
            new AntdUI.ContextMenuStripItemDivider(),
            new AntdUI.ContextMenuStripItem("📂 在檔案總管中開啟")
            {
                ID = IdOpenExplorer,
            },
            new AntdUI.ContextMenuStripItem("📋 複製完整路徑")
            {
                ID = IdCopyPath,
            },
            new AntdUI.ContextMenuStripItem("🔄 重新載入封面")
            {
                ID = IdRefreshCover,
            },
            new AntdUI.ContextMenuStripItem("👤 作者資訊")
            {
                ID = IdAuthorInfo,
            },
        };

        if (isWork)
        {
            items.Add(new AntdUI.ContextMenuStripItem("ℹ 詳細資訊")
            {
                ID = IdWorkDetail,
            });
        }

        var extendedSub = BuildExtendedSearchSubItems(ctx);
        if (extendedSub.Length > 0)
        {
            items.Add(new AntdUI.ContextMenuStripItem("🔍 延伸搜尋")
            {
                ID = IdExtendedSearch,
                Sub = extendedSub,
            });
        }

        if (isWork)
        {
            items.Add(new AntdUI.ContextMenuStripItem("📚 加入閱讀清單")
            {
                ID = IdAddToList,
                Sub = BuildAddToListSubItems(ctx.VirtualLibraries, ctx.WorkLibraryIds),
            });

            items.Add(new AntdUI.ContextMenuStripItemDivider());

            var current = ctx.CurrentStatus;
            items.Add(new AntdUI.ContextMenuStripItem("✅ 標為已完成")
            {
                ID = IdStatusCompleted,
                Enabled = current != ReadStatus.Completed,
                Tag = ReadStatus.Completed,
            });
            items.Add(new AntdUI.ContextMenuStripItem("📖 標為進行中")
            {
                ID = IdStatusReading,
                Enabled = current != ReadStatus.Reading,
                Tag = ReadStatus.Reading,
            });
            items.Add(new AntdUI.ContextMenuStripItem("⏹ 標為放棄")
            {
                ID = IdStatusDropped,
                Enabled = current != ReadStatus.Dropped,
                Tag = ReadStatus.Dropped,
            });
            items.Add(new AntdUI.ContextMenuStripItem("↺ 重置為未讀")
            {
                ID = IdStatusUnread,
                Enabled = current != ReadStatus.Unread && current != null,
                Tag = ReadStatus.Unread,
            });
        }

        return items.ToArray();
    }

    /// <summary>依上下文建立「🔍 延伸搜尋」子選單項目（作者／系列／翻譯組）。</summary>
    public static AntdUI.IContextMenuStripItem[] BuildExtendedSearchSubItems(WorkMenuContext ctx)
    {
        var sub = new List<AntdUI.IContextMenuStripItem>();
        TryAddExtendedSearchItem(sub, "👤 搜尋同作者", "artist", ctx.AuthorName);
        TryAddExtendedSearchItem(sub, "📚 搜尋同系列", "series", ctx.Series);
        TryAddExtendedSearchItem(sub, "🏷 搜尋同翻譯組", "group", ctx.TranslationGroup);
        return sub.ToArray();
    }

    private static void TryAddExtendedSearchItem(
        List<AntdUI.IContextMenuStripItem> sub,
        string labelPrefix,
        string fieldName,
        string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return;
        var value = rawValue.Trim();
        var token = SearchQueryParser.BuildFieldToken(fieldName, value);
        if (string.IsNullOrEmpty(token)) return;

        sub.Add(new AntdUI.ContextMenuStripItem($"{labelPrefix}：{value}")
        {
            ID = IdExtendedSearchRun,
            Tag = token,
        });
    }

    private static AntdUI.IContextMenuStripItem[] BuildAddToListSubItems(
        IReadOnlyList<VirtualLibrary>? libraries,
        IReadOnlyCollection<long>? workLibraryIds)
    {
        var sub = new List<AntdUI.IContextMenuStripItem>();
        if (libraries != null && libraries.Count > 0)
        {
            var idSet = workLibraryIds as IReadOnlySet<long>
                        ?? new HashSet<long>(workLibraryIds ?? Array.Empty<long>());
            foreach (var lib in libraries)
            {
                sub.Add(new AntdUI.ContextMenuStripItem(lib.Name)
                {
                    ID = IdAddToListToggle,
                    Checked = idSet.Contains(lib.Id),
                    Tag = lib.Id,
                });
            }
            sub.Add(new AntdUI.ContextMenuStripItemDivider());
        }

        sub.Add(new AntdUI.ContextMenuStripItem("➕ 建立新閱讀清單並加入…")
        {
            ID = IdAddToListCreate,
        });
        return sub.ToArray();
    }

    /// <summary>開啟作品／目錄右鍵選單並統一派發點選結果。</summary>
    public static void OpenWorkContextMenu(Control anchor, WorkMenuContext ctx, WorkMenuActions actions)
    {
        var items = BuildWorkMenuItems(ctx);
        AntdUI.ContextMenuStrip.open(anchor, mi => DispatchWorkMenuItem(mi, ctx.Path, actions), items);
    }

    /// <summary>依選單項目 ID 與 Tag 派發動作。</summary>
    public static void DispatchWorkMenuItem(AntdUI.IContextMenuStrip? mi, string path, WorkMenuActions actions)
    {
        if (mi == null) return;
        switch (mi.ID)
        {
            case IdOpenPrimary:
                actions.OpenPrimary?.Invoke(path);
                break;
            case IdOpenExternal:
                actions.OpenExternal?.Invoke(path);
                break;
            case IdOpenNewTab:
                actions.OpenNewTab?.Invoke(path);
                break;
            case IdOpenExplorer:
                actions.OpenInExplorer?.Invoke(path);
                break;
            case IdCopyPath:
                if (actions.CopyPath != null) actions.CopyPath(path);
                else TryCopyText(path);
                break;
            case IdRefreshCover:
                actions.RefreshCover?.Invoke(path);
                break;
            case IdAuthorInfo:
                actions.ShowAuthorInfo?.Invoke(path);
                break;
            case IdWorkDetail:
                actions.ShowWorkDetail?.Invoke(path);
                break;
            case IdExtendedSearchRun:
                if (mi.Tag is string dsl && !string.IsNullOrWhiteSpace(dsl))
                {
                    actions.ExecuteSearchDsl?.Invoke(dsl);
                }
                break;
            case IdAddToListToggle:
                if (mi.Tag is long libraryId)
                {
                    actions.ToggleVirtualLibrary?.Invoke(path, libraryId);
                }
                break;
            case IdAddToListCreate:
                actions.CreateVirtualLibraryAndAdd?.Invoke(path);
                break;
            case IdStatusCompleted:
            case IdStatusReading:
            case IdStatusDropped:
            case IdStatusUnread:
                if (mi.Tag is ReadStatus status)
                {
                    actions.SetReadStatus?.Invoke(path, status);
                }
                break;
        }
    }

    /// <summary>判斷分頁 Tag 是否為不可關閉的首頁儀表板。</summary>
    public static bool IsDashboardTag(string? tag)
        => string.Equals(tag, DashboardTabTag, StringComparison.Ordinal);

    /// <summary>
    /// 純索引版分頁關閉目標計算（永遠排除 DASHBOARD 頁，便於單元測試）。
    /// </summary>
    public static List<int> GetTabCloseIndices(IReadOnlyList<string?> pageTags, int targetIndex, TabCloseScope scope)
    {
        var indices = new List<int>();
        if (targetIndex < 0 || targetIndex >= pageTags.Count) return indices;

        switch (scope)
        {
            case TabCloseScope.Self:
                if (!IsDashboardTag(pageTags[targetIndex]))
                    indices.Add(targetIndex);
                break;
            case TabCloseScope.Others:
                for (int i = 0; i < pageTags.Count; i++)
                {
                    if (i != targetIndex && !IsDashboardTag(pageTags[i]))
                        indices.Add(i);
                }
                break;
            case TabCloseScope.Right:
                for (int i = targetIndex + 1; i < pageTags.Count; i++)
                {
                    if (!IsDashboardTag(pageTags[i]))
                        indices.Add(i);
                }
                break;
        }
        return indices;
    }

    /// <summary>取得分頁標頭批次關閉目標清單（永遠保留 DASHBOARD 頁）。</summary>
    public static List<AntdUI.TabPage> GetTabCloseTargets(
        IReadOnlyList<AntdUI.TabPage> pages,
        AntdUI.TabPage target,
        TabCloseScope scope)
    {
        int targetIndex = -1;
        var tags = new string?[pages.Count];
        for (int i = 0; i < pages.Count; i++)
        {
            if (ReferenceEquals(pages[i], target)) targetIndex = i;
            tags[i] = pages[i].Tag as string;
        }

        var indices = GetTabCloseIndices(tags, targetIndex, scope);
        var result = new List<AntdUI.TabPage>(indices.Count);
        foreach (int idx in indices)
        {
            result.Add(pages[idx]);
        }
        return result;
    }

    /// <summary>建立分頁標頭右鍵選單項目清單。</summary>
    public static AntdUI.IContextMenuStripItem[] BuildTabHeaderMenuItems(
        IReadOnlyList<AntdUI.TabPage> pages,
        AntdUI.TabPage target)
    {
        string? tagPath = target.Tag as string;
        bool hasFolderPath = !string.IsNullOrEmpty(tagPath) && !IsDashboardTag(tagPath);
        bool canCloseSelf = GetTabCloseTargets(pages, target, TabCloseScope.Self).Count > 0;
        bool canCloseOthers = GetTabCloseTargets(pages, target, TabCloseScope.Others).Count > 0;
        bool canCloseRight = GetTabCloseTargets(pages, target, TabCloseScope.Right).Count > 0;

        return new AntdUI.IContextMenuStripItem[]
        {
            new AntdUI.ContextMenuStripItem("🔄 重新整理此分頁") { ID = IdTabRefresh },
            new AntdUI.ContextMenuStripItem("📋 複製資料夾路徑")
            {
                ID = IdTabCopyPath,
                Enabled = hasFolderPath,
            },
            new AntdUI.ContextMenuStripItem("📂 在檔案總管開啟")
            {
                ID = IdTabOpenExplorer,
                Enabled = hasFolderPath,
            },
            new AntdUI.ContextMenuStripItemDivider(),
            new AntdUI.ContextMenuStripItem("✕ 關閉此分頁")
            {
                ID = IdTabCloseSelf,
                Enabled = canCloseSelf,
            },
            new AntdUI.ContextMenuStripItem("關閉其他分頁")
            {
                ID = IdTabCloseOthers,
                Enabled = canCloseOthers,
            },
            new AntdUI.ContextMenuStripItem("關閉右側分頁")
            {
                ID = IdTabCloseRight,
                Enabled = canCloseRight,
            },
        };
    }

    /// <summary>安全寫入剪貼簿（忽略剪貼簿被佔用或無 STA 執行緒例外）。</summary>
    public static void TryCopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 剪貼簿可能被其他程序鎖定，靜默忽略
        }
    }
}
