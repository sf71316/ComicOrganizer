# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案性質

ComicOrganizer 是一套**漫畫/圖集管理工具**，已從 `script/圖片整理.ps1`（命令列 PowerShell legacy）轉型為 `src/` 下的 **C# WinForms 視覺化應用程式**，並從單純「自動歸檔工具」長成完整的本地漫畫庫管理器。目前能力涵蓋：智慧封面提取、模糊匹配自動歸檔、自我學習關鍵字 → 目錄映射、SQLite metadata catalog 與縮圖快取、檔名 metadata 解析、nhentai 外部 metadata 同步、EhTag 標籤翻譯、內建閱讀器（含 OCR + LLM 翻譯）、搜尋 DSL 與虛擬庫。

開發時應**以 `src/` 下的 C# 專案為主**；`script/` 下的 PowerShell 腳本只是規格參考，不應再擴充。

## 常用指令

所有指令對著 solution `src/ComicOrganizer.slnx`（也可直接指定專案 csproj）。

```bash
# 建置整個 solution
dotnet build src/ComicOrganizer.slnx

# 執行 WinForms 應用
dotnet run --project src/ComicOrganizer.App/ComicOrganizer.App.csproj

# 執行所有 xUnit 測試
dotnet test src/ComicOrganizer.Tests/ComicOrganizer.Tests.csproj

# 執行單一測試（依方法名）
dotnet test src/ComicOrganizer.Tests/ComicOrganizer.Tests.csproj --filter "FullyQualifiedName~Test_JapaneseAuthorLearning_ShouldWork"

# 依測試類別過濾
dotnet test src/ComicOrganizer.Tests/ComicOrganizer.Tests.csproj --filter "FullyQualifiedName~SelfLearningTests"

# Release 發行（單一 exe，framework-dependent，需目標機有 .NET 8 runtime）
dotnet publish src/ComicOrganizer.App/ComicOrganizer.App.csproj -c Release
```

注意事項：
- 目標框架是 `net8.0-windows10.0.19041.0`（WinForms + Windows 10 SDK API，如原生 OCR），**只能在 Windows 上 build/run**。
- `App.csproj` 的 Release PropertyGroup 才會啟用 `win-x64` / `PublishSingleFile` / `ReadyToRun`；日常 `build`/`run` 不受影響。
- 沒有 lint 設定，但 `Nullable` 與 `ImplicitUsings` 都啟用，請維持 nullable 標註。
- 部分測試需要外部資源才有意義：`FullRealDataBatchTest` **寫死了 `E:\Project\...\Tests\H` 路徑**做人工觀察（不 assert，無資料時掃到空目錄但不 fail）；`OcrTest` / `RealTranslationFlowTest` 需要 PaddleOCR 模型檔與可連線的 LLM 端點，否則跳過或失敗。

## 架構地圖

UI 與業務邏輯解耦：`MainForm`（`MainForm.cs`）在建構時組一條 service chain，把所有檔案系統、比對、快取、解析、翻譯邏輯都收斂到 `Core/`，UI 只負責呈現與事件轉發。建構順序見 `MainForm.cs:34-100`：

```
ConfigManager → AppConfig                     （appsettings.json）
UserPreferencesManager → UserPreferences      （preferences.json；舊版 Theme/Language 會從 appsettings 一次性遷移）
LocalizationManager / ThemeManager 初始化（必須在 InitializeComponent 前，Designer 才取得正確主題色）
MappingIntelligence → ArchiveOrganizer + CoverManager
SqliteAssetMetaStore + FileSystemThumbStore + CacheWriter   （cache.db / thumbs/，CoverManager.AttachCache 掛上）
RebuildProgressTracker → CacheIntegrityChecker → CacheRebuildService / CatalogReconcileService / ReparseAllService
TagTranslator + TagTranslationUpdater         （translations/db.text.json.gz，disk 優先 → fallback embedded）
ReadRecordQueue                               （閱讀紀錄延遲提交）
```

三個主 UserControl（`galleryControl` / `archiveControl` / `metadataManageControl`）都疊在 `contentHost` 內，靠 `Visible` 切換（`ShowGallery/ShowArchive/ShowMetadataManage`），由 `menuStrip1` 的「功能」選單驅動。`Form.Load` 時才呼叫各 control 的 `Initialize(...)` 注入上面的服務（此時 handle 已建立、`Invoke` 才能用）。

### Core 層（`ComicOrganizer.App/Core/`）

**歸檔比對（根目錄）**
- **`ArchiveOrganizer.FindBestMatch()`** — 兩階段比對：
  1. 先查 `MappingIntelligence` 學過的關鍵字（命中即 `Confidence=100`、`IsLearned=true`）。
  2. 否則對每個目標資料夾算 `CalculateSmartScore`：完全包含 → 95、`ExtractAllKeywords` 命中 → 90，否則退回 `FuzzySharp.Fuzz.WeightedRatio`。
  - `ExtractAllKeywords` 用 regex `[\[\(\{](.*?)[\]\)\}]` 抽出括號內容，再用 `- × x , 空格 : | / \` 切詞，並過濾掉 `AppConfig.Blacklist`（如 "DL版"、"AI Generated"）與 `BlacklistPatterns`（如 `C\d{2,3}` Comic Market 編號）視為雜訊。**新增雜訊規則應加到 `AppConfig` 而非寫死在 `ArchiveOrganizer`**。
- **`MappingIntelligence`** — 自我學習的關鍵字 → 目錄映射表，獨立持久化在 `mappings.json`。`Learn()` 把 key 正規化（`ToLower().Trim()`）後寫入並立刻 `Save()`；`TryGetMappedFolder()` **依 key 長度由長到短**比對避免短關鍵字誤觸。唯一寫入入口是 `PreviewForm.ExecuteArchiveAsync()` 成功搬檔後 —— 即「使用者確認過的成功移動」才進知識庫。
- **`CoverManager.GetCoverAsync()`** — 多型來源統一回傳 `Image?`。資料夾走遞迴（深度上限 `AppConfig.MaxScanDepth`）：當前目錄 `cover.*` → 符合大小範圍（`Min/MaxCoverFileSizeBytes`）的字典序第一張圖 → 第一個壓縮檔 → 字典序首個子目錄；壓縮檔（zip/cbz/rar/7z）統一走 **SharpCompress** 取第一張圖；單檔圖片直接讀。先查 `cache.db`（依 path 算 SHA1 `CoverThumbKey` + 比對 mtime）命中就直接讀 `thumbs/`，否則解碼縮圖後經 `AttachCache` 掛上的 `CacheWriter` 背景寫入。**PDF 封面不在此處理，PDF 只在 Reader 的 `PdfReaderSource` 支援。**

**Settings 子層（`Core/Settings/`）**
- `ConfigManager` ↔ `appsettings.json`、`UserPreferencesManager` ↔ `preferences.json`，皆存在 `AppDomain.CurrentDomain.BaseDirectory`（= `bin/.../`）。
- `SecureStringStore` — 用 Windows **DPAPI（CurrentUser scope）**加解密敏感值（nhentai API key 等），密文 base64 存進 appsettings；跨機器拷貝無法解密（刻意的安全設計）。`AppConfigExtensions` 提供 get/set 包裝。

**Cache / Catalog 子層（`Core/Cache/` + 根目錄 `SqliteAssetMetaStore` / `IAssetMetaStore`）**
- `IAssetMetaStore` → `SqliteAssetMetaStore`（`cache.db`，**Dapper + Microsoft.Data.Sqlite**，snake_case ↔ PascalCase 對映，目前 schema v8 逐版 migration）。存 asset catalog、asset_meta、author、tag、author_tag、bookmark、virtual_library、external source 等。`SqliteConnection` 非 thread-safe，所有方法用內部 `_lock` 序列化，別繞過。
- `IThumbStore` → `FileSystemThumbStore`（縮圖存 `thumbs/<前2字元>/<key>.jpg`，tmp + rename 原子寫入分散到 256 子目錄；提供 `TotalSizeBytes()` / `Clear()` / `EnumerateAllKeys()`，本身**不做**自動容量淘汰）。
- `CacheWriter` — 背景 queue worker，把 metadata + 縮圖寫入序列化，避免阻塞 UI 與部分寫入。**Dispose 順序：先 `CacheWriter`（drain queue + join worker）再 `_metaStore`**（見 `MainForm.OnFormClosed`）。
- `CacheRebuildService`（完整重建索引）/ `CatalogReconcileService`（偵測新增/刪除/移動同步 catalog，產 `ReconcileReport`）/ `ReparseAllService`（規則變更後重解析已編目項目）/ `CacheIntegrityChecker`（孤兒偵測）。進度都透過 `RebuildProgressTracker`，其 `StateChanged` 事件直接驅動 `MainForm` 底部 statusBar。

**Metadata 解析（根目錄）**
- `FilenameMetadataParser` — 從檔名/資料夾名抽出 event / author / title / parody / language / 翻譯組等（作者格式、語言對映表）。
- `FolderRoleDetector` — bottom-up 推測資料夾層級角色（Root / Author / Series / Work / Chapter / Image），詳見記憶 `project_folder_role_detector`。
- `ParsedMetadata` / `AssetMetadata` / `AssetCatalogEntry` / `Author` / `Tag` / `ReadStatus` / `Bookmark` 是對應 DB 表的 model。

**ExternalSync 子層（`Core/ExternalSync/`）** — nhentai metadata 整合（#52）
- `IMetadataAPIClient` → `NhentaiApiClient`（呼叫 nhentai API，DPAPI 加密的 key，自帶限速）。
- `NhentaiIdExtractor` 從檔名抽 gallery id；`AssetExternalSource` 把原始回應存進 DB 避免重複呼叫；`MergeResult` 處理與本地手動編輯欄位的衝突；`BatchExternalFetchService` + `BatchSyncStrategy` 做作者批量同步。

**Translation 子層（`Core/Translation/`）** — EhTag 標籤翻譯（#53）
- `TagTranslationDb` lazy 解壓 embedded `Resources/db.text.json.gz`（EhTagTranslation Database，CC BY-NC-SA 4.0）；`TagTranslator` 是門面（nhentai type/slug → 中文，簡體經 `ZhConverter` 轉繁）；`TagTranslationUpdater` 背景檢查 GitHub release 新版（24h 防 spam，`MainForm.CheckTranslationDbUpdateInBackground`）。簡繁轉換用 **OpenCCNET**（見記憶 `reference_chinese_conversion`）。

**Reader 子層（`Core/Reader/`）** — 內建閱讀器（#49 / #51）
- `IReaderSource` → `FolderReaderSource` / `ArchiveReaderSource`（SharpCompress，支援 rar/7z）/ `PdfReaderSource`（PDFtoImage）。
- `ReaderImageLoader`（LRU + 鄰頁預載）、`ContinuousLayout`（RTL/LTR 連續捲軸版面）、`FontSizeOptimizer`。
- OCR：`IOcrService` → `PaddleOcrService`（**PaddleOCRSharp + Paddle.Runtime.win_x64**，模型 `inference/PP-OCRv6_small_{det,rec}_infer` + `PP-OCRv5_mobile_cls_infer`，全離線）/ `WindowsOcrService`（Windows 10+ 原生），可在「系統設定 → 閱讀器」動態切換。翻譯：`ITranslationService` → `LlmTranslationService`（OpenAI-compatible / Ollama 端點，端點與並發在 `AppConfig`）。
- **OCR→翻譯→重繪管線（`ReaderForm.cs`）**：OCR 原始 block 先經 `IOcrService.MergeBlocks()` 合併跨行/斷氣泡的文字（`ReaderForm.cs:1909`）→ `LlmTranslationService` 用 system prompt + few-shot 把日文譯成繁中，並**刻意把擬聲詞/狀聲詞/辨識雜訊翻成空字串 `""`**（`LlmTranslationService.cs:55-71`）→ 重繪時**只對各子氣泡區域分別塗白遮罩**再排版繪製譯文（`ReaderForm.cs:2119-2136`），空譯文區塊直接略過（不塗白、保留手繪狀聲詞）。
- **最近一次重構（commit `954c280`）動的就是這套 OCR/翻譯架構與字體計算（`FontSizeOptimizer`）**。

**Search / VirtualLibrary 子層**
- `Core/Search/`：`SearchQueryParser.Parse()` 把字串解析成 `SearchQuery`（term 清單，支援 `artist:`/`author:`、`tag:`、`language:`/`lang:`、`read:`/`status:` 欄位與 `-` 否定，未知 field 退化成純文字；positive AND、negative except）→ `SearchExecutor` 轉 SQL 查 catalog。
- `Core/VirtualLibrary/`：`VirtualLibrary` / `VirtualLibraryProvider` / `IVirtualLibraryNode` — 使用者存的 DSL query 變成動態虛擬庫，結果即時跑不快取。

**其他**：`Core/Theming/`（`ThemeManager` / `ThemeColors` / `LocalizationManager` / `IThemedControl`，見記憶 `project_theme_colors`）、`NaturalStringComparer`、`ReadRecordQueue`（閱讀計數延遲提交）。

### Models（`ComicOrganizer.App/Models/`）

`AppConfig`（路徑、外部 viewer、Blacklist/BlacklistPatterns、外部 metadata 與翻譯/OCR 相關設定、DPAPI 密文等）、`UserPreferences`（主題 / 語言 / reader 偏好）、`AppTheme`、`ArchiveItem` / `ArchiveGroup`（歸檔 Table 綁定）、`GalleryListItem` / `MoveTask` / `AppConfigExtensions`。

### UI 層（`ComicOrganizer.App/UI/`）

使用 **AntdUI 2.3.12**（WinForms 上的 Ant Design 風格元件庫），不是原生 Forms 或 WPF；`MainForm` 繼承 `AntdUI.Window`。**AntdUI 有大量踩坑，動 UI 前先看記憶 `feedback_antdui_quirks`**。整體 UI 層級見記憶 `project_ui_architecture`。

- **`GalleryControl`（漫畫庫管理）** — 拆成多個 partial：`.cs`（主邏輯 + view mode）、`.Tree`（左側導覽，Folder / ByAuthor / ByTag / MyLib / ReadingTrack 多模式）、`.Gallery`（卡片網格 `ComicCardControl`）、`.Tabs`（per-tab view mode）、`.Search`（搜尋條 + DSL）、`.Dashboard`（首頁最近閱讀）、`.Drawer`（右側詳情抽屜 `DetailDrawerHost` / `WorkDetailPanel` / `AuthorInfoPanel`）、`.ExternalSync`（單本/批量 nhentai 同步）。
- **`ArchiveControl`（自動歸檔）** — 掃描 `UnorganizedDirectory` 產生 `ArchiveItem`（勾選框、多選、右鍵菜單、跨磁碟卷移動），按信心排序；「預覽歸檔計畫」開 `PreviewForm`（`AntdUI.Collapse` 依目標目錄分群，每群有 Switch 執行/略過 + 刪除，`ExecuteArchiveAsync()` 真正搬檔 `Directory.Move`/`File.Move`，成功後 `_intelligence.Learn(kw, 目標資料夾)`）。
- **`MetadataManageControl`（詮釋資料管理）** — 三 section：`TagManagePanel` / `AuthorManagePanel`+`AuthorInfoPanel`+`AuthorWorksListPanel` / `MetadataToolsPanel`（重新解析、重建索引、更新翻譯庫）。作者面板的「同步選取作品」透過 `RequestBatchSync` / `RequestSyncOne` 事件委派回 `GalleryControl`。
- 其他 Form/Dialog：`ReaderForm`（內建閱讀器，翻頁/雙頁/連續捲軸/書籤/OCR+翻譯 toolbar）、`SettingsForm`、`RebuildCacheForm`（非 modal `Show()`，背景跑可關閉）、`NhentaiSearchPickerDialog` / `ExternalMetadataMergeDialog` / `BatchSyncModeDialog`、`TagPickerDialog` / `AuthorPickerDialog`、`UpdateTranslationDbDialog`。

### 資料持久化一覽（皆在 exe 同層 `BaseDirectory`）

| 路徑 | 內容 |
|------|------|
| `appsettings.json` | `AppConfig`（含 DPAPI 加密的 nhentai key） |
| `preferences.json` | `UserPreferences`（主題 / 語言 / reader 偏好） |
| `mappings.json` | `MappingIntelligence` 學過的關鍵字 → 目錄映射 |
| `cache.db` | SQLite metadata catalog（asset / metadata / author / tag / bookmark / virtual_library / external source…） |
| `thumbs/` | 縮圖檔（雜湊命名） |
| `translations/db.text.json.gz` | EhTag 翻譯庫 disk cache（無則用 embedded） |
| `inference/` | PaddleOCR 模型檔 |

### 測試（`ComicOrganizer.Tests/`，xUnit + `ITestOutputHelper`）

涵蓋面已遠超歸檔本身。重點分組：
- **歸檔/學習**：`SelfLearningTests`（改 `ArchiveOrganizer` 比對邏輯的主要回歸網）、`RealDataHybridTest`（兩階段：富資訊檔名「教」→ 簡化檔名驗證 `IsLearned=true`）、`PersistenceTests`（獨立 `test_mappings.json` 驗 save/load）。
- **解析/metadata**：`FilenameMetadataParserTests`、`FolderRoleAnalysisTest` / `FolderRoleDetectorTests`、`MetadataPersistenceTest` / `MetadataEditTests` / `ScanMetadataIntegrationTest` / `AuthorAggregationTests`。
- **快取/DB**：`CatalogReconcileTests`、`ReparseAllTests`、`MigrationV1V2Test`、`BookmarkStoreTests`。
- **外部/翻譯**：`NhentaiApiClientTests`、`ExternalSourceTests`、`TagTranslationTests` / `TagTranslationStoreTests` / `TagTranslationUpdaterTests`、`TranslationTest`、`RealTranslationFlowTest`、`OcrTest`。
- **搜尋/虛擬庫**：`SearchDslTests`、`VirtualLibraryQueryTests` / `VirtualLibraryStoreTests`。
- **閱讀器/效能**：`ContinuousLayoutTests`、`FontSizeConsistencyTests`、`CoverDecodePerfTest` / `FolderCoverPerfTest` / `WebpOptimizationTest` / `ImageFormatBenchmarkTest`。
- **大批量人工觀察**：`FullRealDataBatchTest`（寫死真實路徑，不 assert）。

## 規範與常見坑

- **回應一律繁體中文**（個人 global 規則）；程式註解既有風格也是繁中，請延續。
- 流程：**思考與討論 → 規劃 → 列工作項目 → 逐項執行（執行前確認）→ 測試**；每項完成後先讓使用者確認再進下一項，不要一次跑完整批改動。
- 對 UI 的非同步操作必須切回 UI thread（`InvokeRequired` → `BeginInvoke`，既有程式如 `MainForm.UpdateStatusFromTracker` / `ShowStatus` 有完整範例，照抄即可）。
- 服務初始化有**時序相依**：`ThemeManager.Initialize` 必須在 `InitializeComponent` 前（否則 Designer 取到錯主題色）；各 UserControl 的 `Initialize(...)` 在 `Form.Load` 才呼叫（handle 已建立）。
- 修改任何 `*.Designer.cs` 時要確保 Designer 區與對應 `.cs` 的 event 綁定一致 —— 曾因 Designer 變更未提交導致建置失敗（見 commit `e5e08d7`）。VS Designer regenerate 會還原寫死的 ARGB 主題色（見記憶 `project_theme_colors`）。
- 知識庫（`mappings.json`）的 key 一律以**小寫正規化形式**儲存；新增比對流程時別忘了同樣 `ToLower()` 才能命中。
- `CacheWriter` / `SqliteAssetMetaStore` 的 dispose 有先後順序（見上）；SqliteConnection 非 thread-safe，DB 存取靠內部 lock 序列化，別繞過。
- DPAPI 密文不可硬塞明文 token 進 `appsettings.json`，一律走 `SecureStringStore` / `AppConfigExtensions`。
- 進行中工作與待辦見記憶 `project_52_53_progress`；roadmap（`GEMINI.md`）：歸檔衝突選擇介面、Undo、空間檢查尚未實作。
