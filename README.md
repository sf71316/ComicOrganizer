# ComicOrganizer

漫畫 / 圖集 自動整理工具，C# WinForms 應用程式。智慧封面提取、模糊匹配自動歸檔、自我學習關鍵字對應目錄。

## 功能特色

### 漫畫庫管理
- 樹狀目錄瀏覽（左側 TreeView）+ 多分頁卡片瀏覽（右側 Tabs）
- 三種卡片尺寸（小 / 中 / 大）+ 列表模式四種檢視，**每個 Tab 獨立模式**
- 智慧封面提取：資料夾首圖 / `cover.*` 命名 / `.zip` `.cbz` 內首圖（受 `MaxScanDepth` 控制遞迴）
- 列表模式 hover 顯示大圖預覽（floating popup，跟隨滑鼠）
- 中鍵點擊 tree 節點開新分頁；右鍵選單支援另開分頁 / 重新整理
- 支援 WebP（透過 SixLabors.ImageSharp，補 GDI+ 不認 webp 的洞）

### 自動歸檔
- 兩階段比對：
  1. 自我學習關鍵字 → 目錄表（`MappingIntelligence`）
  2. 否則用 `FuzzySharp.WeightedRatio` + 智能加權（完全包含 95、關鍵字命中 90）
- 黑名單規則排除雜訊（"DL版"、"AI Generated"、Comic Market 編號 `C\d{2,3}` 等）
- 預覽介面分群顯示（依目標目錄）+ 信心分數排序，使用者確認後才搬檔
- 「使用者確認的成功移動」進入學習庫，下次更聰明

### 系統設定
- Light / Dark / System 三主題切換（即時生效）
- 多語介面（繁中 / English）
- 路徑設定 + 外部圖片瀏覽器 + 啟動參數模板（`{path}` 佔位符）
- `MaxScanDepth` 控制封面遞迴深度

## 技術堆疊

| 類別 | 套件 / 技術 |
|---|---|
| Runtime | .NET 8 (`net8.0-windows7.0`) |
| UI 框架 | WinForms + [AntdUI 2.3.11](https://github.com/AntdUI/AntdUI)（Ant Design 風格元件庫）|
| 圖檔解碼 | System.Drawing.Common + SixLabors.ImageSharp（WebP 支援）|
| 模糊比對 | FuzzySharp |
| 壓縮檔 | System.IO.Compression（ZIP/CBZ）|
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
```

> ⚠️ 目標 framework `net8.0-windows`（WinForms），只能在 **Windows** 上 build / run。

## 專案結構

```
src/
├── ComicOrganizer.slnx
├── ComicOrganizer.App/
│   ├── Core/                     # 核心邏輯
│   │   ├── ConfigManager.cs       # JSON 設定持久化
│   │   ├── MappingIntelligence.cs # 學習庫（mappings.json）
│   │   ├── ArchiveOrganizer.cs    # 兩階段比對 + 智能評分
│   │   ├── CoverManager.cs        # 封面提取（資料夾/zip/單檔）
│   │   ├── ThemeManager.cs        # 主題切換廣播
│   │   ├── ThemeColors.cs         # 主題色 token
│   │   ├── LocalizationManager.cs # 多語管理
│   │   └── IThemedControl.cs      # 主題/i18n 自管 interface
│   ├── Models/                    # 資料模型
│   ├── UI/
│   │   ├── MainForm.*             # 主視窗
│   │   ├── GalleryControl.*       # 漫畫庫管理（左 Tree + 右 Tabs）
│   │   ├── ArchiveControl.*       # 自動歸檔
│   │   ├── PreviewForm.*          # 歸檔預覽 + 確認
│   │   ├── SettingsForm.*         # 設定
│   │   ├── ComicCardControl.*     # 漫畫卡片（含 hover 工具列、tooltip）
│   │   └── BufferedFlowLayoutPanel.cs   # FlowLayoutPanel 加 DoubleBuffer
│   └── Properties/
│       ├── Resources.resx         # 預設資源
│       ├── Resources.zh-TW.resx   # 繁中
│       └── Resources.en-US.resx   # English
└── ComicOrganizer.Tests/
    ├── SelfLearningTests.cs       # 自我學習回歸測試
    ├── PersistenceTests.cs        # 學習庫存取
    ├── RealDataHybridTest.cs      # 兩階段學習驗證
    └── FullRealDataBatchTest.cs   # 大批量真實資料驗證
```

## 設定檔

| 檔案 | 預設位置 | 用途 |
|---|---|---|
| `appsettings.json` | `bin/.../appsettings.json` | 來源/根目錄、語言、主題、外部 viewer 等 |
| `mappings.json` | App 工作目錄 | 學習得到的「關鍵字 → 目錄名」對應 |

## Roadmap

- [ ] **封面 / 元資料快取庫**（SQLite + thumbnail files）大幅縮短重複進入目錄的載入時間
- [ ] **FileSystemWatcher 增量更新**，不必整 lib 重掃
- [ ] **作者階層識別**（多種庫結構模板 + FolderRoleLearner 學習庫）
- [ ] **封面預繪 letterbox canvas**：載入後先繪到帶 letterbox bg 的固定尺寸 Bitmap，typeStrip overlay 完全融入背景；視覺最完整但實作較複雜

## 授權

私人專案。
