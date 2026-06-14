# ComicOrganizer 專案概觀

這是一個用於整理漫畫、圖集資料夾並提供本地閱讀體驗的工具專案。**目前主程式為 `src/` 下的 C# WinForms 應用程式**，已具備自動歸檔、SQLite 索引、metadata 解析、外部資料同步、標籤翻譯與內建閱讀器（含 OCR + LLM 翻譯）等功能。早期的 PowerShell 腳本（`script/圖片整理.ps1`）已退為 **legacy 規格參考**，不再擴充。

> 開發細節（建置指令、完整架構地圖、Core 各子系統、測試、常見坑）請以 **`CLAUDE.md`** 為準；本文件只做高層概觀與路線圖。

## 專案結構

- `src/`（**開發主力**，solution `ComicOrganizer.slnx`，`net8.0-windows`）
  - `ComicOrganizer.App/`：WinForms 應用。`Core/` 收斂所有業務邏輯（歸檔比對、封面/快取、metadata 解析、外部同步、翻譯、閱讀器、搜尋、虛擬庫），`UI/` 為 AntdUI 介面，`Models/` 為設定與資料模型。
  - `ComicOrganizer.Tests/`：xUnit 測試。
- `script/`（**legacy，僅供參考**）
  - `圖片整理.ps1`：最初的命令列腳本，含封面重命名、封面提取及自動歸檔邏輯。

## 核心功能說明

### 1. 封面管理
- **封面提取**：從子目錄或壓縮檔（Zip/Rar/7z，C# 端統一走 SharpCompress）自動提取第一張圖片作為目錄封面；若目錄內已有 `cover.*` 則優先採用。提取結果寫入 SQLite + `thumbs/` 縮圖快取。
- **命名統一（legacy）**：把子目錄圖檔統一改名為 `cover.ext` 是 **PowerShell 腳本**的行為；C# 的 `CoverManager` 只讀取既有 `cover.*`，不改名。

### 2. 未整理檔案歸檔（已 GUI 化）
- 掃描來源目錄，透過標題中的 `[關鍵字]` 等括號內容自動匹配目標庫路徑。
- C# 端為兩階段比對：先查自我學習知識庫（`mappings.json`），再以模糊匹配（FuzzySharp / WeightedRatio）計分；經「歸檔預覽清單」確認後才搬檔，成功的移動才回寫知識庫。

### 3. C# 閱讀器內建翻譯與 OCR 引擎（已實作）
- **多引擎 OCR 支援**：
  - 整合了 **Windows Native OCR** 與 **PaddleOCRSharp（PP-OCRv6 small det/rec + PP-OCRv5 cls 模型）**，可 100% 離線、免額外下載直接運行。
  - 支援在「系統設定 → 閱讀器」設定介面中動態切換 OCR 引擎。
- **段落合併與局部重繪**：
  - 自動針對跨行、或因氣泡斷開的文字進行段落合併（`MergeBlocks`）。
  - 重繪時僅針對原本的多個子氣泡區域進行局部塗白遮罩（Masking），並在大外接矩形中排版繪製繁體中文譯文，最大化保留插圖背景。
- **狀聲詞/雜訊 LLM 過濾**：
  - 透過調優 LLM System Prompt 與 Few-Shot 範例，使 LLM 將無對話意義的擬聲詞、狀聲詞與辨識雜訊（例如 `nー`, `vhah`, `ちる…`）翻譯為空字串 `""`。
  - 渲染引擎會自動忽略空譯文區塊（不塗白、不重繪），完美保留漫畫手繪狀聲詞字型。

## 開發方向與路線圖 (Roadmap)

### 已完成（早期路線圖目標，現已落地）
- ✅ **C# 視覺化轉型**：GUI 介面（`MainForm` + 三大 UserControl）、視覺化路徑配置、歸檔預覽清單（`PreviewForm`，搬檔前可逐項略過/修正）、多執行緒掃描（async + `CacheWriter` 背景隊列，不卡 UI）。
- ✅ **智慧匹配**：引入模糊匹配（FuzzySharp）提高關鍵字辨識率；黑名單 regex（`AppConfig.BlacklistPatterns`）過濾雜訊。
- ✅ **本地閱讀器 + OCR/翻譯**（見上節）。

### 進行中／後續延伸
- 自我學習知識庫、SQLite metadata catalog、nhentai 外部 metadata 同步、EhTag 標籤翻譯、搜尋 DSL 與虛擬庫等（細節見 `CLAUDE.md`）。
- **多規則提取**：目前 `ExtractAllKeywords` 的抽取 regex 仍寫死，尚未開放使用者自定義作者/系列辨識規則。

### 尚未實作
- **衝突處理**：當一個檔案符合多個目標目錄時，提供選擇介面。
- **操作安全**：復原（Undo）記錄、移動前空間檢查。

## 使用與配置 (Legacy PowerShell)

> 以下僅適用於 `script/圖片整理.ps1` legacy 腳本；C# 應用的設定改存於 exe 同層的 `appsettings.json`（由「系統設定」頁管理）。

### 執行方式
```powershell
.\script\圖片整理.ps1
```

### 關鍵配置
- `$UnorganizedDirectory`: 來源路徑。
- `$RootDirectory`: 目標庫路徑。
- `$Debug`: 設為 `$true` 可進行模擬測試。
