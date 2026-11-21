using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace ComicOrganizer.Avalonia.Views.Controls;

/// <summary>
/// 保留頁面的切頁容器：每個頁面 ViewModel 只建立一次 View，之後切頁只切換顯示，不再銷毀重建
/// （原本 SukiSideMenu 每次切頁都經 ViewLocator 重建整頁，卡片多時要 1~14 秒）。
/// 切頁動畫比照 SukiUI 的 SukiTransitioningContentControl：舊頁淡出、新頁淡入同時進行，各 250ms、線性；
/// 新頁淡入完成前不接受點擊，與原本行為一致。
/// </summary>
public sealed class KeepAlivePageHost : Panel
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(250);

    private readonly Dictionary<object, Control> _pages = new(ReferenceEqualityComparer.Instance);
    private readonly IDataTemplate _viewLocator = new ViewLocator();
    private readonly Stopwatch _fadeClock = new();

    private Control? _current;
    private Control? _fadingOut;
    private bool _fadeRunning;

    /// <summary>目前顯示中的頁面 ViewModel。</summary>
    public object? CurrentPage { get; private set; }

    /// <summary>切到指定頁面；第一次進入時才建立 View。</summary>
    public void ShowPage(object? pageViewModel)
    {
        if (pageViewModel is null || ReferenceEquals(pageViewModel, CurrentPage))
        {
            return;
        }

        if (!_pages.TryGetValue(pageViewModel, out var view))
        {
            view = _viewLocator.Build(pageViewModel) ?? new TextBlock { Text = "找不到檢視" };
            view.DataContext = pageViewModel;
            view.IsVisible = false;
            _pages[pageViewModel] = view;
            Children.Add(view);
        }

        // 連續快速切頁：上一段動畫中、既不是舊頁也不是新頁的頁面，直接收起
        foreach (var child in Children)
        {
            if (!ReferenceEquals(child, view) && !ReferenceEquals(child, _current))
            {
                HidePage(child);
            }
        }

        _fadingOut = _current;
        _current = view;
        CurrentPage = pageViewModel;

        if (_fadingOut is not null)
        {
            _fadingOut.IsHitTestVisible = false;
            _fadingOut.ZIndex = 0;
        }

        view.ZIndex = 1;
        view.Opacity = 0;
        view.IsHitTestVisible = false;
        view.IsVisible = true;

        // 起點以當下透明度換算，快速連切時舊頁從目前的透明度繼續淡出，不會閃一下
        _fadeClock.Restart();
        if (!_fadeRunning)
        {
            _fadeRunning = true;
            RequestFrame();
        }
    }

    private void RequestFrame()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            // 尚未掛上視窗（啟動初期）：直接完成，不做動畫
            CompleteFade();
            return;
        }

        top.RequestAnimationFrame(OnFadeFrame);
    }

    private void OnFadeFrame(TimeSpan _)
    {
        double t = Math.Clamp(_fadeClock.Elapsed.TotalMilliseconds / FadeDuration.TotalMilliseconds, 0, 1);
        if (_current is not null)
        {
            _current.Opacity = t;
        }

        if (_fadingOut is not null)
        {
            _fadingOut.Opacity = Math.Min(_fadingOut.Opacity, 1 - t);
        }

        if (t >= 1)
        {
            CompleteFade();
            return;
        }

        RequestFrame();
    }

    private void CompleteFade()
    {
        _fadeRunning = false;
        _fadeClock.Stop();

        if (_fadingOut is not null)
        {
            HidePage(_fadingOut);
            _fadingOut = null;
        }

        if (_current is not null)
        {
            _current.Opacity = 1;
            _current.IsHitTestVisible = true;
        }
    }

    // 註：曾實驗把隱藏頁從畫面樹移除（View 物件保留），但重新掛回時 Avalonia 要重新套用整頁樣式，
    // 實測切回要 1.7~2.4 秒，比重建還慢，因此維持「掛著但隱藏」。
    private static void HidePage(Control page)
    {
        page.IsVisible = false;
        page.IsHitTestVisible = false;
        page.Opacity = 1;
        page.ZIndex = 0;
    }
}
