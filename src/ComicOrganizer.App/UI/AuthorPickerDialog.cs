using AntdUI;
using ComicOrganizer.App.Core;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 選一位作者作為合併目標。可搜尋過濾。
/// </summary>
public class AuthorPickerDialog : AntdUI.Window, IThemedControl
{
    private readonly List<AuthorUsage> _all;
    private readonly AntdUI.Input txtSearch;
    private readonly System.Windows.Forms.ListBox lstAuthors;
    private readonly AntdUI.Button btnOk;
    private readonly AntdUI.Button btnCancel;
    private readonly AntdUI.Label lblPrompt;

    public AuthorUsage? SelectedAuthor { get; private set; }

    public AuthorPickerDialog(string title, string prompt, IReadOnlyList<AuthorUsage> allAuthors, long excludeId)
    {
        _all = allAuthors.Where(a => a.Author.Id != excludeId).ToList();

        Text = title;
        Size = new System.Drawing.Size(500, 480);
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

        lstAuthors = new System.Windows.Forms.ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
        };
        lstAuthors.SelectedIndexChanged += (s, e) =>
        {
            if (lstAuthors.SelectedIndex >= 0 && lstAuthors.SelectedIndex < _filtered.Count)
            {
                SelectedAuthor = _filtered[lstAuthors.SelectedIndex];
                btnOk.Enabled = true;
            }
            else
            {
                SelectedAuthor = null;
                btnOk.Enabled = false;
            }
        };
        lstAuthors.DoubleClick += (s, e) =>
        {
            if (SelectedAuthor != null)
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
            PlaceholderText = LocalizationManager.GetString("AuthorPickerSearchPlaceholder") ?? "搜尋目標作者...",
            PrefixSvg = "SearchOutlined",
            Dock = DockStyle.Top,
            Size = new System.Drawing.Size(0, 32),
            Margin = new Padding(0, 6, 0, 0),
        };
        txtSearch.TextChanged += (s, e) => RefreshList();

        topPanel.Controls.Add(txtSearch);
        topPanel.Controls.Add(lblPrompt);

        content.Controls.Add(lstAuthors);
        content.Controls.Add(topPanel);
        content.Controls.Add(btnRow);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        RefreshList();
        this.WireThemeAndLocalization();
    }

    private List<AuthorUsage> _filtered = new();

    private void RefreshList()
    {
        var q = (txtSearch.Text ?? "").Trim();
        _filtered = string.IsNullOrEmpty(q)
            ? _all.OrderByDescending(a => a.WorkCount).ThenBy(a => a.Author.MainName).ToList()
            : _all.Where(a => a.Author.MainName.Contains(q, StringComparison.OrdinalIgnoreCase))
                  .OrderByDescending(a => a.WorkCount).ThenBy(a => a.Author.MainName).ToList();

        lstAuthors.BeginUpdate();
        lstAuthors.Items.Clear();
        foreach (var a in _filtered)
        {
            var folderHint = string.IsNullOrEmpty(a.Author.FolderPath) ? "  [meta]" : "";
            lstAuthors.Items.Add($"{a.Author.MainName}{folderHint}    ({a.WorkCount} 部 / {a.AliasCount} 別名)");
        }
        lstAuthors.EndUpdate();

        SelectedAuthor = null;
        btnOk.Enabled = false;
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        lblPrompt.ForeColor = ThemeColors.Text;
        lstAuthors.BackColor = ThemeColors.Surface;
        lstAuthors.ForeColor = ThemeColors.Text;
    }

    public void ApplyLocalization()
    {
        btnOk.Text = LocalizationManager.GetString("DialogOk") ?? "確定";
        btnCancel.Text = LocalizationManager.GetString("DialogCancel") ?? "取消";
        txtSearch.PlaceholderText = LocalizationManager.GetString("AuthorPickerSearchPlaceholder") ?? "搜尋目標作者...";
    }
}
