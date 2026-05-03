# WebP 影像處理效能優化報告

本文件記錄了針對 WebP 格式影像載入流程的優化實作與效能對比結果。

## 1. 現狀與問題 (Problem Statement)

在原始實作中，由於 GDI+ (System.Drawing) 原生不支援 WebP 解碼，我們使用了 `SixLabors.ImageSharp` 作為解碼器。然而，在將解碼後的像素資料轉換為 GDI+ `Bitmap` 時，存在以下效能瓶頸：

- **額外的記憶體配置 (Memory Allocation)**：舊方法會配置一個 `byte[]` 緩衝區來中轉像素資料。對於 4K 解析度影像，單次載入需額外配置約 **31.6 MB**。
- **重複的記憶體拷貝 (Data Copying)**：像素資料需經過 `ImageSharp -> byte[] -> GDI+ Bitmap` 兩次拷貝。
- **GC 壓力**：快速瀏覽圖庫時，大量暫存陣列會頻繁觸發垃圾回收 (GC)，導致介面卡頓。

## 2. 優化方案 (Solution)

我們實作了 **零配置記憶體直接拷貝 (Zero-Allocation Direct Pixel Copy)** 方案：

1. **啟用 Unsafe 支援**：在專案中允許 `unsafe` 程式碼區塊。
2. **直接存取記憶體**：利用 GDI+ 的 `LockBits` 取得 `Bitmap` 的記憶體首位址指標 (`Scan0`)。
3. **消除中轉緩衝區**：使用 ImageSharp 的 `ProcessPixelRows` API，透過 `unsafe` 指標將像素資料從解碼器的內部緩衝區直接寫入 GDI+ 的記憶體。
4. **支援 Stride 對齊**：手動處理像素列的 Stride 偏移，確保所有尺寸的影像皆能正確顯示。

## 3. 效能實測對比

測試環境：4K 解析度影像 (3840x2160)，執行 20 次取平均值。

| 指標 | 原本方式 (byte[] 中轉) | 改善方式 (Unsafe 直接拷貝) | 效益提升 |
| :--- | :--- | :--- | :--- |
| **平均處理時間** | **15.18 ms** | **9.65 ms** | **1.57x 速度提升** |
| **記憶體配置 (Alloc)** | **31.64 MB** | **0.00 MB** | **100% 消除暫存配置** |

### 關鍵結論：
- **時間縮短 36%**：減少一次完整記憶體拷貝後，單張影像處理速度顯著提升。
- **記憶體零負擔**：在高解析度情境下，每張影像節省了超過 30MB 的 Heap 配置，這在大量圖庫預覽時能徹底消除因 GC 引起的卡頓感。

## 4. 相關檔案
- **核心實作**：`src/ComicOrganizer.App/Core/CoverManager.cs` 中的 `ConvertImageSharpToBitmap` 方法。
- **測試驗證**：`src/ComicOrganizer.Tests/WebpOptimizationTest.cs`。

## 5. 後續建議
- 目前此優化僅套用於 WebP 格式（因為必須經過 ImageSharp）。對於 JPEG/PNG，GDI+ 的 `Image.FromFile` 已經非常高效，維持現狀即可。
- 未來若有跨平台需求而全面轉向 ImageSharp 時，此 `unsafe` 轉換邏輯將成為圖庫流暢度的核心基礎。
