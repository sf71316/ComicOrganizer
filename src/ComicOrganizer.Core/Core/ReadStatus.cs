namespace ComicOrganizer.App.Core;

/// <summary>
/// 漫畫閱讀狀態。SQLite 用 INTEGER 儲存（搭配 schema 的 read_status 欄位）。
/// 數字編碼明確指定，避免將來重排造成資料錯位。
/// </summary>
public enum ReadStatus
{
    Unread    = 0,
    Reading   = 1,
    Completed = 2,
    Dropped   = 3,
}
