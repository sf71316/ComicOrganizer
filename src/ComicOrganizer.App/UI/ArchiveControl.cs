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

    public ArchiveControl()
    {
        InitializeComponent();
        btnScan.Click += async (s, e) => await ScanAsync();
        btnExecute.Click += (s, e) => OpenPreview();
        this.WireThemeAndLocalization();
    }

    public void Initialize(AppConfig config, MappingIntelligence intelligence, ArchiveOrganizer organizer)
    {
        _config = config;
        _intelligence = intelligence;
        _organizer = organizer;
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

        using var preview = new PreviewForm(items.ToList(), _config.RootDirectory, _intelligence, _organizer);
        preview.ShowDialog(this.FindForm());
    }
}
