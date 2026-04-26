using AntdUI;
using ComicOrganize.App.Core;
using ComicOrganize.App.Models;
using System.ComponentModel;

namespace ComicOrganize.App.UI;

public partial class PreviewForm : Window
{
    private List<ArchiveGroup> _groups;
    private readonly MappingIntelligence _intelligence;
    private readonly ArchiveOrganizer _organizer;

    public PreviewForm(List<ArchiveItem> items, string rootDirectory, MappingIntelligence intelligence, ArchiveOrganizer organizer)
    {
        InitializeComponent();
        _intelligence = intelligence;
        _organizer = organizer;

        _groups = items.GroupBy(x => x.DestinationPath)
                       .Select(g => new ArchiveGroup
                       {
                           DestinationPath = g.Key,
                           Items = new BindingList<ArchiveItem>(g.ToList())
                       }).ToList();

        // 確保父容器支援捲軸
        container.AutoScroll = true;

        this.Load += (s, e) => InitializeOfficialCollapseUI();

        btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
        btnExecute.Click += async (s, e) => await ExecuteArchiveAsync();
    }

    private void InitializeOfficialCollapseUI()
    {
        container.Controls.Clear();

        // 1. 建立主 Collapse 元件
        var collapse = new AntdUI.Collapse
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Name = "collapseMain",
            ContentPadding = new Size(5, 5),
            HeaderBg = Color.FromArgb(194, 193, 192)
        };

        // 確保初始寬度大於 0，否則佈局引擎不會工作
        collapse.Width = container.Width > 0 ? container.Width - 20 : 800;
        collapse.Height = 100; // 給予初始高度

        // 處理群組開關事件
        collapse.ButtonClick += (s, e) =>
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
                Text = $"{group.DisplayTitle} {group.DestinationPath}",
                Expand = true,
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
                    collapse.LoadLayout();
                }
            };

            collapseItem.Controls.Add(table);
            collapse.Items.Add(collapseItem);
        }

        container.Controls.Add(collapse);
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
        progress.Visible = true;

        await Task.Run(() =>
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

                    try
                    {
                        string destDir = Path.GetDirectoryName(targetPath)!;
                        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                        if (Directory.Exists(item.FullPath)) Directory.Move(item.FullPath, targetPath);
                        else File.Move(item.FullPath, targetPath);

                        var kws = _organizer.ExtractAllKeywords(item.Source);
                        foreach (var kw in kws)
                        {
                            if (_organizer.IsKeywordValidForTarget(kw, Path.GetFileName(group.DestinationPath)))
                                _intelligence.Learn(kw, Path.GetFileName(group.DestinationPath));
                        }
                        this.Invoke(() => item.Status = "完成");
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(() => item.Status = "失敗: " + ex.Message);
                    }
                }
            }
        });

        AntdUI.Message.success(this, "歸檔處理完畢");
        btnExecute.Loading = false;
        btnCancel.Text = "關閉";
        btnCancel.Enabled = true;
    }
}
