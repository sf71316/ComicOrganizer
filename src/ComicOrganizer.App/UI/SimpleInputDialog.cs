using AntdUI;
using ComicOrganizer.App.Core;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 簡單的「輸入單一字串」modal dialog，可放 hint label 與初始值。
/// 用法：var d = new SimpleInputDialog("重新命名標籤", "新名稱：", oldName);
///        if (d.ShowDialog(parent) == DialogResult.OK) use d.Value;
/// </summary>
public class SimpleInputDialog : AntdUI.Window, IThemedControl
{
    private readonly AntdUI.Label lblPrompt;
    private readonly AntdUI.Input txtInput;       // 普通 input mode 用
    private readonly AntdUI.Select? txtSelect;    // autocomplete mode 用（不為 null 時走 Select）
    private readonly AntdUI.Button btnOk;
    private readonly AntdUI.Button btnCancel;

    public string Value => (txtSelect?.Text ?? txtInput.Text)?.Trim() ?? "";

    public SimpleInputDialog(string title, string prompt, string initialValue = "")
        : this(title, prompt, initialValue, autocomplete: null) { }

    /// <summary>autocomplete 模式：傳 translator 進來會用 Select + EhTag artist suggest 取代 Input。</summary>
    public SimpleInputDialog(string title, string prompt, string initialValue, TagTranslator? autocomplete)
    {
        Text = title;
        Size = new System.Drawing.Size(420, 180);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var content = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 18, 20, 18),
        };
        Controls.Add(content);

        lblPrompt = new AntdUI.Label
        {
            Text = prompt,
            Dock = DockStyle.Top,
            Height = 26,
        };

        txtInput = new AntdUI.Input
        {
            Text = initialValue,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 6, 0, 0),
            Size = new System.Drawing.Size(0, 32),
        };
        txtInput.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; AcceptOk(); }
            if (e.KeyCode == Keys.Escape) { e.Handled = true; AcceptCancel(); }
        };

        if (autocomplete != null && autocomplete.IsAvailable)
        {
            txtSelect = new AntdUI.Select
            {
                Text = initialValue,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 6, 0, 0),
                Size = new System.Drawing.Size(0, 32),
            };
            txtSelect.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; AcceptOk(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; AcceptCancel(); }
            };
            AutocompleteHelpers.BindArtistAutocomplete(txtSelect, autocomplete);
        }

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
        };
        btnOk.Click += (s, e) => AcceptOk();

        btnCancel = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogCancel") ?? "取消",
            Width = 80, Height = 32,
        };
        btnCancel.Click += (s, e) => AcceptCancel();

        btnRow.Controls.Add(btnOk);
        btnRow.Controls.Add(btnCancel);

        // autocomplete mode 用 Select 取代 Input；非 autocomplete mode 用 Input
        if (txtSelect != null) content.Controls.Add(txtSelect);
        else content.Controls.Add(txtInput);
        content.Controls.Add(lblPrompt);
        content.Controls.Add(btnRow);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        this.WireThemeAndLocalization();
    }

    private void AcceptOk()
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AcceptCancel()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        lblPrompt.ForeColor = ThemeColors.Text;
    }

    public void ApplyLocalization()
    {
        // title / prompt 由建構子設定，不從 i18n 換
        btnOk.Text = LocalizationManager.GetString("DialogOk") ?? "確定";
        btnCancel.Text = LocalizationManager.GetString("DialogCancel") ?? "取消";
    }
}
