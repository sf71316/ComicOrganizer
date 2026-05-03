namespace ComicOrganizer.App.UI;

/// <summary>內建 reader 縮放模式。FitPage 預設；Custom 由 Ctrl+wheel 觸發。</summary>
public enum ReaderZoomMode
{
    FitPage,    // 適合視窗（保持比例完整顯示）
    FitWidth,   // 適合寬度（垂直可捲）
    Actual,     // 100%
    Custom,     // 使用者自訂百分比
}
