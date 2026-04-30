namespace ComicOrganize.App.UI;

/// <summary>
/// FlowLayoutPanel 子類別，啟用 DoubleBuffered 減少捲動 / resize 抖動 + 卡頓。
/// 預設的 FlowLayoutPanel 對大量子控件（漫畫卡片）會逐一重繪，捲動延遲明顯；
/// 開 DoubleBuffered (= OptimizedDoubleBuffer + AllPaintingInWmPaint)
/// 改成繪到後備緩衝再一次貼上前景，避免閃爍 + 加速。
/// 注意不要加 UserPaint — container 加了會不畫子控件。
/// </summary>
internal class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
    }
}
