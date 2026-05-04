using AntdUI;
using ComicOrganizer.App.Core;
using System.Windows.Forms;

namespace ComicOrganizer.App;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private AntdUI.PageHeader windowBar;
    private System.Windows.Forms.MenuStrip menuStrip1;
    private System.Windows.Forms.Panel contentHost;

    // 通用狀態列 (背景任務、操作完成提示等都共用，由 ShowStatus(msg) 觸發)
    private AntdUI.Panel statusBar;
    private AntdUI.Label statusLabel;

    private UI.GalleryControl galleryControl;
    private UI.ArchiveControl archiveControl;
    private UI.MetadataManageControl metadataManageControl;

    private ToolStripMenuItem mnuFile;
    private ToolStripMenuItem mnuFileGallery;
    private ToolStripMenuItem mnuFileArchive;
    private ToolStripMenuItem mnuFileMetadata;
    private ToolStripSeparator mnuFileSep1;
    private ToolStripMenuItem mnuFileSettings;
    private ToolStripSeparator mnuFileSepCache;
    private ToolStripMenuItem mnuFileRebuildCache;
    private ToolStripSeparator mnuFileSep2;
    private ToolStripMenuItem mnuFileExit;

    private ToolStripMenuItem mnuView;
    private ToolStripMenuItem mnuViewMode;
    private ToolStripMenuItem mnuViewModeSmall;
    private ToolStripMenuItem mnuViewModeMedium;
    private ToolStripMenuItem mnuViewModeLarge;
    private ToolStripMenuItem mnuViewModeList;

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
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
        windowBar = new PageHeader();
        menuStrip1 = new MenuStrip();
        mnuFile = new ToolStripMenuItem();
        mnuFileGallery = new ToolStripMenuItem();
        mnuFileArchive = new ToolStripMenuItem();
        mnuFileMetadata = new ToolStripMenuItem();
        mnuFileSep1 = new ToolStripSeparator();
        mnuFileSettings = new ToolStripMenuItem();
        mnuFileSepCache = new ToolStripSeparator();
        mnuFileRebuildCache = new ToolStripMenuItem();
        mnuFileSep2 = new ToolStripSeparator();
        mnuFileExit = new ToolStripMenuItem();
        mnuView = new ToolStripMenuItem();
        mnuViewMode = new ToolStripMenuItem();
        mnuViewModeSmall = new ToolStripMenuItem();
        mnuViewModeMedium = new ToolStripMenuItem();
        mnuViewModeLarge = new ToolStripMenuItem();
        mnuViewModeList = new ToolStripMenuItem();
        contentHost = new System.Windows.Forms.Panel();
        galleryControl = new ComicOrganizer.App.UI.GalleryControl();
        archiveControl = new ComicOrganizer.App.UI.ArchiveControl();
        metadataManageControl = new ComicOrganizer.App.UI.MetadataManageControl();
        statusBar = new AntdUI.Panel();
        statusLabel = new AntdUI.Label();
        menuStrip1.SuspendLayout();
        contentHost.SuspendLayout();
        statusBar.SuspendLayout();
        SuspendLayout();
        // 
        // windowBar
        // 
        windowBar.BackColor = Color.FromArgb(60, 60, 60);
        windowBar.Dock = DockStyle.Top;
        windowBar.Font = new Font("Microsoft JhengHei UI", 10F);
        windowBar.ForeColor = Color.White;
        windowBar.Icon = (Image)resources.GetObject("windowBar.Icon");
        windowBar.IconSvg = "";
        windowBar.Location = new Point(0, 0);
        windowBar.Name = "windowBar";
        windowBar.Padding = new Padding(3);
        windowBar.ShowButton = true;
        windowBar.ShowIcon = true;
        windowBar.Size = new Size(1280, 48);
        windowBar.TabIndex = 0;
        windowBar.Text = "Comic Organize";
        windowBar.UseSystemStyleColor = true;
        // 
        // menuStrip1
        // 
        menuStrip1.BackColor = Color.FromArgb(30, 30, 30);
        menuStrip1.Font = new Font("Microsoft JhengHei UI", 10F);
        menuStrip1.ForeColor = Color.FromArgb(220, 220, 220);
        menuStrip1.Items.AddRange(new ToolStripItem[] { mnuFile });
        menuStrip1.Location = new Point(0, 48);
        menuStrip1.Name = "menuStrip1";
        menuStrip1.Padding = new Padding(8, 4, 0, 4);
        menuStrip1.Size = new Size(1280, 30);
        menuStrip1.TabIndex = 1;
        menuStrip1.Text = "menuStrip1";
        // 
        // mnuFile
        // 
        mnuFile.DropDownItems.AddRange(new ToolStripItem[] { mnuFileGallery, mnuFileArchive, mnuFileMetadata, mnuFileSep1, mnuFileSettings, mnuFileSepCache, mnuFileRebuildCache, mnuFileSep2, mnuFileExit });
        mnuFile.Name = "mnuFile";
        mnuFile.Size = new Size(63, 22);
        mnuFile.Text = "功能(&F)";
        // 
        // mnuFileGallery
        // 
        mnuFileGallery.Name = "mnuFileGallery";
        mnuFileGallery.Size = new Size(200, 22);
        mnuFileGallery.Text = "漫畫庫管理(&G)";
        // 
        // mnuFileArchive
        // 
        mnuFileArchive.Name = "mnuFileArchive";
        mnuFileArchive.Size = new Size(200, 22);
        mnuFileArchive.Text = "自動歸檔(&A)";
        //
        // mnuFileMetadata
        //
        mnuFileMetadata.Name = "mnuFileMetadata";
        mnuFileMetadata.Size = new Size(200, 22);
        mnuFileMetadata.Text = "詮釋資料管理(&M)";
        //
        // mnuFileSep1
        //
        mnuFileSep1.Name = "mnuFileSep1";
        mnuFileSep1.Size = new Size(197, 6);
        // 
        // mnuFileSettings
        // 
        mnuFileSettings.Name = "mnuFileSettings";
        mnuFileSettings.Size = new Size(200, 22);
        mnuFileSettings.Text = "系統設定(&S)...";
        // 
        // mnuFileSepCache
        // 
        mnuFileSepCache.Name = "mnuFileSepCache";
        mnuFileSepCache.Size = new Size(197, 6);
        // 
        // mnuFileRebuildCache
        // 
        mnuFileRebuildCache.Name = "mnuFileRebuildCache";
        mnuFileRebuildCache.Size = new Size(200, 22);
        mnuFileRebuildCache.Text = "重建索引資料庫(&R)...";
        // 
        // mnuFileSep2
        // 
        mnuFileSep2.Name = "mnuFileSep2";
        mnuFileSep2.Size = new Size(197, 6);
        // 
        // mnuFileExit
        // 
        mnuFileExit.Name = "mnuFileExit";
        mnuFileExit.Size = new Size(200, 22);
        mnuFileExit.Text = "結束(&X)";
        // 
        // mnuView
        // 
        mnuView.DropDownItems.AddRange(new ToolStripItem[] { mnuViewMode });
        mnuView.Name = "mnuView";
        mnuView.Size = new Size(65, 22);
        mnuView.Text = "檢視(&V)";
        // 
        // mnuViewMode
        // 
        mnuViewMode.DropDownItems.AddRange(new ToolStripItem[] { mnuViewModeSmall, mnuViewModeMedium, mnuViewModeLarge, mnuViewModeList });
        mnuViewMode.Name = "mnuViewMode";
        mnuViewMode.Size = new Size(142, 22);
        mnuViewMode.Text = "檢視模式(&M)";
        // 
        // mnuViewModeSmall
        // 
        mnuViewModeSmall.Name = "mnuViewModeSmall";
        mnuViewModeSmall.Size = new Size(121, 22);
        mnuViewModeSmall.Text = "縮圖 (小)";
        // 
        // mnuViewModeMedium
        // 
        mnuViewModeMedium.Checked = true;
        mnuViewModeMedium.CheckState = CheckState.Checked;
        mnuViewModeMedium.Name = "mnuViewModeMedium";
        mnuViewModeMedium.Size = new Size(121, 22);
        mnuViewModeMedium.Text = "縮圖 (中)";
        // 
        // mnuViewModeLarge
        // 
        mnuViewModeLarge.Name = "mnuViewModeLarge";
        mnuViewModeLarge.Size = new Size(121, 22);
        mnuViewModeLarge.Text = "縮圖 (大)";
        // 
        // mnuViewModeList
        // 
        mnuViewModeList.Name = "mnuViewModeList";
        mnuViewModeList.Size = new Size(121, 22);
        mnuViewModeList.Text = "列表";
        // 
        // contentHost
        // 
        contentHost.Controls.Add(galleryControl);
        contentHost.Controls.Add(archiveControl);
        contentHost.Controls.Add(metadataManageControl);
        contentHost.Dock = DockStyle.Fill;
        contentHost.Location = new Point(0, 78);
        contentHost.Name = "contentHost";
        contentHost.Size = new Size(1280, 614);
        contentHost.TabIndex = 2;
        // 
        // galleryControl
        // 
        galleryControl.Dock = DockStyle.Fill;
        galleryControl.Location = new Point(0, 0);
        galleryControl.Name = "galleryControl";
        galleryControl.Size = new Size(1280, 614);
        galleryControl.TabIndex = 0;
        // 
        // archiveControl
        // 
        archiveControl.BackColor = Color.FromArgb(30, 30, 30);
        archiveControl.Dock = DockStyle.Fill;
        archiveControl.Location = new Point(0, 0);
        archiveControl.Name = "archiveControl";
        archiveControl.Size = new Size(1280, 614);
        archiveControl.TabIndex = 1;
        archiveControl.Visible = false;
        //
        // metadataManageControl
        //
        metadataManageControl.Dock = DockStyle.Fill;
        metadataManageControl.Location = new Point(0, 0);
        metadataManageControl.Name = "metadataManageControl";
        metadataManageControl.Size = new Size(1280, 614);
        metadataManageControl.TabIndex = 2;
        metadataManageControl.Visible = false;
        //
        // statusBar
        // 
        statusBar.Back = ThemeColors.StatusBar;
        statusBar.Controls.Add(statusLabel);
        statusBar.Dock = DockStyle.Bottom;
        statusBar.Location = new Point(0, 692);
        statusBar.Name = "statusBar";
        statusBar.Padding = new Padding(12, 1, 12, 1);
        statusBar.Radius = 0;
        statusBar.Size = new Size(1280, 22);
        statusBar.TabIndex = 3;
        // 
        // statusLabel
        // 
        statusLabel.BackColor = Color.Transparent;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Font = new Font("Microsoft JhengHei UI", 9F);
        statusLabel.ForeColor = Color.White;
        statusLabel.Location = new Point(12, 1);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(1256, 20);
        statusLabel.TabIndex = 0;
        statusLabel.Text = "";
        // 
        // MainForm
        // 
        ClientSize = new Size(1280, 720);
        Controls.Add(contentHost);
        Controls.Add(statusBar);
        Controls.Add(menuStrip1);
        Controls.Add(windowBar);
        MainMenuStrip = menuStrip1;
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Comic Organize - 專業版";
        menuStrip1.ResumeLayout(false);
        menuStrip1.PerformLayout();
        contentHost.ResumeLayout(false);
        statusBar.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
