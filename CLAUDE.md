# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案性質

ComicOrganizer 是一套**本地漫畫/圖集管理工具**：智慧封面提取、模糊匹配自動歸檔、自我學習關鍵字 → 目錄映射、SQLite metadata catalog 與縮圖快取、檔名 metadata 解析、nhentai 外部 metadata 同步、EhTag 標籤翻譯、內建閱讀器（OCR + LLM 翻譯）、搜尋 DSL 與虛擬庫，以及「圖搜漫」截圖反查。

**目前主力 UI 是 `ComicOrganizer.Avalonia`（Avalonia + SukiUI）**（Phase 0~4 + 圖搜漫，commit `4766115`..`b90ac04`）。業務邏輯已抽到不依賴 Windows 的 `ComicOrganizer.Core`。舊的 WinForms 版 `ComicOrganizer.App`（AntdUI）仍在 solution 內、可編譯、也被 Tests / Plugin 參考，但這波沒有新功能，視為 legacy —— **新功能優先做在 Avalonia + Core**。`script/` 下的 PowerShell 只是最早的規格參考，不再擴充。

> README.md 的架構 / 執行指令仍停在 WinForms 版，尚未更新。

## 常用指令

所有指令對著 solution `src/ComicOrganizer.slnx`（也可直接指定專案 csproj）。

```bash
# 建置整個 solution（首次 restore 約 2 分鐘）
dotnet build src/ComicOrganizer.slnx

# 執行新版 Avalonia 應用（主力）
dotnet run --project src/ComicOrganizer.Avalonia/ComicOrganizer.Avalonia.csproj
#   可加參數：--data-dir <路徑>  --read-only
#   環境變數：COMIC_ORGANIZER_DATA_DIR / COMIC_ORGANIZER_READONLY=1

# 執行舊版 WinForms 應用（legacy）
dotnet run --project src/ComicOrganizer.App/ComicOrganizer.App.csproj

# 執行所有 xUnit 測試
dotnet test src/ComicOrganizer.Tests/ComicOrganizer.Tests.csproj

# 依方法名 / 類別過濾
dotnet test src/ComicOrganizer.Tests/ComicOrganizer.Tests.csproj --filter "FullyQualifiedName~SelfLearningTests"

# WinForms Release 發行（單一 exe，framework-dependent，需 .NET 8 runtime）
dotnet publish src/ComicOrganizer.App/ComicOrganizer.App.csproj -c Release
```

注意事項：
- Core / VisualSearch 是 `net8.0`；App / Avalonia / Plugin / Tests 是 `net8.0-windows10.0.19041.0`，**整個 solution 只能在 Windows 上 build/run**。Avalonia 與 VisualSearch 固定 x64。
- Release / publish 設定（win-x64、SingleFile、ReadyToRun）**只有 App.csproj 有**；Avalonia 目前沒有 publish 設定。
- 沒有 lint 設定，但 `Nullable` 與 `ImplicitUsings` 都啟用，請維持 nullable 標註。
- 部分測試需要外部資源：`FullRealDataBatchTest` 寫死 `E:\Project\...\Tests\H`（不 assert）；`OcrTest` / `RealTranslationFlowTest` 需要 PaddleOCR 模型與可連線的 LLM 端點；VisualSearch 相關測試需 ONNX 模型。

## Solution 結構

| 專案 | TFM | 角色 | 參考 |
|------|-----|------|------|
| `ComicOrganizer.Core` | net8.0 | 業務邏輯 / DB / 解析 / 閱讀器資料層（不依賴 System.Drawing / WinForms） | — |
| `ComicOrganizer.VisualSearch` | net8.0 x64 | 「圖搜漫」：ONNX (DirectML) + USearch 向量索引 | Core |
| `ComicOrganizer.Avalonia` | net8.0-windows | **主力 UI**：Avalonia 12.1 + SukiUI 7 + CommunityToolkit.Mvvm | Core, VisualSearch |
| `ComicOrganizer.App` | net8.0-windows | legacy WinForms UI（AntdUI 2.3.12） | Core |
| `ComicOrganizer.Plugin.PaddleOcr` | net8.0-windows | PaddleOCR 外掛（拆出以縮小主程式） | Core, App（`Private=false`） |
| `ComicOrganizer.Tests` | net8.0-windows | xUnit | 上面全部 |

**命名空間沒有跟著專案改名**：Core 內的類別仍是 `ComicOrganizer.App.Core.*` / `ComicOrganizer.App.Models.*`，嵌入資源 LogicalName 也沿用 `ComicOrganizer.App.Resources.*`（刻意保持相容，別順手改）。Core 對 App / Tests / Avalonia 開了 `InternalsVisibleTo`。

## Core 層（`src/ComicOrganizer.Core/`）

套件：Dapper、Microsoft.Data.Sqlite、FuzzySharp、SharpCompress、PDFtoImage、SkiaSharp、OpenCCNET、ProtectedData（DPAPI）。嵌入資源：EhTag `db.text.json.gz`、獅尾黑體、`zh_tw_words.txt.gz`（jieba-tw 詞表）。

**歸檔比對（`Core/`）**
- **`ArchiveOrganizer.FindBestMatch()`** — 兩階段：先查 `MappingIntelligence` 學過的關鍵字（命中 `Confidence=100`、`IsLearned=true`）；否則對每個目標資料夾算 `CalculateSmartScore`：完全包含 → 95、`ExtractAllKeywords` 命中 → 90，否則退回 `FuzzySharp.Fuzz.WeightedRatio`。
  - `ExtractAllKeywords` 用 regex `[\[\(\{](.*?)[\]\)\}]` 抽括號內容，再用 `- × x , 空格 : | / \` 切詞，過濾 `AppConfig.Blacklist` / `BlacklistPatterns`。**新增雜訊規則加到 `AppConfig`，不要寫死在 `ArchiveOrganizer`**。
- `ArchiveMoveHelper` — 搬檔（含跨磁碟卷）共用邏輯。
- **`MappingIntelligence`**（在 `Core/Settings/`）— 自我學習關鍵字 → 目錄映射，持久化在 `mappings.json`。key 一律 `ToLower().Trim()`；`TryGetMappedFolder()` 依 key 長度由長到短比對。只有「使用者確認過的成功搬檔」才 `Learn()`。

**Settings（`Core/Settings/`）**：`ConfigManager` ↔ `appsettings.json`、`UserPreferencesManager` ↔ `preferences.json`、`SecureStringStore`（Windows DPAPI CurrentUser scope 加密 nhentai key 等，跨機器無法解密；走 `AppConfigExtensions` get/set）。

**Cache / Catalog**
- `IAssetMetaStore` → `SqliteAssetMetaStore`（`cache.db`，Dapper + Microsoft.Data.Sqlite，snake_case ↔ PascalCase）。**schema v9**（v9 新增 `tag_alias`：多個標籤合併成虛擬主標籤，舊名當別名自動導向；nhentai 匯入與 `SearchExecutor` 都會走 alias）。另有 `EnsureVirtualLibraryItemTable()` 每次啟動檢查。建構子有 `readOnly` 參數（`Mode=ReadOnly`、不跑 migration）。`SqliteConnection` 非 thread-safe，所有方法走內部 `_lock`，別繞過。
- `Core/Cache/`：`CacheWriter`（背景 queue 寫 metadata + 縮圖）、`FileSystemThumbStore`（`thumbs/<前2字元>/<key>.jpg`，tmp+rename 原子寫）、`CatalogReconcileService`、`ReparseAllService`、`CacheIntegrityChecker`、`RebuildProgressTracker`。**Dispose 順序：先 `CacheWriter`（drain + join）再 meta store**。
- `CacheRebuildService` 與 `CoverManager` 目前**仍在 App**（WinForms 用）；Avalonia 用自己的 `SkiaCoverService`。

**Metadata 解析**：`FilenameMetadataParser`（event / author / title / parody / language / 翻譯組）、`FolderRoleDetector`（bottom-up 推測 Root/Author/Series/Work/Chapter/Image，見記憶 `project_folder_role_detector`）。

**其他子層**
- `Core/ExternalSync/` — nhentai（`NhentaiApiClient` 自帶限速、`NhentaiIdExtractor`、`AssetExternalSource` 存原始回應、`MergeResult`、`BatchExternalFetchService` + `BatchSyncStrategy`）。
- `Core/Translation/` — EhTag 標籤翻譯：`TagTranslationDb`（lazy 解壓 embedded，CC BY-NC-SA 4.0）、`TagTranslator`、`TagTranslationUpdater`（GitHub release，24h 防 spam）、`ZhConverter`（OpenCCNET，見記憶 `reference_chinese_conversion`）。
- `Core/Search/` — `SearchQueryParser`/`SearchQuery`（`artist:`/`author:`、`tag:`、`language:`/`lang:`、`read:`/`status:`、`-` 否定）→ `SearchExecutor` 轉 SQL；新增 `DslSuggestionService`（自動完成）、`SearchFuzzyRecommender`（0 結果模糊推薦）。
- `Core/VirtualLibrary/` — 存起來的 DSL query = 動態虛擬庫，即時跑不快取。
- `Core/Explore/` — `ExploreWallService`（靈感探索牆：雙主軸探索、隨機雙題材碰撞）。
- `Core/Reader/` — `IReaderSource` → `Folder/Archive(SharpCompress)/PdfReaderSource(PDFtoImage)`、`SkiaPageRenderer`（SKImage LRU + 背景預載）、`ContinuousLayout`、`SkiaTranslationRenderer`、`BubbleEnvelope`、`WordBreakAdvisor`/`ChineseWordDict`、`LlmTranslationService`（OpenAI-compatible / Ollama / 雲端端點；**擬聲詞/雜訊刻意譯成空字串 `""`**，重繪時空譯文不塗白）、`ReaderImageEnhancer`（超解析，內建 `BuiltInAnimeLineSuperResPlugin`，可從 `plugins\ImageEnhancer\*.dll` 載外掛）、`ISkiaOcr.cs`（`ISkiaOcrService` / `ISkiaOcrPlugin` / `SkiaOcrPluginManager`）。
- `Models/` — `AppConfig`(+Extensions)、`UserPreferences`、`AppTheme`、`MoveTask`、`GallerySortModels`、`PagePickerHelper`、`DrawerWidthHelper`、`DelayedLoadingTracker`。

## Avalonia UI（`src/ComicOrganizer.Avalonia/`）— 主力

- **啟動**：`Program.Main` 先把 `BaseDirectory\lib` 加進原生 DLL 搜尋路徑 → 若帶 `--visual-index-worker` 則以圖搜漫建庫子行程執行後結束 → 否則 `App.OnFrameworkInitializationCompleted`：`new AppDataContext(args)` → `ApplyInitialTheme()` → `new MainWindowViewModel(ctx)` → `MainWindow`；結束時依序 Dispose。
- **`Services/AppDataContext` = 組合根**：解析 DataDirectory → Config / Preferences 載入 → `Lazy` SqliteAssetMetaStore（可唯讀）→ `Lazy` FileSystemThumbStore → `VisualIndexHostService` → `Lazy` TagTranslator → `SkiaOcrPluginManager.RegisterNativeFallback(WindowsSkiaOcrService)` → SukiToastManager；也管 SukiUI 主題（深/淺 + 5 種強調色）。
- **MVVM**：CommunityToolkit.Mvvm（`[ObservableProperty]` / `[RelayCommand]`）+ `ViewLocator`；`ViewModels/` ↔ `Views/*.axaml`。
- **導覽**：`SukiSideMenu` 7 頁 —— 靈感探索牆（`ExplorePageViewModel`，啟動即載）、漫畫庫畫廊（`Gallery`）、閱讀軌跡（`ReadingTrack`）、圖搜漫（`VisualSearch`）、自動歸檔（`AutoArchive`）、資料管理/維護中心（`DataManagement`）、系統設定（`Settings`）。其餘頁面由 `StartDeferredInitialization()` 在視窗顯示後背景載入。
  - **保留頁面**：`SukiSideMenu UseCustomContent="True"`，內容是 `Views/Controls/KeepAlivePageHost`；每頁 View 只在第一次進入時經 `ViewLocator` 建立，之後切頁只切換 `IsVisible`（淡出 / 淡入各 250ms，比照 SukiUI）。View 會一直掛在畫面樹上（VM 訂閱不會解除、捲動位置保留）；頁面若需要「重新顯示時」做事，覆寫 `OnPropertyChanged` 監看 `IsVisibleProperty`（見 Gallery / Explore）。
  - 實測：Avalonia 11 上隱藏頁仍會拖慢其他頁繪製；12 版無此問題。把隱藏頁移出畫面樹再掛回會重新套用整頁樣式（比重建還慢），不要這樣做。
- **繪製效能（4K 全螢幕）**：`BoxShadow` 每個都多一次繪製（與模糊半徑無關），搭配旋轉圖片在大量卡片時很貴。`DeckCoverControl` 的疊牌容器用 `BitmapCache`（倍率 = `RenderScaling`）整組快取，徽章 / 輪播圓點刻意放在快取外以保留 ClearType；`Program.cs` 把 Skia GPU 資源快取從預設約 28 MB 調到 256 MB。探索牆 4K 捲動 22.5 → 53.0 FPS（本機 RTX 4080）。代價：顯示卡記憶體多約 360 MB（全來自 GPU 快取；點陣圖快取幾乎不增加）。程式 RAM 主要隨「已載入卡片數」成長（每張卡片持有解碼後封面與整組控制項；3,500 本全部載入約 8 GB）。
- **封面依顯示大小解碼**：`SkiaCoverService` 接受 `CoverDecodeSize`（實際像素框），以 UniformToFill 需要的寬度縮小解碼、絕不放大，快取 key 加 `@寬x高`；縮放比例由 `MainWindow` 經 `ReportDisplayScale` 回報（只增不減）。探索牆疊牌第 1 張維持完整尺寸（單張精選樣式會放大顯示），第 2、3 張用 `CoverDipSize.DeckCover`，隨機組合預覽全用 `RandomComboPreview`；這些 DIP 值必須與 XAML 一致。畫廊 / 閱讀軌跡仍用完整尺寸。
- **漫畫庫畫廊虛擬化**：卡片格與列表用 `Avalonia.Controls.ItemsRepeater`（`VerticalCacheLength=0.5`），一次放入全部作品（沒有「載入更多」）。封面格子固定寬度（SukiUI Basic 按鈕不會撐開內容，寬度用 `GalleryCardCoverWidth` 資源）。
  - **卡片範本外層一定要包 `StableDataContextHost`**：ItemsRepeater 回收卡片時一律先把 DataContext 設成 null（`MustClearDataContext`，關不掉），造成所有繫結報錯、按鈕啟用狀態傳遍子元件、樣式與轉場重套；外層容器擋掉 null、直接換成新項目，拖捲軸時 UI 工作約減半。
  - 卡片建立時 `NotifyItemRealized` 載入封面，每張卡片一個取消權杖，回收時（`NotifyItemRecycled`）取消還在排隊 / 解壓縮的載入；封面同時最多 4 張在載。回收的卡片保留最近 120 張的封面，更早的放掉。
- **封面快取與記憶體**：`SkiaCoverService` 記憶體 LRU 512 張；擠出快取的點陣圖**不 Dispose**（可能仍顯示在卡片或詳細資訊抽屜上），改由 `BitmapReleaseGcTrigger` 累計像素量，達 256 MB（間隔至少 2 秒）觸發背景 GC 讓原生記憶體早點回收。
- **縮圖自動寫回**：一般模式下，現場從原始檔解出的封面由 Core 的 `ThumbWriteBackQueue` 在背景寫回 `thumbs\` 與 cache.db 縮圖索引（key 與「資料管理 → 增量更新縮圖」相同，`cover_origin=skia-lazy`；略過不在 catalog / 單張圖片 / 已有縮圖；達 `ThumbCacheMaxBytes` 停寫不淘汰）。唯讀模式 `AppDataContext.CreateThumbWriteBack()` 回傳 null，完全不寫。Dispose 順序：封面服務 → 寫回佇列 → AppDataContext。
- **自訂控制項**：`DslSearchBox`（區塊化 DSL 搜尋框）、`DeckCoverControl`（疊牌懸停輪播）、`SkiaComicViewport`。
- **閱讀器**：`ReaderWindow` + `ReaderViewModel`，經 `IReaderWindowLauncher`（`AvaloniaReaderWindowLauncher`，fallback 外部 viewer）開啟。`SkiaComicViewport` 用 `ICustomDrawOperation` 直接畫 SKCanvas（單頁 / 雙頁跨頁 / 連續捲動）；`Services/ReaderPagePipeline` 負責頁快取、預載、超解析、OCR→翻譯。
- **唯讀保護**：以 `--read-only` / `COMIC_ORGANIZER_READONLY=1` 啟用，各 VM 寫入動作會被攔截並 toast 提示（共用開發庫 `D:\ComicOrganizer` 常用此模式）。
- UI thread：非同步結果更新 UI 要經 `Dispatcher.UIThread`。Avalonia 12 會嚴格檢查控制項是否在 UI 執行緒操作；測試中要建立 / 操作 View 時，用 `AvaloniaTestThread.Run(...)` 送到測試專用 UI 執行緒。
- TextBox 浮水印屬性在 Avalonia 12 是 `PlaceholderText`（`Watermark` 已過時）。

## 圖搜漫（`src/ComicOrganizer.VisualSearch/`）

截圖反查作品 + 頁碼 + 分鏡位置；需求規格見 `docs/code_artifact.md`（零外部服務、目標 1 萬本 / ~1000 萬向量、待機 0 VRAM）。
- 管線：YOLO 分鏡偵測（`panel_detector.onnx`）→ Meta SSCD 特徵（`sscd_disc_mixup.onnx`）→ USearch 分片（`shards\panels_NNN.usearch`）+ 獨立 SQLite `visual_index.db`；XxHash3 指紋做增量更新。ONNX 走 DirectML。
- 主要類別：`VisualSearchEngine`、`VisualIndexingPipeline`、`ShardedVectorStore`、`VisualIndexDatabase`、`VisualComicReader`、`VisualSearchPaths`。
- 檔案在 `<DataDirectory>\visual_search\{models,shards,visual_index.db}`，與 `cache.db` 分開。**模型不進 repo**，由設定頁下載（`AppConfig.VisualSearchPanelModelUrl` / `VisualSearchEmbedModelUrl`）或本機匯入。
- 建庫由 Avalonia 的 `VisualIndexHostService` 開**子行程** `--visual-index-worker --data-dir ...` 執行，結束後 VRAM 交還系統。開關：`EnableVisualSearch`（預設 false）、`VisualSearchAutoIndexAfterArchive`、`VisualSearchEnableMultiCropRerank`。

## OCR 外掛

兩套並存：
- **Core 新版**：`SkiaOcrPluginManager` + `ISkiaOcrPlugin`（不依賴 System.Drawing），掃 `BaseDirectory\plugins` 與**上一層的 `plugins`**；遇 `ReflectionTypeLoadException` 只取能載入的型別。原生 Windows OCR 由宿主 `RegisterNativeFallback` 註冊。Avalonia 用這套。
- **App 舊版**：`App/Core/Reader/OcrPluginManager` + `IOcrPlugin`，掃 `plugins\**\*.dll`（略過 `paddle_`/`mkl`/`opencv_`/`DirectML`/`System.`/`Microsoft.` 開頭），collectible `AssemblyLoadContext` 載入。
- `ComicOrganizer.Plugin.PaddleOcr` 兩套介面都實作（`PaddleOcrPlugin` / `PaddleSkiaOcrPlugin`），**OutputPath 直接輸出到 `ComicOrganizer.App\bin\<Config>\net8.0-windows10.0.19041.0\plugins\PaddleOCR\`**。模型 `inference\PP-OCRv6_small_{det,rec}_infer` 等放 BaseDirectory。

## Legacy WinForms（`src/ComicOrganizer.App/`）

`MainForm`（`AntdUI.Window`）組 service chain；三個主 UserControl（`GalleryControl` / `ArchiveControl` / `MetadataManageControl`）疊在 `contentHost` 用 `Visible` 切換；`ReaderForm` 有完整 GDI+/Skia-GPU 閱讀器與 OCR→翻譯→重繪管線。App 端仍保留：`CoverManager`、`CacheRebuildService`、`AutocompleteHelpers`、`Core/Theming/`、`Core/Reader/` 的 `WindowsOcrService`、`ComicTextDetector`（ONNX 文字遮罩）、`TextEraser`、`FontSizeOptimizer`、`ReaderFonts`、`ReaderImageLoader`、`BubbleEnvelopeGdi`、`ModelDownloader`；`Models/` 的 `ArchiveItem/Group`、`GalleryListItem`、`ReaderNavigationHelper`、`TabNavigationHistory`、`WindowLayoutHelper`。

動 WinForms 前看記憶 `feedback_antdui_quirks` / `project_ui_architecture` / `project_theme_colors`；修改 `*.Designer.cs` 要確保 event 綁定一致（commit `e5e08d7` 曾因此建置失敗），VS Designer regenerate 會還原寫死的 ARGB 主題色。

## 資料持久化

**Avalonia 的 DataDirectory** 解析順序：① 建構參數 / `--data-dir` / `COMIC_ORGANIZER_DATA_DIR` → ② `BaseDirectory` 已有 `cache.db` 或 `appsettings.json` → ③ `D:\ComicOrganizer` 存在（共享開發庫）→ ④ 退回 `BaseDirectory`。WinForms 版與 Core managers 預設都用 `BaseDirectory`。不使用 AppData。

| 路徑（DataDirectory 下） | 內容 |
|------|------|
| `appsettings.json` | `AppConfig`（含 DPAPI 加密的 nhentai key） |
| `preferences.json` | `UserPreferences` |
| `mappings.json` | `MappingIntelligence` 關鍵字 → 目錄映射 |
| `cache.db` | SQLite catalog（schema v9） |
| `thumbs/` | 縮圖 |
| `translations/db.text.json.gz` | EhTag 翻譯庫 disk cache（無則用 embedded） |
| `visual_search/` | 圖搜漫模型、向量分片、`visual_index.db` |

以 BaseDirectory 為準（不跟 DataDirectory）：`plugins/`、`inference/`（PaddleOCR 模型）、`lib/`（原生 DLL）。

## 測試（`src/ComicOrganizer.Tests/`，xUnit + `ITestOutputHelper`）

- 歸檔/學習：`SelfLearningTests`（改比對邏輯的主要回歸網）、`RealDataHybridTest`、`PersistenceTests`、`ArchiveMoveHelperTests`。
- 解析/metadata：`FilenameMetadataParserTests`、`FolderRoleDetectorTests`、`MetadataEditTests`、`AuthorAggregationTests`、`TagAliasVirtualMergeTests`。
- 快取/DB：`CatalogReconcileTests`、`ReparseAllTests`、`MigrationV1V2Test`、`BookmarkStoreTests`。
- 外部/翻譯：`NhentaiApiClientTests`、`ExternalSourceTests`、`TagTranslation*Tests`、`RealTranslationFlowTest`、`OcrTest`、`OcrPluginTests`。
- 搜尋/虛擬庫/探索：`SearchDslTests`、`ExtendedSearchCoreTests`、`VirtualLibrary*Tests`、`ExploreWallServiceTests`。
- Avalonia ViewModel：`ExplorePageViewModelTests`、`GalleryPageViewModelTests`、`ReadingTrackPageViewModelTests`、`ReaderViewModelTests`、`Phase4ManagementTests`、`SkiaCoverServiceTests`。
- 閱讀器/圖搜漫/效能：`SkiaPageRendererTests`、`SkiaTranslationRendererTests`、`ReaderImageEnhancerTests`、`ContinuousLayoutTests`、`VisualSearchEngineTests`、`CoverDecodePerfTest` / `WebpOptimizationTest` 等。

## 規範與常見坑

- **回應一律繁體中文**；程式註解也用繁中。區塊敘述（using/if/foreach/try）一律多行標準區塊，禁止單行寫法。
- 流程：**思考與討論 → 規劃 → 列工作項目 → 逐項執行（執行前確認）→ 測試**；每項完成先讓使用者確認再進下一項。
- 新邏輯優先放 Core（保持 `net8.0`、不引入 System.Drawing / WinForms）；UI 專屬放 Avalonia。
- 知識庫 key 一律小寫正規化；新增比對流程記得 `ToLower()`。
- `CacheWriter` → meta store 的 dispose 順序；SqliteConnection 靠內部 lock 序列化。
- DPAPI 密文不可塞明文進 `appsettings.json`，走 `SecureStringStore` / `AppConfigExtensions`。
- ONNX 模型（comic-text-detector、圖搜漫模型）**絕不進 repo**（授權問題，見記憶 `project_licensing`）。
- 進行中工作見記憶 `project_52_53_progress`；roadmap：歸檔衝突選擇介面、Undo、空間檢查尚未實作。
