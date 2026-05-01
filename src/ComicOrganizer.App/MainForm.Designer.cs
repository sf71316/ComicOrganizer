using AntdUI;
using System.Windows.Forms;

namespace ComicOrganizer.App;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private AntdUI.PageHeader windowBar;
    private System.Windows.Forms.MenuStrip menuStrip1;
    private System.Windows.Forms.Panel contentHost;

    private UI.GalleryControl galleryControl;
    private UI.ArchiveControl archiveControl;

    private ToolStripMenuItem mnuFile;
    private ToolStripMenuItem mnuFileGallery;
    private ToolStripMenuItem mnuFileArchive;
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
        archiveControl = new ComicOrganizer.App.UI.ArchiveControl();
        galleryControl = new ComicOrganizer.App.UI.GalleryControl();
        menuStrip1.SuspendLayout();
        contentHost.SuspendLayout();
        SuspendLayout();
        // 
        // windowBar
        // 
        windowBar.BackColor = Core.ThemeColors.Primary;
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
        menuStrip1.BackColor = Core.ThemeColors.Background;
        menuStrip1.Font = new Font("Microsoft JhengHei UI", 10F);
        menuStrip1.ForeColor = Core.ThemeColors.Text;
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
        mnuFile.DropDownItems.AddRange(new ToolStripItem[] { mnuFileGallery, mnuFileArchive, mnuFileSep1, mnuFileSettings, mnuFileSepCache, mnuFileRebuildCache, mnuFileSep2, mnuFileExit });
        mnuFile.Name = "mnuFile";
        mnuFile.Size = new Size(63, 22);
        mnuFile.Text = "功能(&F)";
        // 
        // mnuFileGallery
        // 
        mnuFileGallery.Name = "mnuFileGallery";
        mnuFileGallery.Size = new Size(164, 22);
        mnuFileGallery.Text = "漫畫庫管理(&G)";
        // 
        // mnuFileArchive
        // 
        mnuFileArchive.Name = "mnuFileArchive";
        mnuFileArchive.Size = new Size(164, 22);
        mnuFileArchive.Text = "自動歸檔(&A)";
        // 
        // mnuFileSep1
        // 
        mnuFileSep1.Name = "mnuFileSep1";
        mnuFileSep1.Size = new Size(161, 6);
        // 
        // mnuFileSettings
        // 
        mnuFileSettings.Name = "mnuFileSettings";
        mnuFileSettings.Size = new Size(164, 22);
        mnuFileSettings.Text = "系統設定(&S)...";
        //
        // mnuFileSepCache
        //
        mnuFileSepCache.Name = "mnuFileSepCache";
        mnuFileSepCache.Size = new Size(161, 6);
        //
        // mnuFileRebuildCache
        //
        mnuFileRebuildCache.Name = "mnuFileRebuildCache";
        mnuFileRebuildCache.Size = new Size(164, 22);
        mnuFileRebuildCache.Text = "重建索引資料庫(&R)...";
        //
        // mnuFileSep2
        // 
        mnuFileSep2.Name = "mnuFileSep2";
        mnuFileSep2.Size = new Size(161, 6);
        // 
        // mnuFileExit
        // 
        mnuFileExit.Name = "mnuFileExit";
        mnuFileExit.Size = new Size(164, 22);
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
        mnuViewMode.Size = new Size(154, 22);
        mnuViewMode.Text = "檢視模式(&M)";
        // 
        // mnuViewModeSmall
        // 
        mnuViewModeSmall.Name = "mnuViewModeSmall";
        mnuViewModeSmall.Size = new Size(130, 22);
        mnuViewModeSmall.Text = "縮圖 (小)";
        // 
        // mnuViewModeMedium
        // 
        mnuViewModeMedium.Checked = true;
        mnuViewModeMedium.CheckState = CheckState.Checked;
        mnuViewModeMedium.Name = "mnuViewModeMedium";
        mnuViewModeMedium.Size = new Size(130, 22);
        mnuViewModeMedium.Text = "縮圖 (中)";
        // 
        // mnuViewModeLarge
        // 
        mnuViewModeLarge.Name = "mnuViewModeLarge";
        mnuViewModeLarge.Size = new Size(130, 22);
        mnuViewModeLarge.Text = "縮圖 (大)";
        // 
        // mnuViewModeList
        // 
        mnuViewModeList.Name = "mnuViewModeList";
        mnuViewModeList.Size = new Size(130, 22);
        mnuViewModeList.Text = "列表";
        // 
        // contentHost
        // 
        contentHost.Controls.Add(galleryControl);
        contentHost.Controls.Add(archiveControl);
        contentHost.Dock = DockStyle.Fill;
        contentHost.Location = new Point(0, 78);
        contentHost.Name = "contentHost";
        contentHost.Size = new Size(1280, 642);
        contentHost.TabIndex = 2;
        // 
        // archiveControl
        // 
        archiveControl.Dock = DockStyle.Fill;
        archiveControl.Location = new Point(0, 0);
        archiveControl.Name = "archiveControl";
        archiveControl.Size = new Size(1280, 642);
        archiveControl.TabIndex = 1;
        archiveControl.Visible = false;
        // 
        // galleryControl
        // 
        galleryControl.Dock = DockStyle.Fill;
        galleryControl.Location = new Point(0, 0);
        galleryControl.Name = "galleryControl";
        galleryControl.Size = new Size(1280, 642);
        galleryControl.TabIndex = 0;
        // 
        // MainForm
        // 
        ClientSize = new Size(1280, 720);
        Controls.Add(contentHost);
        Controls.Add(menuStrip1);
        Controls.Add(windowBar);
        MainMenuStrip = menuStrip1;
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Comic Organize - 專業版";
        menuStrip1.ResumeLayout(false);
        menuStrip1.PerformLayout();
        contentHost.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
