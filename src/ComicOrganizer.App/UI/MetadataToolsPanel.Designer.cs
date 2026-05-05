using AntdUI;

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
        lblStatsTitle = new System.Windows.Forms.Label();
        lblStatsTotal = new System.Windows.Forms.Label();
        lblStatsParsed = new System.Windows.Forms.Label();
        lblStatsUnparsed = new System.Windows.Forms.Label();
        lblStatsManual = new System.Windows.Forms.Label();
        lblStatsAuto = new System.Windows.Forms.Label();
        btnRefreshStats = new AntdUI.Button();
        reparsePanel = new AntdUI.Panel();
        lblReparseTitle = new System.Windows.Forms.Label();
        lblReparseDesc = new System.Windows.Forms.Label();
        btnReparse = new AntdUI.Button();
        progressBar = new AntdUI.Progress();
        lblProgressStatus = new System.Windows.Forms.Label();
        lblProgressCurrent = new System.Windows.Forms.Label();
        btnCancel = new AntdUI.Button();

        statsPanel.SuspendLayout();
        reparsePanel.SuspendLayout();
        SuspendLayout();
        //
        // lblTitle
        //
        lblTitle.AutoSize = true;
        lblTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 13F, System.Drawing.FontStyle.Bold);
        lblTitle.Location = new System.Drawing.Point(20, 15);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new System.Drawing.Size(120, 25);
        lblTitle.TabIndex = 0;
        lblTitle.Text = "詮釋資料工具";
        //
        // statsPanel
        //
        statsPanel.Controls.Add(btnRefreshStats);
        statsPanel.Controls.Add(lblStatsAuto);
        statsPanel.Controls.Add(lblStatsManual);
        statsPanel.Controls.Add(lblStatsUnparsed);
        statsPanel.Controls.Add(lblStatsParsed);
        statsPanel.Controls.Add(lblStatsTotal);
        statsPanel.Controls.Add(lblStatsTitle);
        statsPanel.Location = new System.Drawing.Point(20, 55);
        statsPanel.Name = "statsPanel";
        statsPanel.Padding = new System.Windows.Forms.Padding(20, 16, 20, 16);
        statsPanel.Radius = 8;
        statsPanel.Size = new System.Drawing.Size(640, 220);
        statsPanel.TabIndex = 1;
        //
        // lblStatsTitle
        //
        lblStatsTitle.AutoSize = true;
        lblStatsTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 11F, System.Drawing.FontStyle.Bold);
        lblStatsTitle.Location = new System.Drawing.Point(20, 16);
        lblStatsTitle.Name = "lblStatsTitle";
        lblStatsTitle.Size = new System.Drawing.Size(80, 21);
        lblStatsTitle.TabIndex = 0;
        lblStatsTitle.Text = "解析統計";
        //
        // lblStatsTotal
        //
        lblStatsTotal.AutoSize = true;
        lblStatsTotal.Location = new System.Drawing.Point(20, 50);
        lblStatsTotal.Name = "lblStatsTotal";
        lblStatsTotal.Size = new System.Drawing.Size(120, 17);
        lblStatsTotal.TabIndex = 1;
        lblStatsTotal.Text = "作品總數: —";
        //
        // lblStatsParsed
        //
        lblStatsParsed.AutoSize = true;
        lblStatsParsed.Location = new System.Drawing.Point(20, 75);
        lblStatsParsed.Name = "lblStatsParsed";
        lblStatsParsed.Size = new System.Drawing.Size(120, 17);
        lblStatsParsed.TabIndex = 2;
        lblStatsParsed.Text = "已 parse: —";
        //
        // lblStatsUnparsed
        //
        lblStatsUnparsed.AutoSize = true;
        lblStatsUnparsed.Location = new System.Drawing.Point(20, 100);
        lblStatsUnparsed.Name = "lblStatsUnparsed";
        lblStatsUnparsed.Size = new System.Drawing.Size(120, 17);
        lblStatsUnparsed.TabIndex = 3;
        lblStatsUnparsed.Text = "未 parse: —";
        //
        // lblStatsManual
        //
        lblStatsManual.AutoSize = true;
        lblStatsManual.Location = new System.Drawing.Point(20, 125);
        lblStatsManual.Name = "lblStatsManual";
        lblStatsManual.Size = new System.Drawing.Size(180, 17);
        lblStatsManual.TabIndex = 4;
        lblStatsManual.Text = "手動編輯保留 (重 parse 跳過): —";
        //
        // lblStatsAuto
        //
        lblStatsAuto.AutoSize = true;
        lblStatsAuto.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9F, System.Drawing.FontStyle.Bold);
        lblStatsAuto.Location = new System.Drawing.Point(20, 150);
        lblStatsAuto.Name = "lblStatsAuto";
        lblStatsAuto.Size = new System.Drawing.Size(160, 17);
        lblStatsAuto.TabIndex = 5;
        lblStatsAuto.Text = "可自動重 parse: —";
        //
        // btnRefreshStats
        //
        btnRefreshStats.IconSvg = "ReloadOutlined";
        btnRefreshStats.Location = new System.Drawing.Point(500, 16);
        btnRefreshStats.Name = "btnRefreshStats";
        btnRefreshStats.Size = new System.Drawing.Size(110, 32);
        btnRefreshStats.TabIndex = 6;
        btnRefreshStats.Text = "重新整理";
        //
        // reparsePanel
        //
        reparsePanel.Controls.Add(btnCancel);
        reparsePanel.Controls.Add(lblProgressCurrent);
        reparsePanel.Controls.Add(lblProgressStatus);
        reparsePanel.Controls.Add(progressBar);
        reparsePanel.Controls.Add(btnReparse);
        reparsePanel.Controls.Add(lblReparseDesc);
        reparsePanel.Controls.Add(lblReparseTitle);
        reparsePanel.Location = new System.Drawing.Point(20, 290);
        reparsePanel.Name = "reparsePanel";
        reparsePanel.Padding = new System.Windows.Forms.Padding(20, 16, 20, 16);
        reparsePanel.Radius = 8;
        reparsePanel.Size = new System.Drawing.Size(640, 290);
        reparsePanel.TabIndex = 2;
        //
        // lblReparseTitle
        //
        lblReparseTitle.AutoSize = true;
        lblReparseTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 11F, System.Drawing.FontStyle.Bold);
        lblReparseTitle.Location = new System.Drawing.Point(20, 16);
        lblReparseTitle.Name = "lblReparseTitle";
        lblReparseTitle.Size = new System.Drawing.Size(150, 21);
        lblReparseTitle.TabIndex = 0;
        lblReparseTitle.Text = "整庫重新解析 metadata";
        //
        // lblReparseDesc
        //
        lblReparseDesc.Location = new System.Drawing.Point(20, 45);
        lblReparseDesc.Name = "lblReparseDesc";
        lblReparseDesc.Size = new System.Drawing.Size(600, 60);
        lblReparseDesc.TabIndex = 1;
        lblReparseDesc.Text = "從檔名重新解析所有未手動編輯的作品。閱讀記錄（讀取次數 / 上次閱讀 / 上次頁碼）會保留。已標記為「手動編輯」的作品會被跳過。";
        //
        // btnReparse
        //
        btnReparse.IconSvg = "PlayCircleOutlined";
        btnReparse.Location = new System.Drawing.Point(20, 115);
        btnReparse.Name = "btnReparse";
        btnReparse.Size = new System.Drawing.Size(180, 40);
        btnReparse.TabIndex = 2;
        btnReparse.Text = "整庫重新解析";
        btnReparse.Type = TTypeMini.Primary;
        //
        // progressBar
        //
        progressBar.Location = new System.Drawing.Point(20, 175);
        progressBar.Name = "progressBar";
        progressBar.Size = new System.Drawing.Size(600, 25);
        progressBar.TabIndex = 3;
        progressBar.Visible = false;
        //
        // lblProgressStatus
        //
        lblProgressStatus.AutoSize = true;
        lblProgressStatus.Location = new System.Drawing.Point(20, 210);
        lblProgressStatus.Name = "lblProgressStatus";
        lblProgressStatus.Size = new System.Drawing.Size(0, 17);
        lblProgressStatus.TabIndex = 4;
        //
        // lblProgressCurrent
        //
        lblProgressCurrent.Location = new System.Drawing.Point(20, 230);
        lblProgressCurrent.Name = "lblProgressCurrent";
        lblProgressCurrent.Size = new System.Drawing.Size(600, 20);
        lblProgressCurrent.TabIndex = 5;
        //
        // btnCancel
        //
        btnCancel.Location = new System.Drawing.Point(220, 115);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new System.Drawing.Size(100, 40);
        btnCancel.TabIndex = 6;
        btnCancel.Text = "取消";
        btnCancel.Visible = false;
        //
        // MetadataToolsPanel
        //
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        Controls.Add(reparsePanel);
        Controls.Add(statsPanel);
        Controls.Add(lblTitle);
        Name = "MetadataToolsPanel";
        Padding = new System.Windows.Forms.Padding(0);
        Size = new System.Drawing.Size(1100, 614);

        statsPanel.ResumeLayout(false);
        statsPanel.PerformLayout();
        reparsePanel.ResumeLayout(false);
        reparsePanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
