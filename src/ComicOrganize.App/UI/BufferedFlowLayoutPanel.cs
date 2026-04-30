namespace ComicOrganize.App.UI;

/// <summary>
/// FlowLayoutPanel 子類別，啟用 DoubleBuffered 減少捲動抖動。
/// 注意：曾試 WS_EX_COMPOSITED (整個容器 composite memory)，雖然消除閃爍但
/// 會強制每 frame 全 composite，對 child 多+各自有自繪 (AntdUI.Panel/Image3D) 的
/// 場景反而拖慢流暢度。最終只保留 DoubleBuffered。
/// 不要加 ControlStyles.UserPaint — container 加了會不畫子控件。
/// </summary>
internal class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
    }
}
