using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// AntdUI.Drawer 內裝載的 host — 管 AuthorInfoPanel 與 WorkDetailPanel 兩個顯示模式，
/// 同一時刻只有一個 Visible。提供統一的事件介面給 GalleryControl 接。
/// </summary>
public partial class DetailDrawerHost : UserControl
{
    public AuthorInfoPanel AuthorPanel => authorPanel;
    public WorkDetailPanel WorkPanel => workPanel;

    public DetailDrawerHost()
    {
        InitializeComponent();
        BackColor = ThemeColors.Surface;
    }

    /// <summary>切到作者模式並套用資料。</summary>
    public void ShowAuthor(AuthorInfo info)
    {
        workPanel.Visible = false;
        authorPanel.Visible = true;
        authorPanel.ShowInfo(info);
    }

    public void ShowAuthorEmpty(string message)
    {
        workPanel.Visible = false;
        authorPanel.Visible = true;
        authorPanel.ShowEmpty(message);
    }

    /// <summary>切到作品模式並套用資料。</summary>
    public void ShowWork(WorkDetail detail, Image? cover)
    {
        authorPanel.Visible = false;
        workPanel.Visible = true;
        workPanel.Show(detail, cover);
    }

    public void ShowWorkEmpty(string message)
    {
        authorPanel.Visible = false;
        workPanel.Visible = true;
        workPanel.ShowEmpty(message);
    }
}
