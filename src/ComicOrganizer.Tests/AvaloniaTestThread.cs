using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace ComicOrganizer.Tests;

/// <summary>
/// 專用 Avalonia UI 測試執行緒。
/// Avalonia 12 的 Dispatcher.UIThread 歸屬「第一個存取 Dispatcher 的執行緒」，且控制項（如 Transitions）會 VerifyAccess。
/// 測試組件載入時先由這條執行緒取得 UIThread 並執行 Dispatcher 迴圈：
/// - VM 以 Dispatcher.UIThread.InvokeAsync 排入的工作會在此執行（不會卡住）
/// - 需要建立 / 操作 View 的測試透過 <see cref="Run(Action)"/> 送到這裡執行
/// </summary>
internal static class AvaloniaTestThread
{
#pragma warning disable CA2255 // 測試組件需在任何測試前先綁定 UIThread
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        using var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.UIThread;
            ready.Set();
            dispatcher.MainLoop(CancellationToken.None);
        })
        {
            IsBackground = true,
            Name = "AvaloniaTestUiThread",
        };
        thread.Start();
        ready.Wait();
    }

    public static void Run(Action action)
    {
        Dispatcher.UIThread.Invoke(action);
    }
}
