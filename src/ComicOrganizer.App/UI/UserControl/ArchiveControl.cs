using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using System.ComponentModel;

namespace ComicOrganizer.App.UI;

public partial class ArchiveControl : UserControl, IThemedControl
{
    private AppConfig? _config;
    private MappingIntelligence? _intelligence;
    private ArchiveOrganizer? _organizer;
    private IAssetMetaStore? _metaStore;
    private TagTranslator? _tagTranslator;
    private UserPreferences? _prefs;
    private ReadRecordQueue? _readRecordQueue;
    private ToolStripMenuItem? _menuItemCheck;
    private ToolStripMenuItem? _menuItemUncheck;
    private ToolStripMenuItem? _menuItemViewComic;
    private ToolStripMenuItem? _menuItemViewDestDir;

    public ArchiveControl()
    {
        InitializeComponent();
        ConfigureTable();
        btnScan.Click += async (s, e) => await ScanAsync();
        btnExecute.Click += (s, e) => OpenPreview();
        this.WireThemeAndLocalization();
    }

    private void ConfigureTable()
    {
        tableArchive.MultipleRows = true;
        tableArchive.Columns = new ColumnCollection
        {
            new ColumnCheck("Checked") { Width = "40" },
            new Column("Source", "來源名稱") { Width = "40%" },
            new Column("TargetFolder", "目標資料夾") { Width = "25%" },
            new Column("Confidence", "置信度") { Width = "10%", Align = ColumnAlign.Right },
            new Column("Status", "狀態") { Width = "10%" },
            new Column("Action", "操作") { Width = "10%" }
        };

        // 初始化右鍵選單
        var menu = new System.Windows.Forms.ContextMenuStrip();
        _menuItemCheck = new ToolStripMenuItem("勾選選取項目", null, (s, e) => SetSelectedRowsChecked(true));
        _menuItemUncheck = new ToolStripMenuItem("取消勾選選取項目", null, (s, e) => SetSelectedRowsChecked(false));
        _menuItemViewComic = new ToolStripMenuItem("瀏覽欲歸檔漫畫", null, (s, e) => ViewSelectedComic());
        _menuItemViewDestDir = new ToolStripMenuItem("瀏覽歸檔目錄", null, (s, e) => OpenSelectedDestDir());

        menu.Items.Add(_menuItemCheck);
        menu.Items.Add(_menuItemUncheck);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_menuItemViewComic);
        menu.Items.Add(_menuItemViewDestDir);

        menu.Opening += (s, e) =>
        {
            var indices = tableArchive.SelectedIndexs;
            bool hasSelection = (indices != null && indices.Length > 0) || tableArchive.SelectedIndex >= 0;
            _menuItemCheck.Enabled = hasSelection;
            _menuItemUncheck.Enabled = hasSelection;

            var item = GetFirstSelectedItem();
            _menuItemViewComic.Enabled = item != null && !string.IsNullOrEmpty(item.FullPath);
            _menuItemViewDestDir.Enabled = item != null && !string.IsNullOrEmpty(item.DestinationPath);
        };

        tableArchive.ContextMenuStrip = menu;

        // 綁定鍵盤 Space 監聽
        tableArchive.KeyDown += TableArchive_KeyDown;

        // 監聽表頭全選 Checkbox 狀態變更
        tableArchive.CheckedOverallChanged += (column, value) =>
        {
            if (tableArchive.DataSource is not BindingList<ArchiveItem> items || items.Count == 0) return;
            if (value.Value == System.Windows.Forms.CheckState.Indeterminate) return;
            bool targetVal = (value.Value == System.Windows.Forms.CheckState.Checked);
            foreach (var item in items)
            {
                item.Checked = targetVal;
            }
        };
    }

    private void TableArchive_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            ToggleSelectedRows();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void SetSelectedRowsChecked(bool checkValue)
    {
        if (tableArchive.DataSource is not BindingList<ArchiveItem> items || items.Count == 0) return;

        var indices = tableArchive.SelectedIndexs;
        if (indices != null && indices.Length > 0)
        {
            foreach (var idx in indices)
            {
                int targetIdx = idx - 1;
                if (targetIdx >= 0 && targetIdx < items.Count)
                {
                    items[targetIdx].Checked = checkValue;
                }
            }
        }
        else
        {
            int singleIdx = tableArchive.SelectedIndex - 1;
            if (singleIdx >= 0 && singleIdx < items.Count)
            {
                items[singleIdx].Checked = checkValue;
            }
        }
    }

    private void ToggleSelectedRows()
    {
        if (tableArchive.DataSource is not BindingList<ArchiveItem> items || items.Count == 0) return;

        var indices = tableArchive.SelectedIndexs;
        var targetItems = new List<ArchiveItem>();

        if (indices != null && indices.Length > 0)
        {
            foreach (var idx in indices)
            {
                int targetIdx = idx - 1;
                if (targetIdx >= 0 && targetIdx < items.Count)
                {
                    targetItems.Add(items[targetIdx]);
                }
            }
        }
        else
        {
            int singleIdx = tableArchive.SelectedIndex - 1;
            if (singleIdx >= 0 && singleIdx < items.Count)
            {
                targetItems.Add(items[singleIdx]);
            }
        }

        if (targetItems.Count == 0) return;

        bool anyUnchecked = targetItems.Any(x => !x.Checked);
        foreach (var item in targetItems)
        {
            item.Checked = anyUnchecked;
        }
    }

    public void Initialize(AppConfig config, MappingIntelligence intelligence, ArchiveOrganizer organizer,
        IAssetMetaStore? metaStore = null, TagTranslator? tagTranslator = null,
        UserPreferences? prefs = null, ReadRecordQueue? readRecordQueue = null)
    {
        _config = config;
        _intelligence = intelligence;
        _organizer = organizer;
        _metaStore = metaStore;
        _tagTranslator = tagTranslator;
        _prefs = prefs;
        _readRecordQueue = readRecordQueue;
    }

    public void ApplyThemeColors()
    {
        this.BackColor = Core.ThemeColors.Background;
        panelToolbar.BackColor = Core.ThemeColors.Background;
        tableArchive.BackColor = Core.ThemeColors.Surface;
        this.Refresh();
    }

    public void ApplyLocalization()
    {
        btnScan.Text = LocalizationManager.GetString("ArchiveScan");
        btnExecute.Text = LocalizationManager.GetString("ArchivePreview");
        tableArchive.EmptyText = LocalizationManager.GetString("NoData");

        // Headers
        if (tableArchive.Columns != null && tableArchive.Columns.Count >= 7)
        {
            tableArchive.Columns[1].Title = LocalizationManager.GetString("ArchiveColSource") ?? "來源名稱";
            tableArchive.Columns[2].Title = LocalizationManager.GetString("ArchiveColTarget") ?? "目標資料夾";

            tableArchive.Columns[3].Title = LocalizationManager.GetString("ArchiveColDestinationPath");
            if (tableArchive.Columns[3].Title == "ArchiveColDestinationPath")
            {
                tableArchive.Columns[3].Title = System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh")
                    ? "預計歸檔路徑"
                    : "Destination Path";
            }

            tableArchive.Columns[4].Title = LocalizationManager.GetString("ArchiveColConfidence") ?? "置信度";
            tableArchive.Columns[5].Title = LocalizationManager.GetString("ArchiveColStatus") ?? "狀態";
            tableArchive.Columns[6].Title = LocalizationManager.GetString("ArchiveColAction") ?? "操作";
        }

        lblStatus.Text = LocalizationManager.GetString("StatusReady");

        // Context menu items
        if (_menuItemCheck != null)
        {
            _menuItemCheck.Text = LocalizationManager.GetString("ArchiveMenuCheck") ?? "勾選選取項目";
        }
        if (_menuItemUncheck != null)
        {
            _menuItemUncheck.Text = LocalizationManager.GetString("ArchiveMenuUncheck") ?? "取消勾選選取項目";
        }
        if (_menuItemViewComic != null)
        {
            _menuItemViewComic.Text = LocalizationManager.GetString("ArchiveMenuItemViewComic");
            if (_menuItemViewComic.Text == "ArchiveMenuItemViewComic")
            {
                _menuItemViewComic.Text = System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh")
                    ? "瀏覽欲歸檔漫畫"
                    : "View comic to archive";
            }
        }
        if (_menuItemViewDestDir != null)
        {
            _menuItemViewDestDir.Text = LocalizationManager.GetString("ArchiveMenuItemViewDestDir");
            if (_menuItemViewDestDir.Text == "ArchiveMenuItemViewDestDir")
            {
                _menuItemViewDestDir.Text = System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh")
                    ? "瀏覽歸檔目錄"
                    : "Open destination directory";
            }
        }
    }
    public void OnConfigUpdated(AppConfig config)
    {
        _config = config;
    }

    private async Task ScanAsync()
    {
        if (_config == null || _organizer == null) return;

        if (string.IsNullOrWhiteSpace(_config.UnorganizedDirectory) || !Directory.Exists(_config.UnorganizedDirectory))
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, "無效的來源路徑");
            return;
        }

        btnScan.Loading = true;
        lblStatus.Text = "正在掃描匹配...";

        try
        {
            var targetFolders = Directory.GetDirectories(_config.RootDirectory)
                                         .Select(Path.GetFileName)
                                         .Where(n => n != null).Cast<string>().ToList();
            var sourceItems = Directory.GetFileSystemEntries(_config.UnorganizedDirectory);

            var list = new List<ArchiveItem>();

            await Task.Run(() =>
            {
                foreach (var itemPath in sourceItems)
                {
                    string name = Path.GetFileName(itemPath);
                    var result = _organizer.FindBestMatch(name, targetFolders);
                    string targetFolder = result?.FolderName ?? "未匹配";
                    string destPath = string.IsNullOrEmpty(result?.FolderName) ? "" : Path.Combine(_config.RootDirectory, result.FolderName);

                    list.Add(new ArchiveItem
                    {
                        Source = name,
                        FullPath = itemPath,
                        TargetFolder = targetFolder,
                        DestinationPath = destPath,
                        Confidence = result?.Confidence.ToString("F1") ?? "0",
                        Status = result != null && result.IsLearned ? "知識庫" : "模糊比對",
                        Checked = result != null && result.Confidence >= 85,
                        Action = new AntdUI.CellLink[] { new AntdUI.CellButton("Delete", "刪除", AntdUI.TTypeMini.Error)
                        { Ghost = true, IconSvg="DeleteOutlined",IconHoverSvg="DeleteFilled" } }
                    });
                }
            });

            var sortedList = new BindingList<ArchiveItem>(
                list.OrderByDescending(x =>
                {
                    double.TryParse(x.Confidence, out double val);
                    return val;
                }).ToList()
            );

            tableArchive.DataSource = sortedList;
            lblStatus.Text = $"掃描完成，共 {list.Count} 個項目";
            btnExecute.Enabled = sortedList.Count > 0;
        }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, ex.Message);
        }
        finally
        {
            btnScan.Loading = false;
        }
    }

    private async void OpenPreview()
    {
        if (_config == null || _intelligence == null || _organizer == null) return;
        if (tableArchive.DataSource is not BindingList<ArchiveItem> items || items.Count == 0) return;

        var checkedItems = items.Where(x => x.Checked).ToList();
        if (checkedItems.Count == 0)
        {
            var form = this.FindForm();
            if (form != null)
            {
                var msg = LocalizationManager.GetString("ArchiveNoCheckedItems");
                if (msg == "ArchiveNoCheckedItems")
                {
                    msg = System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh")
                        ? "請先勾選要歸檔的項目"
                        : "Please select items to archive";
                }
                AntdUI.Message.warn(form, msg);
            }
            return;
        }

        using var preview = new PreviewForm(checkedItems, _config.RootDirectory, _intelligence, _organizer,
            _config, _metaStore, _tagTranslator);
        preview.ShowDialog(this.FindForm());

        if (preview.IsExecuted)
        {
            await ScanAsync();
        }
    }

    private ArchiveItem? GetFirstSelectedItem()
    {
        if (tableArchive.DataSource is not BindingList<ArchiveItem> items || items.Count == 0) return null;
        var indices = tableArchive.SelectedIndexs;
        if (indices == null || indices.Length == 0)
        {
            int singleIdx = tableArchive.SelectedIndex;
            if (singleIdx >= 0) indices = new int[] { singleIdx };
        }
        if (indices != null && indices.Length > 0)
        {
            foreach (var idx in indices)
            {
                int dataIdx = idx - 1;
                if (dataIdx >= 0 && dataIdx < items.Count)
                {
                    return items[dataIdx];
                }
            }
        }
        return null;
    }

    private void ViewSelectedComic()
    {
        var item = GetFirstSelectedItem();
        if (item == null || string.IsNullOrEmpty(item.FullPath)) return;

        var path = item.FullPath;
        if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, "檔案或目錄不存在");
            return;
        }

        if (_config == null) return;

        IReaderSource source;
        var ext = Path.GetExtension(path);
        try
        {
            if (SupportedExtensions.Archive.Contains(ext) && File.Exists(path))
                source = new ArchiveReaderSource(path);
            else if (SupportedExtensions.Pdf.Contains(ext) && File.Exists(path))
                source = new PdfReaderSource(path);
            else
                source = new FolderReaderSource(path);

            if (source.PageCount == 0)
            {
                source.Dispose();
                var form = this.FindForm();
                if (form != null) AntdUI.Message.error(form, "找不到任何圖片");
                return;
            }
        }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, "無法載入漫畫: " + ex.Message);
            return;
        }

        bool rtl = (_prefs?.DefaultReadingDirection ?? "rtl") == "rtl";
        bool spreadFromCover = _prefs?.DualPageSpreadFromCover ?? false;
        int thrSec = _config.ReadCountThresholdSeconds;
        int thrPages = _config.ReadCountThresholdPages;
        bool track = _prefs?.TrackReadingRecord ?? true;

        var formReader = new ReaderForm(source, rtl, spreadFromCover, _metaStore,
                                        _readRecordQueue, thrSec, thrPages, track);
        formReader.Show();
    }

    private void OpenSelectedDestDir()
    {
        var item = GetFirstSelectedItem();
        if (item == null || string.IsNullOrEmpty(item.DestinationPath)) return;

        try
        {
            if (!System.IO.Directory.Exists(item.DestinationPath))
            {
                System.IO.Directory.CreateDirectory(item.DestinationPath);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = item.DestinationPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, "無法開啟目錄: " + ex.Message);
        }
    }
}
