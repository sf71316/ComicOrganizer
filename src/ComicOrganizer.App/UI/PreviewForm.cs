using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using System.ComponentModel;

namespace ComicOrganizer.App.UI;

public partial class PreviewForm : Window
{
    private List<ArchiveGroup> _groups;
    private readonly MappingIntelligence _intelligence;
    private readonly ArchiveOrganizer _organizer;
    private readonly AppConfig? _config;
    private readonly IAssetMetaStore? _metaStore;
    private readonly TagTranslator? _tagTranslator;

    public PreviewForm(List<ArchiveItem> items, string rootDirectory, MappingIntelligence intelligence, ArchiveOrganizer organizer,
        AppConfig? config = null, IAssetMetaStore? metaStore = null, TagTranslator? tagTranslator = null)
    {
        InitializeComponent();
        _intelligence = intelligence;
        _organizer = organizer;
        _config = config;
        _metaStore = metaStore;
        _tagTranslator = tagTranslator;

        _groups = items.GroupBy(x => x.DestinationPath)
                       .Select(g => new ArchiveGroup
                       {
                           DestinationPath = g.Key,
                           Items = new BindingList<ArchiveItem>(g.ToList())
                       }).ToList();

        // 確保父容器支援捲軸
        //container.AutoScroll = true;

        this.Load += (s, e) => InitializeOfficialCollapseUI();

        btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
        btnExecute.Click += async (s, e) => await ExecuteArchiveAsync();

        // checkbox 啟用條件：config 已注入 + EnableExternalMetadata + 有 store + 有 API key
        var canFetch = _config != null
                       && _config.EnableExternalMetadata
                       && _metaStore != null
                       && !string.IsNullOrEmpty(_config.GetNhentaiApiKey());
        cbFetchMetadata.Enabled = canFetch;

        LocalizationManager.LanguageChanged += (s, e) => ApplyLocalization();
        ApplyLocalization();
        ThemeManager.ThemeChanged += (s, e) => ThemeManager.ApplyTheme(this);
        ThemeManager.ApplyTheme(this);
    }

    private void ApplyLocalization()
    {
        this.Text = LocalizationManager.GetString("PreviewTitle");
        btnExecute.Text = LocalizationManager.GetString("ArchiveExecuteRun");
        btnCancel.Text = LocalizationManager.GetString("SettingsCancel");
        cbFetchMetadata.Text = LocalizationManager.GetString("PreviewFetchMetaCheckbox") ?? "搬完抓 metadata";
        // Update headers if already initialized
        foreach (var item in collapseMain.Items)
        {
            foreach (var btn in item.Buttons)
            {
                if (btn is AntdUI.CollapseGroupButton sbtn)
                {
                    sbtn.CheckedText = LocalizationManager.GetString("ArchiveExecute");
                    sbtn.UnCheckedText = LocalizationManager.GetString("ArchiveSkip");
                }
            }
            if (item.Controls.Count > 0 && item.Controls[0] is AntdUI.Table table)
            {
                table.Columns[0].Title = LocalizationManager.GetString("SourceName");
                table.Columns[1].Title = LocalizationManager.GetString("Action");
            }
        }
    }

    private void InitializeOfficialCollapseUI()
    {
        //container.Controls.Clear();

        // 1. 建立主 Collapse 元件
        //var collapse = new AntdUI.Collapse
        //{
        //    Unique= true,
        //    Dock = DockStyle.Top,
        //    AutoSize = true,
        //    Name = "collapseMain",
        //    ContentPadding = new Size(5, 5),
        //    HeaderBg = Color.FromArgb(194, 193, 192),
        //    FontExpand = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold)
        //};

        // 確保初始寬度大於 0，否則佈局引擎不會工作
        collapseMain.Width = this.splitter1.Panel2.Width > 0 ? this.splitter1.Panel2.Width - 20 : 800;
        collapseMain.Height = 100; // 給予初始高度

        // 處理群組開關事件
        collapseMain.ButtonClick += (s, e) =>
        {
            var grp = _groups.FirstOrDefault(g => g.DestinationPath == e.Value.Name);
            if (grp != null)
            {
                grp.Checked = e.Value.Checked;
                UpdateExecuteButtonState();
            }
        };

        foreach (var group in _groups)
        {
            // 2. 建立 CollapseItem
            var collapseItem = new AntdUI.CollapseItem
            {
                Text = $"{group.DisplayTitle} ",
                //Expand = true,
                Height = 200,
                AutoScroll = true,

            };

            // 3. 加入 Switch 按鈕
            var switchBtn = new AntdUI.CollapseGroupButton
            {
                Name = group.DestinationPath,
                EditType = AntdUI.EButtonEditTypes.Switch,
                CheckedText = "執行",
                UnCheckedText = "略過",
                Checked = group.Checked,
                SwitchMode = true,
                Width = 100
            };
            collapseItem.Buttons.Add(switchBtn);

            var table = new AntdUI.Table
            {
                Dock = DockStyle.Fill,
                DataSource = group.Items,
                AutoSizeColumnsMode = AntdUI.ColumnsMode.Fill,
                Gap = 4,
            };

            table.Columns = new ColumnCollection {
                new Column("Source", "來源名稱") { Width = "90%" },
                new Column("Action", "操作")
            };

            table.CellButtonClick += (s, e) =>
            {
                if (e.Btn.Id == "Delete" && e.Record is ArchiveItem archiveItem)
                {
                    group.Items.Remove(archiveItem);
                    collapseItem.Text = group.DisplayTitle;
                    if (group.Items.Count == 0) collapseItem.Visible = false;
                    UpdateExecuteButtonState();
                    collapseMain.LoadLayout();
                }
            };

            collapseItem.Controls.Add(table);
            collapseMain.Items.Add(collapseItem);
        }
        UpdateExecuteButtonState();

    }

    private void UpdateExecuteButtonState()
    {
        btnExecute.Enabled = _groups.Any(x => x.Checked && x.Items.Count > 0);
    }

    private async Task ExecuteArchiveAsync()
    {
        var checkedGroups = _groups.Where(g => g.Checked && g.Items.Count > 0).ToList();
        int totalFiles = checkedGroups.Sum(g => g.Items.Count);
        if (totalFiles == 0) return;

        btnExecute.Loading = true;
        btnCancel.Enabled = false;
        cbFetchMetadata.Enabled = false;
        progress.Visible = true;

        // 是否抓 metadata：勾選 + checkbox 啟用前提（config + store + API key）已在 ctor 驗證
        bool fetchEnabled = cbFetchMetadata.Checked && _metaStore != null && _config != null;
        NhentaiApiClient? client = null;
        BatchExternalFetchService? fetchService = null;
        BatchSyncStrategy? strategy = null;
        if (fetchEnabled)
        {
            var apiKey = _config!.GetNhentaiApiKey();
            client = new NhentaiApiClient(apiKey!);
            fetchService = new BatchExternalFetchService(_metaStore!, client, _tagTranslator);
            strategy = BatchSyncStrategy.FromPreset(OverwritePolicyPreset.KeepAuthor);
        }

        try
        {
            int currentCount = 0;
            foreach (var group in checkedGroups)
            {
                foreach (var item in group.Items)
                {
                    currentCount++;
                    string targetPath = Path.Combine(group.DestinationPath, item.Source);

                    this.Invoke(() =>
                    {
                        item.Status = "移動中...";
                        lblProgress.Text = $"處理: {item.Source}";
                        progress.Value = (int)((double)currentCount / totalFiles * 100);
                    });

                    string moveStatus;
                    bool moved = false;
                    try
                    {
                        string destDir = Path.GetDirectoryName(targetPath)!;
                        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                        if (Directory.Exists(item.FullPath)) Directory.Move(item.FullPath, targetPath);
                        else File.Move(item.FullPath, targetPath);
                        moved = true;

                        // 寫 catalog（前提：搬完才能 fetch metadata）
                        if (_metaStore != null) UpsertCatalogForMovedItem(targetPath);

                        // Learn keywords
                        var kws = _organizer.ExtractAllKeywords(item.Source);
                        foreach (var kw in kws)
                        {
                            if (_organizer.IsKeywordValidForTarget(kw, Path.GetFileName(group.DestinationPath)))
                                _intelligence.Learn(kw, Path.GetFileName(group.DestinationPath));
                        }

                        moveStatus = "完成";
                    }
                    catch (Exception ex)
                    {
                        moveStatus = "失敗: " + ex.Message;
                    }

                    // 抓 metadata（搬成功 + 勾選 + 服務就緒）
                    if (moved && fetchEnabled && fetchService != null && strategy != null)
                    {
                        this.Invoke(() => lblProgress.Text = $"處理: {item.Source} (抓 metadata...)");
                        try
                        {
                            var res = await fetchService.ProcessOneAsync(targetPath, strategy);
                            switch (res.Status)
                            {
                                case BatchItemStatus.Applied when res.Merge != null:
                                    var detail = _metaStore!.GetWorkDetail(targetPath);
                                    if (detail != null) fetchService.ApplyMergeResult(detail, res.Merge);
                                    moveStatus += " · meta✓";
                                    break;
                                case BatchItemStatus.Skipped:
                                    moveStatus += $" · meta跳過({res.Message})";
                                    break;
                                case BatchItemStatus.NotFound:
                                    moveStatus += $" · meta找不到(id={res.ExternalId})";
                                    break;
                                case BatchItemStatus.Error:
                                    moveStatus += $" · meta失敗({res.Message})";
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            moveStatus += " · meta失敗(" + ex.Message + ")";
                        }
                    }

                    var finalStatus = moveStatus;
                    this.Invoke(() => item.Status = finalStatus);
                }
            }
        }
        finally
        {
            client?.Dispose();
        }

        AntdUI.Message.success(this, "歸檔處理完畢");
        btnExecute.Loading = false;
        btnCancel.Text = "關閉";
        btnCancel.Enabled = true;
    }

    /// <summary>搬檔成功後寫 catalog，讓後續 metadata fetch 能拿到 asset_id。</summary>
    private void UpsertCatalogForMovedItem(string newPath)
    {
        if (_metaStore == null) return;
        try
        {
            bool isFolder = Directory.Exists(newPath);
            var name = Path.GetFileName(newPath);
            long mtime;
            long? fileSize = null;
            if (isFolder)
            {
                mtime = new DirectoryInfo(newPath).LastWriteTimeUtc.ToFileTimeUtc();
            }
            else
            {
                var fi = new FileInfo(newPath);
                mtime = fi.LastWriteTimeUtc.ToFileTimeUtc();
                fileSize = fi.Length;
            }
            _metaStore.UpsertCatalogOnly(new AssetMeta
            {
                Path = newPath,
                Name = name,
                IsFolder = isFolder,
                FileSize = fileSize,
                Mtime = mtime,
            });
        }
        catch { /* catalog 寫失敗不致命；下次 UpdateIndex 會補 */ }
    }
}
