using AntdUI;
using ComicOrganizer.App.Core;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

public partial class MetadataManageControl : UserControl, IThemedControl
{
    private IAssetMetaStore? _metaStore;
    private bool _initialized;

    /// <summary>Phase I：使用者在「作者」master-detail 右側按「同步選取作品」 — host (MainForm)
    /// 接此事件，委派到 GalleryControl.BatchSyncForAuthor(authorId, paths)。</summary>
    public event Action<long, IReadOnlyList<string>>? RequestBatchSync;

    /// <summary>使用者點 row 上的 sync icon — 對單本跑 Phase E 流程（host 委派到 SyncOneWork）。</summary>
    public event Action<string>? RequestSyncOne;

    /// <summary>使用者在標籤或作者管理頁點「在畫廊檢視」— host 切至畫廊並執行對應 DSL 搜尋。</summary>
    public event Action<string>? RequestSearchInGallery;

    public MetadataManageControl()
    {
        InitializeComponent();

        btnNavTags.Click += (s, e) => SwitchTo(NavSection.Tags);
        btnNavAuthors.Click += (s, e) => SwitchTo(NavSection.Authors);
        btnNavTools.Click += (s, e) => SwitchTo(NavSection.Tools);

        // 把 author panel 的 RequestBatchSync 往上層 bubble — host 由此入口走 Phase F 流程
        authorPanel.RequestBatchSync += (id, paths) => RequestBatchSync?.Invoke(id, paths);
        authorPanel.RequestSyncOne += path => RequestSyncOne?.Invoke(path);
        tagPanel.RequestViewInGallery += dsl => RequestSearchInGallery?.Invoke(dsl);
        authorPanel.RequestViewInGallery += dsl => RequestSearchInGallery?.Invoke(dsl);

        this.WireThemeAndLocalization();
    }

    public void Initialize(IAssetMetaStore metaStore, ReparseAllService reparseService,
                           RebuildProgressTracker tracker, TagTranslator? translator = null)
    {
        _metaStore = metaStore;
        tagPanel.Initialize(metaStore);
        tagPanel.SetTagTranslator(translator);
        authorPanel.Initialize(metaStore, translator);
        toolsPanel.Initialize(metaStore, reparseService, tracker);
        _initialized = true;
    }

    /// <summary>由 host 注入翻譯庫線上更新所需的 service + path（可獨立呼叫，不強制走 Initialize）。</summary>
    public void InitializeTranslationDb(TagTranslator translator, TagTranslationUpdater updater, string diskPath)
    {
        toolsPanel.InitializeTranslationDb(translator, updater, diskPath);
    }

    /// <summary>由 host 設定（依 config.EnableExternalMetadata） — 控制 author 右側「同步選取作品」按鈕啟用。</summary>
    public bool ExternalMetadataEnabled
    {
        get => authorPanel.ExternalMetadataEnabled;
        set => authorPanel.ExternalMetadataEnabled = value;
    }

    /// <summary>由 MainForm.ShowMetadataManage 呼叫，每次切到此 tab 時刷新當前 section。</summary>
    public void OnShown()
    {
        if (!_initialized) return;
        if (_current == NavSection.Tags) tagPanel.Refresh();
        else if (_current == NavSection.Authors) authorPanel.Refresh();
        else toolsPanel.Refresh();
    }

    private enum NavSection { Tags, Authors, Tools }
    private NavSection _current = NavSection.Tags;

    private void SwitchTo(NavSection section)
    {
        _current = section;
        btnNavTags.Type = section == NavSection.Tags ? TTypeMini.Primary : TTypeMini.Default;
        btnNavAuthors.Type = section == NavSection.Authors ? TTypeMini.Primary : TTypeMini.Default;
        btnNavTools.Type = section == NavSection.Tools ? TTypeMini.Primary : TTypeMini.Default;
        tagPanel.Visible = section == NavSection.Tags;
        authorPanel.Visible = section == NavSection.Authors;
        toolsPanel.Visible = section == NavSection.Tools;

        if (_initialized)
        {
            if (section == NavSection.Tags) tagPanel.Refresh();
            else if (section == NavSection.Authors) authorPanel.Refresh();
            else toolsPanel.Refresh();
        }
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Background;
        sidebarPanel.BackColor = ThemeColors.Background;
        contentPanel.BackColor = ThemeColors.Surface;
    }

    public void ApplyLocalization()
    {
        btnNavTags.Text = LocalizationManager.GetString("MetaNavTags") ?? "標籤";
        btnNavAuthors.Text = LocalizationManager.GetString("MetaNavAuthors") ?? "作者";
        btnNavTools.Text = LocalizationManager.GetString("MetaNavTools") ?? "工具";
    }
}
