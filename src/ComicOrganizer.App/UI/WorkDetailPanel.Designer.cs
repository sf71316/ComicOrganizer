namespace ComicOrganizer.App.UI;

partial class WorkDetailPanel
{
    private System.ComponentModel.IContainer components = null;

    // Header
    private System.Windows.Forms.Panel headerPanel;
    private System.Windows.Forms.Label lblTitle;
    private AntdUI.Button btnEdit;
    private AntdUI.Button btnFetchExternal;
    private AntdUI.Button btnClose;

    // 內容
    private System.Windows.Forms.Panel contentPanel;
    private System.Windows.Forms.TableLayoutPanel layout;

    // Cover + 主名
    private System.Windows.Forms.Panel coverPanel;
    private System.Windows.Forms.PictureBox picCover;
    private System.Windows.Forms.Label lblWorkTitle;
    private AntdUI.Input txtTitle;        // edit mode 取代 lblWorkTitle
    private System.Windows.Forms.LinkLabel lnkAuthor;
    private AntdUI.Input txtAuthor;       // edit mode 取代 lnkAuthor（輸入作者名 → save 時 UpsertAuthor）

    // metadata grid
    private AntdUI.Divider divider1;
    private System.Windows.Forms.Label lblMetaInfo;
    private System.Windows.Forms.TableLayoutPanel editGrid;   // edit mode 取代 lblMetaInfo
    private AntdUI.Input txtSeries;
    private AntdUI.Input txtChapter;
    private AntdUI.Input txtLanguage;
    private AntdUI.Input txtTranslationGroup;
    private AntdUI.InputNumber numRating;

    // 閱讀記錄
    private AntdUI.Divider divider2;
    private System.Windows.Forms.Label lblReadStatsHeader;
    private System.Windows.Forms.Label lblReadStats;

    // tags
    private AntdUI.Divider divider3;
    private System.Windows.Forms.Label lblTagsHeader;
    private System.Windows.Forms.FlowLayoutPanel flowTags;
    private System.Windows.Forms.FlowLayoutPanel addTagPanel;   // edit mode：txtAddTag + btnAddTag
    private AntdUI.Input txtAddTag;
    private AntdUI.Button btnAddTag;

    // 檔案資訊
    private AntdUI.Divider divider4;
    private System.Windows.Forms.Label lblFileInfoHeader;
    private System.Windows.Forms.Label lblFileInfo;

    // 操作
    private AntdUI.Divider divider5;
    private System.Windows.Forms.FlowLayoutPanel flowActions;
    private AntdUI.Button btnRead;
    private AntdUI.Button btnExplorer;
    private AntdUI.Button btnResetReading;
    private AntdUI.Button btnSave;
    private AntdUI.Button btnCancel;

    // Empty
    private System.Windows.Forms.Label lblEmpty;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        headerPanel = new System.Windows.Forms.Panel();
        lblTitle = new System.Windows.Forms.Label();
        btnEdit = new AntdUI.Button();
        btnFetchExternal = new AntdUI.Button();
        btnClose = new AntdUI.Button();
        contentPanel = new System.Windows.Forms.Panel();
        layout = new System.Windows.Forms.TableLayoutPanel();
        coverPanel = new System.Windows.Forms.Panel();
        picCover = new System.Windows.Forms.PictureBox();
        lblWorkTitle = new System.Windows.Forms.Label();
        txtTitle = new AntdUI.Input();
        lnkAuthor = new System.Windows.Forms.LinkLabel();
        txtAuthor = new AntdUI.Input();
        divider1 = new AntdUI.Divider();
        lblMetaInfo = new System.Windows.Forms.Label();
        editGrid = new System.Windows.Forms.TableLayoutPanel();
        txtSeries = new AntdUI.Input();
        txtChapter = new AntdUI.Input();
        txtLanguage = new AntdUI.Input();
        txtTranslationGroup = new AntdUI.Input();
        numRating = new AntdUI.InputNumber();
        divider2 = new AntdUI.Divider();
        lblReadStatsHeader = new System.Windows.Forms.Label();
        lblReadStats = new System.Windows.Forms.Label();
        divider3 = new AntdUI.Divider();
        lblTagsHeader = new System.Windows.Forms.Label();
        flowTags = new System.Windows.Forms.FlowLayoutPanel();
        addTagPanel = new System.Windows.Forms.FlowLayoutPanel();
        txtAddTag = new AntdUI.Input();
        btnAddTag = new AntdUI.Button();
        divider4 = new AntdUI.Divider();
        lblFileInfoHeader = new System.Windows.Forms.Label();
        lblFileInfo = new System.Windows.Forms.Label();
        divider5 = new AntdUI.Divider();
        flowActions = new System.Windows.Forms.FlowLayoutPanel();
        btnRead = new AntdUI.Button();
        btnExplorer = new AntdUI.Button();
        btnResetReading = new AntdUI.Button();
        btnSave = new AntdUI.Button();
        btnCancel = new AntdUI.Button();
        lblEmpty = new System.Windows.Forms.Label();

        SuspendLayout();
        headerPanel.SuspendLayout();
        contentPanel.SuspendLayout();
        layout.SuspendLayout();
        coverPanel.SuspendLayout();

        //
        // headerPanel
        //
        headerPanel.Dock = System.Windows.Forms.DockStyle.Top;
        headerPanel.Height = 36;
        headerPanel.Padding = new System.Windows.Forms.Padding(8, 4, 4, 4);
        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(btnClose);
        //
        // lblTitle
        //
        lblTitle.Dock = System.Windows.Forms.DockStyle.Fill;
        lblTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 11F, System.Drawing.FontStyle.Bold);
        lblTitle.Name = "lblTitle";
        lblTitle.Text = "作品詳細資訊";
        lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        //
        // btnClose
        //
        btnClose.Dock = System.Windows.Forms.DockStyle.Right;
        btnClose.Ghost = true;
        btnClose.IconRatio = 0.6F;
        btnClose.IconSvg = "CloseOutlined";
        btnClose.Name = "btnClose";
        btnClose.Size = new System.Drawing.Size(28, 28);
        btnClose.Text = "";
        //
        // btnEdit — 編輯切換（在 action bar，跟 Read/Explorer/ResetReading 同列）
        //
        btnEdit.IconSvg = "FormOutlined";
        btnEdit.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnEdit.Name = "btnEdit";
        btnEdit.Size = new System.Drawing.Size(80, 30);
        btnEdit.Text = "編輯";
        btnEdit.Type = AntdUI.TTypeMini.Primary;
        //
        // btnFetchExternal — 從 nhentai 拉取（在 action bar 最右）
        //
        btnFetchExternal.IconSvg = "ApiOutlined";
        btnFetchExternal.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnFetchExternal.Name = "btnFetchExternal";
        btnFetchExternal.Size = new System.Drawing.Size(110, 30);
        btnFetchExternal.Text = "從 nhentai 拉取";
        btnFetchExternal.Visible = false;

        //
        // contentPanel
        //
        contentPanel.AutoScroll = true;
        contentPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        contentPanel.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
        contentPanel.Controls.Add(layout);

        //
        // layout
        //
        layout.AutoSize = true;
        layout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        layout.Dock = System.Windows.Forms.DockStyle.Top;
        // 編輯模式控件用各自的 row（藏起來時 AutoSize 自動收掉）
        for (int i = 0; i < 20; i++) layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        layout.Controls.Add(coverPanel, 0, 0);
        layout.Controls.Add(lblWorkTitle, 0, 1);     // view 標題
        layout.Controls.Add(txtTitle, 0, 2);          // edit 標題
        layout.Controls.Add(lnkAuthor, 0, 3);         // view 作者
        layout.Controls.Add(txtAuthor, 0, 4);         // edit 作者
        layout.Controls.Add(divider1, 0, 5);
        layout.Controls.Add(lblMetaInfo, 0, 6);       // view metadata
        layout.Controls.Add(editGrid, 0, 7);          // edit metadata grid
        layout.Controls.Add(divider2, 0, 8);
        layout.Controls.Add(lblReadStatsHeader, 0, 9);
        layout.Controls.Add(lblReadStats, 0, 10);
        layout.Controls.Add(divider3, 0, 11);
        layout.Controls.Add(lblTagsHeader, 0, 12);
        layout.Controls.Add(flowTags, 0, 13);
        layout.Controls.Add(addTagPanel, 0, 14);      // edit only
        layout.Controls.Add(divider4, 0, 15);
        layout.Controls.Add(lblFileInfoHeader, 0, 16);
        layout.Controls.Add(lblFileInfo, 0, 17);
        layout.Controls.Add(divider5, 0, 18);
        layout.Controls.Add(flowActions, 0, 19);

        //
        // coverPanel - 容納 picCover 並置中
        //
        coverPanel.AutoSize = true;
        coverPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        coverPanel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
        coverPanel.Controls.Add(picCover);
        //
        // picCover - 256x360 大封面，居中對齊
        //
        picCover.Anchor = System.Windows.Forms.AnchorStyles.Top;
        picCover.Size = new System.Drawing.Size(256, 360);
        picCover.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
        picCover.Cursor = System.Windows.Forms.Cursors.Hand;

        //
        // lblWorkTitle
        //
        lblWorkTitle.AutoSize = true;
        lblWorkTitle.MaximumSize = new System.Drawing.Size(340, 0);
        lblWorkTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 12F, System.Drawing.FontStyle.Bold);
        lblWorkTitle.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
        lblWorkTitle.Name = "lblWorkTitle";
        lblWorkTitle.Text = "(Title)";
        //
        // txtTitle (edit mode 替換 lblWorkTitle)
        //
        txtTitle.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
        txtTitle.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        txtTitle.Name = "txtTitle";
        txtTitle.PlaceholderText = "標題";
        txtTitle.Size = new System.Drawing.Size(340, 32);
        txtTitle.Visible = false;
        //
        // lnkAuthor
        //
        lnkAuthor.AutoSize = true;
        lnkAuthor.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5F);
        lnkAuthor.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lnkAuthor.Name = "lnkAuthor";
        lnkAuthor.Text = "";
        //
        // txtAuthor (edit mode 替換 lnkAuthor — 輸入作者主名，save 時 UpsertAuthor 取或建)
        //
        txtAuthor.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
        txtAuthor.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
        txtAuthor.Name = "txtAuthor";
        txtAuthor.PlaceholderText = "作者主名（留空=無作者）";
        txtAuthor.Size = new System.Drawing.Size(340, 30);
        txtAuthor.Visible = false;

        //
        // dividers
        //
        divider1.Dock = System.Windows.Forms.DockStyle.Fill;
        divider1.Height = 8;
        divider1.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider1.Name = "divider1";
        divider2.Dock = System.Windows.Forms.DockStyle.Fill;
        divider2.Height = 8;
        divider2.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider2.Name = "divider2";
        divider3.Dock = System.Windows.Forms.DockStyle.Fill;
        divider3.Height = 8;
        divider3.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider3.Name = "divider3";
        divider4.Dock = System.Windows.Forms.DockStyle.Fill;
        divider4.Height = 8;
        divider4.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider4.Name = "divider4";
        divider5.Dock = System.Windows.Forms.DockStyle.Fill;
        divider5.Height = 8;
        divider5.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider5.Name = "divider5";

        //
        // lblMetaInfo - 多行 metadata（view mode）
        //
        lblMetaInfo.AutoSize = true;
        lblMetaInfo.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5F);
        lblMetaInfo.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblMetaInfo.Name = "lblMetaInfo";
        lblMetaInfo.Text = "";
        //
        // editGrid（edit mode metadata 表單：5 row × 2 col）
        //
        editGrid.AutoSize = true;
        editGrid.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        editGrid.ColumnCount = 2;
        editGrid.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 70F));
        editGrid.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        editGrid.Dock = System.Windows.Forms.DockStyle.Fill;
        editGrid.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        editGrid.Name = "editGrid";
        for (int i = 0; i < 5; i++) editGrid.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        editGrid.Controls.Add(MakeEditLabel("系列"), 0, 0);
        editGrid.Controls.Add(txtSeries, 1, 0);
        editGrid.Controls.Add(MakeEditLabel("章節"), 0, 1);
        editGrid.Controls.Add(txtChapter, 1, 1);
        editGrid.Controls.Add(MakeEditLabel("語言"), 0, 2);
        editGrid.Controls.Add(txtLanguage, 1, 2);
        editGrid.Controls.Add(MakeEditLabel("翻譯組"), 0, 3);
        editGrid.Controls.Add(txtTranslationGroup, 1, 3);
        editGrid.Controls.Add(MakeEditLabel("評分"), 0, 4);
        editGrid.Controls.Add(numRating, 1, 4);
        editGrid.Visible = false;
        //
        // edit inputs 共用屬性 — Anchor=L|R 讓 column 100% 寬撐滿，固定 Height 給 AutoSize row 用
        //
        const int InputH = 30;
        txtSeries.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
        txtSeries.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        txtSeries.Name = "txtSeries";
        txtSeries.Size = new System.Drawing.Size(200, InputH);
        txtChapter.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
        txtChapter.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        txtChapter.Name = "txtChapter";
        txtChapter.Size = new System.Drawing.Size(200, InputH);
        txtLanguage.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
        txtLanguage.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        txtLanguage.Name = "txtLanguage";
        txtLanguage.PlaceholderText = "ja / en / zh-CN / zh-TW ...";
        txtLanguage.Size = new System.Drawing.Size(200, InputH);
        txtTranslationGroup.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
        txtTranslationGroup.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        txtTranslationGroup.Name = "txtTranslationGroup";
        txtTranslationGroup.Size = new System.Drawing.Size(200, InputH);
        numRating.Anchor = System.Windows.Forms.AnchorStyles.Left;
        numRating.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        numRating.Minimum = 0;
        numRating.Maximum = 5;
        numRating.Name = "numRating";
        numRating.Size = new System.Drawing.Size(80, InputH);

        //
        // lblReadStatsHeader
        //
        lblReadStatsHeader.AutoSize = true;
        lblReadStatsHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblReadStatsHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblReadStatsHeader.Name = "lblReadStatsHeader";
        lblReadStatsHeader.Text = "閱讀記錄";
        //
        // lblReadStats
        //
        lblReadStats.AutoSize = true;
        lblReadStats.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5F);
        lblReadStats.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblReadStats.Name = "lblReadStats";
        lblReadStats.Text = "";

        //
        // lblTagsHeader
        //
        lblTagsHeader.AutoSize = true;
        lblTagsHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblTagsHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblTagsHeader.Name = "lblTagsHeader";
        lblTagsHeader.Text = "標籤";
        //
        // flowTags
        //
        flowTags.AutoSize = true;
        flowTags.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowTags.Dock = System.Windows.Forms.DockStyle.Fill;
        flowTags.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        flowTags.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        flowTags.Name = "flowTags";
        flowTags.WrapContents = true;
        //
        // addTagPanel — edit mode：txtAddTag + btnAddTag
        //
        addTagPanel.AutoSize = true;
        addTagPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        addTagPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        addTagPanel.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        addTagPanel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        addTagPanel.Name = "addTagPanel";
        addTagPanel.WrapContents = false;
        addTagPanel.Controls.Add(txtAddTag);
        addTagPanel.Controls.Add(btnAddTag);
        addTagPanel.Visible = false;
        //
        // txtAddTag
        //
        txtAddTag.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        txtAddTag.Name = "txtAddTag";
        txtAddTag.PlaceholderText = "新增標籤（Enter 加入）";
        txtAddTag.Size = new System.Drawing.Size(220, 30);
        //
        // btnAddTag
        //
        btnAddTag.IconSvg = "PlusOutlined";
        btnAddTag.Margin = new System.Windows.Forms.Padding(0);
        btnAddTag.Name = "btnAddTag";
        btnAddTag.Size = new System.Drawing.Size(60, 30);
        btnAddTag.Text = "加入";

        //
        // lblFileInfoHeader
        //
        lblFileInfoHeader.AutoSize = true;
        lblFileInfoHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblFileInfoHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblFileInfoHeader.Name = "lblFileInfoHeader";
        lblFileInfoHeader.Text = "檔案資訊";
        //
        // lblFileInfo
        //
        lblFileInfo.AutoSize = true;
        lblFileInfo.MaximumSize = new System.Drawing.Size(340, 0);
        lblFileInfo.Font = new System.Drawing.Font("Microsoft JhengHei UI", 8.5F);
        lblFileInfo.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblFileInfo.Name = "lblFileInfo";
        lblFileInfo.Text = "";

        //
        // flowActions
        //
        flowActions.AutoSize = true;
        flowActions.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowActions.Dock = System.Windows.Forms.DockStyle.Fill;
        flowActions.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        flowActions.Margin = new System.Windows.Forms.Padding(0);
        flowActions.Name = "flowActions";
        flowActions.WrapContents = true;
        flowActions.Controls.Add(btnRead);
        flowActions.Controls.Add(btnExplorer);
        flowActions.Controls.Add(btnEdit);
        flowActions.Controls.Add(btnFetchExternal);
        flowActions.Controls.Add(btnResetReading);
        flowActions.Controls.Add(btnSave);
        flowActions.Controls.Add(btnCancel);
        //
        // btnRead
        //
        btnRead.IconSvg = "PlayCircleOutlined";
        btnRead.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnRead.Name = "btnRead";
        btnRead.Size = new System.Drawing.Size(96, 30);
        btnRead.Text = "閱讀";
        btnRead.Type = AntdUI.TTypeMini.Primary;
        //
        // btnExplorer
        //
        btnExplorer.IconSvg = "FolderOpenOutlined";
        btnExplorer.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnExplorer.Name = "btnExplorer";
        btnExplorer.Size = new System.Drawing.Size(96, 30);
        btnExplorer.Text = "開啟位置";
        //
        // btnResetReading
        //
        btnResetReading.IconSvg = "UndoOutlined";
        btnResetReading.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnResetReading.Name = "btnResetReading";
        btnResetReading.Size = new System.Drawing.Size(120, 30);
        btnResetReading.Text = "重置閱讀記錄";
        btnResetReading.Type = AntdUI.TTypeMini.Default;
        //
        // btnSave / btnCancel — edit mode 的操作按鈕
        //
        btnSave.IconSvg = "SaveOutlined";
        btnSave.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnSave.Name = "btnSave";
        btnSave.Size = new System.Drawing.Size(96, 30);
        btnSave.Text = "儲存";
        btnSave.Type = AntdUI.TTypeMini.Primary;
        btnSave.Visible = false;
        btnCancel.Margin = new System.Windows.Forms.Padding(0);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new System.Drawing.Size(80, 30);
        btnCancel.Text = "取消";
        btnCancel.Visible = false;

        //
        // lblEmpty
        //
        lblEmpty.Dock = System.Windows.Forms.DockStyle.Fill;
        lblEmpty.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F);
        lblEmpty.Name = "lblEmpty";
        lblEmpty.Text = "";
        lblEmpty.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        lblEmpty.Visible = false;

        //
        // WorkDetailPanel
        //
        Controls.Add(contentPanel);
        Controls.Add(headerPanel);
        Controls.Add(lblEmpty);
        Name = "WorkDetailPanel";
        Size = new System.Drawing.Size(380, 600);

        coverPanel.ResumeLayout(false);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        contentPanel.ResumeLayout(false);
        contentPanel.PerformLayout();
        headerPanel.ResumeLayout(false);
        ResumeLayout(false);
    }

    /// <summary>editGrid 用的小 Label helper — 統一 font / margin。</summary>
    private static System.Windows.Forms.Label MakeEditLabel(string text) => new System.Windows.Forms.Label
    {
        AutoSize = true,
        Anchor = System.Windows.Forms.AnchorStyles.Left,
        Font = new System.Drawing.Font("Microsoft JhengHei UI", 9F),
        Margin = new System.Windows.Forms.Padding(0, 6, 6, 0),
        Text = text,
    };
}
