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
    private ToolStripMenuItem? _menuItemCheck;
    private ToolStripMenuItem? _menuItemUncheck;

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
            new ColumnCheck("Checked", "") { Width = "5%" },
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
        menu.Items.Add(_menuItemCheck);
        menu.Items.Add(_menuItemUncheck);

        menu.Opening += (s, e) =>
        {
            var indices = tableArchive.SelectedIndexs;
            bool hasSelection = (indices != null && indices.Length > 0) || tableArchive.SelectedIndex >= 0;
            _menuItemCheck.Enabled = hasSelection;
            _menuItemUncheck.Enabled = hasSelection;
        };

        tableArchive.ContextMenuStrip = menu;

        // 綁定鍵盤 Space 監聽
        tableArchive.KeyDown += TableArchive_KeyDown;
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
        IAssetMetaStore? metaStore = null, TagTranslator? tagTranslator = null)
    {
        _config = config;
        _intelligence = intelligence;
        _organizer = organizer;
        _metaStore = metaStore;
        _tagTranslator = tagTranslator;
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
        lblStatus.Text = LocalizationManager.GetString("StatusReady");
        tableArchive.EmptyText = LocalizationManager.GetString("NoData");

        if (tableArchive.Columns != null && tableArchive.Columns.Count >= 6)
        {
            tableArchive.Columns[1].Title = LocalizationManager.GetString("ArchiveColSource") ?? "來源名稱";
            tableArchive.Columns[2].Title = LocalizationManager.GetString("ArchiveColTarget") ?? "目標資料夾";
            tableArchive.Columns[3].Title = LocalizationManager.GetString("ArchiveColConfidence") ?? "置信度";
            tableArchive.Columns[4].Title = LocalizationManager.GetString("ArchiveColStatus") ?? "狀態";
            tableArchive.Columns[5].Title = LocalizationManager.GetString("ArchiveColAction") ?? "操作";
        }

        if (_menuItemCheck != null)
            _menuItemCheck.Text = LocalizationManager.GetString("ArchiveMenuCheck") ?? "勾選選取項目";
        if (_menuItemUncheck != null)
            _menuItemUncheck.Text = LocalizationManager.GetString("ArchiveMenuUncheck") ?? "取消勾選選取項目";
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

    private void OpenPreview()
    {
        if (_config == null || _intelligence == null || _organizer == null) return;
        if (tableArchive.DataSource is not BindingList<ArchiveItem> items || items.Count == 0) return;

        using var preview = new PreviewForm(items.ToList(), _config.RootDirectory, _intelligence, _organizer,
            _config, _metaStore, _tagTranslator);
        preview.ShowDialog(this.FindForm());
    }
}
