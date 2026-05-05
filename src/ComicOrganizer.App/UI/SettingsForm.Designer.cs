using AntdUI;

namespace ComicOrganizer.App.UI;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.PageHeader windowBar;
    private AntdUI.Panel content;
    private AntdUI.Panel footer;

    // 左側選單 + 四個分類按鈕
    private AntdUI.Panel leftMenu;
    private AntdUI.Button btnMenuGeneral;
    private AntdUI.Button btnMenuLibrary;
    private AntdUI.Button btnMenuReader;
    private AntdUI.Button btnMenuExternal;

    // 內容區四個 sub-panel（toggle Visible 切換）
    private AntdUI.Panel generalPanel;
    private System.Windows.Forms.Panel libraryPanel;  // 用 Forms.Panel 才有 AutoScroll
    private AntdUI.Panel readerPanel;
    private AntdUI.Panel externalPanel;

    // externalPanel 控件 (#52)
    private AntdUI.Label lblExtTitle;
    private AntdUI.Label lblExtDesc;
    private AntdUI.Switch swEnableExternal;
    private AntdUI.Label lblExtEnable;
    private AntdUI.Label lblExtKey;
    private AntdUI.Input txtExtKey;
    private AntdUI.Button btnTestConnection;
    private AntdUI.Label lblExtStatus;

    // generalPanel 控件
    private AntdUI.Label labelTheme;
    private AntdUI.Select selectTheme;
    private AntdUI.Label labelLang;
    private AntdUI.Select selectLang;
    private AntdUI.Checkbox cbDebug;
    private AntdUI.Checkbox cbTrackReading;

    // readerPanel 控件
    private AntdUI.Checkbox cbUseBuiltInReader;
    private AntdUI.Label labelReadingDirection;
    private AntdUI.Select selectReadingDirection;
    private AntdUI.Checkbox cbDualPageSpreadFromCover;

    // libraryPanel 控件
    private AntdUI.Label label1;
    private AntdUI.Label label2;
    private AntdUI.Label label3;
    private AntdUI.Input txtSource;
    private AntdUI.Input txtTarget;
    private AntdUI.Input txtImageViewer;
    private AntdUI.Input txtImageViewerArgs;
    private AntdUI.Label labelImageViewerArgs;
    private AntdUI.Button btnBrowseSource;
    private AntdUI.Button btnBrowseTarget;
    private AntdUI.Button btnBrowseViewer;
    private AntdUI.Label labelMaxScanDepth;
    private AntdUI.InputNumber numMaxScanDepth;
    private AntdUI.Label labelCoverSizeRange;
    private AntdUI.InputNumber numMinCoverSize;
    private AntdUI.Select selectMinCoverUnit;
    private AntdUI.Label labelCoverSizeTilde;
    private AntdUI.InputNumber numMaxCoverSize;
    private AntdUI.Select selectMaxCoverUnit;
    private AntdUI.Label labelThumbCacheMax;
    private AntdUI.InputNumber numThumbCacheMax;
    private AntdUI.Select selectThumbCacheUnit;

    // Footer 按鈕
    private AntdUI.Button btnSave;
    private AntdUI.Button btnCancel;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        windowBar = new PageHeader();
        footer = new AntdUI.Panel();
        btnSave = new AntdUI.Button();
        btnCancel = new AntdUI.Button();
        leftMenu = new AntdUI.Panel();
        btnMenuExternal = new AntdUI.Button();
        btnMenuReader = new AntdUI.Button();
        btnMenuLibrary = new AntdUI.Button();
        btnMenuGeneral = new AntdUI.Button();
        content = new AntdUI.Panel();
        generalPanel = new AntdUI.Panel();
        cbTrackReading = new Checkbox();
        cbDebug = new Checkbox();
        selectLang = new Select();
        labelLang = new AntdUI.Label();
        selectTheme = new Select();
        labelTheme = new AntdUI.Label();
        libraryPanel = new System.Windows.Forms.Panel();
        selectThumbCacheUnit = new Select();
        numThumbCacheMax = new InputNumber();
        labelThumbCacheMax = new AntdUI.Label();
        selectMaxCoverUnit = new Select();
        numMaxCoverSize = new InputNumber();
        labelCoverSizeTilde = new AntdUI.Label();
        selectMinCoverUnit = new Select();
        numMinCoverSize = new InputNumber();
        labelCoverSizeRange = new AntdUI.Label();
        numMaxScanDepth = new InputNumber();
        labelMaxScanDepth = new AntdUI.Label();
        btnBrowseViewer = new AntdUI.Button();
        btnBrowseTarget = new AntdUI.Button();
        btnBrowseSource = new AntdUI.Button();
        txtImageViewerArgs = new Input();
        labelImageViewerArgs = new AntdUI.Label();
        txtImageViewer = new Input();
        txtTarget = new Input();
        txtSource = new Input();
        label3 = new AntdUI.Label();
        label2 = new AntdUI.Label();
        label1 = new AntdUI.Label();
        readerPanel = new AntdUI.Panel();
        cbDualPageSpreadFromCover = new Checkbox();
        selectReadingDirection = new Select();
        labelReadingDirection = new AntdUI.Label();
        cbUseBuiltInReader = new Checkbox();
        externalPanel = new AntdUI.Panel();
        lblExtStatus = new AntdUI.Label();
        btnTestConnection = new AntdUI.Button();
        txtExtKey = new Input();
        lblExtKey = new AntdUI.Label();
        swEnableExternal = new Switch();
        lblExtEnable = new AntdUI.Label();
        lblExtDesc = new AntdUI.Label();
        lblExtTitle = new AntdUI.Label();
        footer.SuspendLayout();
        leftMenu.SuspendLayout();
        content.SuspendLayout();
        generalPanel.SuspendLayout();
        libraryPanel.SuspendLayout();
        readerPanel.SuspendLayout();
        externalPanel.SuspendLayout();
        SuspendLayout();
        // 
        // windowBar
        // 
        windowBar.BackColor = Color.FromArgb(60, 60, 60);
        windowBar.Dock = DockStyle.Top;
        windowBar.ForeColor = Color.White;
        windowBar.IconSvg = "SettingOutlined";
        windowBar.Location = new Point(0, 0);
        windowBar.Name = "windowBar";
        windowBar.Padding = new Padding(3);
        windowBar.ShowButton = true;
        windowBar.ShowIcon = true;
        windowBar.Size = new Size(900, 48);
        windowBar.TabIndex = 0;
        windowBar.Text = "系統設定";
        windowBar.UseSystemStyleColor = true;
        // 
        // footer
        // 
        footer.BackColor = Color.FromArgb(189, 189, 189);
        footer.Controls.Add(btnSave);
        footer.Controls.Add(btnCancel);
        footer.Dock = DockStyle.Bottom;
        footer.Location = new Point(0, 610);
        footer.Name = "footer";
        footer.Padding = new Padding(20, 15, 20, 15);
        footer.Radius = 0;
        footer.Size = new Size(900, 70);
        footer.TabIndex = 3;
        // 
        // btnSave
        // 
        btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSave.BackColor = Color.FromArgb(60, 60, 60);
        btnSave.ForeColor = Color.White;
        btnSave.Location = new Point(760, 15);
        btnSave.Name = "btnSave";
        btnSave.Size = new Size(120, 40);
        btnSave.TabIndex = 1;
        btnSave.Text = "儲存設定";
        btnSave.Type = TTypeMini.Primary;
        btnSave.Click += btnSave_Click;
        // 
        // btnCancel
        // 
        btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnCancel.Location = new Point(640, 15);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(110, 40);
        btnCancel.TabIndex = 0;
        btnCancel.Text = "取消";
        // 
        // leftMenu
        // 
        leftMenu.BackColor = Color.FromArgb(245, 245, 245);
        leftMenu.Controls.Add(btnMenuExternal);
        leftMenu.Controls.Add(btnMenuReader);
        leftMenu.Controls.Add(btnMenuLibrary);
        leftMenu.Controls.Add(btnMenuGeneral);
        leftMenu.Dock = DockStyle.Left;
        leftMenu.Location = new Point(0, 48);
        leftMenu.Name = "leftMenu";
        leftMenu.Padding = new Padding(15, 20, 15, 20);
        leftMenu.Radius = 0;
        leftMenu.Size = new Size(180, 562);
        leftMenu.TabIndex = 1;
        // 
        // btnMenuExternal
        // 
        btnMenuExternal.Dock = DockStyle.Top;
        btnMenuExternal.IconSvg = "ApiOutlined";
        btnMenuExternal.Location = new Point(15, 155);
        btnMenuExternal.Name = "btnMenuExternal";
        btnMenuExternal.Size = new Size(150, 45);
        btnMenuExternal.TabIndex = 3;
        btnMenuExternal.Text = "外部資料來源";
        btnMenuExternal.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // btnMenuReader
        // 
        btnMenuReader.Dock = DockStyle.Top;
        btnMenuReader.IconSvg = "BookOutlined";
        btnMenuReader.Location = new Point(15, 110);
        btnMenuReader.Name = "btnMenuReader";
        btnMenuReader.Size = new Size(150, 45);
        btnMenuReader.TabIndex = 2;
        btnMenuReader.Text = "閱讀器";
        btnMenuReader.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // btnMenuLibrary
        // 
        btnMenuLibrary.Dock = DockStyle.Top;
        btnMenuLibrary.IconSvg = "DatabaseOutlined";
        btnMenuLibrary.Location = new Point(15, 65);
        btnMenuLibrary.Name = "btnMenuLibrary";
        btnMenuLibrary.Size = new Size(150, 45);
        btnMenuLibrary.TabIndex = 1;
        btnMenuLibrary.Text = "漫畫庫管理";
        btnMenuLibrary.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // btnMenuGeneral
        // 
        btnMenuGeneral.Dock = DockStyle.Top;
        btnMenuGeneral.IconSvg = "SettingOutlined";
        btnMenuGeneral.Location = new Point(15, 20);
        btnMenuGeneral.Name = "btnMenuGeneral";
        btnMenuGeneral.Size = new Size(150, 45);
        btnMenuGeneral.TabIndex = 0;
        btnMenuGeneral.Text = "一般設定";
        btnMenuGeneral.TextAlign = ContentAlignment.MiddleLeft;
        btnMenuGeneral.Type = TTypeMini.Primary;
        // 
        // content
        // 
        content.BackColor = Color.FromArgb(189, 189, 189);
        content.Controls.Add(externalPanel);
        content.Controls.Add(generalPanel);
        content.Controls.Add(libraryPanel);
        content.Controls.Add(readerPanel);
        content.Dock = DockStyle.Fill;
        content.Location = new Point(180, 48);
        content.Name = "content";
        content.Radius = 0;
        content.Size = new Size(720, 562);
        content.TabIndex = 2;
        // 
        // generalPanel
        // 
        generalPanel.BackColor = Color.Transparent;
        generalPanel.Controls.Add(cbTrackReading);
        generalPanel.Controls.Add(cbDebug);
        generalPanel.Controls.Add(selectLang);
        generalPanel.Controls.Add(labelLang);
        generalPanel.Controls.Add(selectTheme);
        generalPanel.Controls.Add(labelTheme);
        generalPanel.Dock = DockStyle.Fill;
        generalPanel.Location = new Point(0, 0);
        generalPanel.Name = "generalPanel";
        generalPanel.Padding = new Padding(40, 30, 40, 30);
        generalPanel.Radius = 0;
        generalPanel.Size = new Size(720, 562);
        generalPanel.TabIndex = 0;
        // 
        // cbTrackReading
        // 
        cbTrackReading.BackColor = Color.Transparent;
        cbTrackReading.Location = new Point(40, 280);
        cbTrackReading.Name = "cbTrackReading";
        cbTrackReading.Size = new Size(400, 30);
        cbTrackReading.TabIndex = 5;
        cbTrackReading.Text = "啟用閱讀記錄 (狀態 / 次數 / 上次閱讀)";
        // 
        // cbDebug
        // 
        cbDebug.BackColor = Color.Transparent;
        cbDebug.Location = new Point(40, 240);
        cbDebug.Name = "cbDebug";
        cbDebug.Size = new Size(300, 30);
        cbDebug.TabIndex = 4;
        cbDebug.Text = "偵錯模式 (不移動檔案)";
        // 
        // selectLang
        // 
        selectLang.Location = new Point(40, 165);
        selectLang.Name = "selectLang";
        selectLang.Size = new Size(250, 40);
        selectLang.TabIndex = 3;
        // 
        // labelLang
        // 
        labelLang.BackColor = Color.Transparent;
        labelLang.Location = new Point(40, 130);
        labelLang.Name = "labelLang";
        labelLang.Size = new Size(200, 30);
        labelLang.TabIndex = 2;
        labelLang.Text = "語言介面";
        // 
        // selectTheme
        // 
        selectTheme.Location = new Point(40, 65);
        selectTheme.Name = "selectTheme";
        selectTheme.Size = new Size(250, 40);
        selectTheme.TabIndex = 1;
        // 
        // labelTheme
        // 
        labelTheme.BackColor = Color.Transparent;
        labelTheme.Location = new Point(40, 30);
        labelTheme.Name = "labelTheme";
        labelTheme.Size = new Size(200, 30);
        labelTheme.TabIndex = 0;
        labelTheme.Text = "介面主題";
        // 
        // libraryPanel
        // 
        libraryPanel.AutoScroll = true;
        libraryPanel.AutoScrollMinSize = new Size(0, 720);
        libraryPanel.BackColor = Color.Transparent;
        libraryPanel.Controls.Add(selectThumbCacheUnit);
        libraryPanel.Controls.Add(numThumbCacheMax);
        libraryPanel.Controls.Add(labelThumbCacheMax);
        libraryPanel.Controls.Add(selectMaxCoverUnit);
        libraryPanel.Controls.Add(numMaxCoverSize);
        libraryPanel.Controls.Add(labelCoverSizeTilde);
        libraryPanel.Controls.Add(selectMinCoverUnit);
        libraryPanel.Controls.Add(numMinCoverSize);
        libraryPanel.Controls.Add(labelCoverSizeRange);
        libraryPanel.Controls.Add(numMaxScanDepth);
        libraryPanel.Controls.Add(labelMaxScanDepth);
        libraryPanel.Controls.Add(btnBrowseViewer);
        libraryPanel.Controls.Add(btnBrowseTarget);
        libraryPanel.Controls.Add(btnBrowseSource);
        libraryPanel.Controls.Add(txtImageViewerArgs);
        libraryPanel.Controls.Add(labelImageViewerArgs);
        libraryPanel.Controls.Add(txtImageViewer);
        libraryPanel.Controls.Add(txtTarget);
        libraryPanel.Controls.Add(txtSource);
        libraryPanel.Controls.Add(label3);
        libraryPanel.Controls.Add(label2);
        libraryPanel.Controls.Add(label1);
        libraryPanel.Dock = DockStyle.Fill;
        libraryPanel.Location = new Point(0, 0);
        libraryPanel.Name = "libraryPanel";
        libraryPanel.Padding = new Padding(40, 30, 40, 30);
        libraryPanel.Size = new Size(720, 562);
        libraryPanel.TabIndex = 1;
        libraryPanel.Visible = false;
        // 
        // selectThumbCacheUnit
        // 
        selectThumbCacheUnit.Items.AddRange(new object[] { "MB", "GB" });
        selectThumbCacheUnit.Location = new Point(150, 655);
        selectThumbCacheUnit.Name = "selectThumbCacheUnit";
        selectThumbCacheUnit.SelectedIndex = 1;
        selectThumbCacheUnit.SelectedValue = "GB";
        selectThumbCacheUnit.Size = new Size(80, 45);
        selectThumbCacheUnit.TabIndex = 21;
        selectThumbCacheUnit.Text = "GB";
        // 
        // numThumbCacheMax
        // 
        numThumbCacheMax.Location = new Point(40, 655);
        numThumbCacheMax.Name = "numThumbCacheMax";
        numThumbCacheMax.Size = new Size(100, 45);
        numThumbCacheMax.TabIndex = 20;
        numThumbCacheMax.Text = "1";
        numThumbCacheMax.Value = new decimal(new int[] { 1, 0, 0, 0 });
        // 
        // labelThumbCacheMax
        // 
        labelThumbCacheMax.BackColor = Color.Transparent;
        labelThumbCacheMax.Location = new Point(40, 620);
        labelThumbCacheMax.Name = "labelThumbCacheMax";
        labelThumbCacheMax.Size = new Size(420, 30);
        labelThumbCacheMax.TabIndex = 19;
        labelThumbCacheMax.Text = "縮圖快取上限 (超過會 LRU 淘汰最舊)";
        // 
        // selectMaxCoverUnit
        // 
        selectMaxCoverUnit.Items.AddRange(new object[] { "KB", "MB" });
        selectMaxCoverUnit.Location = new Point(380, 560);
        selectMaxCoverUnit.Name = "selectMaxCoverUnit";
        selectMaxCoverUnit.SelectedIndex = 1;
        selectMaxCoverUnit.SelectedValue = "MB";
        selectMaxCoverUnit.Size = new Size(80, 45);
        selectMaxCoverUnit.TabIndex = 18;
        selectMaxCoverUnit.Text = "MB";
        // 
        // numMaxCoverSize
        // 
        numMaxCoverSize.Location = new Point(270, 560);
        numMaxCoverSize.Name = "numMaxCoverSize";
        numMaxCoverSize.Size = new Size(100, 45);
        numMaxCoverSize.TabIndex = 17;
        numMaxCoverSize.Text = "5";
        numMaxCoverSize.Value = new decimal(new int[] { 5, 0, 0, 0 });
        // 
        // labelCoverSizeTilde
        // 
        labelCoverSizeTilde.BackColor = Color.Transparent;
        labelCoverSizeTilde.Location = new Point(240, 568);
        labelCoverSizeTilde.Name = "labelCoverSizeTilde";
        labelCoverSizeTilde.Size = new Size(20, 30);
        labelCoverSizeTilde.TabIndex = 16;
        labelCoverSizeTilde.Text = "～";
        // 
        // selectMinCoverUnit
        // 
        selectMinCoverUnit.Items.AddRange(new object[] { "KB", "MB" });
        selectMinCoverUnit.Location = new Point(150, 560);
        selectMinCoverUnit.Name = "selectMinCoverUnit";
        selectMinCoverUnit.SelectedIndex = 0;
        selectMinCoverUnit.SelectedValue = "KB";
        selectMinCoverUnit.Size = new Size(80, 45);
        selectMinCoverUnit.TabIndex = 15;
        selectMinCoverUnit.Text = "KB";
        // 
        // numMinCoverSize
        // 
        numMinCoverSize.Location = new Point(40, 560);
        numMinCoverSize.Name = "numMinCoverSize";
        numMinCoverSize.Size = new Size(100, 45);
        numMinCoverSize.TabIndex = 14;
        numMinCoverSize.Text = "50";
        numMinCoverSize.Value = new decimal(new int[] { 50, 0, 0, 0 });
        // 
        // labelCoverSizeRange
        // 
        labelCoverSizeRange.BackColor = Color.Transparent;
        labelCoverSizeRange.Location = new Point(40, 525);
        labelCoverSizeRange.Name = "labelCoverSizeRange";
        labelCoverSizeRange.Size = new Size(420, 30);
        labelCoverSizeRange.TabIndex = 13;
        labelCoverSizeRange.Text = "封面取樣檔案大小範圍";
        // 
        // numMaxScanDepth
        // 
        numMaxScanDepth.Location = new Point(40, 465);
        numMaxScanDepth.Name = "numMaxScanDepth";
        numMaxScanDepth.Size = new Size(150, 45);
        numMaxScanDepth.TabIndex = 12;
        numMaxScanDepth.Text = "3";
        numMaxScanDepth.Value = new decimal(new int[] { 3, 0, 0, 0 });
        // 
        // labelMaxScanDepth
        // 
        labelMaxScanDepth.BackColor = Color.Transparent;
        labelMaxScanDepth.Location = new Point(40, 430);
        labelMaxScanDepth.Name = "labelMaxScanDepth";
        labelMaxScanDepth.Size = new Size(300, 30);
        labelMaxScanDepth.TabIndex = 11;
        labelMaxScanDepth.Text = "搜尋層數限制 (封面遞迴)";
        // 
        // btnBrowseViewer
        // 
        btnBrowseViewer.DefaultBack = Color.FromArgb(60, 60, 60);
        btnBrowseViewer.ForeColor = Color.White;
        btnBrowseViewer.Location = new Point(500, 265);
        btnBrowseViewer.Name = "btnBrowseViewer";
        btnBrowseViewer.Size = new Size(80, 45);
        btnBrowseViewer.TabIndex = 8;
        btnBrowseViewer.Text = "瀏覽";
        // 
        // btnBrowseTarget
        // 
        btnBrowseTarget.DefaultBack = Color.FromArgb(60, 60, 60);
        btnBrowseTarget.ForeColor = Color.White;
        btnBrowseTarget.Location = new Point(500, 165);
        btnBrowseTarget.Name = "btnBrowseTarget";
        btnBrowseTarget.Size = new Size(80, 45);
        btnBrowseTarget.TabIndex = 5;
        btnBrowseTarget.Text = "瀏覽";
        // 
        // btnBrowseSource
        // 
        btnBrowseSource.DefaultBack = Color.FromArgb(60, 60, 60);
        btnBrowseSource.ForeColor = Color.White;
        btnBrowseSource.Location = new Point(500, 65);
        btnBrowseSource.Name = "btnBrowseSource";
        btnBrowseSource.Size = new Size(80, 45);
        btnBrowseSource.TabIndex = 2;
        btnBrowseSource.Text = "瀏覽";
        // 
        // txtImageViewerArgs
        // 
        txtImageViewerArgs.Location = new Point(40, 365);
        txtImageViewerArgs.Name = "txtImageViewerArgs";
        txtImageViewerArgs.PlaceholderText = "例如: \"{path}\"  或  -r \"{path}\"";
        txtImageViewerArgs.Size = new Size(540, 45);
        txtImageViewerArgs.TabIndex = 10;
        // 
        // labelImageViewerArgs
        // 
        labelImageViewerArgs.BackColor = Color.Transparent;
        labelImageViewerArgs.Location = new Point(40, 330);
        labelImageViewerArgs.Name = "labelImageViewerArgs";
        labelImageViewerArgs.Size = new Size(300, 30);
        labelImageViewerArgs.TabIndex = 9;
        labelImageViewerArgs.Text = "啟動參數（{path} = 路徑）";
        // 
        // txtImageViewer
        // 
        txtImageViewer.Location = new Point(40, 265);
        txtImageViewer.Name = "txtImageViewer";
        txtImageViewer.PlaceholderText = "外部圖片瀏覽器執行檔路徑 (.exe)";
        txtImageViewer.Size = new Size(450, 45);
        txtImageViewer.TabIndex = 7;
        // 
        // txtTarget
        // 
        txtTarget.Location = new Point(40, 165);
        txtTarget.Name = "txtTarget";
        txtTarget.PlaceholderText = "漫畫庫總根目錄 (Root)";
        txtTarget.Size = new Size(450, 45);
        txtTarget.TabIndex = 4;
        // 
        // txtSource
        // 
        txtSource.Location = new Point(40, 65);
        txtSource.Name = "txtSource";
        txtSource.PlaceholderText = "來源目錄 (待整理檔案路徑)";
        txtSource.Size = new Size(450, 45);
        txtSource.TabIndex = 1;
        // 
        // label3
        // 
        label3.BackColor = Color.Transparent;
        label3.Location = new Point(40, 230);
        label3.Name = "label3";
        label3.Size = new Size(200, 30);
        label3.TabIndex = 6;
        label3.Text = "圖片瀏覽器";
        // 
        // label2
        // 
        label2.BackColor = Color.Transparent;
        label2.Location = new Point(40, 130);
        label2.Name = "label2";
        label2.Size = new Size(200, 30);
        label2.TabIndex = 3;
        label2.Text = "歸檔目錄";
        // 
        // label1
        // 
        label1.BackColor = Color.Transparent;
        label1.Location = new Point(40, 30);
        label1.Name = "label1";
        label1.Size = new Size(200, 30);
        label1.TabIndex = 0;
        label1.Text = "來源目錄";
        // 
        // readerPanel
        // 
        readerPanel.BackColor = Color.Transparent;
        readerPanel.Controls.Add(cbDualPageSpreadFromCover);
        readerPanel.Controls.Add(selectReadingDirection);
        readerPanel.Controls.Add(labelReadingDirection);
        readerPanel.Controls.Add(cbUseBuiltInReader);
        readerPanel.Dock = DockStyle.Fill;
        readerPanel.Location = new Point(0, 0);
        readerPanel.Name = "readerPanel";
        readerPanel.Padding = new Padding(40, 30, 40, 30);
        readerPanel.Radius = 0;
        readerPanel.Size = new Size(720, 562);
        readerPanel.TabIndex = 2;
        readerPanel.Visible = false;
        // 
        // cbDualPageSpreadFromCover
        // 
        cbDualPageSpreadFromCover.BackColor = Color.Transparent;
        cbDualPageSpreadFromCover.Location = new Point(40, 175);
        cbDualPageSpreadFromCover.Name = "cbDualPageSpreadFromCover";
        cbDualPageSpreadFromCover.Size = new Size(420, 30);
        cbDualPageSpreadFromCover.TabIndex = 3;
        cbDualPageSpreadFromCover.Text = "雙頁模式封面獨立顯示";
        // 
        // selectReadingDirection
        // 
        selectReadingDirection.Location = new Point(40, 115);
        selectReadingDirection.Name = "selectReadingDirection";
        selectReadingDirection.Size = new Size(250, 40);
        selectReadingDirection.TabIndex = 2;
        // 
        // labelReadingDirection
        // 
        labelReadingDirection.BackColor = Color.Transparent;
        labelReadingDirection.Location = new Point(40, 80);
        labelReadingDirection.Name = "labelReadingDirection";
        labelReadingDirection.Size = new Size(200, 30);
        labelReadingDirection.TabIndex = 1;
        labelReadingDirection.Text = "閱讀方向";
        // 
        // cbUseBuiltInReader
        // 
        cbUseBuiltInReader.BackColor = Color.Transparent;
        cbUseBuiltInReader.Location = new Point(40, 30);
        cbUseBuiltInReader.Name = "cbUseBuiltInReader";
        cbUseBuiltInReader.Size = new Size(420, 30);
        cbUseBuiltInReader.TabIndex = 0;
        cbUseBuiltInReader.Text = "使用內建閱讀器（取代外部 viewer）";
        // 
        // externalPanel
        // 
        externalPanel.BackColor = Color.Transparent;
        externalPanel.Controls.Add(lblExtStatus);
        externalPanel.Controls.Add(btnTestConnection);
        externalPanel.Controls.Add(txtExtKey);
        externalPanel.Controls.Add(lblExtKey);
        externalPanel.Controls.Add(swEnableExternal);
        externalPanel.Controls.Add(lblExtEnable);
        externalPanel.Controls.Add(lblExtDesc);
        externalPanel.Controls.Add(lblExtTitle);
        externalPanel.Dock = DockStyle.Fill;
        externalPanel.Location = new Point(0, 0);
        externalPanel.Name = "externalPanel";
        externalPanel.Padding = new Padding(40, 30, 40, 30);
        externalPanel.Radius = 0;
        externalPanel.Size = new Size(720, 562);
        externalPanel.TabIndex = 3;
        externalPanel.Visible = false;
        // 
        // lblExtStatus
        // 
        lblExtStatus.BackColor = Color.Transparent;
        lblExtStatus.Location = new Point(40, 295);
        lblExtStatus.Name = "lblExtStatus";
        lblExtStatus.Size = new Size(620, 50);
        lblExtStatus.TabIndex = 7;
        lblExtStatus.Text = "(未測試)";
        // 
        // btnTestConnection
        // 
        btnTestConnection.IconSvg = "ApiOutlined";
        btnTestConnection.Location = new Point(120, 245);
        btnTestConnection.Name = "btnTestConnection";
        btnTestConnection.Size = new Size(140, 36);
        btnTestConnection.TabIndex = 6;
        btnTestConnection.Text = "測試連線";
        // 
        // txtExtKey
        // 
        txtExtKey.Location = new Point(120, 192);
        txtExtKey.Name = "txtExtKey";
        txtExtKey.PasswordChar = '●';
        txtExtKey.PlaceholderText = "nhk_...";
        txtExtKey.Size = new Size(420, 36);
        txtExtKey.TabIndex = 5;
        // 
        // lblExtKey
        // 
        lblExtKey.BackColor = Color.Transparent;
        lblExtKey.Location = new Point(40, 195);
        lblExtKey.Name = "lblExtKey";
        lblExtKey.Size = new Size(80, 30);
        lblExtKey.TabIndex = 4;
        lblExtKey.Text = "API Key:";
        // 
        // swEnableExternal
        // 
        swEnableExternal.Location = new Point(120, 150);
        swEnableExternal.Name = "swEnableExternal";
        swEnableExternal.Size = new Size(50, 25);
        swEnableExternal.TabIndex = 3;
        // 
        // lblExtEnable
        // 
        lblExtEnable.BackColor = Color.Transparent;
        lblExtEnable.Location = new Point(40, 145);
        lblExtEnable.Name = "lblExtEnable";
        lblExtEnable.Size = new Size(80, 30);
        lblExtEnable.TabIndex = 2;
        lblExtEnable.Text = "啟用：";
        // 
        // lblExtDesc
        // 
        lblExtDesc.BackColor = Color.Transparent;
        lblExtDesc.Location = new Point(40, 65);
        lblExtDesc.Name = "lblExtDesc";
        lblExtDesc.Size = new Size(620, 60);
        lblExtDesc.TabIndex = 1;
        lblExtDesc.Text = "啟用後可從 nhentai API 拉取作品 metadata。";
        // 
        // lblExtTitle
        // 
        lblExtTitle.BackColor = Color.Transparent;
        lblExtTitle.Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold);
        lblExtTitle.Location = new Point(40, 30);
        lblExtTitle.Name = "lblExtTitle";
        lblExtTitle.Size = new Size(500, 30);
        lblExtTitle.TabIndex = 0;
        lblExtTitle.Text = "nhentai API";
        // 
        // SettingsForm
        // 
        BackColor = Color.FromArgb(30, 30, 30);
        ClientSize = new Size(900, 680);
        Controls.Add(content);
        Controls.Add(leftMenu);
        Controls.Add(footer);
        Controls.Add(windowBar);
        Name = "SettingsForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "系統設定";
        footer.ResumeLayout(false);
        leftMenu.ResumeLayout(false);
        content.ResumeLayout(false);
        generalPanel.ResumeLayout(false);
        libraryPanel.ResumeLayout(false);
        readerPanel.ResumeLayout(false);
        externalPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
