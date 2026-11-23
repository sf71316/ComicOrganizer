# Avalonia 12 效能工作紀錄：已完成與待完成

更新日期：2026-11-22
分支：`perf/avalonia12-gallery-virtualization`（由 `eval/avalonia12` 開出）

本文件整理 Avalonia 12 升級評估期間的效能工作：已完成哪些、實測數據、量測方式，以及還沒做的項目。

---

## 一、量測方式

- **4K 測試機**：Windows 11、RTX 4080、3840×2160、縮放 150%，畫廊 3,512 本作品，視窗最大化。
- **測試包（PerfKit）**：
  - 把使用者資料目錄**複製**一份；
  - 程式一律以 `--read-only` 啟動，不寫 cache.db，也不能歸檔、搬移或刪除；
  - 紀錄檔只有頁面名稱、數量、毫秒數和硬體資訊，不含作品名稱或路徑。
- **拖捲軸測試**（`COMIC_ORGANIZER_PERF_ONLY=drag`）：模擬三種拖法。
  1. 快速拖到底（1 秒）；
  2. 極快往回（0.5 秒）；
  3. 慢速拖一段（3 秒）。

  紀錄內容：
  - 每幀 UI 執行緒工作量；
  - 超過 50 ms 的卡頓及其組成（版面配置／GC／其他）；
  - 停下後畫面內封面多久補齊；
  - 封面來源（記憶體快取／縮圖檔／原始檔）與排隊長度；
  - 拖完後的記憶體分項。
- **本機重現**：用硬連結做出 3,200 本、沒有縮圖的資料複本，用 `dotnet-trace` 取樣 UI 執行緒，找出熱點。
- **暫時量測程式不進版控**：
  - 檔案：`Services/PerfTrace.cs`、`Services/CoverPerfCounters.cs`；
  - 標記「【暫時量測】」的呼叫；
  - `COMIC_ORGANIZER_EXP_*` 實驗開關。

  這些只在工作目錄中使用，合併前必須移除。

---

## 二、已完成

| Commit | 內容 | 實測 |
|---|---|---|
| `36709e0` | 升級 Avalonia 12.1.6 + SukiUI 7.0.1 | — |
| `8bae990` | 切頁時保留頁面（`KeepAlivePageHost`），不再每次重建 View | 切回畫廊／探索牆 1.5–2.1 s → 24–35 ms |
| `1eb0665` | 探索牆疊牌用點陣圖快取，Skia GPU 資源快取調為 256 MB | 4K 探索牆捲動 22.5 → 53.0 FPS（代價：顯示卡記憶體多約 360 MB） |
| `f4ad67b` | 探索牆封面依顯示大小解碼 | 非 .NET 記憶體約 −380 MB |
| `05e0339` | 畫廊虛擬化 + 修正 1–4（見下） | 見下 |

### `05e0339` 的細節

**畫廊虛擬化**
- 卡片格與列表改用 `ItemsRepeater`（`VerticalCacheLength` 0.5），一次放入全部作品，移除「載入更多」。
- 封面格子固定寬度。
- 4K 3,512 本：程式結束時的記憶體 8.3 GB → 4.5 GB。

**修正 1：卡片回收不再經過「空資料」狀態（`StableDataContextHost`）**
- 問題：ItemsRepeater 回收卡片時，一律先把 DataContext 設成 null（`MustClearDataContext`，無法關閉）。
- 這會引發一連串成本：
  - 所有繫結報錯；
  - 按鈕資料驗證；
  - 啟用狀態傳遍子元件；
  - 樣式與轉場重新套用。
- 修法：卡片外包一層容器，擋掉 null，直接換成新項目。
- 本機取樣（拖動 8 秒）：

  | 熱點 | 修正前 | 修正後 |
  |---|---|---|
  | 繫結報錯 | 354 ms | 0 ms |
  | 啟用狀態傳遞 | 486 ms | 5 ms |
  | 回收清空 | 464 ms | 22 ms |
  | UI 執行緒總忙碌時間 | 1,848 ms | 1,184 ms |

- 拖動時每幀 UI 工作量約減半：

  | 拖法 | 修正前 | 修正後 |
  |---|---|---|
  | 本機快速拖 | 17.5 ms | 9.0 ms |
  | 本機慢速拖 | 10 ms | 5.5 ms |
  | 4K 慢速拖 | 5.1／4.8 ms | 2.1–2.5 ms |
  | 4K 極快往回 | 22.9／17.6 ms | 9.7–12.2 ms |

**修正 2：卡片捲走就取消封面載入**
- 每張卡片有一個取消權杖，回收時取消還在排隊或解壓縮中的載入。
- 已取消的進行中載入不再沿用。
- 4K 結果：

  | 項目 | 修正前 | 修正後 |
  |---|---|---|
  | 封面排隊最多 | 1,100–1,300 張 | 50 張 |
  | 卡片捲走後才載完（白做工） | 每段 670–912 張 | 0 張 |
  | 停下後封面補齊 | 3 秒以上仍全缺 | 0.1–0.25 秒 |
  | 拖完再完整 GC 後的私有記憶體 | 4.9／4.3 GB | 2.8／2.2 GB |

**修正 3A：一般模式自動寫回縮圖（Core `ThumbWriteBackQueue`）**
- 現場從原始檔解出的封面，在背景寫回 `thumbs\` 與 cache.db 的縮圖索引。
- 規則：
  - 縮圖檔名的算法與「資料管理 → 增量更新縮圖」相同；
  - 略過：不在資料庫的作品、單張圖片、已有縮圖的作品；
  - 縮圖目錄達到 `ThumbCacheMaxBytes` 上限就停止寫入，不刪舊縮圖；
  - 唯讀模式完全不寫。
- 本機：第二次載入每張 9–12 ms → 1.1 ms。1,595 張縮圖共 15 MB。

**修正 4：放掉的封面早點回收原生記憶體（`BitmapReleaseGcTrigger`）**
- 被擠出記憶體快取的封面點陣圖**不 Dispose**，因為它可能仍顯示在卡片或詳細資訊抽屜上。
- 改為累計大小：達 256 MB（兩次至少間隔 2 秒）就觸發一次背景 GC。
- 本機：拖完、還沒完整 GC 時的其他原生記憶體 838／1,333 MB → 636／661 MB。代價是慢速拖 3 秒內的 GC 暫停合計多約 25 ms。
- 4K 尚未驗證：測試資料沒有縮圖，大部分封面在拖動中被取消，累積量未達門檻（最多 53 MB）。

**已確認不是問題**
- 4K「快速拖到底」後有 5 張卡片一直沒有封面：已確認是這 5 本的原始檔解不出封面，不是程式錯誤。

### 審查後修正（2026-11-22）

審查 `d6370d2`、`83fd816`（零繫結卡片、探索牆虛擬化、封面讀取降配置）時找到 5 個問題，已全部修正。每項都有測試，且確認修正前測試會失敗。

| Commit | 問題 | 修法 |
|---|---|---|
| `4aeac70` | 探索牆從「單張」切回「疊牌」時，對整面牆全部卡片預載副封面，且無法取消（虛擬化後清單裝的是全部卡片） | 只替已建立的卡片補載，並沿用各卡片自己的取消權杖 |
| `4aeac70` | 牆面查詢在進入鑽取頁後才回來時，會取消鑽取頁卡片的封面載入；進入鑽取頁時被回收的牆面卡片也會被遺忘、封面一直不放 | 封面追蹤只清被替換的那個清單（牆面 / 鑽取）；鑽取頁預載改用獨立取消來源 |
| `4aeac70` | 卡片改成不包 `StableDataContextHost` 後，回收時的 null / 新項目 DataContext 會傳遍卡片內約 60 個子元件 | 卡片內部視覺樹固定使用一個本地 DataContext，外層變更只通知卡片本身 |
| `6332af8` | 超過 64 MB 的單張圖片拿不到封面（改版前的 `SKData.Create(stream)` 沒有上限） | 只受單一陣列長度上限（約 2 GB）限制；緩衝區剛好讀滿時先試讀 1 byte，避免多配置兩倍緩衝區 |
| `6332af8` | .NET `ZipFile` 不讀 Unicode 路徑附加欄位（0x7075），舊工具以 Shift-JIS / Big5 存的檔名會變成亂碼，可能選到和改版前不同的封面 | 條目名含亂碼（U+FFFD）時整檔交給 SharpCompress，結果與改版前一致 |
| `19459b7` | 卡片卸下視覺樹期間退訂作品變更，掛回時沒重新套用，可能一直顯示舊封面或舊選取外框 | 掛回時重新訂閱並重新套用封面與選取狀態 |

本機取樣（3,600 本、有縮圖、拖捲軸同一段約 8 秒）：

| 項目 | `05e0339` | 同事版（`83fd816`） | `4aeac70` 之後 |
|---|---|---|---|
| UI 執行緒忙碌時間 | 910 ms | 735 ms | 524 ms |
| 卡片換資料（重新繫結） | 307 ms | 67 ms | 96 ms |
| 回收清空（`ClearElement`） | 11 ms | 141 ms | 22 ms |
| 版面配置（`ItemsRepeater.MeasureOverride`） | 386 ms | 391 ms | 181 ms |

- 每幀 UI 工作量（只跑 1 次，遠端桌面雜訊大，僅供參考）：快速拖 7.4 ms、極快往回 5.3 ms、慢速拖 4.1 ms。
- 文字排版（`TextBlock.CreateTextLayout`）前後沒有明顯差異（約 86–90 ms）。
- 已截圖確認：畫廊剛開時卡片內容正常；捲動後每張卡片的標題與封面一致。

### 重整不再從上往下重繪（`bf317ae`，2026-11-22）

處理下方「待完成」原第 7、8 項。重整或局部修改後，畫廊、探索牆、閱讀軌跡不再清空重建、跳回頂端並由上往下補封面。

| 範圍 | 做法 |
|---|---|
| 共用 | `BatchObservableCollection.SyncTo`：依參考比對做差異更新，內容完全相同時不發通知；變動超過 `max(64, 筆數/8)` 才整批重設。`CoverSlotVm.TryAdoptImageFrom`：來源路徑、縮圖 key、解碼尺寸都相同才沿用封面 |
| 畫廊 | 同一瀏覽範圍（DSL／目錄／側欄節點）重整時，沿用內容未變的 `GalleryItemVm`；有變的換新 VM，但接收舊封面、選取狀態與詳細資訊抽屜的指向。不重設捲動，只在重設捲動時預載封面（預載畫面外的項目會留下回收佇列追蹤不到的封面） |
| 畫廊側欄 | 節點內容未變則沿用，並保留選中節點的反白；改用 `ItemsRepeater` 虛擬化 |
| 列表模式 | 新增零繫結的 `GalleryListRowControl`（原範本 21 個繫結），與卡片共用 `GalleryItemControlBase` |
| 探索牆／鑽取頁 | 沿用作者、題材、組合、作品卡片與封面，同範圍不跳回頂端。record 的清單欄位只比參考，所以內容比對放在 Core 的 `ExploreContentEquality`。代表作依「未讀優先」重排時，疊牌第 2、3 張是小尺寸解碼，不會被沿用到第 1 張 |
| 閱讀軌跡 | 沿用項目與封面，保留已按「載入更多」展開的筆數；紀錄牆改用 `ItemsRepeater` 虛擬化（卡片高度統一）。「繼續閱讀」卡片高度不一且筆數少，維持 `ItemsControl` |

驗證：
- 新增 `RefreshReuseTests` 13 個與列表控制項測試 1 個；把修正關掉時關鍵測試會失敗。全部 741 個測試 735 個通過，6 個為既有需外部資源的測試。
- 實機（唯讀、3,600 本測試複本，與修改前版本對照）：

| 情境 | 修改前 | 修改後 |
|---|---|---|
| 畫廊捲動後按重整 | 跳回第 1 筆 | 停在原位，60 ms／260 ms 截圖都沒有空白佔位 |
| 側欄標籤模式重整 | 選中節點反白消失 | 保留 |
| 探索牆捲動後重整 | — | 停在原位，封面不閃空白 |
| 閱讀軌跡「最近新增」 | — | 卡片等高、封面正常、「載入更多」正常 |

- 探索牆重整時頂端會短暫出現進度條，內容被往下推約 14 px，載入完恢復。這是原本就有的行為，未處理。

### 跨目錄切換不再由上往下補封面（`dc90f24`，2026-11-23）

處理下方「待完成」第 8 項。進入子目錄或回到漫畫庫根目錄時，記憶體快取裡已有的封面在第一幀就畫出來；側欄、麵包屑與作品清單在同一次 UI 更新內完成。

文件列出的 4 個根因核對結果：

| # | 根因 | 結果 |
|---|---|---|
| 1 | 換範圍時新項目一律從沒有封面開始 | 成立 |
| 2 | 記憶體裡已有的封面仍要排隊、逐張回 UI 執行緒 | 成立，且機制多一層：`StartCoverPreload` 在卡片建立前就在背景搶走 4 個載入名額，卡片建立時沿用這筆進行中的載入；每張封面要等跳回 UI 執行緒設定完才交還名額，而 UI 正忙著排版，所以每輪只補 4 張 |
| 3 | 先換清單、後歸零捲動造成兩次排版 | 不成立：歸零是同步設定，和換清單在同一個 UI 工作內，版面只跑一次。未改 |
| 4 | 側欄／麵包屑與作品清單分兩拍更新 | 成立 |

做法：

| 項目 | 做法 |
|---|---|
| A | `SkiaCoverService.TryGetMemoryCachedCover`：只查記憶體 LRU，不讀磁碟。key 規則與非同步載入一致（有效縮圖 key 用 `thumb:`，否則 `src:`）。文件建議「先查縮圖 key 再查來源 key」不對：兩者是擇一，不是依序；另外要查「原尺寸」key（原圖比顯示框窄時以原尺寸存放），但只在原圖不比所需寬時沿用，不拿大張代替小框 |
| B | `CoverSlotVm.EnsureLoadedAsync` 在 UI 執行緒上先同步查記憶體快取，命中就直接設定封面（畫廊、探索牆、閱讀軌跡共用）；畫廊 `StartCoverPreload` 也先在 UI 執行緒查一次。排隊中的載入若已被補上封面就直接結束 |
| C | 封面解碼完就交還載入名額，不等跳回 UI 執行緒 |
| D | `ReloadSidebarAndItemsAsync` 背景一次算好側欄、範圍與作品清單，UI 一次套用（`ApplyScopePayloadOnUi`） |

驗證：
- 新增 `CrossFolderCoverTests` 4 個，其中 3 個修正前失敗（第 4 個測新增的查詢方法）。全部 745 個測試 739 個通過，6 個為既有需外部資源的測試。
- 實機：SandboxLarge 唯讀複本（根目錄 400 個作者資料夾，全部有縮圖），1920×1040 最大化。由程式自動切換目錄，在每次排版完成時數畫面內缺封面的卡片（PerfTrace `COMIC_ORGANIZER_PERF_ONLY=dirnav`，暫時量測）：

| 情境（各 4 輪） | 修改前 | 修改後 |
|---|---|---|
| 回根目錄 | 第一幀 10/10 缺封面，55–86 ms 補齊 | 0 缺 |
| 再進同一子目錄 | 10/10 缺，15–23 ms 補齊 | 0 缺 |
| 第一次進子目錄（要讀磁碟縮圖） | 10/10 缺，41–71 ms 補齊 | 0–1 缺，最慢 21 ms 補齊 |
| 每次切換排版次數 | 11–13 次 | 1 次（有缺時 2–3 次） |

封面記憶體快取上限（文件建議評估 512 → 1024）：連續逛 40 個沒看過的子目錄（約 800 張封面）後回根目錄：

| 上限 | 回根目錄第一幀缺封面 | 封面點陣圖 | 私有記憶體 |
|---|---|---|---|
| 512 | 6/10，8 ms 後補齊（從磁碟縮圖讀回約 1 ms／張） | 301 MB | 1,086 MB |
| 1024 | 0 | 541 MB | 1,324 MB |

結論：維持 512。被擠掉的封面不到一幀就補回，加大要多約 240 MB，且逛得更多或在根目錄捲得更深時一樣會被擠掉。

### 停用 SukiUI 7.0.1 `GlassCard` 預設由上往下裁切進場動畫（2026-11-23）

實機錄影（逐幀拆解 `動畫.gif`）確認：切換目錄與點回漫畫庫根目錄時，卡片封面其實皆已載入完畢，但每張卡片都會出現約 250–300 ms「從上往下掀開（如拉窗簾）」的裁切動畫。
- **根因**：升級 SukiUI 7.0.1 後，`SukiUI.Controls.GlassCard.IsAnimatedProperty` 預設值為 `True`，每次卡片掛上視覺樹（`GlassCard.OnAttachedToVisualTree()`）即觸發由上往下裁切進場動畫；先前僅停用 `Transitions` 不會關閉此動畫。
- **做法**：
  - 於 `App.axaml` 全域樣式對 `suki|GlassCard` 設定 `IsAnimated="False"`，同時於 `GalleryCardControl.axaml`、`GalleryListRowControl.axaml` 與 `GalleryPageView.axaml` 顯式設定 `IsAnimated="False"`。
  - 於 `GalleryCardTransitionTests` 補上 `Assert.False(card.IsAnimated)` 驗證（22/22 通過）。

---

## 三、待完成

依建議優先順序排列。

1. **在 4K 上驗證修正 4（需要有縮圖的資料）**
   - 使用者在正式資料用一般模式到「資料管理」重建快取（修正 3B）；
   - 重跑 `prepare.cmd`，讓測試複本也有縮圖；
   - 執行 `perf_v6_drag_max.cmd`。

   要確認：封面約 1 ms 就能載入時，背景 GC 有沒有把拖動後的原生記憶體壓下來，以及 GC 暫停是否可以接受。

2. **卡片換資料（重新繫結）的成本（已實作，本機已驗證，待 4K 驗證）**
   - 修正 1 之後，回收的卡片換成另一本作品時，整張卡片重新繫結：本機取樣約 301 ms（拖動 8 秒），約佔 UI 執行緒忙碌時間（1,184 ms）的 25%，是目前拖動時最大的一塊。
   - 組成：每個繫結換來源並重新取值約 150 ms、退訂舊作品變更通知約 43 ms、訂閱新作品約 31 ms、文字排版失效。
   - 更正：先前把 `Animatable.OnPropertyChangedCore` 誤判為「轉場動畫約 360 ms」；真正的轉場動畫（`ClockBase.Pulse` + `TransitionInstance`）約 50–90 ms。
   - 已處理：
     - 卡片格網改為零 XAML 繫結的 `GalleryCardControl`：每張卡片由 24 個獨立 `{Binding}` 降為 0 個、24 次 `INotifyPropertyChanged` 退訂／訂閱降為 1 次（僅監聽 `Cover1`、`HasCover1`、`IsSelected`），7 顆按鈕／選單改由 Click 事件直連，並忽略回收時的 `null` `DataContext`。
     - 順手將每張卡片上 SukiUI 與封面的 39 條 `Transitions` 清零（`GlassCard` / `Button` / 模板 `Border`/`ContentPresenter`/`Viewbox` / 封面 `Image`）。
     - 修正 `StableDataContextHost` 在 `OnPropertyChanged` 搶先寫入 `child.DataContext` 導致 Avalonia 12 首次載入截斷 `EndUpdate`、未捲動前繫結不取值的問題（改於 `OnDataContextEndUpdate()` 之後鎖定）。
   - 本機取樣：重新繫結 307 → 96 ms，UI 執行緒忙碌 910 → 524 ms（見「審查後修正」）。

3. **文字排版成本（已實作；本機取樣看不出改善，待 4K 驗證）**
   - `TextBlock.CreateTextLayout` 在修正 1 之後仍約 208 ms（拖動 8 秒）。
   - 已處理：
     - 封面載入後隱藏佔位底色與 `FallbackGlyph`（`IsVisible="{Binding !HasCover1}"`），有縮圖時直接跳過 `FontSize=36` 佔位字元排版；
     - 標題 `StackPanel` 顯式綁定 `Width="{DynamicResource GalleryCardCoverWidth}"`，避免 `TextWrapping`/`TextTrimming` 在 Measure 與 Arrange 階段以不同寬度重算兩次 `TextLayout`；
     - `GalleryItemVm` 顯示字串（`PageCountText`、`SizeDisplay`、`ModifiedDisplay`、`SubtitleText`、`PrimaryActionTooltip`、`TypeBadgeText`）改為建構時快取，消除回收重用時的重複格式化與字串配置。
   - 本機取樣（有縮圖）：`TextBlock.CreateTextLayout` 整段約 226 → 231 ms，沒有明顯差異。

4. **快速拖到底的每幀成本**
   - 這種拖法每幀都跳過超過一個畫面，所以每幀要重建約 42 張卡片（約每張 60 個視覺節點）。
   - 4K 上同一版本有時約 16 ms/幀、有時約 45 ms/幀，呈現兩種結果，原因不明。
   - 需要先找出原因，再考慮捲動很快時延後建立卡片等做法。

5. **探索牆虛擬化（已實作，待 4K 實測驗證）**
   - 探索牆 4 組清單（作者／主題／雙組合／鑽取作品）改用 `ItemsRepeater` + `UniformGridLayout` + `StableDataContextHost`（`VerticalCacheLength=0.5`），切換頁籤時清空隱藏清單以立刻回收卡片與封面。
   - `ExplorePageViewModel` 加入 `NotifyCardRealized` / `NotifyCardRecycled`（每卡獨立取消權杖 + `RecycledCoverRetention=60` 回收佇列）；`DeckCoverControl` 快取色碼 Brush 組合。

6. **探索牆載入產生的垃圾（已定位並大幅削減，待 4K 實測驗證）**
   - 實測 `D:\ComicOrganizer` 拆解確認：1.4 GB .NET 垃圾主要來自無縮圖環境下 `SkiaCoverService` 現場解壓縮封面的 LOH `byte[]` 倍增配置（55 張即配置 230.87 MB）。
   - 已將 `.zip/.cbz` 優先改走 `System.IO.Compression.ZipFile` 並以 `ArrayPool<byte>.Shared` + `SKData.CreateCopy` 消除 `MemoryStream` 倍增與二次複製，同條件實測 .NET 配置量 `230.87 MB → 101.88 MB`（`-55.9%`）。

7. **重整從上往下重繪、側欄與列表模式未零繫結／虛擬化（已完成，`bf317ae`）**
   - 細節見「二、已完成 → 重整不再從上往下重繪」。
   - 其餘頁面已盤點：`AutoArchivePageView.axaml`（`VisibleCandidates` 為非虛擬化 `ItemsControl` 但純文字無封面，僅大量掃描時影響低）、`DataManagementPageView.axaml`（主清單已用虛擬化 `ListBox`）、`VisualSearchPageView.axaml`（僅 Top-K 結果）皆無明顯重整閃爍問題。

8. **畫廊跨目錄切換（進入子目錄／點選「🏠 漫畫庫根目錄」）仍觸發由上往下重繪（已完成，`dc90f24`）**
   - 細節、根因核對結果與快取上限實測見「二、已完成 → 跨目錄切換不再由上往下補封面」。以下保留原始盤點。
   - 實機驗證（`55a0c27`）：同目錄「↻ 重整」已不再重繪，但**點擊畫廊中的子目錄**或**點擊左側「🏠 漫畫庫根目錄」**切換目錄時，畫面依然會出現明顯由上往下補圖／重繪波浪。
   - **根因盤點**：
     1. **`sameScope == false` 時新項目一律以 `Cover1 = null` 開始**：在 `GalleryPageViewModel.LoadCurrentScopeItemsCoreAsync`（約 L1282–L1290），跨目錄切換因 `scopeKey != _loadedScopeKey` 使 `sameScope = false`，建立全新一批 `GalleryItemVm` 並執行 `Items.SyncTo(filtered)`（觸發 `ReplaceAll` / `Reset`）。新項目建構時未同步查詢 `SkiaCoverService` 記憶體 LRU 快取（`_cacheMap`），導致第一幀所有卡片一律先畫成彩色佔位底圖＋`FallbackGlyph`。
     2. **已命中 RAM 快取的封面仍走背景排隊＋逐張回拋 UI 執行緒**：`ApplyFilterAndSortOnUi` 尾端呼叫 `StartCoverPreload(filtered, 24, cancelPrevious: true)`（L1975），在 `Task.Run` 背景執行緒逐張呼叫 `EnsureCoverLoadedAsync`；而 `CoverSlotVm.LoadCoreAsync`（`ExploreCardViewModels.cs` L430–L475）未在進 `concurrencyLimiter.WaitAsync` 前先同步檢查 `SkiaCoverService` 記憶體快取，導致即使是 RAM 內 0 ms 可取的 `Bitmap` 仍先等背景排程、再分批 `runOnUiAsync` 回 UI 執行緒逐張替換佔位圖，形成肉眼可見的由上往下重刷。
     3. **`ScrollResetRequested` 順序晚於 `Items.SyncTo(filtered)`**：在 `ApplyFilterAndSortOnUi`（L1963–L1970）先執行 `Items.SyncTo(filtered)` 觸發集合 `Reset`（此時 `ScrollViewer.Offset` 仍停在舊目錄的捲動位置），之後才觸發 `ScrollResetRequested?.Invoke()` 將捲動歸零，使 `ItemsRepeater` 在單次切目錄內產生兩次排版與實現範圍變動。
     4. **`ReloadSidebarAndItemsAsync` 拆成兩段獨立 Background → UI 更新**：在 `GalleryPageViewModel.ReloadSidebarAndItemsAsync`（L1159–L1216），先 `Task.Run(BuildSidebarNodes)` 並切回 UI 更新側欄與麵包屑，再啟動第二次 `Task.Run(ResolveScopePayload)` 並再次切回 UI 更新 `Items`，讓視覺切換被拆成兩拍。
   - **建議修正方向**：
     1. 在 `SkiaCoverService` 公開純記憶體 O(1) 查詢方法 `TryGetMemoryCachedCover(sourcePath, thumbKey, decodeSize)`（僅查 `_cacheMap`，不做任何磁碟 I/O；先查 `BuildThumbCacheKey` 再查 `BuildSourceCacheKey`），並評估將 `maxMemoryCacheItems` 由 `512` 適度提高（例如 `1024`），避免根目錄（約 380+ 個作者目錄）與子目錄往返切換時將根目錄封面擠出 LRU。
     2. 在 `CoverSlotVm.EnsureLoadedAsync` 進鎖／背景前（以及 `ApplyFilterAndSortOnUi` 將新項目送入 `Items.SyncTo` 之前），若 `TryGetMemoryCachedCover` 命中，直接於 UI 執行緒同步設定 `Image` 與 `_completed = true`，讓卡片在 `ElementPrepared` 第一幀即具備封面，消除佔位閃爍與逐張補圖波浪。
     3. `ApplyFilterAndSortOnUi` 中若 `resetScroll == true`，改為**先觸發 `ScrollResetRequested?.Invoke()`** 再執行 `Items.SyncTo(filtered)`。
     4. `ReloadSidebarAndItemsAsync` 合併成單次 `Task.Run` 同時計算側欄／麵包屑與目標清單，再於同一次 `_runOnUiAsync` 批次套用至 UI。

9. **合併前清理**
   - 移除暫時量測程式（見「一、量測方式」）。
   - 決定 Avalonia 12 升級評估的結論，以及是否合併回 `master`。
