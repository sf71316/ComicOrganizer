namespace ComicOrganize.App.UI;

/// <summary>
/// FlowLayoutPanel 子類別，啟用 DoubleBuffered + WS_EX_COMPOSITED 減少捲動 / resize 抖動 + 卡頓。
/// - DoubleBuffered (= OptimizedDoubleBuffer + AllPaintingInWmPaint): 對 panel 自身重繪有效
/// - WS_EX_COMPOSITED (extended window style 0x02000000): 整個容器跟所有子控件 composite 到
///   memory 一次貼出，對「大量子控件 + 捲動」場景效果最強。
///   副作用：某些控件動畫可能延遲；Tabs / 自繪控件 host 在裡面通常沒問題。
/// 注意：不要加 ControlStyles.UserPaint — container 加了會不畫子控件。
/// </summary>
internal class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    private const int WS_EX_COMPOSITED = 0x02000000;

    public BufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_COMPOSITED;
            return cp;
        }
    }
}
