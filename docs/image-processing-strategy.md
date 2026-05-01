# 影像處理策略

封面 / 縮圖載入路徑的決策依據。修改 `CoverManager` 前先讀這份。

## TL;DR

- **JPEG / PNG 走 GDI+**（`System.Drawing.Image.FromFile` + `Graphics.DrawImage`）
- **WebP 走 ImageSharp**（GDI+ 無 codec），但要修 PNG round-trip 改用 LockBits 直接拷貝 pixel
- **取尺寸用 `ImageSharp.Image.Identify`**（53× 比 GDI+ 快）—— 未來 cache、layout 預先計算等場景用
- **不全面改 ImageSharp**：對中等大小 JPEG，GDI+ 已經夠快，DCT 優勢甜蜜點窄

## 為什麼不全面改 ImageSharp

跟直覺相反。實測（測試圖：1045×1500 JPEG / 500KB，i7 8 core）：

| Target 寬 | GDI+ | ImageSharp 全解 | ImageSharp + DCT |
|---|---|---|---|
| 170 (Small) | **11.97 ms** | 17.50 ms | 13.18 ms |
| 250 (Medium) | 12.77 ms | 11.12 ms | **7.05 ms** ← 甜蜜點 |
| 330 (Large) | 13.22 ms | 11.77 ms | **10.62 ms** |

GDI+ 用 Windows 原生 codec，對中等 JPEG 已經 ~13ms。ImageSharp 純 managed 必須有殺手級理由才划算——那個理由是 **JPEG DCT pre-scaling**，但只有目標寬度落在 source 的 1/2、1/4、1/8 整數倍附近時才大贏。

### DCT 甜蜜點原理

JPEG 編碼是 8×8 DCT block，解碼可以**只解 1/2、1/4、1/8 解析度**，工作量降 4×、16×、64×。對 1045 寬的 source：

| Target | 最近 DCT scale | DCT 後尺寸 | 還要 resize | 結果 |
|---|---|---|---|---|
| 170 | 1/8 → 130 太小，得用 1/4 | 261 | 縮 35% | 中等 |
| **250** | **1/4 = 261** | 261 | 幾乎不縮 | **最快** |
| 330 | 1/4 = 261 太小，用 1/2 | 522 | 縮 37% | 中等 |

→ ImageSharp DCT 只有在「target 寬度 ≈ source/4」時大勝 GDI+。

對 2000~3000 寬的大圖（H 漫常見），target 250 對應 1/8 scale，可能更賺；待實測。

## Identify 是隱藏寶藏

```
GDI+ Image.FromFile + img.Width  : 6.14 ms （仍完整解碼）
ImageSharp.Image.Identify(path)  : 0.11 ms （53× 更快）
```

`Image.Identify` 只讀 JPEG/PNG/WebP header，不解碼像素。適用場景：
- **Cache 查詢**：先取尺寸跟 mtime 算 cache key，命中就不解碼
- **Layout 預先計算**：知道圖比例後再排版，避免閃動
- **格式偵測**：避免依副檔名猜（mis-named .jpg 其實是 webp 的情況）

## 各格式分流策略

```
LoadAndResizeImage(path, maxWidth)
  ext = .webp                 → ImageSharp（唯一選項）
  ext = .jpg / .jpeg / .png   → GDI+（夠快）
  ext = unknown               → ImageSharp.Image.Identify 偵測 + 對應路徑
```

WebP 路徑**現況有 bug**——透過 PNG encode + Bitmap 重解碼，等於做兩次工：

```csharp
// 現況（CoverManager.cs ConvertImageSharpToBitmap）
imgSharp.Save(ms, new PngEncoder());   // ← 浪費
return new Bitmap(ms);                  // ← PNG 又重解
```

正確做法：直接 `LockBits` 拷貝 pixel。需要 `unsafe` block 或 `Marshal.Copy`：

```csharp
using var imgSharp = SixLabors.ImageSharp.Image.Load<Bgra32>(path);
var bmp = new Bitmap(imgSharp.Width, imgSharp.Height, PixelFormat.Format32bppArgb);
var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
unsafe
{
    var span = new Span<byte>((byte*)bmpData.Scan0, bmpData.Stride * bmp.Height);
    imgSharp.CopyPixelDataTo(span);
}
bmp.UnlockBits(bmpData);
```

## 何時重評估

下列任一觸發時重跑 benchmark 並考慮全面改 ImageSharp：

1. **真實大尺寸漫畫測下來 DCT 勝率 ≥ 2×**：用 H 漫資料夾（封面常 2000+ 寬）跑 `CoverDecodePerfTest`，看 DCT 對 250 target 是否進一步加速
2. **實作 #44 SQLite cache 庫**：cache 命中流程必用 `Image.Identify`；同時改解碼路徑增加邊際成本不大
3. **跨平台需求**：若要 Linux/macOS，GDI+ `System.Drawing.Common` 在非 Windows 已 deprecated，必須換 ImageSharp

## 不做什麼

- ❌ 為了統一 codepath 全面改 ImageSharp（無實測支持的優化）
- ❌ 把 GDI+ 路徑加 try/catch fallback ImageSharp（雙倍 dispose 風險，行為難測）
- ❌ 把 `Image.Identify` 用在熱 path 取代 GDI+ decode（目的不對——Identify 是給「不需要 pixel」的場景用）

## 相關檔案

- `src/ComicOrganizer.App/Core/CoverManager.cs` — 解碼 + resize 主邏輯
- `src/ComicOrganizer.Tests/CoverDecodePerfTest.cs` — benchmark，重跑用
