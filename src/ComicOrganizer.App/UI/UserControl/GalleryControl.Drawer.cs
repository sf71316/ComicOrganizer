using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// GalleryControl 詳情 drawer（#47）— AuthorInfoPanel + WorkDetailPanel 共用 host。
/// 從 GalleryControl.cs 抽出 — 事件 wire / 編輯儲存 / 開關 drawer / 寬度控制 /
/// 重置閱讀記錄 / 顯示作者作品詳情。
/// </summary>
public partial class GalleryControl
{
    private bool _isPromptingUnsaved;
    private bool _allowCloseWithoutPrompt;

    /// <summary>
    /// detailDrawerHost 內兩個 panel 的事件統一接：CloseRequested / 點作品 / Author 連結 / Explorer。
    /// 從 ctor 抽出避免 ctor 過長。
    /// </summary>
    private void WireDrawerEvents()
    {
        // 兩個 panel 的 X 鈕都走未存檔防護關閉流程
        detailDrawerHost.AuthorPanel.CloseRequested += () => RequestCloseDetailDrawer();
        detailDrawerHost.WorkPanel.CloseRequested += () => RequestCloseDetailDrawer();

        // 點 AuthorInfoPanel 的 RecentlyRead / AllWorks 行 → 先檢查未存檔再決定是否開閱讀
        detailDrawerHost.AuthorPanel.RequestOpenWork += path =>
            GuardUnsavedChanges(() => RecordReadAndOpenViewer(path, refreshAuthorPanel: true));

        // WorkDetailPanel 的 Author 連結 → 先檢查未存檔再切回 AuthorInfoPanel
        detailDrawerHost.WorkPanel.RequestOpenAuthor += authorId =>
            GuardUnsavedChanges(() => OpenAuthorByIdCore(authorId));

        // WorkDetailPanel 的「閱讀」按鈕 + cover 點擊
        detailDrawerHost.WorkPanel.RequestOpenViewer += path =>
            RecordReadAndOpenViewer(path, refreshAuthorPanel: false);

        // WorkDetailPanel 的「開啟位置」 → Explorer
        detailDrawerHost.WorkPanel.RequestExplorer += path => OpenInExplorer(path);

        // 「重置閱讀記錄」：彈確認 Modal → ResetReadingRecord → 刷面板 + card icon
        detailDrawerHost.WorkPanel.RequestResetReading += path => ConfirmAndResetWorkReading(path);

        // 編輯儲存：寫 metadata + 設 manually_edited + 重 set tags + 刷面板 / 卡片
        detailDrawerHost.WorkPanel.RequestSaveEdit += data => SaveWorkEdit(data);

        // 從 nhentai 拉取 metadata（外部）
        detailDrawerHost.WorkPanel.RequestFetchExternal += async path => await FetchExternalMetadata(path);

        // 進 edit mode → drawer auto-widen 到 panel 要求的最小寬度（不縮回 — 尊重 user 拖拉的結果）
        detailDrawerHost.WorkPanel.RequestExpandWidth += minW => ExpandDrawerTo(minW);
        detailDrawerHost.AuthorPanel.RequestExpandWidth += minW => ExpandDrawerTo(minW);

        // 點 header 「展開最大寬度」按鈕 → toggle maximize
        detailDrawerHost.WorkPanel.RequestToggleMaximize += () => ToggleDrawerMaximize();
        detailDrawerHost.AuthorPanel.RequestToggleMaximize += () => ToggleDrawerMaximize();

        // 顯示按鈕：依 config.EnableExternalMetadata
        detailDrawerHost.WorkPanel.ShowFetchExternalButton =
            _prefs != null && _config != null && _config.EnableExternalMetadata;

        // AuthorInfoPanel 加「清除此作者所有閱讀記錄」入口（透過 panel 暴露的 event）
        detailDrawerHost.AuthorPanel.RequestResetAuthorReading += authorId => ConfirmAndResetAuthorReading(authorId);

        // AuthorInfoPanel 編輯儲存
        detailDrawerHost.AuthorPanel.RequestSaveEdit += data => SaveAuthorEdit(data);

        // 「同步此作者所有作品」批次拉取（#52 Phase F）
        detailDrawerHost.AuthorPanel.RequestBatchSyncAll += async authorId => await BatchSyncAllForAuthor(authorId);

        // 顯示按鈕：依 config.EnableExternalMetadata
        detailDrawerHost.AuthorPanel.ShowBatchFetchButton =
            _prefs != null && _config != null && _config.EnableExternalMetadata;
    }

    /// <summary>AuthorInfoPanel「儲存」事件 handler — RenameAuthor + SetAuthorFolderPath + 別名 diff 寫入 + SetAuthorTags。</summary>
    private void SaveAuthorEdit(AuthorEditData data)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        try
        {
            // 1. 主名 — 改了才呼叫；衝突時 Modal 詢問是否合併
            var current = _metaStore.GetAuthorInfo(data.AuthorId);
            if (current != null && !string.Equals(current.Author.MainName, data.MainName))
            {
                try { _metaStore.RenameAuthor(data.AuthorId, data.MainName); }
                catch (InvalidOperationException)
                {
                    if (form != null)
                    {
                        var conflict = _metaStore.FindAuthorByMainName(data.MainName);
                        if (conflict != null)
                        {
                            UI.ModalEx.Open(new AntdUI.Modal.Config(form, "作者名稱衝突",
                                $"已存在主名為「{data.MainName}」的作者（id={conflict.Id}）。\n要把當前作者的所有作品/別名/標籤合併進去嗎？\n\n此動作不可復原。",
                                AntdUI.TType.Warn)
                            {
                                OkText = "合併",
                                CancelText = "取消",
                                OnOk = _ =>
                                {
                                    try { _metaStore.MergeAuthor(data.AuthorId, conflict.Id); }
                                    catch { return true; }
                                    BeginInvoke(new Action(() =>
                                    {
                                        if (IsDisposed) return;
                                        ForceCloseDetailDrawer();
                                        var ignored = LoadTreeAsync();
                                    }));
                                    return true;
                                }
                            });
                            return;   // 等使用者按 OK；合併後不繼續走下面流程
                        }
                    }
                    return;
                }
            }

            // 2. 別名 diff：DB 中存在但 data 沒有 = 刪；data 中 Id=0 = 新增；其他 = 改
            var dbAliases = _metaStore.ListAuthorAliases(data.AuthorId).ToDictionary(a => a.Id);
            var keepIds = new HashSet<long>();
            foreach (var alias in data.Aliases)
            {
                if (alias.Id == 0)
                {
                    _metaStore.AddAuthorAlias(data.AuthorId, alias.Name, alias.Lang);
                }
                else
                {
                    keepIds.Add(alias.Id);
                    if (dbAliases.TryGetValue(alias.Id, out var dbA) &&
                        (dbA.Name != alias.Name || (dbA.Lang ?? "") != (alias.Lang ?? "")))
                    {
                        _metaStore.UpdateAuthorAlias(alias.Id, alias.Name, alias.Lang);
                    }
                }
            }
            foreach (var dbA in dbAliases.Values)
                if (!keepIds.Contains(dbA.Id)) _metaStore.DeleteAuthorAlias(dbA.Id);

            // 4. tag — 整批 reset
            _metaStore.SetAuthorTags(data.AuthorId, data.Tags);

            // 5. 刷面板
            var fresh = _metaStore.GetAuthorInfo(data.AuthorId);
            if (fresh != null) detailDrawerHost.ShowAuthor(fresh);
        }
        catch (Exception ex)
        {
            if (form != null) AntdUI.Message.error(form, "儲存失敗: " + ex.Message);
        }
    }

    /// <summary>WorkDetailPanel「儲存」事件 handler — 寫 metadata + author 解析 + tags + manually_edited 旗標。</summary>
    private void SaveWorkEdit(WorkEditData data)
    {
        if (_metaStore == null) return;
        var assetId = _metaStore.GetAssetId(data.Path);
        if (assetId == null) return;

        try
        {
            // 解析作者：留空 = 清空 author_id；有名 = UpsertAuthor 取或建
            long? authorId = null;
            if (!string.IsNullOrWhiteSpace(data.AuthorName))
            {
                authorId = _metaStore.UpsertAuthor(data.AuthorName.Trim());
            }

            // 取舊 metadata 保留閱讀相關欄位（read_status / read_count / last_read / last_page），
            // 編輯不應動到閱讀記錄
            var existing = _metaStore.GetMetadata(assetId.Value);
            var updated = new AssetMetadata
            {
                AssetId = assetId.Value,
                AuthorId = authorId,
                Title = data.Title,
                Series = data.Series,
                ChapterNo = data.ChapterNo,
                Language = data.Language,
                TranslationGroup = data.TranslationGroup,
                Rating = data.Rating,
                PageCount = existing?.PageCount,
                ReadStatus = existing?.ReadStatus ?? ReadStatus.Unread,
                ReadCount = existing?.ReadCount ?? 0,
                LastRead = existing?.LastRead,
                LastPage = existing?.LastPage,
                ExternalId = existing?.ExternalId,
                ParsedAt = existing?.ParsedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ManuallyEdited = true,
            };
            _metaStore.UpsertMetadata(updated);
            _metaStore.SetAssetTags(assetId.Value, data.Tags);

            // 刷面板 + 卡片 metadata view（已寫入 DB，先清編輯態避免重複提示）
            detailDrawerHost.DiscardEditingState();
            ShowWorkDetailCore(data.Path);
            RefreshCardReadStatusByPath(data.Path);
        }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, "儲存失敗: " + ex.Message);
        }
    }

    private void ConfirmAndResetWorkReading(string path)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;
        var name = Path.GetFileName(path);
        UI.ModalEx.Open(new AntdUI.Modal.Config(form, "重置閱讀記錄",
            $"重置「{name}」的閱讀狀態 / 次數 / 上次閱讀 / 上次頁碼？\n\n此動作不可復原；metadata（標題 / 作者 / 標籤）會保留。",
            AntdUI.TType.Warn)
        {
            OkText = "重置",
            CancelText = "取消",
            OnOk = _ =>
            {
                try { _metaStore.ResetReadingRecord(path); }
                catch { return true; }
                // Modal OnOk 由 AntdUI 在 worker thread fire，UI 更新需 marshal 回 UI thread
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    detailDrawerHost.DiscardEditingState();
                    ShowWorkDetailCore(path);
                    RefreshCardReadStatusByPath(path);
                });
                return true;
            }
        });
    }

    private void ConfirmAndResetAuthorReading(long authorId)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;
        var info = _metaStore.GetAuthorInfo(authorId);
        if (info == null) return;
        UI.ModalEx.Open(new AntdUI.Modal.Config(form, "清除作者閱讀記錄",
            $"清除「{info.Author.MainName}」底下所有 {info.TotalWorks} 件作品的閱讀記錄？\n\n此動作不可復原；metadata 會保留。",
            AntdUI.TType.Warn)
        {
            OkText = "清除",
            CancelText = "取消",
            OnOk = _ =>
            {
                try { _metaStore.ResetReadingRecordsForAuthor(authorId); }
                catch { return true; }
                // 重撈 + 刷面板 + 卡片 icon — marshal 回 UI thread（Modal callback 在 worker thread）
                var fresh = _metaStore.GetAuthorInfo(authorId);
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    if (fresh != null) detailDrawerHost.ShowAuthor(fresh);
                    foreach (var w in info.AllWorks) RefreshCardReadStatusByPath(w.Path);
                });
                return true;
            }
        });
    }

    /// <summary>
    /// 共用：紀錄一次「讀過」+ 開外部 viewer + 刷對應 panel + 同步卡片 icon。
    /// AuthorInfoPanel 與 WorkDetailPanel 點作品的行為共用此 helper。
    /// </summary>
    private void RecordReadAndOpenViewer(string path, bool refreshAuthorPanel)
    {
        // 走 queue 延遲提交：30s 後才實際 RecordRead；queue.Recorded event 觸發時自動刷卡片 icon
        EnqueueReadIfEnabled(path);
        OpenForReading(path);
        if (refreshAuthorPanel) ShowAuthorInfoCore(path);
        else ShowWorkDetailCore(path);
    }

    private void OpenAuthorByIdCore(long authorId)
    {
        if (_metaStore == null) return;
        var info = _metaStore.GetAuthorInfo(authorId);
        if (info == null)
            detailDrawerHost.ShowAuthorEmpty(LocalizationManager.GetString("AuthorInfoNoData") ?? "作者資料缺失");
        else
            detailDrawerHost.ShowAuthor(info);
    }

    /// <summary>
    /// 解析 path 對應的 author，用 AntdUI.Drawer 從右邊滑出顯示（含未存檔編輯保護）。
    /// </summary>
    private void ShowAuthorInfo(string path)
        => GuardUnsavedChanges(() => ShowAuthorInfoCore(path));

    private void ShowAuthorInfoCore(string path)
    {
        if (_metaStore == null)
        {
            detailDrawerHost.ShowAuthorEmpty(LocalizationManager.GetString("AuthorInfoNoData") ?? "無法解析此路徑的作者資訊");
        }
        else
        {
            var authorId = _metaStore.ResolveAuthorIdByPath(path);
            if (authorId == null)
            {
                detailDrawerHost.ShowAuthorEmpty(LocalizationManager.GetString("AuthorInfoNoData") ?? "無法解析此路徑的作者資訊");
            }
            else
            {
                var info = _metaStore.GetAuthorInfo(authorId.Value);
                if (info == null)
                    detailDrawerHost.ShowAuthorEmpty(LocalizationManager.GetString("AuthorInfoNoData") ?? "作者資料缺失");
                else
                    detailDrawerHost.ShowAuthor(info);
            }
        }
        EnsureDetailDrawerOpen();
    }

    /// <summary>
    /// 解析 path 對應的作品詳細資料，用 AntdUI.Drawer 從右邊滑出顯示（含未存檔編輯保護）。
    /// 先以 null cover 開啟 drawer 避免閃爍，async 補上 cover image。
    /// </summary>
    private void ShowWorkDetail(string path)
        => GuardUnsavedChanges(() => ShowWorkDetailCore(path));

    private void ShowWorkDetailCore(string path)
    {
        if (_metaStore == null)
        {
            detailDrawerHost.ShowWorkEmpty(LocalizationManager.GetString("WorkDetailNoData") ?? "找不到此作品的詳細資訊");
            EnsureDetailDrawerOpen();
            return;
        }
        var detail = _metaStore.GetWorkDetail(path);
        if (detail == null)
        {
            detailDrawerHost.ShowWorkEmpty(LocalizationManager.GetString("WorkDetailNoData") ?? "找不到此作品的詳細資訊");
            EnsureDetailDrawerOpen();
            return;
        }
        detailDrawerHost.ShowWork(detail, null);
        EnsureDetailDrawerOpen();
        _ = LoadWorkCoverAsync(path);
    }

    private bool HasPendingUnsavedDrawerEdits()
        => _detailDrawerForm != null && !_detailDrawerForm.IsDisposed && detailDrawerHost.HasUnsavedChanges;

    private void GuardUnsavedChanges(Action proceed)
    {
        if (!HasPendingUnsavedDrawerEdits())
        {
            proceed();
            return;
        }

        ConfirmDiscardUnsavedChanges(() =>
        {
            detailDrawerHost.DiscardEditingState();
            proceed();
        });
    }

    private void RequestCloseDetailDrawer()
    {
        if (_detailDrawerForm == null || _detailDrawerForm.IsDisposed) return;
        if (!detailDrawerHost.HasUnsavedChanges)
        {
            ForceCloseDetailDrawer();
            return;
        }

        ConfirmDiscardUnsavedChanges(ForceCloseDetailDrawer);
    }

    private void ForceCloseDetailDrawer()
    {
        detailDrawerHost.DiscardEditingState();
        if (_detailDrawerForm == null || _detailDrawerForm.IsDisposed) return;
        _allowCloseWithoutPrompt = true;
        try
        {
            _detailDrawerForm.Close();
        }
        finally
        {
            _allowCloseWithoutPrompt = false;
        }
    }

    private void ConfirmDiscardUnsavedChanges(Action onDiscardConfirmed)
    {
        if (_isPromptingUnsaved) return;

        var form = this.FindForm();
        if (form == null || form.IsDisposed)
        {
            onDiscardConfirmed();
            return;
        }

        int confirmed = 0;
        _isPromptingUnsaved = true;
        try
        {
            UI.ModalEx.Open(new AntdUI.Modal.Config(form,
                LocalizationManager.GetString("UnsavedChangesTitle") ?? "未儲存的編輯",
                LocalizationManager.GetString("UnsavedChangesMessage") ?? "有未儲存的編輯內容，是否放棄變更？",
                AntdUI.TType.Warn)
            {
                OkText = LocalizationManager.GetString("DiscardChanges") ?? "放棄變更",
                CancelText = LocalizationManager.GetString("ContinueEditing") ?? "繼續編輯",
                OnOk = _ =>
                {
                    Interlocked.Exchange(ref confirmed, 1);
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (IsDisposed) return;
                                onDiscardConfirmed();
                            }
                            finally
                            {
                                _isPromptingUnsaved = false;
                            }
                        }));
                    }
                    catch
                    {
                        _isPromptingUnsaved = false;
                    }
                    return true;
                }
            });
        }
        finally
        {
            if (Volatile.Read(ref confirmed) == 0)
                _isPromptingUnsaved = false;
        }
    }

    private void OnDetailDrawerFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowCloseWithoutPrompt) return;
        if (e.CloseReason != CloseReason.UserClosing) return;
        if (!detailDrawerHost.HasUnsavedChanges)
        {
            detailDrawerHost.DiscardEditingState();
            return;
        }

        e.Cancel = true;
        if (_isPromptingUnsaved) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                RequestCloseDetailDrawer();
            }));
        }
        catch { /* control disposed */ }
    }

    /// <summary>非同步載 work cover 後 marshal 回 UI thread 套到 WorkDetailPanel。</summary>
    private async Task LoadWorkCoverAsync(string path)
    {
        try
        {
            var img = await _coverManager.GetCoverAsync(path, 300);
            if (img == null) return;
            if (this.IsDisposed) { img.Dispose(); return; }
            this.BeginInvoke(new Action(() =>
            {
                if (!this.IsDisposed) detailDrawerHost.WorkPanel.SetCover(img);
                else img.Dispose();
            }));
        }
        catch { /* cover 取不到不影響 panel */ }
    }

    /// <summary>把 drawer Form 撐寬到至少 minWidth（auto-widen on edit mode）。
    /// 尊重 user 拖拉：若當前已比 minWidth 寬則不動。Drawer 還沒開時 no-op。</summary>
    private void ExpandDrawerTo(int minWidth)
    {
        if (_detailDrawerForm == null || _detailDrawerForm.IsDisposed) return;
        if (_detailDrawerForm.Width >= minWidth) return;
        SetDrawerWidth(minWidth);
    }

    /// <summary>「展開最大寬度」按鈕 toggle — 已在 max 狀態 → 回 580；不在 → 撐到接近螢幕邊。</summary>
    private int? _preMaximizedWidth;
    private void ToggleDrawerMaximize()
    {
        if (_detailDrawerForm == null || _detailDrawerForm.IsDisposed) return;
        var ownerForm = this.FindForm();
        if (ownerForm == null) return;

        // 接近螢幕邊：留 60 px 給 owner 的左側看得到（避免完全覆蓋）
        var maxWidth = Math.Max(640, ownerForm.ClientSize.Width - 60);
        var atMax = _detailDrawerForm.Width >= maxWidth - 4;   // 允許 4 px 容差

        if (atMax)
        {
            // 還原：先用記下的值，沒有就回 580（edit-mode 預設）
            SetDrawerWidth(_preMaximizedWidth ?? 580);
            _preMaximizedWidth = null;
        }
        else
        {
            _preMaximizedWidth = _detailDrawerForm.Width;   // 記住，回退能還原
            SetDrawerWidth(maxWidth);
        }
    }

    private void SetDrawerWidth(int newWidth)
    {
        if (_detailDrawerForm == null || _detailDrawerForm.IsDisposed) return;
        var oldWidth = _detailDrawerForm.Width;
        if (oldWidth == newWidth) return;
        var deltaX = newWidth - oldWidth;
        _detailDrawerForm.Left -= deltaX;   // 靠右 dock：寬度增加 → Left 減少才會視覺貼右
        _detailDrawerForm.Width = newWidth;
    }

    /// <summary>共用 Drawer open/refresh 邏輯：已開→Activate；未開→新開 380 寬 Drawer。</summary>
    private void EnsureDetailDrawerOpen()
    {
        if (_detailDrawerForm != null && !_detailDrawerForm.IsDisposed)
        {
            _detailDrawerForm.Activate();
            return;
        }

        var ownerForm = this.FindForm();
        if (ownerForm == null) return;

        // 若上一輪 filter 還在等放開滑鼠按鍵，先清掉避免殘留
        if (_drawerClickFilter != null)
        {
            _drawerClickFilter.Detach();
            _drawerClickFilter = null;
        }

        // Drawer 量 Content.Size 決定 drawer 大小。每次強制 reset 成 380 × formHeight
        detailDrawerHost.Dock = DockStyle.None;
        detailDrawerHost.Size = new Size(420, ownerForm.ClientSize.Height);

        var config = new AntdUI.Drawer.Config(ownerForm, detailDrawerHost)
        {
            Align = AntdUI.TAlignMini.Right,
            Mask = false,
            Padding = 0,
            Dispose = false,
            OnClose = () =>
            {
                if (_drawerClickFilter != null)
                {
                    _drawerClickFilter.MarkDrawerClosed();
                    if (!_drawerClickFilter.HasPendingButtonUp)
                    {
                        _drawerClickFilter.Detach();
                        _drawerClickFilter = null;
                    }
                }
                if (_detailDrawerForm != null)
                {
                    _detailDrawerForm.FormClosing -= OnDetailDrawerFormClosing;
                }
                detailDrawerHost.DiscardEditingState();
                _detailDrawerForm = null;
            },
        };

        _detailDrawerForm = AntdUI.Drawer.open(config);

        // 點 drawer 以外區域 → 消費穿透點擊並以非同步方式觸發未存檔檢查與關閉
        if (_detailDrawerForm != null)
        {
            _detailDrawerForm.FormClosing += OnDetailDrawerFormClosing;
            _drawerClickFilter = new DrawerOutsideClickFilter(
                _detailDrawerForm,
                onOutsideClick: RequestCloseDetailDrawer,
                isPrompting: () => _isPromptingUnsaved);
            Application.AddMessageFilter(_drawerClickFilter);
        }
    }
}
