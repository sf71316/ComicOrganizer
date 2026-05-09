namespace ComicOrganizer.App.UI;

partial class AuthorInfoPanel
{
    private System.ComponentModel.IContainer components = null;

    // Header
    private System.Windows.Forms.Panel headerPanel;
    private System.Windows.Forms.Label lblTitle;
    private AntdUI.Button btnEdit;
    private AntdUI.Button btnClose;
    private AntdUI.Button btnExpandMax;

    // 資料區（ShowEmpty 時隱藏）
    private System.Windows.Forms.Panel contentPanel;
    private System.Windows.Forms.TableLayoutPanel layout;
    private System.Windows.Forms.Label lblMainName;
    private AntdUI.Select txtMainName;           // edit — Select with EhTag autocomplete
    private System.Windows.Forms.Label lblFolderPath;
    private System.Windows.Forms.Label lblAliases;
    private System.Windows.Forms.FlowLayoutPanel flowAliasesEdit;   // edit：每個別名 chip + 末尾 add
    private System.Windows.Forms.FlowLayoutPanel addAliasPanel;     // edit：txtAddAliasName + lang + button
    private AntdUI.Select txtAddAliasName;       // Select with EhTag autocomplete
    private AntdUI.Input txtAddAliasLang;
    private AntdUI.Button btnAddAlias;
    private AntdUI.Divider divider1;
    private System.Windows.Forms.Label lblStats;
    private AntdUI.Divider divider2;
    private System.Windows.Forms.Label lblTagsHeader;
    private System.Windows.Forms.FlowLayoutPanel flowTags;
    private System.Windows.Forms.FlowLayoutPanel addTagPanel;       // edit only
    private AntdUI.Input txtAddTag;
    private AntdUI.Button btnAddTag;
    private AntdUI.Divider divider3;
    private System.Windows.Forms.Label lblRecentHeader;
    private System.Windows.Forms.FlowLayoutPanel flowRecent;
    private AntdUI.Divider divider4;
    private System.Windows.Forms.Label lblAllWorksHeader;
    private System.Windows.Forms.FlowLayoutPanel flowAllWorks;
    private AntdUI.Divider divider5;
    private System.Windows.Forms.FlowLayoutPanel flowActions;
    private AntdUI.Button btnResetAuthorReading;
    private AntdUI.Button btnBatchFetch;       // 批次同步此作者所有作品（#52 Phase F）
    private AntdUI.Button btnSave;
    private AntdUI.Button btnCancel;

    // ShowEmpty 訊息
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
        btnClose = new AntdUI.Button();
        btnExpandMax = new AntdUI.Button();
        contentPanel = new System.Windows.Forms.Panel();
        layout = new System.Windows.Forms.TableLayoutPanel();
        lblMainName = new System.Windows.Forms.Label();
        txtMainName = new AntdUI.Select();
        lblFolderPath = new System.Windows.Forms.Label();
        lblAliases = new System.Windows.Forms.Label();
        flowAliasesEdit = new System.Windows.Forms.FlowLayoutPanel();
        addAliasPanel = new System.Windows.Forms.FlowLayoutPanel();
        txtAddAliasName = new AntdUI.Select();
        txtAddAliasLang = new AntdUI.Input();
        btnAddAlias = new AntdUI.Button();
        divider1 = new AntdUI.Divider();
        lblStats = new System.Windows.Forms.Label();
        divider2 = new AntdUI.Divider();
        lblTagsHeader = new System.Windows.Forms.Label();
        flowTags = new System.Windows.Forms.FlowLayoutPanel();
        addTagPanel = new System.Windows.Forms.FlowLayoutPanel();
        txtAddTag = new AntdUI.Input();
        btnAddTag = new AntdUI.Button();
        divider3 = new AntdUI.Divider();
        lblRecentHeader = new System.Windows.Forms.Label();
        flowRecent = new System.Windows.Forms.FlowLayoutPanel();
        divider4 = new AntdUI.Divider();
        lblAllWorksHeader = new System.Windows.Forms.Label();
        flowAllWorks = new System.Windows.Forms.FlowLayoutPanel();
        divider5 = new AntdUI.Divider();
        flowActions = new System.Windows.Forms.FlowLayoutPanel();
        btnResetAuthorReading = new AntdUI.Button();
        btnBatchFetch = new AntdUI.Button();
        btnSave = new AntdUI.Button();
        btnCancel = new AntdUI.Button();
        lblEmpty = new System.Windows.Forms.Label();

        SuspendLayout();
        headerPanel.SuspendLayout();
        contentPanel.SuspendLayout();
        layout.SuspendLayout();

        //
        // headerPanel
        //
        headerPanel.Dock = System.Windows.Forms.DockStyle.Top;
        headerPanel.Height = 36;
        headerPanel.Padding = new System.Windows.Forms.Padding(8, 4, 4, 4);
        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(btnExpandMax);
        headerPanel.Controls.Add(btnClose);
        //
        // lblTitle
        //
        lblTitle.Dock = System.Windows.Forms.DockStyle.Fill;
        lblTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 11F, System.Drawing.FontStyle.Bold);
        lblTitle.Name = "lblTitle";
        lblTitle.Text = "作者資訊";
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
        // btnExpandMax — drawer 寬度撐到接近螢幕邊（toggle）
        //
        btnExpandMax.Dock = System.Windows.Forms.DockStyle.Right;
        btnExpandMax.Ghost = true;
        btnExpandMax.IconRatio = 0.6F;
        btnExpandMax.IconSvg = "FullscreenOutlined";
        btnExpandMax.Name = "btnExpandMax";
        btnExpandMax.Size = new System.Drawing.Size(28, 28);
        btnExpandMax.Text = "";
        //
        // btnEdit — 編輯切換（在 action bar，跟「清除作者閱讀記錄」同列）
        //
        btnEdit.IconSvg = "FormOutlined";
        btnEdit.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnEdit.Name = "btnEdit";
        btnEdit.Size = new System.Drawing.Size(80, 30);
        btnEdit.Text = "編輯";
        btnEdit.Type = AntdUI.TTypeMini.Primary;

        //
        // contentPanel
        //
        contentPanel.AutoScroll = true;
        contentPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        contentPanel.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
        contentPanel.Controls.Add(layout);

        //
        // layout (TableLayoutPanel — 每個 section 一行 AutoSize)
        //
        layout.AutoSize = true;
        layout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        layout.Dock = System.Windows.Forms.DockStyle.Top;
        // 編輯模式 row 跟 view row 同層、AutoSize 隱藏時自動收掉
        for (int i = 0; i < 22; i++) layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        layout.Controls.Add(lblMainName, 0, 0);
        layout.Controls.Add(txtMainName, 0, 1);          // edit
        layout.Controls.Add(lblFolderPath, 0, 2);        // 兩種模式都顯示（純資訊，不可編輯）
        layout.Controls.Add(lblAliases, 0, 4);
        layout.Controls.Add(flowAliasesEdit, 0, 5);      // edit：可刪別名 chips
        layout.Controls.Add(addAliasPanel, 0, 6);         // edit：加別名輸入
        layout.Controls.Add(divider1, 0, 7);
        layout.Controls.Add(lblStats, 0, 8);
        layout.Controls.Add(divider2, 0, 9);
        layout.Controls.Add(lblTagsHeader, 0, 10);
        layout.Controls.Add(flowTags, 0, 11);
        layout.Controls.Add(addTagPanel, 0, 12);          // edit
        layout.Controls.Add(divider3, 0, 13);
        layout.Controls.Add(lblRecentHeader, 0, 14);
        layout.Controls.Add(flowRecent, 0, 15);
        layout.Controls.Add(divider4, 0, 16);
        layout.Controls.Add(lblAllWorksHeader, 0, 17);
        layout.Controls.Add(flowAllWorks, 0, 18);
        layout.Controls.Add(divider5, 0, 19);
        layout.Controls.Add(flowActions, 0, 20);

        //
        // lblMainName
        //
        lblMainName.AutoSize = true;
        lblMainName.Font = new System.Drawing.Font("Microsoft JhengHei UI", 14F, System.Drawing.FontStyle.Bold);
        lblMainName.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
        lblMainName.Name = "lblMainName";
        lblMainName.Text = "(Author)";
        //
        // txtMainName (edit)
        //
        txtMainName.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
        txtMainName.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        txtMainName.Name = "txtMainName";
        txtMainName.PlaceholderText = "作者主名";
        txtMainName.Size = new System.Drawing.Size(280, 32);
        txtMainName.Visible = false;
        //
        // lblFolderPath
        //
        lblFolderPath.AutoSize = true;
        lblFolderPath.MaximumSize = new System.Drawing.Size(280, 0);
        lblFolderPath.Font = new System.Drawing.Font("Microsoft JhengHei UI", 8F);
        lblFolderPath.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblFolderPath.Name = "lblFolderPath";
        lblFolderPath.Text = "";
        //
        // lblAliases
        //
        lblAliases.AutoSize = true;
        lblAliases.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9F);
        lblAliases.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
        lblAliases.Name = "lblAliases";
        lblAliases.Text = "";
        //
        // flowAliasesEdit (edit) — 顯示可刪別名 chips
        //
        flowAliasesEdit.AutoSize = true;
        flowAliasesEdit.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowAliasesEdit.Dock = System.Windows.Forms.DockStyle.Fill;
        flowAliasesEdit.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        flowAliasesEdit.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        flowAliasesEdit.Name = "flowAliasesEdit";
        flowAliasesEdit.WrapContents = true;
        flowAliasesEdit.Visible = false;
        //
        // addAliasPanel (edit) — 新增別名 input
        //
        addAliasPanel.AutoSize = true;
        addAliasPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        addAliasPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        addAliasPanel.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        addAliasPanel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
        addAliasPanel.Name = "addAliasPanel";
        addAliasPanel.WrapContents = false;
        addAliasPanel.Controls.Add(txtAddAliasName);
        addAliasPanel.Controls.Add(txtAddAliasLang);
        addAliasPanel.Controls.Add(btnAddAlias);
        addAliasPanel.Visible = false;
        txtAddAliasName.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
        txtAddAliasName.Name = "txtAddAliasName";
        txtAddAliasName.PlaceholderText = "別名";
        txtAddAliasName.Size = new System.Drawing.Size(140, 30);
        txtAddAliasLang.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
        txtAddAliasLang.Name = "txtAddAliasLang";
        txtAddAliasLang.PlaceholderText = "lang";
        txtAddAliasLang.Size = new System.Drawing.Size(60, 30);
        btnAddAlias.IconSvg = "PlusOutlined";
        btnAddAlias.Margin = new System.Windows.Forms.Padding(0);
        btnAddAlias.Name = "btnAddAlias";
        btnAddAlias.Size = new System.Drawing.Size(60, 30);
        btnAddAlias.Text = "加入";

        //
        // divider1 / divider2 / divider3 / divider4
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
        divider5.Margin = new System.Windows.Forms.Padding(0, 8, 0, 4);
        divider5.Name = "divider5";
        //
        // flowActions — 操作按鈕容器
        //
        flowActions.AutoSize = true;
        flowActions.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowActions.Dock = System.Windows.Forms.DockStyle.Fill;
        flowActions.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        flowActions.Margin = new System.Windows.Forms.Padding(0, 4, 0, 0);
        flowActions.Name = "flowActions";
        flowActions.WrapContents = true;
        flowActions.Controls.Add(btnEdit);
        flowActions.Controls.Add(btnBatchFetch);
        flowActions.Controls.Add(btnResetAuthorReading);
        flowActions.Controls.Add(btnSave);
        flowActions.Controls.Add(btnCancel);
        //
        // btnResetAuthorReading
        //
        btnResetAuthorReading.IconSvg = "UndoOutlined";
        btnResetAuthorReading.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnResetAuthorReading.Name = "btnResetAuthorReading";
        btnResetAuthorReading.Size = new System.Drawing.Size(180, 30);
        btnResetAuthorReading.Text = "清除此作者閱讀記錄";
        btnResetAuthorReading.Type = AntdUI.TTypeMini.Default;

        //
        // btnBatchFetch — 同步此作者所有作品（從 nhentai 批次拉取）
        //
        btnBatchFetch.IconSvg = "ApiOutlined";
        btnBatchFetch.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnBatchFetch.Name = "btnBatchFetch";
        btnBatchFetch.Size = new System.Drawing.Size(180, 30);
        btnBatchFetch.Text = "同步此作者所有作品";
        btnBatchFetch.Visible = false;   // 由 host 依 EnableExternalMetadata config 控制顯示
        //
        // btnSave / btnCancel (edit only)
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
        // lblStats
        //
        lblStats.AutoSize = true;
        lblStats.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5F);
        lblStats.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblStats.Name = "lblStats";
        lblStats.Text = "";

        //
        // lblTagsHeader
        //
        lblTagsHeader.AutoSize = true;
        lblTagsHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblTagsHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblTagsHeader.Name = "lblTagsHeader";
        lblTagsHeader.Text = "主要標籤";
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
        // addTagPanel (edit only) — 新增 author tag
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
        txtAddTag.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        txtAddTag.Name = "txtAddTag";
        txtAddTag.PlaceholderText = "新增作者標籤（Enter 加入）";
        txtAddTag.Size = new System.Drawing.Size(220, 30);
        btnAddTag.IconSvg = "PlusOutlined";
        btnAddTag.Margin = new System.Windows.Forms.Padding(0);
        btnAddTag.Name = "btnAddTag";
        btnAddTag.Size = new System.Drawing.Size(60, 30);
        btnAddTag.Text = "加入";

        //
        // lblRecentHeader
        //
        lblRecentHeader.AutoSize = true;
        lblRecentHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblRecentHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblRecentHeader.Name = "lblRecentHeader";
        lblRecentHeader.Text = "最近閱讀";
        //
        // flowRecent
        //
        flowRecent.AutoSize = true;
        flowRecent.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowRecent.Dock = System.Windows.Forms.DockStyle.Fill;
        flowRecent.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
        flowRecent.Margin = new System.Windows.Forms.Padding(0);
        flowRecent.Name = "flowRecent";
        flowRecent.WrapContents = false;

        //
        // lblAllWorksHeader
        //
        lblAllWorksHeader.AutoSize = true;
        lblAllWorksHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblAllWorksHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblAllWorksHeader.Name = "lblAllWorksHeader";
        lblAllWorksHeader.Text = "全部作品";
        //
        // flowAllWorks
        //
        flowAllWorks.AutoSize = true;
        flowAllWorks.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowAllWorks.Dock = System.Windows.Forms.DockStyle.Fill;
        flowAllWorks.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
        flowAllWorks.Margin = new System.Windows.Forms.Padding(0);
        flowAllWorks.Name = "flowAllWorks";
        flowAllWorks.WrapContents = false;

        //
        // lblEmpty (ShowEmpty 時顯示)
        //
        lblEmpty.Dock = System.Windows.Forms.DockStyle.Fill;
        lblEmpty.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F);
        lblEmpty.Name = "lblEmpty";
        lblEmpty.Text = "";
        lblEmpty.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        lblEmpty.Visible = false;

        //
        // AuthorInfoPanel
        //
        Controls.Add(contentPanel);   // Dock.Fill 後 add → 取剩餘
        Controls.Add(headerPanel);    // Dock.Top 先 add
        Controls.Add(lblEmpty);       // 最上層，預設隱藏；ShowEmpty 時 BringToFront
        Name = "AuthorInfoPanel";
        Size = new System.Drawing.Size(320, 600);

        layout.ResumeLayout(false);
        layout.PerformLayout();
        contentPanel.ResumeLayout(false);
        contentPanel.PerformLayout();
        headerPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
