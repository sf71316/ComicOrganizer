using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// GalleryControl 左側 sidebar tree（#48）。
/// 從 GalleryControl.cs 抽出 — folder mode + 4 種虛擬庫 mode + tag chip 過濾
/// + tree 右鍵選單。
/// </summary>
public partial class GalleryControl
{
    private void UpdateTreeLoadingText(AntdUI.TreeItemCollection items)
    {
        foreach (var item in items)
        {
            if (item.Tag as string == LazyPlaceholderTag)
            {
                item.Text = LocalizationManager.GetString("Loading");
            }
            if (item.Sub.Count > 0)
            {
                UpdateTreeLoadingText(item.Sub);
            }
        }
    }

    /// <summary>Tree 節點右鍵 → 四項選單（檔案總管 / 目前頁籤 / 另開頁籤 / 作者資訊），各帶 SVG icon。</summary>
    private void ShowTreeNodeContextMenu(string path)
    {
        var items = new AntdUI.IContextMenuStripItem[]
        {
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenInExplorer") ?? "檔案總管開啟")
                { IconSvg = "FolderOpenOutlined" },
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenInCurrentTab") ?? "目前頁籤開啟")
                { IconSvg = "FileOutlined" },
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenInNewTab") ?? "另開新頁籤")
                { IconSvg = "PlusOutlined" },
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuAuthorInfo") ?? "作者資訊")
                { IconSvg = "UserOutlined" },
        };

        AntdUI.ContextMenuStrip.open(treeFolders, mi =>
        {
            string? t = mi.Text;
            if (t == null) return;
            if (t.Contains("檔案總管") || t.Contains("Explorer")) OpenInExplorer(path);
            else if (t.Contains("目前頁籤") || t.Contains("Current")) OpenFolderTab(path, false);
            else if (t.Contains("另開") || t.Contains("New Tab")) OpenFolderTab(path, true);
            else if (t.Contains("作者") || t.Contains("Author")) ShowAuthorInfo(path);
        }, items);
    }

    /// <summary>Tree 空白處右鍵 → 一項選單（重新整理整棵 tree）。</summary>
    private void ShowTreeBlankContextMenu()
    {
        var items = new AntdUI.IContextMenuStripItem[]
        {
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuRefresh") ?? "重新整理")
                { IconSvg = "RedoOutlined" },
        };
        AntdUI.ContextMenuStrip.open(treeFolders, mi =>
        {
            _ = LoadTreeAsync();
        }, items);
    }

    private Task LoadTreeAsync()
    {
        // 依 ViewMode 分流：Folder 走原本邏輯；其他 mode 走虛擬庫
        return _currentViewMode switch
        {
            TreeViewMode.Folder => LoadFolderTreeAsync(),
            TreeViewMode.ByAuthor => LoadAuthorTreeAsync(),
            TreeViewMode.ByTag => LoadTagTreeAsync(),
            TreeViewMode.MyLib => LoadMyLibTreeAsync(),
            TreeViewMode.ReadingTrack => LoadReadingTrackTreeAsync(),
            _ => Task.CompletedTask,
        };
    }

    private Task LoadFolderTreeAsync()
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory)) return Task.CompletedTask;

        treeFolders.Items.Clear();
        AntdUI.Spin.open(splitter2.Panel1, async config =>
        {
            var rootDir = new DirectoryInfo(_config.RootDirectory);
            var rootNode = await Task.Run(() => CreateFolderNode(rootDir));
            this.Invoke(() =>
            {
                treeFolders.Items.Add(rootNode);
                rootNode.Expand = true;
                LoadSubDirectories(rootNode, rootDir.FullName);
                UpdateTreeWidth();
            });
        });
        return Task.CompletedTask;
    }

    /// <summary>「依作者」mode tree — 一個作者一個 node。</summary>
    private Task LoadAuthorTreeAsync()
    {
        if (_metaStore == null) return Task.CompletedTask;
        treeFolders.Items.Clear();
        var authors = VirtualLibraryProvider.GetAuthorNodes(_metaStore);
        foreach (var node in authors)
        {
            var item = new AntdUI.TreeItem($"{node.DisplayName} ({node.WorkCount})") { IconSvg = node.IconSvg, Tag = node };
            treeFolders.Items.Add(item);
        }
        UpdateTreeWidth();
        return Task.CompletedTask;
    }

    /// <summary>取當前 UI 語系 — 跟 store v6 tag_translation lang 對齊（zh-TW / zh-CN / 其他原樣）。</summary>
    private static string GetCurrentTagLang()
    {
        var lang = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (lang.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
        return lang;
    }

    /// <summary>category 群組標題的本地化對照（給 tag tree group header 用）。</summary>
    private static string LocalizeTagCategory(string category)
    {
        // 已知 nhentai category 有 i18n key 就走 i18n，否則原樣
        var key = category.ToLowerInvariant() switch
        {
            "artist" => "TagCategoryArtist",
            "group" => "TagCategoryGroup",
            "parody" => "TagCategoryParody",
            "character" => "TagCategoryCharacter",
            "language" => "TagCategoryLanguage",
            "category" => "TagCategoryCategory",
            "tag" => "TagCategoryTag",
            _ => "",
        };
        return string.IsNullOrEmpty(key)
            ? category
            : (LocalizationManager.GetString(key) ?? category);
    }

    /// <summary>「依標籤」mode tree — 依 category 分群顯示，依當前語系翻譯 tag 名 + group header。</summary>
    private Task LoadTagTreeAsync()
    {
        if (_metaStore == null) return Task.CompletedTask;
        treeFolders.Items.Clear();
        var tags = VirtualLibraryProvider.GetTagNodes(_metaStore);

        // schema v6 batch 查譯文，避免 N+1
        var lang = GetCurrentTagLang();
        var translations = _metaStore.GetTagTranslations(tags.Select(t => t.TagId).ToList(), lang);
        var uncatLabel = LocalizationManager.GetString("TagCategoryUncategorized") ?? "(未分類)";

        // 依 category 分群（null=未分類最後），group header 也本地化
        var groups = tags.GroupBy(t => t.Category ?? uncatLabel)
                         .OrderBy(g => g.Key == uncatLabel ? 1 : 0)
                         .ThenBy(g => g.Key);
        foreach (var group in groups)
        {
            var groupHeader = group.Key == uncatLabel ? uncatLabel : LocalizeTagCategory(group.Key);
            var groupNode = new AntdUI.TreeItem(groupHeader) { IconSvg = "FolderOutlined" };
            foreach (var t in group.OrderByDescending(x => x.WorkCount).ThenBy(x => x.Name))
            {
                // 顯示譯名（命中）或原文（未命中）
                var display = translations.TryGetValue(t.TagId, out var zh) ? zh : t.Name;
                var child = new AntdUI.TreeItem($"{display} ({t.WorkCount})") { IconSvg = t.IconSvg, Tag = t };
                groupNode.Sub.Add(child);
            }
            groupNode.Expand = true;
            treeFolders.Items.Add(groupNode);
        }
        UpdateTreeWidth();
        return Task.CompletedTask;
    }

    /// <summary>「依語言」mode tree。</summary>
    /// <summary>「我的閱讀清單」mode tree — 列出所有自訂虛擬庫（#54 Phase 3）。
    /// pinned 的放最上方加分隔；節點 right-click 開 context menu（編輯 / 刪除 / 釘選 / 複製 DSL）。</summary>
    private Task LoadMyLibTreeAsync()
    {
        if (_metaStore == null) return Task.CompletedTask;
        treeFolders.Items.Clear();
        var nodes = VirtualLibraryProvider.GetMyLibraryNodes(_metaStore);
        bool seenUnpinned = false;
        foreach (var node in nodes)
        {
            // pinned 區塊 → unpinned 區塊之間加 divider（第一個非釘選的 item 前）
            if (!seenUnpinned && !node.Library.Pinned && treeFolders.Items.Count > 0)
            {
                seenUnpinned = true;
                // 視覺上用一個禁用、無 tag 的虛擬節點當分隔
                treeFolders.Items.Add(new AntdUI.TreeItem("─────") { Enabled = false });
            }
            var label = node.Library.Pinned ? $"📌 {node.DisplayName}" : node.DisplayName;
            var item = new AntdUI.TreeItem(label) { IconSvg = node.IconSvg, Tag = node };
            treeFolders.Items.Add(item);
        }

        // 沒任何虛擬庫 → 顯示提示節點（disabled）
        if (nodes.Count == 0)
        {
            treeFolders.Items.Add(new AntdUI.TreeItem(
                LocalizationManager.GetString("MyLibEmptyHint")
                    ?? "尚無自訂閱讀清單；右鍵新增") { Enabled = false });
        }

        UpdateTreeWidth();
        return Task.CompletedTask;
    }

    /// <summary>「閱讀軌跡」mode tree — 4 個 fixed sub-node。
    /// MostReadAuthors / MostReadTags 是 group header — 展開為 top10 child。</summary>
    private Task LoadReadingTrackTreeAsync()
    {
        if (_metaStore == null) return Task.CompletedTask;
        treeFolders.Items.Clear();
        var nodes = VirtualLibraryProvider.GetReadingTrackNodes(_metaStore);
        foreach (var node in nodes)
        {
            var label = node.WorkCount.HasValue ? $"{node.DisplayName} ({node.WorkCount})" : node.DisplayName;
            var item = new AntdUI.TreeItem(label) { IconSvg = node.IconSvg, Tag = node };

            // MostReadAuthors / MostReadTags 展開 children
            if (node.Kind == ReadingTrackKind.MostReadAuthors)
            {
                var top = _metaStore.ListMostReadAuthors(10);
                foreach (var a in top)
                {
                    var child = new AntdUI.TreeItem($"{a.Author.MainName} (讀 {a.TotalReads} 次)")
                    {
                        IconSvg = "UserOutlined",
                        Tag = new AuthorVirtualNode(a.Author.Id, a.Author.MainName, 0),
                    };
                    item.Sub.Add(child);
                }
                item.Expand = true;
            }
            else if (node.Kind == ReadingTrackKind.MostReadTags)
            {
                var top = _metaStore.ListMostReadTags(10);
                foreach (var t in top)
                {
                    var child = new AntdUI.TreeItem($"{t.Tag.Name} (讀 {t.TotalReads} 次)")
                    {
                        IconSvg = "TagOutlined",
                        Tag = new TagVirtualNode(t.Tag.Id, t.Tag.Name, t.Tag.Category, 0),
                    };
                    item.Sub.Add(child);
                }
                item.Expand = true;
            }
            treeFolders.Items.Add(item);
        }
        UpdateTreeWidth();
        return Task.CompletedTask;
    }

    /// <summary>WireViewModeToggle — 註冊 segViewMode 點擊 + tree click 路由。</summary>
    private void WireViewModeToggle()
    {
        // segViewMode items 動態建（要 i18n）。SegmentedItem 只有 parameterless ctor，用 init 設 Text + IconSvg
        segViewMode.Items.Clear();
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = LocalizationManager.GetString("ViewModeFolder") ?? "資料夾", IconSvg = "FolderOutlined" });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = LocalizationManager.GetString("ViewModeAuthor") ?? "作者", IconSvg = "UserOutlined" });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = LocalizationManager.GetString("ViewModeTag") ?? "標籤", IconSvg = "TagOutlined" });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = LocalizationManager.GetString("ViewModeMyLib") ?? "我的閱讀清單", IconSvg = "BookOutlined" });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = LocalizationManager.GetString("ViewModeReadingTrack") ?? "閱讀軌跡", IconSvg = "HistoryOutlined" });
        segViewMode.SelectIndex = 0;
        // SelectIndexChanged: IntEventHandler — (sender, IntEventArgs e)，e.Value 是新 index
        segViewMode.SelectIndexChanged += (s, e) =>
        {
            _currentViewMode = (TreeViewMode)e.Value;
            // 切到 Tag mode 才顯示 chip bar；其他 mode 隱藏並清空
            flowChipBar.Visible = _currentViewMode == TreeViewMode.ByTag;
            if (_currentViewMode != TreeViewMode.ByTag)
            {
                _selectedTagIds.Clear();
                _selectedTagNames.Clear();
                flowChipBar.Controls.Clear();
            }
            _ = LoadTreeAsync();
        };

        // Tree click → 依 Item.Tag 型別分流（Folder mode 走原本 Tag = string 路徑；虛擬庫走 IVirtualLibraryNode）
        // TreeSelectEventArgs.Item 是 TreeItem，.Item.Tag 才是綁定的物件
        treeFolders.NodeMouseClick += (s, e) =>
        {
            if (e.Item?.Tag is IVirtualLibraryNode vnode)
            {
                _treeRightOnNode = e.Button == MouseButtons.Right;
                if (e.Button == MouseButtons.Left) OnVirtualNodeClicked(vnode);
                else if (e.Button == MouseButtons.Middle) OpenVirtualNodeInNewTab(vnode);
                else if (e.Button == MouseButtons.Right) ShowVirtualNodeContextMenu(vnode);
            }
        };
    }

    /// <summary>虛擬庫 node 右鍵選單 — 通用兩項（另開頁籤 / 複製名稱）；
    /// MyVirtualLibraryNode 加管理項（編輯 / 釘選 / 刪除 / 複製 DSL）。</summary>
    private void ShowVirtualNodeContextMenu(IVirtualLibraryNode vnode)
    {
        var openNewTabText = "🔗 " + (LocalizationManager.GetString("MenuOpenInNewTab") ?? "另開頁籤");
        var copyText = "📋 " + (LocalizationManager.GetString("MenuCopyName") ?? "複製名稱");

        var items = new List<AntdUI.IContextMenuStripItem>
        {
            new AntdUI.ContextMenuStripItem(openNewTabText),
            new AntdUI.ContextMenuStripItem(copyText),
        };

        // MyVirtualLibraryNode 額外管理項
        string? editText = null, pinText = null, copyDslText = null, deleteText = null;
        if (vnode is MyVirtualLibraryNode my)
        {
            editText = "✏ " + (LocalizationManager.GetString("MenuEdit") ?? "編輯");
            pinText = my.Library.Pinned
                ? ("📍 " + (LocalizationManager.GetString("MenuUnpin") ?? "取消釘選"))
                : ("📌 " + (LocalizationManager.GetString("MenuPin") ?? "釘選"));
            copyDslText = "🧬 " + (LocalizationManager.GetString("MenuCopyDsl") ?? "複製 DSL");
            deleteText = "🗑 " + (LocalizationManager.GetString("MenuDelete") ?? "刪除");
            items.Add(new AntdUI.ContextMenuStripItem(editText));
            items.Add(new AntdUI.ContextMenuStripItem(pinText));
            items.Add(new AntdUI.ContextMenuStripItem(copyDslText));
            items.Add(new AntdUI.ContextMenuStripItem(deleteText));
        }

        AntdUI.ContextMenuStrip.open(treeFolders, mi =>
        {
            var t = mi.Text;
            if (t == openNewTabText) OpenVirtualNodeInNewTab(vnode);
            else if (t == copyText)
            {
                try { Clipboard.SetText(vnode.DisplayName); } catch { }
            }
            else if (t == editText && vnode is MyVirtualLibraryNode em) EditMyVirtualLibrary(em.Library);
            else if (t == pinText && vnode is MyVirtualLibraryNode pm)
            {
                _metaStore?.SetVirtualLibraryPinned(pm.Library.Id, !pm.Library.Pinned);
                _ = LoadTreeAsync();
            }
            else if (t == copyDslText && vnode is MyVirtualLibraryNode cm)
            {
                try { Clipboard.SetText(cm.Library.Dsl); } catch { }
            }
            else if (t == deleteText && vnode is MyVirtualLibraryNode dm) DeleteMyVirtualLibrary(dm.Library);
        }, items.ToArray());
    }

    /// <summary>tree 空白處（或非 node）右鍵時，在 MyLib mode 下加「新增」選項。</summary>
    private void ShowMyLibBlankContextMenu()
    {
        var addText = "➕ " + (LocalizationManager.GetString("MenuAddVirtualLib") ?? "新增閱讀清單");
        var refreshText = "🔄 " + (LocalizationManager.GetString("MenuRefresh") ?? "重新整理");
        var items = new AntdUI.IContextMenuStripItem[]
        {
            new AntdUI.ContextMenuStripItem(addText),
            new AntdUI.ContextMenuStripItem(refreshText),
        };
        AntdUI.ContextMenuStrip.open(treeFolders, mi =>
        {
            if (mi.Text == addText) AddMyVirtualLibrary();
            else if (mi.Text == refreshText) _ = LoadTreeAsync();
        }, items);
    }

    private void AddMyVirtualLibrary()
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;
        using var dlg = new SaveVirtualLibraryDialog(_metaStore);
        if (dlg.ShowDialog(form) != DialogResult.OK) return;
        _metaStore.CreateVirtualLibrary(dlg.ResultName, dlg.ResultDsl, dlg.ResultIconSvg);
        _ = LoadTreeAsync();
    }

    private void EditMyVirtualLibrary(VirtualLibrary existing)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;
        using var dlg = new SaveVirtualLibraryDialog(_metaStore, existing);
        if (dlg.ShowDialog(form) != DialogResult.OK) return;
        _metaStore.UpdateVirtualLibrary(existing.Id, dlg.ResultName, dlg.ResultDsl, dlg.ResultIconSvg);
        _ = LoadTreeAsync();
    }

    private void DeleteMyVirtualLibrary(VirtualLibrary vlib)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;
        UI.ModalEx.Open(new AntdUI.Modal.Config(form,
            LocalizationManager.GetString("MyLibDeleteTitle") ?? "刪除閱讀清單",
            string.Format(
                LocalizationManager.GetString("MyLibDeleteMsgFmt") ?? "確定刪除「{0}」？此動作不可復原。",
                vlib.Name),
            AntdUI.TType.Warn)
        {
            OkText = LocalizationManager.GetString("MenuDelete") ?? "刪除",
            CancelText = LocalizationManager.GetString("DialogCancel") ?? "取消",
            OnOk = _ => OnConfirmDeleteMyVirtualLibrary(vlib),
        });
    }

    /// <summary>實際刪除虛擬庫 + 刷 tree（marshal 回 UI thread）；OnOk 從 worker thread 呼叫。</summary>
    private bool OnConfirmDeleteMyVirtualLibrary(VirtualLibrary vlib)
    {
        if (_metaStore == null) return true;
        try { _metaStore.DeleteVirtualLibrary(vlib.Id); }
        catch { return true; }
        Action refresh = ReloadMyLibTree;
        BeginInvoke(refresh);
        return true;
    }

    private void ReloadMyLibTree()
    {
        if (IsDisposed) return;
        _ = LoadTreeAsync();
    }

    /// <summary>把虛擬庫 node 的作品另開到新 tab（Tag mode 仍走 chip 過濾，其餘 mode 直接開）。</summary>
    private void OpenVirtualNodeInNewTab(IVirtualLibraryNode vnode)
    {
        if (_metaStore == null) return;
        // Tag mode 在 chip 多選上沒有「另開頁籤」概念 → 退化為 left-click 行為
        if (_currentViewMode == TreeViewMode.ByTag && vnode is TagVirtualNode)
        {
            OnVirtualNodeClicked(vnode);
            return;
        }
        var paths = vnode.ResolveWorks(_metaStore);
        OpenVirtualWorksTab(vnode.DisplayName, paths, forceNew: true);
    }

    /// <summary>左鍵點到虛擬庫 node — 依 mode 跟 node 類型決定行為。
    /// Tag node 在 Tag mode 下加入 chip 多選；其他都是直接開 tab 顯示作品。</summary>
    private void OnVirtualNodeClicked(IVirtualLibraryNode vnode)
    {
        if (_metaStore == null) return;

        if (_currentViewMode == TreeViewMode.ByTag && vnode is TagVirtualNode tag)
        {
            // 累積 chip — 已在 set 中代表 toggle off
            if (_selectedTagIds.Contains(tag.TagId))
            {
                _selectedTagIds.Remove(tag.TagId);
                _selectedTagNames.Remove(tag.TagId);
            }
            else
            {
                _selectedTagIds.Add(tag.TagId);
                // 取譯名（有就用，沒有就原文）— 跟 tree 顯示一致
                var lang = GetCurrentTagLang();
                var zh = _metaStore.GetTagTranslation(tag.TagId, lang);
                _selectedTagNames[tag.TagId] = !string.IsNullOrEmpty(zh) ? zh : tag.Name;
            }
            RebuildChipBar();
            ApplyTagFilter();
            return;
        }

        // 其他 mode：直接開 tab 顯示作品
        var paths = vnode.ResolveWorks(_metaStore);
        OpenVirtualWorksTab(vnode.DisplayName, paths);
    }

    /// <summary>重建 chip bar — 每個 tag 一個 X 可移除；末端「✕ 清除全部」按鈕。
    /// AntdUI.Tag 必須 AutoSize=true，否則 FlowLayoutPanel 會把它算成 ~4px 高（X 點不到）— 見 commit 1248450。</summary>
    private void RebuildChipBar()
    {
        flowChipBar.SuspendLayout();
        flowChipBar.Controls.Clear();
        foreach (var (id, name) in _selectedTagNames)
        {
            var capturedId = id;
            var chip = new AntdUI.Tag
            {
                Text = name,
                CloseIcon = true,
                AutoSize = true,
                Margin = new Padding(2),
                Font = new Font("Microsoft JhengHei UI", 9F),
            };
            chip.CloseChanged += (s, e) =>
            {
                _selectedTagIds.Remove(capturedId);
                _selectedTagNames.Remove(capturedId);
                BeginInvoke(new Action(() => { RebuildChipBar(); ApplyTagFilter(); }));
                return true;
            };
            flowChipBar.Controls.Add(chip);
        }
        // 清除全部按鈕 — 至少 2 個 chip 才顯示
        if (_selectedTagNames.Count >= 2)
        {
            var btnClear = new AntdUI.Button
            {
                Text = LocalizationManager.GetString("ChipClearAll") ?? "清除全部",
                IconSvg = "CloseCircleOutlined",
                Type = AntdUI.TTypeMini.Default,
                Ghost = true,
                Margin = new Padding(4, 2, 2, 2),
                AutoSize = true,
            };
            btnClear.Click += (s, e) =>
            {
                _selectedTagIds.Clear();
                _selectedTagNames.Clear();
                BeginInvoke(new Action(() => { RebuildChipBar(); ApplyTagFilter(); }));
            };
            flowChipBar.Controls.Add(btnClear);
        }
        flowChipBar.ResumeLayout(true);
    }

    /// <summary>用 _selectedTagIds 跑 ListWorksByTags(AND) 並更新 tab 顯示。
    /// 0 chip 時清空當前 tab 並把標題改回提示文字（避免顯示舊結果造成「沒反應」錯覺）。</summary>
    private void ApplyTagFilter()
    {
        if (_metaStore == null) return;
        if (_selectedTagIds.Count == 0)
        {
            var page = tabsContent.SelectedTab;
            if (page == null) return;
            var flow = FindTabFlowLayout(page);
            if (flow != null) flow.Controls.Clear();
            page.Text = LocalizationManager.GetString("TagFilterEmpty") ?? "標籤篩選";
            var bc = FindTabBreadcrumb(page);
            if (bc != null)
            {
                bc.Items.Clear();
                bc.Items.Add(new AntdUI.BreadcrumbItem
                {
                    Text = " " + (LocalizationManager.GetString("TagFilterEmptyHint") ?? "點 tree 選一個或多個標籤") + " ",
                    IconSvg = "TagOutlined"
                });
            }
            return;
        }
        var paths = _metaStore.ListWorksByTags(_selectedTagIds.ToList(), intersect: true);
        var title = string.Join(" + ", _selectedTagNames.Values);
        OpenVirtualWorksTab(title, paths);
    }

    private void LoadSubDirectories(AntdUI.TreeItem node, string path)
    {
        // 已展開過就不重掃（外部變動靠右鍵「重新整理」手動觸發）
        bool isPlaceholder = node.Sub.Count == 1
                             && (node.Sub[0].Tag as string) == LazyPlaceholderTag;
        if (!isPlaceholder) return;

        // 改 async：UI thread 不阻塞，使用者雙擊 tree node 觸發 expand 不卡頓
        Task.Run(() =>
        {
            List<DirectoryInfo> subDirs;
            try
            {
                subDirs = new DirectoryInfo(path).GetDirectories()
                    .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                    .OrderBy(d => d.Name)
                    .ToList();
            }
            catch { return; }

            if (this.IsDisposed) return;
            try
            {
                this.Invoke(() =>
                {
                    if (treeFolders.IsDisposed) return;
                    node.Sub.Clear();
                    foreach (var subDir in subDirs)
                    {
                        node.Sub.Add(CreateFolderNode(subDir));
                    }
                    UpdateTreeWidth();
                });
            }
            catch { /* form/tree 已 dispose 等競態忽略 */ }
        });
    }

    private void UpdateTreeWidth()
    {
        if (treeFolders.Items.Count == 0) return;

        int maxWidth = 250;
        using (var g = treeFolders.CreateGraphics())
        {
            foreach (var item in treeFolders.Items)
            {
                int itemWidth = MeasureNodeWidth(g, item, 0);
                if (itemWidth > maxWidth) maxWidth = itemWidth;
            }
        }

        treeFolders.Width = maxWidth + 40;
    }

    private int MeasureNodeWidth(Graphics g, AntdUI.TreeItem item, int level)
    {
        var size = g.MeasureString(item.Text, treeFolders.Font);
        int currentWidth = (level * 20) + 30 + (int)size.Width;

        int maxSubWidth = currentWidth;
        if (item.Expand && item.Sub.Count > 0)
        {
            foreach (var subItem in item.Sub)
            {
                int subWidth = MeasureNodeWidth(g, subItem, level + 1);
                if (subWidth > maxSubWidth) maxSubWidth = subWidth;
            }
        }
        return maxSubWidth;
    }

    private AntdUI.TreeItem CreateFolderNode(DirectoryInfo dir)
    {
        var node = new AntdUI.TreeItem(dir.Name)
        {
            Tag = dir.FullName,
            IconSvg = "FolderOpenOutlined"
        };

        try
        {
            if (dir.GetDirectories().Any(d => (d.Attributes & FileAttributes.Hidden) == 0))
            {
                node.Sub.Add(new AntdUI.TreeItem("Loading...") { Tag = LazyPlaceholderTag });
            }
        }
        catch { }

        return node;
    }

    private void treeFolders_BeforeExpand(object sender, AntdUI.TreeExpandEventArgs e)
    {
        if (e.Item.Tag is string path)
        {
            LoadSubDirectories(e.Item, path);
        }
        e.CanExpand = true;
    }

    private void treeFolders_NodeMouseDoubleClick(object sender, AntdUI.TreeSelectEventArgs e)
    {
        // 改用滑鼠中鍵開新頁籤，這裡留空讓 AntdUI Tree 走預設行為（toggle expand）
    }
}
