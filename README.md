# ComicOrganizer

本地**漫畫 / 圖集庫管理器與閱讀平台**，主力桌面程式為基於 **.NET 8 + Avalonia 12 + SukiUI 7** 打造的現代化 UI（`ComicOrganizer.Avalonia`），核心業務邏輯抽離為不依賴 Windows UI 的 `ComicOrganizer.Core`，並整合獨立「圖搜漫」向量搜尋模組 `ComicOrganizer.VisualSearch`。

> 💡 早期 WinForms 版（`ComicOrganizer.App`）仍保留於 solution 中供維護與相容參考，不再擴充新功能；最初的 PowerShell 腳本（`script/圖片整理.ps1`）則已退為歷史規格參考。

---

## 功能特色

### 1. 7 大側邊分頁工作流（`ComicOrganizer.Avalonia`）

| 分頁 | 核心功能 |
|---|---|
| **✨ 靈感探索牆** | 作者牆／主題・原作牆探索、隨機雙題材靈感碰撞、疊牌封面懸停輪播（`DeckCoverControl`）、點選作品快速開啟預覽或加入書籃 |
| **📚 漫畫庫畫廊** | 依「目錄／作者／標籤／⭐ 清單（虛擬庫）」切換瀏覽；支援三種卡片尺寸＋列表模式、自訂滾輪步幅、作者或作品分群檢視；`ItemsRepeater` 虛擬化大庫卷軸、自動識別直接含圖片的子資料夾作為作品、右側詳情抽屜、右鍵選單、多頁快速預覽窗（開頭前 6 頁 / 全本均勻抽 6 頁＋單頁放大） |
| **📌 待挑書籃** | 跨「探索牆」與「畫廊」共用的右側暫存抽屜；支援調整排序、快速預覽、**一鍵全部以閱讀器開啟**（開啟不移除項目），或透過確認對話框新建／併入既有「⭐ 閱讀清單」 |
| **🕒 閱讀軌跡** | 記錄閱讀進度與最後瀏覽時間，支援直接續讀或跳回畫廊定位 |
| **🔎 圖搜漫** | 截圖反查本地漫畫庫中的作品、頁碼與分鏡框區域；採用 YOLO 分鏡偵測 + Meta SSCD 特徵萃取 + USearch 向量分片索引（ONNX DirectML 推論），由獨立子行程（`--visual-index-worker`）建庫並於完成後釋放 VRAM |
| **📦 自動歸檔** | 兩階段比對：先查詢自我學習關鍵字知識庫（`MappingIntelligence`、`mappings.json`），未命中再以 `FuzzySharp.WeightedRatio` + 智慧權重評分；支援黑名單 regex 過濾雜訊與歸檔預覽確認後搬移（含跨磁碟移動） |
| **🏷️ 資料管理** | SQLite catalog（`cache.db` schema v9）管理、全庫重建索引、增量對帳（偵測新增／刪除／移動與偏移修復）、規則變更後重解析、增量縮圖補建、`tag_alias` 標籤合併、EhTag 翻譯庫更新、nhentai 外部 metadata 單本／作者批量同步 |
| **⚙️ 系統設定** | 來源／目標／歸檔與外部瀏覽器路徑配置、深淺模式與 9 組強調色切換、閱讀器預設偏好（版面・方向・縮放・OCR 引擎・LLM 翻譯端點與漫畫線條超解析）、圖搜漫模型下載／本機匯入與索引參數、DPAPI 加密敏感金鑰（nhentai API key 等） |

---

### 2. 內建閱讀器（`ReaderWindow` + `SkiaComicViewport`）
- **支援來源**：資料夾（含直接放圖片的章節資料夾）、壓縮檔（`.zip` / `.cbz` / `.rar` / `.cbr` / `.7z` / `.cb7`，經 `SharpCompress` 讀取並支援偏移檔頭掃描容錯）、PDF（`PDFtoImage`）。
- **閱讀模式與繪製效能**：
  - `SkiaSharp` 直接繪製畫布：單頁、雙頁跨頁、連續長條捲軸（含韓漫 0px 無縫緊貼切換）、RTL/LTR 翻頁方向、適頁 / 適寬 / 100% 與任意比例縮放。
  - LRU 頁快取 + 鄰頁背景預載 + 壓縮檔背景預解，右側側欄整合頁碼縮圖、書籤管理、翻譯校正分頁與底部跳頁滑桿。
  - 內建漫畫線條超解析（`BuiltInAnimeLineSuperResPlugin`，亦可從 `plugins\ImageEnhancer\*.dll` 擴充）。
- **OCR → LLM 翻譯 → 局部重繪管線**：
  - **多 OCR 引擎**：離線 PaddleOCR 外掛（`ComicOrganizer.Plugin.PaddleOcr`）與 Windows 原生 OCR，支援執行期動態切換。
  - **翻譯與去雜訊**：OpenAI-compatible / Ollama / 雲端 LLM 端點將對話日譯繁中，並過濾無意義擬聲詞／雜訊為空字串（不遮罩、保留手繪字型）。
  - **排版重繪**：段落合併（`BubbleEnvelope`）、`jieba-tw` 繁中斷詞防詞中斷行、內嵌獅尾黑體 Medium。

---

### 3. 搜尋 DSL 與動態虛擬庫
- 全域搜尋框與 `DslSearchBox` 支援欄位查詢、`@artist:` / `@tag:` 自動完成與 0 結果模糊推薦：
  - 支援欄位：`artist:` / `author:`、`tag:`、`language:` / `lang:`、`read:` / `status:`、`series:`、`group:` / `translator:` / `scanlator:`
  - 支援 `-` 否定排除與引號字串，未知欄位自動退化為純文字搜尋
- 可將常用查詢條件或書籃作品儲存為「⭐ 清單（虛擬庫）」長期追蹤。

---

### 4. 外觀主題、視窗層次與操作安全
- **深／淺色模式 + 9 組強調色**：支援高飽和 5 色（蔚藍 / 紫韻 / 玫瑰 / 翡翠 / 琥珀）＋ **4 組低飽和度莫蘭迪護眼配色**（霧霾藍灰 / 鼠尾草綠 / 煙燻藕紫 / 暖沙棕灰）。
- **明確視窗外框與三階明暗分工**：採用冷炭藍灰外框標題列（搭配動態主題強調色細滾邊）與 `1px` 視窗外框邊線；閱讀器標題列再降為更沉穩低干擾暗框，與次深 HUD 工具列及純淨閱讀畫布區分。
- **操作與資料庫保護**：全域彈窗點擊外部自動關閉（`GlobalPopupLightDismissHelper`）；支援 `--read-only` / `COMIC_ORGANIZER_READONLY=1` 唯讀保護模式防止誤寫共用資料庫；敏感憑證使用 Windows DPAPI（CurrentUser）加密儲存。

---

## 技術堆疊

| 類別 | 套件 / 技術 |
|---|---|
| **Runtime** | .NET 8（Core / VisualSearch 為 `net8.0`；UI / Plugin / Tests 為 `net8.0-windows10.0.19041.0`） |
| **主力 UI 框架** | Avalonia 12.1.6 + `Avalonia.Controls.ItemsRepeater` 12.0.0 + SukiUI 7.0.1 + CommunityToolkit.Mvvm 8.4.2 |
| **圖形與渲染** | SkiaSharp 3.119.4 / 3.119.2（支援 JPG / JPEG / PNG / WebP / BMP / GIF 封面依顯示尺寸解碼與快取） |
| **圖搜漫引擎** | Microsoft.ML.OnnxRuntime.DirectML 1.24.4 + Cloud.Unum.USearch 2.26.6 + System.IO.Hashing |
| **資料庫** | SQLite（`Microsoft.Data.Sqlite` 10.0.7 + `Dapper` 2.1.72） |
| **壓縮檔與 PDF** | SharpCompress 0.48.0（ZIP/CBZ/RAR/CBR/7z/CB7）+ PDFtoImage 5.2.1 |
| **文字處理與比對** | OpenCCNET 1.1.0 + FuzzySharp 2.0.2 |
| **OCR** | PaddleOCRSharp 6.2.0 / Paddle.Runtime.win_x64 3.4.0 + Windows 原生 OCR API |
| **舊版 UI (Legacy)** | WinForms + AntdUI 2.3.12（`ComicOrganizer.App`） |
| **測試** | xUnit |

---

## 建置、執行與測試

> ⚠️ 因主力應用與外掛／Tests 目標為 `net8.0-windows10.0.19041.0`（含 Windows 原生 OCR、DPAPI、DirectML 與 Win10 SDK），需在 **Windows（x64）** 上建置與執行。

```bash
# 建置整個 Solution
dotnet build src/ComicOrganizer.slnx

# 執行新版 Avalonia 主力應用
dotnet run --project src/ComicOrganizer.Avalonia/ComicOrganizer.Avalonia.csproj

# 指定資料目錄或以唯讀模式啟動（避免寫入正式庫）
dotnet run --project src/ComicOrganizer.Avalonia/ComicOrganizer.Avalonia.csproj -- --data-dir "D:\ComicOrganizer" --read-only

# 執行舊版 WinForms 應用（Legacy）
dotnet run --project src/ComicOrganizer.App/ComicOrganizer.App.csproj

# 執行單元測試
dotnet test src/ComicOrganizer.Tests/ComicOrganizer.Tests.csproj

# 依類別或測試名稱篩選執行（部分需真實資料集或 ONNX/OCR 模型的測試除外）
dotnet test src/ComicOrganizer.Tests/ComicOrganizer.Tests.csproj --filter "FullyQualifiedName~SelfLearningTests"

# Release 單一執行檔發行（framework-dependent，需目標機有 .NET 8 runtime）
dotnet publish src/ComicOrganizer.Avalonia/ComicOrganizer.Avalonia.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none -o "D:\ComicOrganizer"
```

---

## 專案結構

```text
src/
├── ComicOrganizer.slnx
├── ComicOrganizer.Core/                  # 跨平台核心業務邏輯（net8.0，無 WinForms/System.Drawing 相依）
│   ├── Core/
│   │   ├── ArchiveOrganizer.cs           # 兩階段歸檔比對 + 智慧評分
│   │   ├── FilenameMetadataParser.cs     # 檔名 / 資料夾 metadata 解析
│   │   ├── FolderRoleDetector.cs         # 資料夾層級角色推測（Root/Author/Series/Work/Chapter）
│   │   ├── SqliteAssetMetaStore.cs       # cache.db catalog（Dapper，schema v9 含 tag_alias）
│   │   ├── SupportedExtensions.cs        # 共用圖片 / 壓縮檔 / PDF 副檔名定義
│   │   ├── Cache/                        # 縮圖寫回佇列、重建追蹤、增量對帳與重解析
│   │   ├── Explore/                      # 靈感探索牆服務與雙題材碰撞
│   │   ├── ExternalSync/                 # nhentai API 整合與批量同步
│   │   ├── Reader/                       # 資料夾/壓縮檔/PDF 來源、Skia 渲染與翻譯管線、超解析外掛介面
│   │   ├── Search/                       # DSL parser / executor、自動完成與模糊推薦
│   │   ├── Settings/                     # Config / Preferences / MappingIntelligence / DPAPI
│   │   ├── Translation/                  # EhTag 標籤翻譯與簡繁轉換
│   │   └── VirtualLibrary/               # 動態虛擬庫與閱讀清單
│   ├── Models/                           # AppConfig / UserPreferences / MoveTask / 版面輔助模型
│   └── Resources/                        # 內嵌 EhTag 翻譯庫、獅尾黑體 Medium、jieba-tw 詞表
├── ComicOrganizer.VisualSearch/          # 圖搜漫（YOLO 分鏡 + SSCD 特徵 + USearch 向量分片 + DirectML）
├── ComicOrganizer.Avalonia/              # 主力桌面 UI（Avalonia 12 + SukiUI 7 + CommunityToolkit.Mvvm）
│   ├── Services/                         # AppDataContext、SkiaCoverService、ReaderPagePipeline、工作書籃、全域彈窗輔助等
│   ├── ViewModels/                       # MainWindow、7 大分頁、Reader、WorkBasket、WorkPreview 等 VM
│   └── Views/                            # AXAML 視窗、分頁與自訂控制項（DeckCoverControl / DslSearchBox / SkiaComicViewport）
├── ComicOrganizer.Plugin.PaddleOcr/      # PaddleOCR 離線辨識外掛
├── ComicOrganizer.App/                   # Legacy WinForms UI（AntdUI）
└── ComicOrganizer.Tests/                 # xUnit 單元與整合測試
```

---

## 資料持久化位置

Avalonia 版依以下順序解析 `DataDirectory`（不使用 `%AppData%`）：
1. 啟動參數 `--data-dir <路徑>` 或環境變數 `COMIC_ORGANIZER_DATA_DIR`
2. 程式所在目錄（`BaseDirectory`）已有 `cache.db` 或 `appsettings.json`
3. 共用開發資料目錄 `D:\ComicOrganizer` 存在時自動採用
4. 退回程式所在目錄 `BaseDirectory`

| 路徑 | 基準位置 | 說明 |
|---|---|---|
| `appsettings.json` | `DataDirectory` | `AppConfig`（路徑設定、外部 viewer、含 DPAPI 加密的 API key 等） |
| `preferences.json` | `DataDirectory` | `UserPreferences`（主題、強調色、閱讀器與畫廊操作偏好） |
| `mappings.json` | `DataDirectory` | 自我學習累積的「關鍵字 → 目標目錄」對應表 |
| `cache.db` | `DataDirectory` | SQLite metadata catalog（作品、作者、標籤與 `tag_alias`、書籤、閱讀軌跡、虛擬庫、外部來源等） |
| `thumbs/` | `DataDirectory` | 雜湊分層縮圖檔（支援現場解出後背景自動寫回） |
| `translations/db.text.json.gz` | `DataDirectory` | EhTag 標籤翻譯庫本地快取（未下載時使用內嵌版） |
| `visual_search/` | `DataDirectory` | 圖搜漫專用模型（`models/`）、向量分片（`shards/`）與 `visual_index.db` |
| `plugins/`、`inference/`、`lib/` | `BaseDirectory` | OCR / 超解析外掛、PaddleOCR 與 text-detector 模型、原生執行期 DLL |

---

## Roadmap

- [ ] 歸檔衝突選擇介面（單檔同時符合多個目標目錄時的挑選畫面）
- [ ] 歸檔操作復原（Undo）記錄
- [ ] 搬檔前目標磁碟剩餘空間檢查
- [ ] FileSystemWatcher 即時增量監聽更新（減少全庫對帳掃描）

---

## 授權與第三方聲明

本專案採 **[PolyForm Noncommercial License 1.0.0](LICENSE)** —— 可自由用於**個人、非商業**用途（使用 / 複製 / 散布 / 修改），**不授予商業使用權利**。

⚠️ 本專案整合多項具額外授權限制（**NonCommercial** 或**僅限個人／學術研究**）之第三方資源，因此**整體僅供個人非商業使用**：
- **EhTagTranslation Database**（標籤翻譯庫）— CC BY-NC-SA 4.0（非商業）
- **獅尾黑體 Medium**（譯文字體，思源黑體衍生）— SIL OFL 1.1
- **jieba-tw 繁體詞表** — MIT
- **comic-text-detector / PaddleOCR / 圖搜漫（YOLO・SSCD）ONNX 模型** — **不隨 repository 散布**，由使用者於需要時按需下載或自行匯入至本機模型目錄。

完整第三方資源列表、授權條款與相依套件宣告請見 **[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)**。
