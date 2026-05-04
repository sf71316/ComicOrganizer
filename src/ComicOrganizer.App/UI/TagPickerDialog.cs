using AntdUI;
using ComicOrganizer.App.Core;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 選一個 tag 作為合併目標。可搜尋過濾。
/// </summary>
public class TagPickerDialog : AntdUI.Window, IThemedControl
{
    private readonly List<TagUsage> _all;
    private readonly long _excludeId;     // 排除來源 tag 自己
    private readonly AntdUI.Input txtSearch;
    private readonly System.Windows.Forms.ListBox lstTags;
    private readonly AntdUI.Button btnOk;
    private readonly AntdUI.Button btnCancel;
    private readonly AntdUI.Label lblPrompt;

    public TagUsage? SelectedTag { get; private set; }

    public TagPickerDialog(string title, string prompt, IReadOnlyList<TagUsage> allTags, long excludeId)
    {
        _all = allTags.Where(t => t.Id != excludeId).ToList();
        _excludeId = excludeId;

        Text = title;
        Size = new System.Drawing.Size(460, 480);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var content = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 14, 18, 14),
        };
        Controls.Add(content);

        var btnRow = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(0, 8, 0, 0),
        };
        btnOk = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogOk") ?? "確定",
            Type = TTypeMini.Primary,
            Width = 80, Height = 32,
            Margin = new Padding(8, 0, 0, 0),
            Enabled = false,
        };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

        btnCancel = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogCancel") ?? "取消",
            Width = 80, Height = 32,
        };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        btnRow.Controls.Add(btnOk);
        btnRow.Controls.Add(btnCancel);

        lstTags = new System.Windows.Forms.ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
        };
        lstTags.SelectedIndexChanged += (s, e) =>
        {
            if (lstTags.SelectedIndex >= 0 && lstTags.SelectedIndex < _filtered.Count)
            {
                SelectedTag = _filtered[lstTags.SelectedIndex];
                btnOk.Enabled = true;
            }
            else
            {
                SelectedTag = null;
                btnOk.Enabled = false;
            }
        };
        lstTags.DoubleClick += (s, e) =>
        {
            if (SelectedTag != null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        var topPanel = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
        };

        lblPrompt = new AntdUI.Label
        {
            Text = prompt,
            Dock = DockStyle.Top,
            Height = 26,
        };

        txtSearch = new AntdUI.Input
        {
            PlaceholderText = LocalizationManager.GetString("TagPickerSearchPlaceholder") ?? "搜尋目標標籤...",
            PrefixSvg = "SearchOutlined",
            Dock = DockStyle.Top,
            Size = new System.Drawing.Size(0, 32),
            Margin = new Padding(0, 6, 0, 0),
        };
        txtSearch.TextChanged += (s, e) => RefreshList();

        topPanel.Controls.Add(txtSearch);
        topPanel.Controls.Add(lblPrompt);

        content.Controls.Add(lstTags);
        content.Controls.Add(topPanel);
        content.Controls.Add(btnRow);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        RefreshList();
        this.WireThemeAndLocalization();
    }

    private List<TagUsage> _filtered = new();

    private void RefreshList()
    {
        var q = (txtSearch.Text ?? "").Trim();
        _filtered = string.IsNullOrEmpty(q)
            ? _all.OrderByDescending(t => t.TotalCount).ThenBy(t => t.Name).ToList()
            : _all.Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                  .OrderByDescending(t => t.TotalCount).ThenBy(t => t.Name).ToList();

        lstTags.BeginUpdate();
        lstTags.Items.Clear();
        foreach (var t in _filtered)
        {
            var cat = string.IsNullOrEmpty(t.Category) ? "" : $"  [{t.Category}]";
            lstTags.Items.Add($"{t.Name}{cat}    ({t.TotalCount})");
        }
        lstTags.EndUpdate();

        SelectedTag = null;
        btnOk.Enabled = false;
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        lblPrompt.ForeColor = ThemeColors.Text;
        lstTags.BackColor = ThemeColors.Surface;
        lstTags.ForeColor = ThemeColors.Text;
    }

    public void ApplyLocalization()
    {
        btnOk.Text = LocalizationManager.GetString("DialogOk") ?? "確定";
        btnCancel.Text = LocalizationManager.GetString("DialogCancel") ?? "取消";
        txtSearch.PlaceholderText = LocalizationManager.GetString("TagPickerSearchPlaceholder") ?? "搜尋目標標籤...";
    }
}
