# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案性質

ComicOrganize 是一套**漫畫/圖集自動整理工具**，正在從 `script/圖片整理.ps1`（命令列 PowerShell legacy）轉型為 `src/` 下的 **C# WinForms 視覺化應用程式**。新版核心功能：智慧封面提取、模糊匹配自動歸檔、自我學習關鍵字 → 目錄映射。

開發時應**以 `src/` 下的 C# 專案為主**；PowerShell 腳本只是規格參考，不應再擴充。

## 常用指令

所有指令都從 `src/` 下執行（solution 為 `ComicOrganize.slnx`）。

```bash
# 建置整個 solution
dotnet build src/ComicOrganize.slnx

# 執行 WinForms 應用
dotnet run --project src/ComicOrganize.App/ComicOrganize.App.csproj

# 執行所有 xUnit 測試
dotnet test src/ComicOrganize.Tests/ComicOrganize.Tests.csproj

# 執行單一測試（依方法名）
dotnet test src/ComicOrganize.Tests/ComicOrganize.Tests.csproj --filter "FullyQualifiedName~Test_JapaneseAuthorLearning_ShouldWork"

# 依測試類別過濾
dotnet test src/ComicOrganize.Tests/ComicOrganize.Tests.csproj --filter "FullyQualifiedName~SelfLearningTests"
```

注意事項：
- 目標框架是 `net8.0-windows`（WinForms），只能在 Windows 上 build/run。
- `FullRealDataBatchTest` 內**寫死了 `E:\Project\...\Tests\H` 路徑**用於批次驗證真實資料；在沒有這份資料的機器上會掃到空目錄但不會 fail。要重現須準備該測試資料。
- 沒有 lint 設定，但 `Nullable` 與 `ImplicitUsings` 都啟用，請維持 nullable 標註。

## 架構地圖

UI 與業務邏輯解耦：MainForm 在啟動時建構一條 service chain（`ConfigManager` → `AppConfig` → `MappingIntelligence` → `ArchiveOrganizer` + `CoverManager`），所有檔案系統與比對邏輯都收斂到 `Core/`。三個 Tab 對應三大功能區塊。

### Core 層（`ComicOrganize.App/Core/`）

- **`ConfigManager`** — JSON 持久化 `AppConfig`，預設儲存在 `AppDomain.CurrentDomain.BaseDirectory/appsettings.json`（也就是 `bin/.../appsettings.json`）。
- **`MappingIntelligence`** — 自我學習的關鍵字 → 目錄映射表，獨立持久化在 `mappings.json`。`Learn()` 會把 key 正規化（`ToLower().Trim()`）後寫入並立刻 `Save()`；`TryGetMappedFolder()` 會**依 key 長度由長到短**比對，避免短關鍵字誤觸。`PreviewForm` 在執行歸檔成功後才呼叫 `Learn()` —— 即「使用者確認過的成功移動」才會進入知識庫。
- **`ArchiveOrganizer.FindBestMatch()`** — 兩階段比對：
  1. 先查 `MappingIntelligence` 學過的關鍵字（命中即 `Confidence=100`、`IsLearned=true`）。
  2. 否則對每個目標資料夾算 `CalculateSmartScore`：完全包含 → 95、`ExtractAllKeywords` 命中 → 90，否則退回 `FuzzySharp.Fuzz.WeightedRatio`。
  - `ExtractAllKeywords` 用 regex `[\[\(\{](.*?)[\]\)\}]` 抽出括號內容，再用 `- × x , 空格 : | / \` 切詞，並過濾掉 `AppConfig.Blacklist`（如 "DL版"、"AI Generated"）與 `BlacklistPatterns`（如 `C\d{2,3}` Comic Market 編號）視為雜訊。**新增雜訊規則應加到 `AppConfig` 而非寫死在 `ArchiveOrganizer`**。
- **`CoverManager.GetCoverAsync()`** — 多型來源都統一回傳 `Image?`：
  - 資料夾：先找命名為 `cover.*` 的圖檔，否則取字典序第一張圖。
  - `.zip` / `.cbz`：用 `System.IO.Compression.ZipFile` 開啟，取字典序第一張圖。
  - 單檔圖片：直接讀取。
  - 全部走 `Task.Run` + `ResizeImage` 縮圖，不阻塞 UI。`SharpCompress` 已加入相依但目前只用了 BCL 的 ZipFile —— 若要支援 rar/7z 需改走 SharpCompress。

### Models（`ComicOrganize.App/Models/`）

- `AppConfig` — 來源/根目錄、外部圖片瀏覽器 exe 路徑、Blacklist、BlacklistPatterns、DebugMode。
- `ArchiveItem`（定義在 `MainForm.cs` 末段，**不是** `Models/`） — Table 綁定的 row，實作 `INotifyPropertyChanged`。
- `ArchiveGroup` — `PreviewForm` 用：依 `DestinationPath` 把 `ArchiveItem` 分組。

### UI 層

使用 **AntdUI 2.3.10**（WinForms 上的 Ant Design 風格元件庫），不是原生 Forms 或 WPF。`MainForm` 繼承 `AntdUI.Window`，三個 Tab：

1. **漫畫庫管理** — 左 `Tree` 列出 `RootDirectory` 子目錄，雙擊節點在右側 `tabsContent` 開新 tab，呼叫 `LoadGalleryAsync` 把每個項目顯示成 180x240 卡片（`PictureBox` + 標題 + 「閱讀」按鈕）。「閱讀」會用 `AppConfig.ImageViewerPath` 指定的外部 viewer 開檔，若未設定則 fallback 到系統預設（`UseShellExecute = true`）。
2. **自動歸檔** — `btnScan` → `ScanAsync()` 呼叫 `ArchiveOrganizer.FindBestMatch` 對 `UnorganizedDirectory` 下的每個項目產生 `ArchiveItem`，按信心分數排序填入 `tableArchive`。`btnExecute`（標題寫「預覽歸檔計畫」）打開 `PreviewForm`。
3. **系統設定** — 路徑設定與 `btnSaveConfig` → `ConfigManager.Save()`。

`PreviewForm` 用 `AntdUI.Collapse` 依目標目錄分群顯示，每群有 `Switch`（執行/略過）與「刪除」CellButton。`ExecuteArchiveAsync()` 真正搬檔（`Directory.Move` / `File.Move`），成功後對該檔名所有有效關鍵字呼叫 `_intelligence.Learn(kw, 目標資料夾名)`，這是知識庫成長的唯一入口。

### 測試

xUnit + `ITestOutputHelper`。重點：
- `SelfLearningTests` — 涵蓋日文、英日混合、學習後再匹配、純英文。改 `ArchiveOrganizer` 比對邏輯時這組是主要回歸網。
- `RealDataHybridTest.Test_StepByStep_DeepLearning` — 兩階段：先用富資訊檔名「教」，再用簡化檔名驗證 `IsLearned=true`。
- `PersistenceTests` — 用獨立 `test_mappings.json` 驗證 `MappingIntelligence` save/load。
- `FullRealDataBatchTest` — 寫死真實路徑做大批量人工觀察（不會 assert）。

## 規範與常見坑

- **回應一律繁體中文**（個人 global 規則）；程式註解既有風格也是繁中，請延續。
- 流程：**思考與討論 → 規劃 → 列工作項目 → 逐項執行（執行前確認）→ 測試**；每項完成後先讓使用者確認再進下一項，不要一次跑完整批改動。
- 對 UI 的非同步操作必須 `this.Invoke(...)` 切回 UI thread（既有程式有完整範例，照抄即可）。
- 修改 `MainForm.Designer.cs` 時要確保 Designer 區與 `MainForm.cs` 的 event 綁定一致 —— 之前曾發生 Designer 變更未提交導致建置失敗（見 commit `e5e08d7`）。
- 知識庫（`mappings.json`）的 key 一律以**小寫正規化形式**儲存；新增比對流程時別忘了同樣 `ToLower()` 才能命中。
- Roadmap（見 `GEMINI.md`）：歸檔衝突選擇介面、Undo、空間檢查目前都還沒實作。
