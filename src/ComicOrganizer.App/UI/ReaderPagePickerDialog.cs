using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 仿 HoneyView「選擇影像」快速跳頁視窗：上方同步縮圖導覽膠捲 + 搜尋／直跳欄 + 虛擬模式檔案清單。
/// </summary>
public sealed class ReaderPagePickerDialog : Form
{
    private const int MaxThumbCache = 36;
    private const int StripHalfRadius = 2;

    private readonly IReaderSource _source;
    private readonly UserPreferences? _prefs;
    private readonly Action? _onSavePrefs;
    private readonly IReadOnlyList<string> _allPageNames;
    private readonly Dictionary<int, ReaderPageMetadata> _metaCache = new();

    private IReadOnlyList<int> _displayIndices = Array.Empty<int>();
    private int _selectedDisplayPos = -1;
    private bool _sortByName;
    private bool _suppressSelectionSync;

    // 上方縮圖快取與單線序背景載入
    private readonly object _thumbLock = new();
    private readonly Dictionary<int, Bitmap> _thumbCache = new();
    private readonly LinkedList<int> _thumbLru = new();
    private readonly Dictionary<int, LinkedListNode<int>> _thumbLruNodes = new();
    private CancellationTokenSource? _thumbLoadCts;
    private readonly List<(Rectangle Rect, int DisplayPos)> _stripHitRects = new();

    private readonly BufferedStripPanel _stripPanel;
    private readonly Panel _searchRow;
    private readonly AntdUI.Input _txtFilter;
    private readonly AntdUI.Input _txtJumpPage;
    private readonly Label _lblTotalPages;
    private readonly BufferedDataGridView _dgvPages;
    private readonly Panel _bottomBar;
    private readonly AntdUI.Button _btnSortMode;
    private readonly AntdUI.Checkbox _chkHideThumbnails;
    private readonly AntdUI.Button _btnOk;
    private readonly AntdUI.Button _btnCancel;

    private static readonly Color StripBackground = Color.FromArgb(30, 30, 30);
    private static readonly Color StripSlotBackground = Color.FromArgb(45, 45, 48);
    private static readonly Color StripSelectedBorder = Color.FromArgb(255, 165, 38);
    private static readonly Color StripNormalBorder = Color.FromArgb(72, 72, 76);

    /// <summary>使用者最終確認跳轉的 0-based 頁碼索引。</summary>
    public int SelectedPageIndex { get; private set; }

    public ReaderPagePickerDialog(
        IReaderSource source,
        int currentIndex,
        UserPreferences? prefs = null,
        Action? onSavePrefs = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _prefs = prefs;
        _onSavePrefs = onSavePrefs;

        int safeCurrent = _source.PageCount > 0
            ? Math.Clamp(currentIndex, 0, _source.PageCount - 1)
            : 0;
        SelectedPageIndex = safeCurrent;

        var names = new string[_source.PageCount];
        for (int i = 0; i < _source.PageCount; i++)
        {
            names[i] = _source.GetPageName(i);
        }
        _allPageNames = names;

        Text = "選擇影像";
        Size = LogicalToDeviceUnits(new Size(840, 640));
        MinimumSize = LogicalToDeviceUnits(new Size(560, 420));
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = true;
        KeyPreview = true;
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        BackColor = ThemeColors.Surface;
        ForeColor = ThemeColors.Text;

        // 1. 上方縮圖導覽膠捲區
        _stripPanel = new BufferedStripPanel
        {
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(190),
            BackColor = StripBackground,
            Visible = !(_prefs?.PagePickerHideThumbnails ?? false),
        };
        _stripPanel.Paint += OnStripPanelPaint;
        _stripPanel.MouseClick += OnStripPanelMouseClick;
        _stripPanel.MouseDoubleClick += OnStripPanelMouseDoubleClick;
        _stripPanel.MouseWheel += OnStripPanelMouseWheel;

        // 2. 中間搜尋＆直跳欄
        _searchRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(44),
            Padding = new Padding(10, 6, 10, 6),
            BackColor = ThemeColors.Surface,
        };
        _txtFilter = new AntdUI.Input
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "篩選檔名...",
            PrefixSvg = "SearchOutlined",
            AllowClear = true,
            BackColor = ThemeColors.Background,
            ForeColor = ThemeColors.Text,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
        };
        var jumpHost = new Panel
        {
            Dock = DockStyle.Right,
            Width = LogicalToDeviceUnits(210),
            Padding = new Padding(10, 0, 0, 0),
            BackColor = Color.Transparent,
        };
        _txtJumpPage = new AntdUI.Input
        {
            Dock = DockStyle.Fill,
            Text = _source.PageCount > 0 ? (safeCurrent + 1).ToString() : "0",
            BackColor = ThemeColors.Background,
            ForeColor = ThemeColors.Text,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
        };
        _lblTotalPages = new Label
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            Text = $" / {_source.PageCount} 頁",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ThemeColors.Text,
            Padding = new Padding(4, 0, 0, 0),
        };
        jumpHost.Controls.Add(_txtJumpPage);
        jumpHost.Controls.Add(_lblTotalPages);
        _searchRow.Controls.Add(_txtFilter);
        _searchRow.Controls.Add(jumpHost);

        // 3. 虛擬檔案清單 DataGridView
        _dgvPages = new BufferedDataGridView
        {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = LogicalToDeviceUnits(32),
            RowTemplate = { Height = LogicalToDeviceUnits(26) },
            ShowCellToolTips = false,
        };
        BuildGridColumns();
        ApplyThemeToGrid();

        // 4. 底部控制列
        _bottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = LogicalToDeviceUnits(48),
            Padding = new Padding(10, 8, 10, 8),
            BackColor = ThemeColors.TabHeader,
        };
        var bottomLeft = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        _btnSortMode = new AntdUI.Button
        {
            Size = LogicalToDeviceUnits(new Size(112, 32)),
            Text = "依名稱排序",
            IconSvg = "SortAscendingOutlined",
            Margin = new Padding(0, 0, 10, 0),
            ForeColor = ThemeColors.Text,
        };
        _chkHideThumbnails = new AntdUI.Checkbox
        {
            AutoSize = true,
            Height = LogicalToDeviceUnits(32),
            Text = "隱藏縮圖導覽",
            Checked = _prefs?.PagePickerHideThumbnails ?? false,
            ForeColor = ThemeColors.Text,
            Margin = new Padding(0, 4, 0, 0),
        };
        bottomLeft.Controls.Add(_btnSortMode);
        bottomLeft.Controls.Add(_chkHideThumbnails);

        var bottomRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        _btnOk = new AntdUI.Button
        {
            Size = LogicalToDeviceUnits(new Size(86, 32)),
            Text = "確定",
            Type = AntdUI.TTypeMini.Primary,
            Margin = new Padding(0, 0, 8, 0),
        };
        _btnCancel = new AntdUI.Button
        {
            Size = LogicalToDeviceUnits(new Size(86, 32)),
            Text = "取消",
            ForeColor = ThemeColors.Text,
            Margin = new Padding(0),
        };
        bottomRight.Controls.Add(_btnOk);
        bottomRight.Controls.Add(_btnCancel);
        _bottomBar.Controls.Add(bottomLeft);
        _bottomBar.Controls.Add(bottomRight);

        // WinForms Dock 反序：Fill 最先加，Bottom 與 Top 依序包圍
        Controls.Add(_dgvPages);
        Controls.Add(_searchRow);
        Controls.Add(_stripPanel);
        Controls.Add(_bottomBar);

        WireEvents();
        RebuildDisplayList(preferPageIndex: safeCurrent);
    }

    private void BuildGridColumns()
    {
        var colIndex = new DataGridViewTextBoxColumn
        {
            Name = "colIndex",
            HeaderText = "#",
            FillWeight = 10F,
            MinimumWidth = LogicalToDeviceUnits(52),
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight },
        };
        var colName = new DataGridViewTextBoxColumn
        {
            Name = "colName",
            HeaderText = "清單",
            FillWeight = 52F,
            MinimumWidth = LogicalToDeviceUnits(180),
        };
        var colModified = new DataGridViewTextBoxColumn
        {
            Name = "colModified",
            HeaderText = "修改日期",
            FillWeight = 23F,
            MinimumWidth = LogicalToDeviceUnits(125),
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
        };
        var colSize = new DataGridViewTextBoxColumn
        {
            Name = "colSize",
            HeaderText = "大小",
            FillWeight = 15F,
            MinimumWidth = LogicalToDeviceUnits(80),
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight },
        };
        _dgvPages.Columns.AddRange(colIndex, colName, colModified, colSize);
    }

    private void ApplyThemeToGrid()
    {
        _dgvPages.BackgroundColor = ThemeColors.Surface;
        _dgvPages.GridColor = ThemeColors.Border;

        _dgvPages.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = ThemeColors.TabHeader,
            ForeColor = ThemeColors.Text,
            SelectionBackColor = ThemeColors.TabHeader,
            SelectionForeColor = ThemeColors.Text,
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold),
            Padding = new Padding(6, 0, 6, 0),
        };
        _dgvPages.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.Text,
            SelectionBackColor = ThemeColors.Primary,
            SelectionForeColor = ThemeColors.HeaderText,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            Padding = new Padding(6, 0, 6, 0),
        };
        _dgvPages.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = ThemeColors.RowAlternateBg,
            ForeColor = ThemeColors.Text,
            SelectionBackColor = ThemeColors.Primary,
            SelectionForeColor = ThemeColors.HeaderText,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            Padding = new Padding(6, 0, 6, 0),
        };
    }

    private void WireEvents()
    {
        _txtFilter.TextChanged += (_, _) => RebuildDisplayList(preferPageIndex: SelectedPageIndex);
        _txtJumpPage.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                TryJumpFromInputBox(confirmAfterJump: false);
            }
        };
        _txtJumpPage.Leave += (_, _) => TryJumpFromInputBox(confirmAfterJump: false);

        _dgvPages.CellValueNeeded += OnGridCellValueNeeded;
        _dgvPages.SelectionChanged += (_, _) => OnGridSelectionChanged();
        _dgvPages.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) ConfirmSelection();
        };
        _dgvPages.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ConfirmSelection();
            }
        };

        _btnSortMode.Click += (_, _) =>
        {
            _sortByName = !_sortByName;
            _btnSortMode.Text = _sortByName ? "依頁序" : "依名稱排序";
            _btnSortMode.Type = _sortByName ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
            RebuildDisplayList(preferPageIndex: SelectedPageIndex);
        };

        _chkHideThumbnails.CheckedChanged += (_, _) =>
        {
            bool hide = _chkHideThumbnails.Checked;
            _stripPanel.Visible = !hide;
            if (_prefs != null)
            {
                _prefs.PagePickerHideThumbnails = hide;
                _onSavePrefs?.Invoke();
            }
            if (!hide) ScheduleStripThumbnailLoads();
        };

        _btnOk.Click += (_, _) => ConfirmSelection();
        _btnCancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        Shown += (_, _) =>
        {
            if (_selectedDisplayPos >= 0)
            {
                ScrollGridToRow(_selectedDisplayPos);
            }
            ScheduleStripThumbnailLoads();
            _dgvPages.Focus();
        };
    }

    private void RebuildDisplayList(int preferPageIndex)
    {
        _displayIndices = PagePickerHelper.BuildDisplayIndices(
            _allPageNames,
            _txtFilter.Text,
            _sortByName);

        _suppressSelectionSync = true;
        try
        {
            _dgvPages.RowCount = _displayIndices.Count;
            _dgvPages.ClearSelection();

            if (_displayIndices.Count == 0)
            {
                _selectedDisplayPos = -1;
                _stripPanel.Invalidate();
                return;
            }

            int pos = 0;
            for (int i = 0; i < _displayIndices.Count; i++)
            {
                if (_displayIndices[i] == preferPageIndex)
                {
                    pos = i;
                    break;
                }
            }

            _selectedDisplayPos = pos;
            SelectedPageIndex = _displayIndices[pos];
            _dgvPages.Rows[pos].Selected = true;
            _dgvPages.CurrentCell = _dgvPages.Rows[pos].Cells[0];
            ScrollGridToRow(pos);
        }
        finally
        {
            _suppressSelectionSync = false;
        }

        SyncJumpInputText();
        _stripPanel.Invalidate();
        ScheduleStripThumbnailLoads();
    }

    private void SelectDisplayPosition(int pos, bool scrollGrid)
    {
        if (pos < 0 || pos >= _displayIndices.Count) return;

        _suppressSelectionSync = true;
        try
        {
            _selectedDisplayPos = pos;
            SelectedPageIndex = _displayIndices[pos];
            _dgvPages.ClearSelection();
            _dgvPages.Rows[pos].Selected = true;
            _dgvPages.CurrentCell = _dgvPages.Rows[pos].Cells[0];
            if (scrollGrid) ScrollGridToRow(pos);
        }
        finally
        {
            _suppressSelectionSync = false;
        }

        SyncJumpInputText();
        _stripPanel.Invalidate();
        ScheduleStripThumbnailLoads();
    }

    private void MoveSelectionBy(int delta)
    {
        if (_displayIndices.Count == 0) return;
        int target = Math.Clamp(_selectedDisplayPos + delta, 0, _displayIndices.Count - 1);
        if (target != _selectedDisplayPos)
        {
            SelectDisplayPosition(target, scrollGrid: true);
        }
    }

    private void ScrollGridToRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _dgvPages.RowCount || _dgvPages.DisplayedRowCount(false) <= 0)
            return;

        int firstVisible = _dgvPages.FirstDisplayedScrollingRowIndex;
        int visibleRows = _dgvPages.DisplayedRowCount(false);
        if (firstVisible < 0 || rowIndex < firstVisible || rowIndex >= firstVisible + visibleRows)
        {
            int targetTop = Math.Max(0, rowIndex - visibleRows / 2);
            try { _dgvPages.FirstDisplayedScrollingRowIndex = targetTop; } catch { }
        }
    }

    private void SyncJumpInputText()
    {
        string text = _displayIndices.Count > 0 ? (SelectedPageIndex + 1).ToString() : "0";
        if (!string.Equals(_txtJumpPage.Text, text, StringComparison.Ordinal))
        {
            _txtJumpPage.Text = text;
        }
    }

    private void TryJumpFromInputBox(bool confirmAfterJump)
    {
        if (!ReaderNavigationHelper.TryParseGoToPageInput(_txtJumpPage.Text, _source.PageCount, out int targetPageIndex))
        {
            SyncJumpInputText();
            return;
        }

        int pos = FindDisplayPosition(targetPageIndex);
        if (pos < 0 && !string.IsNullOrEmpty(_txtFilter.Text))
        {
            // 若目標頁被檔名過濾排除，先清過濾再定位
            _txtFilter.Text = "";
            pos = FindDisplayPosition(targetPageIndex);
        }

        if (pos >= 0)
        {
            SelectDisplayPosition(pos, scrollGrid: true);
            if (confirmAfterJump) ConfirmSelection();
        }
    }

    private int FindDisplayPosition(int pageIndex)
    {
        for (int i = 0; i < _displayIndices.Count; i++)
        {
            if (_displayIndices[i] == pageIndex) return i;
        }
        return -1;
    }

    private void OnGridSelectionChanged()
    {
        if (_suppressSelectionSync) return;
        if (_dgvPages.CurrentRow == null) return;
        int row = _dgvPages.CurrentRow.Index;
        if (row < 0 || row >= _displayIndices.Count || row == _selectedDisplayPos) return;

        _selectedDisplayPos = row;
        SelectedPageIndex = _displayIndices[row];
        SyncJumpInputText();
        _stripPanel.Invalidate();
        ScheduleStripThumbnailLoads();
    }

    private void OnGridCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _displayIndices.Count) return;
        int pageIndex = _displayIndices[e.RowIndex];
        if (!_metaCache.TryGetValue(pageIndex, out var meta))
        {
            meta = _source.GetPageMetadata(pageIndex);
            _metaCache[pageIndex] = meta;
        }

        e.Value = e.ColumnIndex switch
        {
            0 => (meta.PageIndex + 1).ToString(),
            1 => meta.Name,
            2 => PagePickerHelper.FormatModifiedTime(meta.LastModified),
            3 => PagePickerHelper.FormatFileSize(meta.SizeBytes),
            _ => string.Empty,
        };
    }

    private void ConfirmSelection()
    {
        if (_displayIndices.Count == 0 || _selectedDisplayPos < 0 || _selectedDisplayPos >= _displayIndices.Count)
            return;
        SelectedPageIndex = _displayIndices[_selectedDisplayPos];
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ScheduleStripThumbnailLoads()
    {
        if (IsDisposed || _chkHideThumbnails.Checked || !_stripPanel.Visible || _displayIndices.Count == 0)
            return;

        _thumbLoadCts?.Cancel();
        _thumbLoadCts?.Dispose();
        _thumbLoadCts = new CancellationTokenSource();
        var token = _thumbLoadCts.Token;

        var positions = PagePickerHelper.GetStripWindowPositions(_selectedDisplayPos, _displayIndices.Count, StripHalfRadius);
        var ordered = positions
            .OrderBy(p => Math.Abs(p - _selectedDisplayPos))
            .Select(p => _displayIndices[p])
            .Distinct()
            .ToList();

        var missing = new List<int>(ordered.Count);
        lock (_thumbLock)
        {
            foreach (int pageIdx in ordered)
            {
                if (_thumbCache.ContainsKey(pageIdx))
                {
                    TouchThumbLruLocked(pageIdx);
                }
                else
                {
                    missing.Add(pageIdx);
                }
            }
        }

        if (missing.Count == 0) return;

        int maxW = LogicalToDeviceUnits(140);
        int maxH = LogicalToDeviceUnits(150);
        _ = LoadStripThumbnailsAsync(missing, maxW, maxH, token);
    }

    private async Task LoadStripThumbnailsAsync(
        IReadOnlyList<int> pageIndices,
        int maxW,
        int maxH,
        CancellationToken token)
    {
        foreach (int pageIndex in pageIndices)
        {
            if (token.IsCancellationRequested || IsDisposed) break;

            Bitmap? bmp = null;
            try
            {
                bmp = await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return null;
                    return ReaderImageLoader.DecodeThumbnail(_source, pageIndex, maxW, maxH);
                }, token);
            }
            catch
            {
                bmp?.Dispose();
                bmp = null;
            }

            if (bmp == null) continue;

            if (token.IsCancellationRequested || IsDisposed)
            {
                bmp.Dispose();
                break;
            }

            lock (_thumbLock)
            {
                if (IsDisposed)
                {
                    bmp.Dispose();
                    break;
                }
                PutThumbnailLocked(pageIndex, bmp);
            }

            try
            {
                BeginInvoke(() =>
                {
                    if (!IsDisposed && !token.IsCancellationRequested && _stripPanel.Visible)
                    {
                        _stripPanel.Invalidate();
                    }
                });
            }
            catch
            {
                break;
            }
        }
    }

    private void PutThumbnailLocked(int pageIndex, Bitmap bmp)
    {
        if (_thumbCache.TryGetValue(pageIndex, out var existing))
        {
            existing.Dispose();
            _thumbCache[pageIndex] = bmp;
            TouchThumbLruLocked(pageIndex);
            return;
        }

        _thumbCache[pageIndex] = bmp;
        var node = _thumbLru.AddFirst(pageIndex);
        _thumbLruNodes[pageIndex] = node;

        while (_thumbCache.Count > MaxThumbCache && _thumbLru.Last != null)
        {
            int victim = _thumbLru.Last.Value;
            _thumbLru.RemoveLast();
            _thumbLruNodes.Remove(victim);
            if (_thumbCache.Remove(victim, out var oldBmp))
            {
                oldBmp.Dispose();
            }
        }
    }

    private void TouchThumbLruLocked(int pageIndex)
    {
        if (_thumbLruNodes.TryGetValue(pageIndex, out var node))
        {
            _thumbLru.Remove(node);
            _thumbLru.AddFirst(node);
        }
    }

    private void OnStripPanelPaint(object? sender, PaintEventArgs e)
    {
        _stripHitRects.Clear();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(StripBackground);

        if (_displayIndices.Count == 0 || _selectedDisplayPos < 0) return;

        int padX = LogicalToDeviceUnits(14);
        int padY = LogicalToDeviceUnits(12);
        int gap = LogicalToDeviceUnits(10);
        int totalSlots = StripHalfRadius * 2 + 1;
        int availW = Math.Max(100, _stripPanel.ClientSize.Width - padX * 2 - gap * (totalSlots - 1));
        int slotW = Math.Max(LogicalToDeviceUnits(64), availW / totalSlots);
        int slotH = Math.Max(LogicalToDeviceUnits(80), _stripPanel.ClientSize.Height - padY * 2);

        int totalStripW = slotW * totalSlots + gap * (totalSlots - 1);
        int originX = Math.Max(padX, (_stripPanel.ClientSize.Width - totalStripW) / 2);

        var positions = PagePickerHelper.GetStripWindowPositions(_selectedDisplayPos, _displayIndices.Count, StripHalfRadius);
        using var slotBgBrush = new SolidBrush(StripSlotBackground);
        using var normalBorderPen = new Pen(StripNormalBorder, 1F);
        using var selectedBorderPen = new Pen(StripSelectedBorder, 3F);
        using var badgeBgBrush = new SolidBrush(Color.FromArgb(185, 20, 20, 20));
        using var selectedBadgeBrush = new SolidBrush(StripSelectedBorder);
        using var badgeTextBrush = new SolidBrush(Color.White);
        using var selectedBadgeTextBrush = new SolidBrush(Color.FromArgb(24, 24, 24));
        using var placeholderBrush = new SolidBrush(Color.FromArgb(150, 150, 155));
        using var badgeFont = new Font("Microsoft JhengHei UI", 8.5F, FontStyle.Bold);
        using var placeholderFont = new Font("Microsoft JhengHei UI", 10F);

        lock (_thumbLock)
        {
            foreach (int pos in positions)
            {
                int slotIdx = (pos - _selectedDisplayPos) + StripHalfRadius;
                if (slotIdx < 0 || slotIdx >= totalSlots) continue;

                int x = originX + slotIdx * (slotW + gap);
                var slotRect = new Rectangle(x, padY, slotW, slotH);
                _stripHitRects.Add((slotRect, pos));

                bool isSelected = pos == _selectedDisplayPos;
                int pageIndex = _displayIndices[pos];

                g.FillRectangle(slotBgBrush, slotRect);

                if (_thumbCache.TryGetValue(pageIndex, out var thumb) && thumb.Width > 0 && thumb.Height > 0)
                {
                    int innerPad = LogicalToDeviceUnits(4);
                    int maxW = Math.Max(1, slotRect.Width - innerPad * 2);
                    int maxH = Math.Max(1, slotRect.Height - innerPad * 2);
                    float scale = Math.Min((float)maxW / thumb.Width, (float)maxH / thumb.Height);
                    int drawW = Math.Max(1, (int)(thumb.Width * scale));
                    int drawH = Math.Max(1, (int)(thumb.Height * scale));
                    int drawX = slotRect.X + (slotRect.Width - drawW) / 2;
                    int drawY = slotRect.Y + (slotRect.Height - drawH) / 2;
                    g.DrawImage(thumb, new Rectangle(drawX, drawY, drawW, drawH));
                }
                else
                {
                    string phText = $"{pageIndex + 1}";
                    var sz = g.MeasureString(phText, placeholderFont);
                    g.DrawString(
                        phText,
                        placeholderFont,
                        placeholderBrush,
                        slotRect.X + (slotRect.Width - sz.Width) / 2f,
                        slotRect.Y + (slotRect.Height - sz.Height) / 2f);
                }

                string badgeText = isSelected ? $"{pageIndex + 1} / {_source.PageCount}" : $"{pageIndex + 1}";
                var badgeSize = g.MeasureString(badgeText, badgeFont);
                int badgePadH = LogicalToDeviceUnits(6);
                int badgePadV = LogicalToDeviceUnits(2);
                int bw = (int)Math.Ceiling(badgeSize.Width) + badgePadH * 2;
                int bh = (int)Math.Ceiling(badgeSize.Height) + badgePadV * 2;
                var badgeRect = new Rectangle(
                    slotRect.X + (slotRect.Width - bw) / 2,
                    slotRect.Bottom - bh - LogicalToDeviceUnits(6),
                    bw,
                    bh);
                g.FillRectangle(isSelected ? selectedBadgeBrush : badgeBgBrush, badgeRect);
                g.DrawString(
                    badgeText,
                    badgeFont,
                    isSelected ? selectedBadgeTextBrush : badgeTextBrush,
                    badgeRect.X + badgePadH,
                    badgeRect.Y + badgePadV);

                if (isSelected)
                {
                    var borderRect = new Rectangle(slotRect.X + 1, slotRect.Y + 1, slotRect.Width - 2, slotRect.Height - 2);
                    g.DrawRectangle(selectedBorderPen, borderRect);
                }
                else
                {
                    var borderRect = new Rectangle(slotRect.X, slotRect.Y, slotRect.Width - 1, slotRect.Height - 1);
                    g.DrawRectangle(normalBorderPen, borderRect);
                }
            }
        }
    }

    private void OnStripPanelMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        foreach (var (rect, pos) in _stripHitRects)
        {
            if (rect.Contains(e.Location))
            {
                SelectDisplayPosition(pos, scrollGrid: true);
                return;
            }
        }
    }

    private void OnStripPanelMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        foreach (var (rect, pos) in _stripHitRects)
        {
            if (rect.Contains(e.Location))
            {
                SelectDisplayPosition(pos, scrollGrid: true);
                ConfirmSelection();
                return;
            }
        }
    }

    private void OnStripPanelMouseWheel(object? sender, MouseEventArgs e)
    {
        if (e.Delta == 0) return;
        MoveSelectionBy(e.Delta > 0 ? -1 : 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _thumbLoadCts?.Cancel();
            _thumbLoadCts?.Dispose();
            _thumbLoadCts = null;

            lock (_thumbLock)
            {
                foreach (var bmp in _thumbCache.Values)
                {
                    bmp.Dispose();
                }
                _thumbCache.Clear();
                _thumbLru.Clear();
                _thumbLruNodes.Clear();
            }
        }
        base.Dispose(disposing);
    }

    private sealed class BufferedStripPanel : Panel
    {
        public BufferedStripPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    private sealed class BufferedDataGridView : DataGridView
    {
        public BufferedDataGridView()
        {
            DoubleBuffered = true;
        }
    }
}
