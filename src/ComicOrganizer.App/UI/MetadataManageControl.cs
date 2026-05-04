using AntdUI;
using ComicOrganizer.App.Core;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

public partial class MetadataManageControl : UserControl, IThemedControl
{
    private IAssetMetaStore? _metaStore;
    private bool _initialized;

    public MetadataManageControl()
    {
        InitializeComponent();

        btnNavTags.Click += (s, e) => SwitchTo(NavSection.Tags);
        btnNavAuthors.Click += (s, e) => SwitchTo(NavSection.Authors);

        this.WireThemeAndLocalization();
    }

    public void Initialize(IAssetMetaStore metaStore)
    {
        _metaStore = metaStore;
        tagPanel.Initialize(metaStore);
        _initialized = true;
    }

    /// <summary>由 MainForm.ShowMetadataManage 呼叫，每次切到此 tab 時刷新一次。</summary>
    public void OnShown()
    {
        if (!_initialized) return;
        tagPanel.Refresh();
    }

    private enum NavSection { Tags, Authors }

    private void SwitchTo(NavSection section)
    {
        // Phase 4 只有 Tags
        btnNavTags.Type = section == NavSection.Tags ? TTypeMini.Primary : TTypeMini.Default;
        btnNavAuthors.Type = section == NavSection.Authors ? TTypeMini.Primary : TTypeMini.Default;
        tagPanel.Visible = section == NavSection.Tags;
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
    }
}
