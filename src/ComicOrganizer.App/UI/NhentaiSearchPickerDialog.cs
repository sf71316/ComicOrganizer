using AntdUI;
using ComicOrganizer.App.Core;
using System.ComponentModel;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 檔名抽不到 6-7 位 id 時的 fallback：讓使用者輸入查詢字串、看 nhentai
/// search 結果並挑一個對應的 gallery。挑完 caller 用該 id 再走
/// GetByExternalIdAsync 取完整 metadata。
/// </summary>
public class NhentaiSearchPickerDialog : AntdUI.Window, IThemedControl
{
    private readonly Func<string, Task<IReadOnlyList<MetadataSearchHit>>> _searchFn;

    public MetadataSearchHit? SelectedHit { get; private set; }

    private readonly AntdUI.Label lblHint;
    private readonly AntdUI.Input txtQuery;
    private readonly AntdUI.Button btnSearch;
    private readonly AntdUI.Table tableHits;
    private readonly AntdUI.Label lblStatus;
    private readonly AntdUI.Button btnOk;
    private readonly AntdUI.Button btnCancel;

    private BindingList<HitRow> _rows = new();

    public NhentaiSearchPickerDialog(string filename, string initialQuery,
                                     Func<string, Task<IReadOnlyList<MetadataSearchHit>>> searchFn)
    {
        _searchFn = searchFn;

        Text = LocalizationManager.GetString("PickerTitle") ?? "搜尋 nhentai 並選取對應作品";
        Size = new System.Drawing.Size(900, 600);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var content = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 16),
        };
        Controls.Add(content);

        // 提示：本地檔名
        lblHint = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = string.Format(
                LocalizationManager.GetString("PickerHintFmt") ?? "本地檔: {0}",
                filename),
        };

        // 搜尋區
        var searchRow = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(0, 6, 0, 6),
        };
        txtQuery = new AntdUI.Input
        {
            Text = initialQuery,
            PlaceholderText = LocalizationManager.GetString("PickerQueryPlaceholder") ?? "標題 / artist:xxx / language:xxx ...",
            Location = new System.Drawing.Point(0, 6),
            Size = new System.Drawing.Size(720, 36),
            PrefixSvg = "SearchOutlined",
        };
        txtQuery.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                _ = ExecuteSearchAsync();
            }
        };
        btnSearch = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("PickerSearch") ?? "搜尋",
            Type = TTypeMini.Primary,
            IconSvg = "SearchOutlined",
            Location = new System.Drawing.Point(728, 6),
            Size = new System.Drawing.Size(110, 36),
        };
        btnSearch.Click += async (s, e) => await ExecuteSearchAsync();
        searchRow.Controls.Add(btnSearch);
        searchRow.Controls.Add(txtQuery);

        // 狀態
        lblStatus = new AntdUI.Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            Text = "",
        };

        // 結果 table
        tableHits = new AntdUI.Table
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = ColumnsMode.Fill,
            EnableHeaderResizing = true,
            RowHeight = 36,
            EmptyText = LocalizationManager.GetString("PickerEmpty") ?? "輸入查詢字串後按搜尋",
        };
        tableHits.Columns = new ColumnCollection
        {
            new Column("Id",            LocalizationManager.GetString("PickerColId")    ?? "ID")    { Width = "9%" },
            new Column("EnglishTitle",  LocalizationManager.GetString("PickerColEng")   ?? "英文標題") { Width = "44%" },
            new Column("JapaneseTitle", LocalizationManager.GetString("PickerColJp")    ?? "日文標題") { Width = "39%" },
            new Column("NumPages",      LocalizationManager.GetString("PickerColPages") ?? "頁數") { Width = "8%", Align = ColumnAlign.Right },
        };
        tableHits.CellClick += (s, e) =>
        {
            if (e.Record is HitRow row)
            {
                SelectedHit = row.Hit;
                if (btnOk != null) btnOk.Enabled = true;
            }
        };
        tableHits.CellDoubleClick += (s, e) =>
        {
            if (e.Record is HitRow row)
            {
                SelectedHit = row.Hit;
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        // 動作
        var btnRow = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 50,
            Padding = new Padding(0, 8, 0, 0),
        };
        btnOk = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("PickerSelect") ?? "選取",
            Type = TTypeMini.Primary,
            Width = 100, Height = 36,
            Margin = new Padding(8, 0, 0, 0),
            Enabled = false,
        };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

        btnCancel = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogCancel") ?? "取消",
            Width = 100, Height = 36,
        };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnRow.Controls.Add(btnOk);
        btnRow.Controls.Add(btnCancel);

        content.Controls.Add(tableHits);
        content.Controls.Add(lblStatus);
        content.Controls.Add(searchRow);
        content.Controls.Add(lblHint);
        content.Controls.Add(btnRow);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        this.WireThemeAndLocalization();

        // 初始 query 若有值，自動搜一次
        if (!string.IsNullOrWhiteSpace(initialQuery))
            _ = ExecuteSearchAsync();
    }

    private async Task ExecuteSearchAsync()
    {
        var q = txtQuery.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(q)) return;

        btnSearch.Loading = true;
        btnOk.Enabled = false;
        lblStatus.Text = LocalizationManager.GetString("PickerSearching") ?? "搜尋中…";
        try
        {
            var hits = await _searchFn(q);
            if (IsDisposed) return;
            _rows = new BindingList<HitRow>(hits.Select(h => new HitRow
            {
                Hit = h,
                Id = h.ExternalId,
                EnglishTitle = h.TitleEnglish ?? "",
                JapaneseTitle = h.TitleJapanese ?? "",
                NumPages = h.NumPages?.ToString() ?? "",
            }).ToList());
            tableHits.DataSource = _rows;
            lblStatus.Text = string.Format(
                LocalizationManager.GetString("PickerHitCountFmt") ?? "{0} 筆結果", hits.Count);
        }
        catch (Exception ex)
        {
            lblStatus.Text = string.Format(
                LocalizationManager.GetString("PickerErrFmt") ?? "搜尋失敗：{0}", ex.Message);
        }
        finally
        {
            btnSearch.Loading = false;
        }
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        lblHint.ForeColor = ThemeColors.Secondary;
        lblStatus.ForeColor = ThemeColors.Secondary;
    }

    public void ApplyLocalization() { /* 標題與按鈕在建構時設定 */ }

    public class HitRow
    {
        public MetadataSearchHit Hit { get; set; } = null!;
        public string Id { get; set; } = "";
        public string EnglishTitle { get; set; } = "";
        public string JapaneseTitle { get; set; } = "";
        public string NumPages { get; set; } = "";
    }
}
