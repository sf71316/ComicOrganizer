using AntdUI;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ComicOrganizer.App.UI;

partial class MetadataToolsPanel
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Label lblTitle;

    // 統計區
    private AntdUI.Panel statsPanel;
    private System.Windows.Forms.Label lblStatsTitle;
    private System.Windows.Forms.Label lblStatsTotal;
    private System.Windows.Forms.Label lblStatsParsed;
    private System.Windows.Forms.Label lblStatsUnparsed;
    private System.Windows.Forms.Label lblStatsManual;
    private System.Windows.Forms.Label lblStatsAuto;
    private AntdUI.Button btnRefreshStats;

    // 重新解析區
    private AntdUI.Panel reparsePanel;
    private System.Windows.Forms.Label lblReparseTitle;
    private System.Windows.Forms.Label lblReparseDesc;
    private AntdUI.Button btnReparse;
    private AntdUI.Progress progressBar;
    private System.Windows.Forms.Label lblProgressStatus;
    private System.Windows.Forms.Label lblProgressCurrent;
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
        lblTitle = new System.Windows.Forms.Label();
        statsPanel = new AntdUI.Panel();
        btnRefreshStats = new AntdUI.Button();
        lblStatsAuto = new System.Windows.Forms.Label();
        lblStatsManual = new System.Windows.Forms.Label();
        lblStatsUnparsed = new System.Windows.Forms.Label();
        lblStatsParsed = new System.Windows.Forms.Label();
        lblStatsTotal = new System.Windows.Forms.Label();
        lblStatsTitle = new System.Windows.Forms.Label();
        reparsePanel = new AntdUI.Panel();
        btnCancel = new AntdUI.Button();
        lblProgressCurrent = new System.Windows.Forms.Label();
        lblProgressStatus = new System.Windows.Forms.Label();
        progressBar = new Progress();
        btnReparse = new AntdUI.Button();
        lblReparseDesc = new System.Windows.Forms.Label();
        lblReparseTitle = new System.Windows.Forms.Label();
        statsPanel.SuspendLayout();
        reparsePanel.SuspendLayout();
        SuspendLayout();
        // 
        // lblTitle
        // 
        lblTitle.AutoSize = true;
        lblTitle.BackColor = Color.Transparent;
        lblTitle.Font = new Font("Microsoft JhengHei UI", 13F, FontStyle.Bold);
        lblTitle.Location = new Point(20, 15);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new Size(118, 23);
        lblTitle.TabIndex = 0;
        lblTitle.Text = "詮釋資料工具";
        // 
        // statsPanel
        // 
        statsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        statsPanel.Controls.Add(btnRefreshStats);
        statsPanel.Controls.Add(lblStatsAuto);
        statsPanel.Controls.Add(lblStatsManual);
        statsPanel.Controls.Add(lblStatsUnparsed);
        statsPanel.Controls.Add(lblStatsParsed);
        statsPanel.Controls.Add(lblStatsTotal);
        statsPanel.Controls.Add(lblStatsTitle);
        statsPanel.Location = new Point(20, 55);
        statsPanel.Name = "statsPanel";
        statsPanel.Padding = new Padding(20, 16, 20, 16);
        statsPanel.Radius = 8;
        statsPanel.Size = new Size(805, 220);
        statsPanel.TabIndex = 1;
        // 
        // btnRefreshStats
        // 
        btnRefreshStats.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnRefreshStats.IconSvg = "ReloadOutlined";
        btnRefreshStats.Location = new Point(672, 19);
        btnRefreshStats.Name = "btnRefreshStats";
        btnRefreshStats.Size = new Size(110, 32);
        btnRefreshStats.TabIndex = 6;
        btnRefreshStats.Text = "重新整理";
        // 
        // lblStatsAuto
        // 
        lblStatsAuto.AutoSize = true;
        lblStatsAuto.BackColor = Color.Transparent;
        lblStatsAuto.Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold);
        lblStatsAuto.Location = new Point(20, 150);
        lblStatsAuto.Name = "lblStatsAuto";
        lblStatsAuto.Size = new Size(110, 15);
        lblStatsAuto.TabIndex = 5;
        lblStatsAuto.Text = "可自動重 parse: —";
        // 
        // lblStatsManual
        // 
        lblStatsManual.AutoSize = true;
        lblStatsManual.BackColor = Color.Transparent;
        lblStatsManual.Location = new Point(20, 125);
        lblStatsManual.Name = "lblStatsManual";
        lblStatsManual.Size = new Size(182, 15);
        lblStatsManual.TabIndex = 4;
        lblStatsManual.Text = "手動編輯保留 (重 parse 跳過): —";
        // 
        // lblStatsUnparsed
        // 
        lblStatsUnparsed.AutoSize = true;
        lblStatsUnparsed.BackColor = Color.Transparent;
        lblStatsUnparsed.Location = new Point(20, 100);
        lblStatsUnparsed.Name = "lblStatsUnparsed";
        lblStatsUnparsed.Size = new Size(72, 15);
        lblStatsUnparsed.TabIndex = 3;
        lblStatsUnparsed.Text = "未 parse: —";
        // 
        // lblStatsParsed
        // 
        lblStatsParsed.AutoSize = true;
        lblStatsParsed.BackColor = Color.Transparent;
        lblStatsParsed.Location = new Point(20, 75);
        lblStatsParsed.Name = "lblStatsParsed";
        lblStatsParsed.Size = new Size(72, 15);
        lblStatsParsed.TabIndex = 2;
        lblStatsParsed.Text = "已 parse: —";
        // 
        // lblStatsTotal
        // 
        lblStatsTotal.AutoSize = true;
        lblStatsTotal.BackColor = Color.Transparent;
        lblStatsTotal.Location = new Point(20, 50);
        lblStatsTotal.Name = "lblStatsTotal";
        lblStatsTotal.Size = new Size(74, 15);
        lblStatsTotal.TabIndex = 1;
        lblStatsTotal.Text = "作品總數: —";
        // 
        // lblStatsTitle
        // 
        lblStatsTitle.AutoSize = true;
        lblStatsTitle.BackColor = Color.Transparent;
        lblStatsTitle.Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold);
        lblStatsTitle.Location = new Point(20, 16);
        lblStatsTitle.Name = "lblStatsTitle";
        lblStatsTitle.Size = new Size(69, 19);
        lblStatsTitle.TabIndex = 0;
        lblStatsTitle.Text = "解析統計";
        // 
        // reparsePanel
        // 
        reparsePanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        reparsePanel.Controls.Add(btnCancel);
        reparsePanel.Controls.Add(lblProgressCurrent);
        reparsePanel.Controls.Add(lblProgressStatus);
        reparsePanel.Controls.Add(progressBar);
        reparsePanel.Controls.Add(btnReparse);
        reparsePanel.Controls.Add(lblReparseDesc);
        reparsePanel.Controls.Add(lblReparseTitle);
        reparsePanel.Location = new Point(20, 290);
        reparsePanel.Name = "reparsePanel";
        reparsePanel.Padding = new Padding(20, 16, 20, 16);
        reparsePanel.Radius = 8;
        reparsePanel.Size = new Size(805, 290);
        reparsePanel.TabIndex = 2;
        // 
        // btnCancel
        // 
        btnCancel.Location = new Point(220, 115);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(100, 40);
        btnCancel.TabIndex = 6;
        btnCancel.Text = "取消";
        btnCancel.Visible = false;
        // 
        // lblProgressCurrent
        // 
        lblProgressCurrent.BackColor = Color.Transparent;
        lblProgressCurrent.Location = new Point(20, 230);
        lblProgressCurrent.Name = "lblProgressCurrent";
        lblProgressCurrent.Size = new Size(600, 20);
        lblProgressCurrent.TabIndex = 5;
        // 
        // lblProgressStatus
        // 
        lblProgressStatus.AutoSize = true;
        lblProgressStatus.BackColor = Color.Transparent;
        lblProgressStatus.Location = new Point(20, 210);
        lblProgressStatus.Name = "lblProgressStatus";
        lblProgressStatus.Size = new Size(0, 15);
        lblProgressStatus.TabIndex = 4;
        // 
        // progressBar
        // 
        progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Back = Color.Transparent;
        progressBar.BackColor = Color.Transparent;
        progressBar.Location = new Point(20, 175);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(746, 25);
        progressBar.TabIndex = 3;
        progressBar.Visible = false;
        // 
        // btnReparse
        // 
        btnReparse.IconSvg = "PlayCircleOutlined";
        btnReparse.Location = new Point(20, 115);
        btnReparse.Name = "btnReparse";
        btnReparse.Size = new Size(180, 40);
        btnReparse.TabIndex = 2;
        btnReparse.Text = "整庫重新解析";
        btnReparse.Type = TTypeMini.Primary;
        // 
        // lblReparseDesc
        // 
        lblReparseDesc.BackColor = Color.Transparent;
        lblReparseDesc.Location = new Point(20, 45);
        lblReparseDesc.Name = "lblReparseDesc";
        lblReparseDesc.Size = new Size(600, 60);
        lblReparseDesc.TabIndex = 1;
        lblReparseDesc.Text = "從檔名重新解析所有未手動編輯的作品。閱讀記錄（讀取次數 / 上次閱讀 / 上次頁碼）會保留。已標記為「手動編輯」的作品會被跳過。";
        // 
        // lblReparseTitle
        // 
        lblReparseTitle.AutoSize = true;
        lblReparseTitle.BackColor = Color.Transparent;
        lblReparseTitle.Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold);
        lblReparseTitle.Location = new Point(20, 16);
        lblReparseTitle.Name = "lblReparseTitle";
        lblReparseTitle.Size = new Size(172, 19);
        lblReparseTitle.TabIndex = 0;
        lblReparseTitle.Text = "整庫重新解析 metadata";
        // 
        // MetadataToolsPanel
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(reparsePanel);
        Controls.Add(statsPanel);
        Controls.Add(lblTitle);
        Name = "MetadataToolsPanel";
        Size = new Size(851, 614);
        statsPanel.ResumeLayout(false);
        statsPanel.PerformLayout();
        reparsePanel.ResumeLayout(false);
        reparsePanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
