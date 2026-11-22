using Avalonia;
using Avalonia.Controls;

namespace ComicOrganizer.Avalonia.Views.Controls;

/// <summary>
/// 虛擬化清單（ItemsRepeater）卡片的外層容器：只把「非 null」的 DataContext 轉給內層卡片。
/// <para>
/// ItemsRepeater 回收卡片時一律把 DataContext 設成 null（ViewManager.ClearElementToElementFactory，無法關閉），
/// 重用時再設回新的項目。若直接作用在卡片上，每張卡片的繫結會在 null 時全部報錯、按鈕的命令繫結
/// 讓「可否點擊」狀態傳遍子元件、觸發樣式重套與轉場動畫，重用時又反向做一次。
/// dotnet-trace 實測這串工作佔拖捲軸時 UI 執行緒的四到五成。
/// </para>
/// <para>
/// 這裡忽略 null，讓卡片直接從舊項目換成新項目（沒有中間的 null 狀態）。代價是被回收、還沒重用的卡片
/// 在畫面外繼續指向舊項目，直到下次被重用。
/// </para>
/// </summary>
public sealed class StableDataContextHost : Decorator
{
    // 已以「本地值」設給內層卡片的項目。必須設成本地值才能擋住外層 null 往內繼承；
    // 不能只比對 child.DataContext，因為第一次轉送前它是從外層繼承來的同一個物件。
    private object? _forwarded;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ChildProperty)
        {
            _forwarded = null;
            ForwardDataContext();
        }
        else if (change.Property == DataContextProperty)
        {
            ForwardDataContext();
        }
    }

    private void ForwardDataContext()
    {
        var child = Child;
        var data = DataContext;
        if (child is null || data is null)
        {
            return;
        }

        if (!ReferenceEquals(_forwarded, data))
        {
            _forwarded = data;
            child.DataContext = data;
        }
    }
}
