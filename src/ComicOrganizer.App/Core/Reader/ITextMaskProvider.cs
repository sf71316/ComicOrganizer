using System.Collections.Generic;
using System.Drawing;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 「文字/氣泡遮罩」提供者抽象 —— 比照 <c>IOcrService</c> 的可抽換策略模式。
/// 不同實作可抽換（設定切換）：
/// - <see cref="ComicTextDetector"/>：路線 2，ONNX 文字偵測（只用整頁圖）。
/// - （未來）flood-fill 輕量實作：路線 A（用 ocrBlocks 當種子）。
/// - 未來別的 ML 模型：再實作一個即可。
/// 遮罩交給共用的 <see cref="TextEraser"/> 抹除原文。
/// </summary>
public interface ITextMaskProvider
{
    /// <summary>是否可用（如模型檔存在）。不可用時呼叫端應 fallback 回現行行為。</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 取得整頁文字遮罩，長度 W*H 的 byte 陣列（1=文字/要抹除、0=其他）。無法處理時回 null（呼叫端 fallback）。
    /// </summary>
    /// <param name="page">整頁原圖。</param>
    /// <param name="ocrBlocks">OCR 文字框；flood-fill 類實作當種子用，CTD 類實作可忽略。</param>
    byte[]? GetTextMask(Image page, IReadOnlyList<OcrBlock> ocrBlocks);
}
