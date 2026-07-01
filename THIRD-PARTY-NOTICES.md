# 第三方資源授權聲明 (Third-Party Notices)

本專案（ComicOrganizer，採 PolyForm Noncommercial License 1.0.0）使用了下列第三方資源。
各資源的著作權歸其原作者所有，並依其各自的授權條款散布。**部分資源帶有額外限制
（NonCommercial 非商業、或僅限個人／學術用途），請務必遵守。**

> ⚠️ 重點：因下列資源含 **NonCommercial（CC BY-NC-SA）** 與 **個人／學術限定** 條款，
> 本專案整體**僅供個人、非商業用途**，請勿用於任何商業目的。

---

## 一、隨原始碼一併散布（bundled in this repository）

### 1. EhTagTranslation Database（標籤翻譯庫）
- **用途**：nhentai 標籤 → 中文翻譯（簡體經 OpenCC 轉繁）。
- **檔案**：`src/ComicOrganizer.App/Resources/db.text.json.gz`（內嵌資源）。
- **來源**：https://github.com/EhTagTranslation/Database
- **授權**：**Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)**
  https://creativecommons.org/licenses/by-nc-sa/4.0/
- **限制**：須標示出處（BY，即本聲明）、**僅限非商業使用（NC）**、對其衍生需以相同授權釋出（SA）。
  本專案僅原樣（verbatim）內嵌散布，未對資料庫內容進行改作。

### 2. 獅尾黑體 Medium（Swei Sans CJK TC Medium，譯文中黑體）
- **用途**：閱讀器譯文排版字體。
- **檔案**：`src/ComicOrganizer.App/Resources/fonts/SweiSansCJKtc-Medium.ttf`
- **來源**：https://github.com/max32002/swei-sans （思源黑體 / Source Han Sans 衍生）
- **授權**：**SIL Open Font License 1.1 (OFL-1.1)** — 授權全文見
  `src/ComicOrganizer.App/Resources/fonts/SIL_Open_Font_License_1.1.txt`
- **限制**：可隨軟體 bundle／散布；保有保留字型名稱（Reserved Font Name），不得單獨販售字型本體。

### 3. jieba-tw 繁體常用詞表
- **用途**：直排譯文避免詞中斷行（最大匹配）。
- **檔案**：`src/ComicOrganizer.App/Resources/dict/zh_tw_words.txt.gz`（取詞頻 ≥ 10）
- **來源**：https://github.com/APCLab/jieba-tw
- **授權**：**MIT License** — 授權全文見
  `src/ComicOrganizer.App/Resources/dict/jieba-tw-LICENSE.txt`

---

## 二、執行時按需下載、**不隨原始碼散布**（NOT bundled）

### 4. comic-text-detector（漫畫文字偵測 ONNX 模型）
- **用途**：閱讀器「精準抹除原文」的像素級文字遮罩。
- **取得方式**：由使用者在設定中開啟功能時，**按需下載**至 `inference/`；本 repository **不包含**此模型檔。
- **原始碼**：https://github.com/dmMaze/comic-text-detector （**GPL-3.0**）
- **ONNX 重打包**：https://huggingface.co/mayocream/comic-text-detector-onnx
- **限制（重要）**：模型訓練自 **Manga109-s** 資料集，依其條款**僅限個人／學術研究用途、不得再散布**。
  正因如此，本專案才採「按需下載」而非內嵌，避免散布該模型。
- **備註**：本專案內 `ComicTextDetector.cs` 僅為呼叫 ONNX Runtime 推論的介接程式（letterbox／門檻／膨脹等
  通用前後處理），並未複製上游 GPL 原始碼。

### 5. PaddleOCR 模型（PP-OCRv6 / PP-OCRv5）
- **用途**：閱讀器離線 OCR。
- **取得方式**：放置於 `inference/`，本 repository 不包含模型檔。
- **來源 / 授權**：PaddleOCR（https://github.com/PaddlePaddle/PaddleOCR）— **Apache License 2.0**。

---

## 三、透過 NuGet 取得的相依套件

下列套件於建置時由 NuGet 還原，**本 repository 未重新散布其二進位檔**；各依其授權條款使用：

| 套件 | 授權 |
|---|---|
| AntdUI | Apache-2.0 |
| SkiaSharp / SkiaSharp.Views.WindowsForms | MIT |
| Microsoft.ML.OnnxRuntime.DirectML | MIT |
| PaddleOCRSharp / Paddle.Runtime.win_x64 | Apache-2.0 |
| Microsoft.Data.Sqlite / Dapper | MIT / Apache-2.0 |
| SharpCompress | MIT |
| SixLabors.ImageSharp (3.x) | Six Labors Split License（開源／低營收使用免費，商業用途另需付費授權）|
| PDFtoImage | MIT |
| OpenCCNET | MIT |
| FuzzySharp | MIT |
| Newtonsoft.Json | MIT |
| System.Drawing.Common | MIT |

> 各套件之實際授權以其 NuGet 套件內所附條款為準；如有出入，以套件方為準。

---

如發現任何授權標示有誤或遺漏，請開 issue 告知，將儘速更正。
