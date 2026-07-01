# ComicOrganizer

本地**漫畫 / 圖集庫管理器**，C# WinForms 應用程式。從早期的「自動歸檔工具」長成完整的本地漫畫庫：智慧封面提取、模糊匹配自動歸檔、自我學習關鍵字對應目錄、SQLite metadata catalog 與縮圖快取、檔名 metadata 解析、nhentai 外部 metadata 同步、EhTag 標籤翻譯、內建閱讀器（OCR + LLM 翻譯）、搜尋 DSL 與虛擬庫。

## 功能特色

### 漫畫庫管理
- 樹狀導覽（Folder / 依作者 / 依標籤 / 我的庫 / 閱讀軌跡多模式）+ 多分頁卡片瀏覽
- 三種卡片尺寸（小 / 中 / 大）+ 列表模式，**每個 Tab 獨立檢視模式**
- 智慧封面提取：資料夾首圖 / `cover.*` 命名 / 壓縮檔（zip/cbz/rar/7z，走 SharpCompress）內首圖（受 `MaxScanDepth` 控制遞迴）
- 列表模式 hover 大圖預覽（floating popup，跟隨滑鼠）
- 右側詳情抽屜：作品資訊 / 作者資訊面板
- 支援 WebP（透過 SixLabors.ImageSharp，補 GDI+ 不認 webp 的洞）

### 自動歸檔
- 兩階段比對：
  1. 自我學習關鍵字 → 目錄表（`MappingIntelligence`，持久化於 `mappings.json`）
  2. 否則用 `FuzzySharp.WeightedRatio` + 智能加權（完全包含 95、關鍵字命中 90）
- 黑名單規則排除雜訊（"DL版"、"AI Generated"、Comic Market 編號 `C\d{2,3}` 等）
- 預覽介面分群顯示（依目標目錄）+ 信心分數排序，使用者確認後才搬檔（支援跨磁碟卷移動）
- 「使用者確認的成功移動」進入學習庫，下次更聰明

### Metadata / Catalog
- SQLite metadata catalog（`cache.db`，Dapper + Microsoft.Data.Sqlite）+ 縮圖快取（`thumbs/`）
- 檔名 / 資料夾名 metadata 解析（event / author / title / parody / language / 翻譯組）
- 資料夾層級角色推測（Root / Author / Series / Work / Chapter / Image）
- 完整重建索引 / 增量對帳（偵測新增・刪除・移動）/ 規則變更後重解析
- nhentai 外部 metadata 同步（單本 / 作者批量，原始回應入庫避免重複呼叫，DPAPI 加密 API key）
- EhTag 標籤翻譯（nhentai type/slug → 中文，簡體經 OpenCC 轉繁；背景檢查 GitHub release 更新）

### 內建閱讀器
- 來源：資料夾 / 壓縮檔（rar/7z，SharpCompress）/ PDF（PDFtoImage）
- 單頁 / 雙頁 / 連續捲軸（RTL/LTR）版面、書籤、閱讀進度與計數
- LRU + 鄰頁預載、壓縮檔背景預解、雙緩衝繪製
- **可選 SkiaSharp / GPU（OpenGL）渲染**（並存於 GDI+，設定開關切換、GL 失敗自動退回）
- **OCR → 翻譯 → 重繪管線**：
  - OCR：PaddleOCR（離線）/ Windows 原生 OCR，可動態切換
  - 翻譯：LLM（OpenAI-compatible / Ollama / Gemini / OpenRouter）日譯繁中
  - 精準抹除原文：comic-text-detector ONNX 像素級文字遮罩（DirectML GPU 推論）
  - flood-fill 量氣泡輪廓自排版、直排避免詞中斷行、內嵌獅尾黑體

### 搜尋 / 虛擬庫
- 搜尋 DSL：`artist:` / `tag:` / `language:` / `read:` 欄位、`-` 否定，未知 field 退化純文字
- 使用者存的 DSL query → 動態虛擬庫，結果即時跑不快取

### 系統設定
- Light / Dark / System 三主題切換（即時生效）+ 多語介面（繁中 / English）
- 路徑設定 + 外部圖片瀏覽器 + 啟動參數模板（`{path}` 佔位符）
- 閱讀器設定（OCR 引擎、翻譯端點、GPU 渲染開關等）
- 敏感值（nhentai key）以 Windows DPAPI（CurrentUser）加密儲存

## 技術堆疊

| 類別 | 套件 / 技術 |
|---|---|
| Runtime | .NET 8（`net8.0-windows10.0.19041.0`，僅 Windows）|
| UI 框架 | WinForms + [AntdUI 2.3.12](https://github.com/AntdUI/AntdUI)（Ant Design 風格元件庫）|
| 圖檔解碼 | System.Drawing.Common + SixLabors.ImageSharp（WebP）|
| GPU 渲染 | SkiaSharp + SkiaSharp.Views.WindowsForms（OpenGL，可選）|
| 資料庫 | SQLite（Microsoft.Data.Sqlite + Dapper）|
| 壓縮檔 | SharpCompress（ZIP/CBZ/RAR/7z）|
| PDF | PDFtoImage |
| OCR | PaddleOCRSharp + Paddle.Runtime.win_x64 / Windows 原生 OCR |
| 文字遮罩 | comic-text-detector（ONNX，Microsoft.ML.OnnxRuntime.DirectML）|
| 簡繁轉換 | OpenCCNET |
| 模糊比對 | FuzzySharp |
| 測試 | xUnit |

## 建置與執行

```bash
# 建置整個 solution
dotnet build src/ComicOrganizer.slnx

# 執行 WinForms 應用
dotnet run --project src/ComicOrganizer.App/ComicOrganizer.App.csproj

# 執行所有測試
dotnet test src/ComicOrganizer.Tests/ComicOrganizer.Tests.csproj

# 執行單一測試（依方法名）
dotnet test src/ComicOrganizer.Tests/ComicOrganizer.Tests.csproj --filter "FullyQualifiedName~Test_JapaneseAuthorLearning_ShouldWork"

# Release 發行（單一 exe，framework-dependent，需目標機有 .NET 8 runtime）
dotnet publish src/ComicOrganizer.App/ComicOrganizer.App.csproj -c Release
```

> ⚠️ 目標 framework `net8.0-windows10.0.19041.0`（WinForms + Windows 10 SDK API，如原生 OCR），只能在 **Windows** 上 build / run。

## 專案結構

```
src/
├── ComicOrganizer.slnx
├── ComicOrganizer.App/
│   ├── Core/
│   │   ├── ArchiveOrganizer.cs        # 兩階段比對 + 智能評分
│   │   ├── MappingIntelligence.cs     # 學習庫（mappings.json）
│   │   ├── CoverManager.cs            # 封面提取（資料夾/壓縮檔/單檔）
│   │   ├── FilenameMetadataParser.cs  # 檔名 metadata 解析
│   │   ├── FolderRoleDetector.cs      # 資料夾層級角色推測
│   │   ├── SqliteAssetMetaStore.cs    # cache.db catalog（Dapper）
│   │   ├── Settings/                  # ConfigManager / 偏好 / DPAPI 加密
│   │   ├── Cache/                     # 縮圖庫 / 背景寫入 / 重建・對帳服務
│   │   ├── Reader/                    # 閱讀器來源 / OCR / 翻譯 / 文字遮罩 / 排版
│   │   ├── Translation/               # EhTag 標籤翻譯
│   │   ├── ExternalSync/              # nhentai metadata 整合
│   │   ├── Search/                    # 搜尋 DSL parser / executor
│   │   ├── VirtualLibrary/            # 動態虛擬庫
│   │   └── Theming/                   # 主題 / 多語 / IThemedControl
│   ├── Models/                        # AppConfig / UserPreferences / 資料模型
│   ├── UI/
│   │   ├── MainForm.*                 # 主視窗（menuStrip + contentHost）
│   │   ├── UserControl/GalleryControl.*  # 漫畫庫管理（多 partial）
│   │   ├── ArchiveControl.*           # 自動歸檔
│   │   ├── MetadataManageControl.*    # 詮釋資料管理
│   │   ├── ReaderForm.*               # 內建閱讀器（含 GPU partial）
│   │   ├── SettingsForm.*             # 設定
│   │   ├── PreviewForm.*              # 歸檔預覽 + 確認
│   │   └── …（各式 dialog / picker）
│   ├── Resources/                     # 翻譯庫 / 內嵌字體 / 繁體詞表
│   └── Properties/                    # Resources.resx（zh-TW / en-US）
└── ComicOrganizer.Tests/             # xUnit（歸檔・解析・快取・外部・翻譯・搜尋・閱讀器）
```

## 資料持久化（皆在 exe 同層 `BaseDirectory`）

| 路徑 | 內容 |
|---|---|
| `appsettings.json` | `AppConfig`（路徑、外部 viewer、含 DPAPI 加密的 nhentai key）|
| `preferences.json` | `UserPreferences`（主題 / 語言 / reader 偏好）|
| `mappings.json` | 學習得到的「關鍵字 → 目錄名」對應 |
| `cache.db` | SQLite metadata catalog（asset / metadata / author / tag / bookmark / 虛擬庫 / 外部來源…）|
| `thumbs/` | 縮圖檔（雜湊命名）|
| `translations/db.text.json.gz` | EhTag 翻譯庫 disk cache（無則用內嵌）|
| `inference/` | PaddleOCR 模型檔、comic-text-detector ONNX |

## Roadmap

- [ ] 歸檔衝突選擇介面
- [ ] 歸檔 **Undo**
- [ ] 搬檔前**空間檢查**
- [ ] FileSystemWatcher 增量更新（不必整 lib 重掃）

## 授權

本專案採 **[PolyForm Noncommercial License 1.0.0](LICENSE)** —— 可自由用於**個人、非商業**用途（使用 / 複製 / 散布 / 修改），但**不授予商業使用**。

⚠️ 本專案另含多項第三方資源，部分帶有額外限制（**NonCommercial** 或**個人／學術限定**），因此**整體僅供個人非商業用途**：

- **comic-text-detector**（文字遮罩 ONNX 模型）— 源碼 GPL-3.0；訓練自 Manga109-s，**僅限個人／學術、不得散布** → 故採**按需下載、不隨 repo 散布**。
- **EhTagTranslation Database**（標籤翻譯庫）— CC BY-NC-SA 4.0（**非商業**）。
- **獅尾黑體 Medium**（譯文字體，思源黑體衍生）— SIL OFL 1.1。
- **jieba-tw 繁體詞表** — MIT。
- **PaddleOCR 模型 / 各 NuGet 套件** — 詳見授權清單。

完整第三方授權與出處見 **[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)**。
