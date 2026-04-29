using AntdUI;
using ComicOrganize.App.Core;
using ComicOrganize.App.Models;
using ComicOrganize.App.UI;

namespace ComicOrganize.App;

public partial class MainForm : Window
{
    private readonly ConfigManager _configManager;
    private AppConfig _config;
    private readonly MappingIntelligence _intelligence;
    private readonly ArchiveOrganizer _organizer;
    private readonly CoverManager _coverManager;

    public MainForm()
    {
        InitializeComponent();

        _configManager = new ConfigManager();
        _config = _configManager.Load();
        
        // 初始化主題與語系
        LocalizationManager.Initialize(_config.Language);
        ThemeManager.Initialize(_config.Theme);
        
        ThemeManager.ThemeChanged += (s, e) => ThemeManager.ApplyTheme(this);
        LocalizationManager.LanguageChanged += (s, e) => ApplyLocalization();
        
        ThemeManager.ApplyTheme(this);
        ApplyLocalization();

        _intelligence = new MappingIntelligence();
        _organizer = new ArchiveOrganizer(_intelligence, _config);
        _coverManager = new CoverManager(_config);

        // 等 Form Load 後 Initialize（此時 handle 已建立、Invoke 才能用）
        this.Load += (s, e) =>
        {
            galleryControl.Initialize(_config, _coverManager);
            archiveControl.Initialize(_config, _intelligence, _organizer);
            ShowGallery();
        };

        WireMenuEvents();
    }

    private void WireMenuEvents()
    {
        mnuFileGallery.Click += (s, e) => ShowGallery();
        mnuFileArchive.Click += (s, e) => ShowArchive();
        mnuFileSettings.Click += (s, e) => OpenSettings();
        mnuFileExit.Click += (s, e) => Close();

        mnuViewModeSmall.Click += (s, e) => ApplyViewMode(GalleryControl.GalleryViewMode.Small);
        mnuViewModeMedium.Click += (s, e) => ApplyViewMode(GalleryControl.GalleryViewMode.Medium);
        mnuViewModeLarge.Click += (s, e) => ApplyViewMode(GalleryControl.GalleryViewMode.Large);
        mnuViewModeList.Click += (s, e) => ApplyViewMode(GalleryControl.GalleryViewMode.List);
    }

    private void ApplyViewMode(GalleryControl.GalleryViewMode mode)
    {
        galleryControl.SetViewMode(mode);
        mnuViewModeSmall.Checked  = mode == GalleryControl.GalleryViewMode.Small;
        mnuViewModeMedium.Checked = mode == GalleryControl.GalleryViewMode.Medium;
        mnuViewModeLarge.Checked  = mode == GalleryControl.GalleryViewMode.Large;
        mnuViewModeList.Checked   = mode == GalleryControl.GalleryViewMode.List;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        DraggableMouseDown();
        base.OnMouseDown(e);
    }

    private void ShowGallery()
    {
        archiveControl.Visible = false;
        galleryControl.Visible = true;
        mnuView.Enabled = true;
    }

    private void ShowArchive()
    {
        galleryControl.Visible = false;
        archiveControl.Visible = true;
        mnuView.Enabled = false;
    }

    private void OpenSettings()
    {
        using var settings = new SettingsForm(_configManager, _config);
        if (settings.ShowDialog(this) == DialogResult.OK)
        {
            _config = settings.Config;
            archiveControl.OnConfigUpdated(_config);
            galleryControl.OnConfigUpdated(_config);
        }
    }

    private void ApplyLocalization()
    {
        this.Text = LocalizationManager.GetString("AppTitle");
        mnuFile.Text = LocalizationManager.GetString("MenuFile"); // Need to ensure these keys exist or match
        mnuFileGallery.Text = LocalizationManager.GetString("MenuGallery");
        mnuFileArchive.Text = LocalizationManager.GetString("MenuArchive");
        mnuFileSettings.Text = LocalizationManager.GetString("MenuSettings");
        mnuFileExit.Text = LocalizationManager.GetString("MenuExit");
        
        mnuView.Text = LocalizationManager.GetString("MenuView");
        mnuViewModeSmall.Text = LocalizationManager.GetString("ViewSmall");
        mnuViewModeMedium.Text = LocalizationManager.GetString("ViewMedium");
        mnuViewModeLarge.Text = LocalizationManager.GetString("ViewLarge");
        mnuViewModeList.Text = LocalizationManager.GetString("ViewList");
    }
}
