#define DEBUG
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.UI;

public partial class ReaderForm : Window, IThemedControl
{

	private class TranslationWork
	{
		public int PageIndex { get; set; }

		public bool IsHighPriority { get; set; }

		public CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();
	}

	private enum TranslationStep
	{
		Ocr,
		Translating,
		Applying,
		Applied,
		Failed
	}

	private record BookmarkMenuTag(string Action, Bookmark Bookmark, int CursorIndex);

	private readonly IReaderSource _source;

	private readonly bool _rtl;

	private readonly bool _spreadFromCover;

	private readonly IAssetMetaStore? _metaStore;

	private int _currentIndex;

	private int _lastSavedPage = -1;

	private readonly DateTime _openedAt = DateTime.UtcNow;

	private int _pagesVisited;

	private bool _readRecorded;

	private bool _completedRecorded;

	private readonly int _readThresholdSeconds;

	private readonly int _readThresholdPages;

	private readonly bool _trackReadingRecord;

	private readonly System.Windows.Forms.Timer _recordCheckTimer = new System.Windows.Forms.Timer
	{
		Interval = 5000
	};

	private ReaderViewMode _viewMode = ReaderViewMode.SinglePage;

	private ReaderZoomMode _zoomMode = ReaderZoomMode.FitPage;

	private float _customZoom = 1f;

	private const float MinZoom = 0.1f;

	private const float MaxZoom = 8f;

	private ContinuousLayout? _layout;

	private readonly System.Windows.Forms.Timer _scrollDebounce = new System.Windows.Forms.Timer
	{
		Interval = 500
	};

	private int _virtualScrollY;

	private readonly Dictionary<int, Image> _cache = new Dictionary<int, Image>();

	private readonly HashSet<int> _loading = new HashSet<int>();

	private readonly object _cacheLock = new object();

	private const int PreloadRadius = 6;

	private const int MaxCacheSize = 24;

	private Image? _leftImage;

	private Image? _rightImage;

	private bool _enableTranslation = false;

	private IOcrService _ocrService = new WindowsOcrService();

	private readonly LlmTranslationService _translationService = new LlmTranslationService(new HttpClient());

	private readonly Dictionary<int, List<OcrBlock>> _translatedPageCache = new Dictionary<int, List<OcrBlock>>();

	// 路線 2：每頁「抹除原文後」的乾淨頁圖（CTD 文字遮罩 + TextEraser）。有此圖時 overlay 跳逐欄白底。
	private readonly Dictionary<int, Bitmap> _erasedPageCache = new Dictionary<int, Bitmap>();

	// 每個 block 的氣泡輪廓(flood-fill envelope，來源影像座標)。null = 偵測失敗(漏出/網點底) → fallback 走 shaped。
	private readonly Dictionary<OcrBlock, BubbleEnvelope?> _envelopeCache = new Dictionary<OcrBlock, BubbleEnvelope?>();
	private bool _debugEnvelope;   // DebugMode 時在譯文上疊 envelope 綠框(診斷溢出/漏出用)

	// 方案2 預縮快取：來源頁圖 → 預縮到顯示尺寸的點陣圖(鍵=來源 Image 物件)。paint 1:1 blit、免每次即時縮放。
	// 由 LoadPageAsync 背景預熱(單頁模式)；來源圖 dispose 時須同步 DropScaled。
	private readonly Dictionary<Image, (int w, int h, Bitmap bmp)> _scaledCache = new Dictionary<Image, (int, int, Bitmap)>();
	private readonly object _scaledLock = new object();

	// 左鍵在內容區按住拖曳 = 移動整個閱讀器視窗(HoneyView 拖窗手感)；以螢幕座標錨定起點。
	private bool _panning;
	private Point _panStartScreen;      // 拖曳起始時的螢幕游標座標
	private Point _formStartLocation;   // 拖曳起始時的視窗左上角位置

	// 文字遮罩提供者（可抽換：ComicTextDetector / 未來 flood-fill / null=關閉走現行矩形）。
	private ITextMaskProvider? _maskProvider;

	private readonly object _translationLock = new object();

	private readonly SemaphoreSlim _llmSemaphore;

	private readonly List<TranslationWork> _translationQueue = new List<TranslationWork>();

	private readonly Dictionary<int, TranslationWork> _activeWorks = new Dictionary<int, TranslationWork>();

	private readonly object _queueLock = new object();

	private readonly List<Task> _workerTasks = new List<Task>();

	private CancellationTokenSource? _queueCts;

	private readonly Dictionary<int, TranslationStep> _pageTranslationSteps = new Dictionary<int, TranslationStep>();

	private readonly object _stepLock = new object();

	private bool _isInstantTranslationActive = false;

	private bool _isAllTranslationActive = false;

	private FormBorderStyle _prevBorderStyle;

	private FormWindowState _prevWindowState;

	private DateTime _lastWheelPageTurnTime = DateTime.MinValue;

	private bool _isFullscreen;

	private long? _assetId;

	private int _bookmarkCursor = -1;

	private static readonly Color ReaderContentBg = Color.FromArgb(99, 98, 99);

	private static readonly Color LoadingPlaceholderBg = Color.FromArgb(80, 80, 80);



	private bool IsContinuous => _viewMode == ReaderViewMode.ContinuousScroll;

	public ReaderForm(IReaderSource source, bool rightToLeft = true, bool dualPageSpreadFromCover = false, IAssetMetaStore? metaStore = null, ReadRecordQueue? readRecordQueue = null, int readThresholdSeconds = 30, int readThresholdPages = 2, bool trackReadingRecord = true)
	{
		InitializeComponent();
		// scrollHost 是 AutoScroll Panel 且預設無雙緩衝 → 翻頁時 picPage 重定位會讓 Panel 重繪背景而閃爍/頓挫；
		// 開雙緩衝(含 picPage)消除翻頁頓挫感。
		EnableDoubleBuffer(scrollHost);
		EnableDoubleBuffer(picPage);
		AppConfig appConfig = new ConfigManager().Load();
		_debugEnvelope = appConfig.DebugMode;
		_ocrService = appConfig.OcrEngineType == 1 ? new PaddleOcrService() : new WindowsOcrService();
		_maskProvider = appConfig.MaskEngineType == 2 ? new ComicTextDetector() : null;
		_useGpuPref = appConfig.UseGpuRendering;
		int num = Math.Max(1, appConfig.TranslationMaxConcurrency);
		_llmSemaphore = new SemaphoreSlim(num, num);
		StartWorkers(num);
		_source = source;
		_rtl = rightToLeft;
		_spreadFromCover = dualPageSpreadFromCover;
		_metaStore = metaStore;
		_readThresholdSeconds = Math.Max(1, readThresholdSeconds);
		_readThresholdPages = Math.Max(1, readThresholdPages);
		_trackReadingRecord = trackReadingRecord;
		readRecordQueue?.Cancel(source.RootPath);
		_recordCheckTimer.Tick += delegate
		{
			if (base.IsDisposed)
			{
				_recordCheckTimer.Stop();
			}
			else if (_readRecorded)
			{
				_recordCheckTimer.Stop();
			}
			else
			{
				TryRecordRead();
			}
		};
		_recordCheckTimer.Start();
		int? num2 = _metaStore?.GetLastPage(source.RootPath);
		_currentIndex = ((num2.HasValue && num2.Value >= 0 && num2.Value < source.PageCount) ? num2.Value : 0);
		_assetId = _metaStore?.GetAssetId(source.RootPath);
		Text = source.DisplayName;
		windowBar.SubText = source.DisplayName;
		ApplyThemeColors();
		btnStatusLoading.SendToBack();
		this.WireThemeAndLocalization();
		picPage.Image = null;
		picPage.Paint += OnPicPagePaint;
		WireEvents();
		// GPU 畫布在 Load(handle 已建)才建，GL 失敗偵測才可靠 → 見 ReaderForm.Gpu.cs
		base.Load += delegate { InitGpuCanvas(); };
		if (source.PageCount > 0)
		{
			GoToPage(_currentIndex);
		}
		else
		{
			lblStatus.Text = "(無圖檔)";
		}
	}

	public void ApplyThemeColors()
	{
		BackColor = ReaderContentBg;
		windowBar.BackColor = ThemeColors.Primary;
		windowBar.ForeColor = ThemeColors.HeaderText;
		toolbar.BackColor = ThemeColors.TabHeader;
		statusBar.BackColor = ThemeColors.TabHeader;
		scrollHost.BackColor = ReaderContentBg;
		picPage.BackColor = ReaderContentBg;
		lblStatus.ForeColor = ThemeColors.Text;
		btnStatusLoading.ForeColor = ThemeColors.Text;
	}

	public void ApplyLocalization()
	{
	}

	private void WireEvents()
	{
		sliderZoom.SetMinMax(20, 120);
		sliderZoom.Value = (sliderZoom.MinValue + sliderZoom.MaxValue) / 2;
		btnFullscreen.Click += delegate
		{
			ToggleFullscreen();
		};
		btnBookmarkToggle.Click += delegate
		{
			ToggleBookmarkOnCurrentPage();
		};
		btnBookmarkList.Click += delegate
		{
			OpenBookmarkList();
		};
		if (!_assetId.HasValue)
		{
			btnBookmarkToggle.Enabled = false;
			btnBookmarkList.Enabled = false;
		}
		segViewMode.SelectIndexChanged += delegate(object s, IntEventArgs e)
		{
			OnViewModeChanged(e.Value);
		};
		segZoomMode.SelectIndexChanged += delegate(object s, IntEventArgs e)
		{
			OnZoomModeChanged(e.Value);
		};
		sliderZoom.ValueChanged += delegate
		{
			OnSliderZoomChanged();
		};
		switchSeamless.CheckedChanged += delegate
		{
			OnSeamlessChanged();
		};
		btnInstantTranslation.Click += delegate
		{
			if (_isInstantTranslationActive)
			{
				_isInstantTranslationActive = false;
				btnInstantTranslation.Type = TTypeMini.Default;
				if (!_isAllTranslationActive)
				{
					_enableTranslation = false;
				UpdateCanvasMode();
				}
				CancelInstantTranslation();
				picPage.Invalidate();
				UpdateStatusBar();
			}
			else
			{
				_isInstantTranslationActive = true;
				_isAllTranslationActive = false;
				btnInstantTranslation.Type = TTypeMini.Primary;
				btnAllTranslation.Type = TTypeMini.Default;
				_enableTranslation = true;
				UpdateCanvasMode();
				CancelAllTranslation();
				OnPageSpreadChanged();
				picPage.Invalidate();
				UpdateStatusBar();
			}
		};
		btnAllTranslation.Click += delegate
		{
			if (_isAllTranslationActive)
			{
				_isAllTranslationActive = false;
				btnAllTranslation.Type = TTypeMini.Default;
				if (!_isInstantTranslationActive)
				{
					_enableTranslation = false;
				UpdateCanvasMode();
				}
				CancelAllTranslation();
				picPage.Invalidate();
				UpdateStatusBar();
			}
			else
			{
				string caption = LocalizationManager.GetString("AllTranslationConfirmTitle") ?? "全部翻譯";
				string text = LocalizationManager.GetString("AllTranslationConfirmPrompt") ?? "背景翻譯整本漫畫需要較長時間，是否繼續？";
				DialogResult dialogResult = MessageBox.Show(this, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
				if (dialogResult == DialogResult.Yes)
				{
					_isAllTranslationActive = true;
					_isInstantTranslationActive = false;
					btnAllTranslation.Type = TTypeMini.Primary;
					btnInstantTranslation.Type = TTypeMini.Default;
					_enableTranslation = true;
				UpdateCanvasMode();
					CancelInstantTranslation();
					StartAllTranslation();
					picPage.Invalidate();
					UpdateStatusBar();
				}
			}
		};
		base.KeyDown += OnKeyDown;
		scrollHost.MouseWheel += OnContentMouseWheel;
		picPage.MouseWheel += OnContentMouseWheel;
		picPage.MouseDown += OnContentMouseDown;
		picPage.MouseMove += OnContentMouseMove;
		picPage.MouseUp += OnContentMouseUp;
		vScrollBarContinuous.ValueChanged += delegate
		{
			if (IsContinuous)
			{
				_virtualScrollY = vScrollBarContinuous.Value;
				picPage.Invalidate();
				UpdateContinuousState();
			}
		};
		_scrollDebounce.Tick += delegate
		{
			_scrollDebounce.Stop();
			if (!base.IsDisposed && IsContinuous)
			{
				SaveProgress();
			}
		};
		base.FormClosing += delegate
		{
			SaveProgress();
			DisposeAllPages();
			try
			{
				_queueCts?.Cancel();
				_queueCts?.Dispose();
				_llmSemaphore?.Dispose();
			}
			catch
			{
			}
		};
		scrollHost.Resize += delegate
		{
			if (IsContinuous && _layout != null)
			{
				ChangeLayoutKeepingFocus(delegate
				{
				});
			}
			else
			{
				LayoutPic();
			}
		};
	}

	private void SaveProgress()
	{
		if (_metaStore == null || _currentIndex == _lastSavedPage)
		{
			return;
		}
		try
		{
			_metaStore.SetLastPage(_source.RootPath, _currentIndex);
			_lastSavedPage = _currentIndex;
		}
		catch
		{
		}
	}

	private void TryRecordRead()
	{
		if (!_trackReadingRecord || _readRecorded || _metaStore == null)
		{
			return;
		}
		double totalSeconds = (DateTime.UtcNow - _openedAt).TotalSeconds;
		if (totalSeconds < (double)_readThresholdSeconds && _pagesVisited < _readThresholdPages)
		{
			return;
		}
		try
		{
			_metaStore.RecordRead(_source.RootPath);
			_readRecorded = true;
			_recordCheckTimer.Stop();
		}
		catch
		{
		}
	}

	private void TryAutoComplete()
	{
		if (!_trackReadingRecord || _completedRecorded || _metaStore == null)
		{
			return;
		}
		int num = _currentIndex + SpreadSize(_currentIndex) - 1;
		if (num < _source.PageCount - 1)
		{
			return;
		}
		try
		{
			_metaStore.SetReadStatus(_source.RootPath, ReadStatus.Completed);
			_completedRecorded = true;
		}
		catch
		{
		}
	}

	private void OnContentMouseWheel(object? sender, MouseEventArgs e)
	{
		if ((Control.ModifierKeys & Keys.Control) != Keys.None)
		{
			if (IsContinuous && _layout != null)
			{
				int num = ((e.Delta > 0) ? 5 : (-5));
				int value = Math.Clamp(sliderZoom.Value + num, sliderZoom.MinValue, sliderZoom.MaxValue);
				sliderZoom.Value = value;
			}
			else
			{
				AdjustCustomZoom((e.Delta > 0) ? 1.1f : 0.9090909f);
			}
		}
		else if (IsContinuous)
		{
			int y = _virtualScrollY - e.Delta;
			ScrollContinuousTo(y);
		}
		else if (_zoomMode == ReaderZoomMode.FitPage)
		{
			// 滾輪翻頁防抖：限制最小間隔 150 毫秒，避免滾輪過快或多重觸發導致跳頁。
			// GPU 模式翻頁極輕量(預載命中即畫) → 略過防抖，對齊 PoC 快撥手感。
			if (!UseGpuNow && (DateTime.Now - _lastWheelPageTurnTime).TotalMilliseconds < 150)
			{
				return;
			}
			_lastWheelPageTurnTime = DateTime.Now;

			if (e.Delta > 0)
			{
				StepSpread(-1);
			}
			else
			{
				StepSpread(1);
			}
		}
	}

	// 左鍵按住在內容區拖曳 = 移動整個閱讀器視窗；全螢幕/最大化(非 Normal)時不移動。
	private void OnContentMouseDown(object? sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left) return;
		if (base.WindowState != FormWindowState.Normal) return;
		_panning = true;
		_panStartScreen = Cursor.Position;
		_formStartLocation = base.Location;
		picPage.Cursor = Cursors.SizeAll;
	}

	private void OnContentMouseMove(object? sender, MouseEventArgs e)
	{
		if (!_panning) return;
		var p = Cursor.Position;
		int dx = p.X - _panStartScreen.X;
		int dy = p.Y - _panStartScreen.Y;
		base.Location = new Point(_formStartLocation.X + dx, _formStartLocation.Y + dy);
	}

	private void OnContentMouseUp(object? sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left) return;
		_panning = false;
		picPage.Cursor = Cursors.Default;
	}

	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Control)
		{
			switch (e.KeyCode)
			{
			case Keys.D0:
			case Keys.NumPad0:
				ResetZoom();
				e.Handled = true;
				return;
			case Keys.Add:
			case Keys.Oemplus:
				AdjustCustomZoom(1.1f);
				e.Handled = true;
				return;
			case Keys.Subtract:
			case Keys.OemMinus:
				AdjustCustomZoom(0.9090909f);
				e.Handled = true;
				return;
			}
		}
		if (e.Shift && !e.Control && !e.Alt)
		{
			switch (e.KeyCode)
			{
			case Keys.B:
				OpenBookmarkList();
				e.Handled = true;
				return;
			case Keys.V:
				GoToNextBookmark();
				e.Handled = true;
				return;
			case Keys.C:
				GoToPrevBookmark();
				e.Handled = true;
				return;
			}
		}
		if (!e.Shift && !e.Control && !e.Alt && e.KeyCode == Keys.B)
		{
			ToggleBookmarkOnCurrentPage();
			e.Handled = true;
			return;
		}
		switch (e.KeyCode)
		{
		default:
			return;
		case Keys.Escape:
			if (_isFullscreen)
			{
				ToggleFullscreen();
			}
			else
			{
				Close();
			}
			break;
		case Keys.Return:
		case Keys.F:
		case Keys.F11:
			ToggleFullscreen();
			break;
		case Keys.Left:
			if (IsContinuous)
			{
				return;
			}
			StepSpread(_rtl ? 1 : (-1));
			break;
		case Keys.Right:
			if (IsContinuous)
			{
				return;
			}
			StepSpread((!_rtl) ? 1 : (-1));
			break;
		case Keys.Back:
		case Keys.Prior:
			if (IsContinuous)
			{
				SmartPageUp();
			}
			else
			{
				StepSpread(-1);
			}
			break;
		case Keys.Space:
		case Keys.Next:
			if (IsContinuous)
			{
				SmartPageDown();
			}
			else
			{
				StepSpread(1);
			}
			break;
		case Keys.Home:
			if (IsContinuous)
			{
				ScrollContinuousTo(0);
			}
			else
			{
				GoToPage(0);
			}
			break;
		case Keys.End:
			if (IsContinuous)
			{
				ScrollContinuousTo(_layout?.TotalHeight ?? 0);
			}
			else
			{
				GoToPage(_source.PageCount - 1);
			}
			break;
		}
		e.Handled = true;
	}

	private void OnSliderZoomChanged()
	{
		if (_layout != null)
		{
			ChangeLayoutKeepingFocus(delegate
			{
				_layout.SetZoom((float)sliderZoom.Value / 100f);
			});
			UpdateStatusBar();
		}
	}

	private void ScrollContinuousTo(int y)
	{
		if (_layout != null)
		{
			int val = Math.Max(0, _layout.TotalHeight - picPage.Height);
			y = Math.Max(0, Math.Min(y, val));
			if (vScrollBarContinuous.Value != y)
			{
				int value = Math.Min(y, vScrollBarContinuous.Maximum);
				vScrollBarContinuous.Value = value;
			}
			else
			{
				_virtualScrollY = y;
				picPage.Invalidate();
				UpdateContinuousState();
			}
		}
	}

	private void SmartPageDown()
	{
		if (_layout != null)
		{
			int virtualScrollY = _virtualScrollY;
			int height = picPage.Height;
			int num = _layout.NextPageBoundary(virtualScrollY);
			int y = ((num - virtualScrollY <= (int)((double)height * 1.1)) ? num : (virtualScrollY + (int)((double)height * 0.9)));
			ScrollContinuousTo(y);
		}
	}

	private void SmartPageUp()
	{
		if (_layout != null)
		{
			int virtualScrollY = _virtualScrollY;
			int height = picPage.Height;
			int num = _layout.PrevPageBoundary(virtualScrollY);
			int y = ((virtualScrollY - num <= (int)((double)height * 1.1)) ? num : Math.Max(0, virtualScrollY - (int)((double)height * 0.9)));
			ScrollContinuousTo(y);
		}
	}

	private void ResetZoom()
	{
		_customZoom = 1f;
		_zoomMode = ReaderZoomMode.FitPage;
		segZoomMode.SelectIndex = 0;
		LayoutPic();
		UpdateStatusBar();
	}

	private void OnZoomModeChanged(int idx)
	{
		if (1 == 0)
		{
		}
		ReaderZoomMode readerZoomMode = idx switch
		{
			0 => ReaderZoomMode.FitPage, 
			1 => ReaderZoomMode.FitWidth, 
			2 => ReaderZoomMode.Actual, 
			_ => ReaderZoomMode.FitPage, 
		};
		if (1 == 0)
		{
		}
		ReaderZoomMode readerZoomMode2 = readerZoomMode;
		if (readerZoomMode2 != _zoomMode)
		{
			_zoomMode = readerZoomMode2;
			_customZoom = 1f;
			LayoutPic();
			UpdateStatusBar();
		}
	}

	private void AdjustCustomZoom(float factor)
	{
		float num = Math.Clamp(_customZoom * factor, 0.1f, 8f);
		if (!(Math.Abs(num - _customZoom) < 0.001f))
		{
			_customZoom = num;
			_zoomMode = ReaderZoomMode.Custom;
			LayoutPic();
			UpdateStatusBar();
		}
	}

	private void LayoutPic()
	{
		if (IsContinuous && _layout != null)
		{
			int num = scrollHost.ClientSize.Width - vScrollBarContinuous.Width;
			int height = scrollHost.ClientSize.Height;
			if (num > 0 && height > 0)
			{
				_layout.SetViewportWidth(num);
				picPage.Size = new Size(num, height);
				picPage.Location = new Point(0, 0);
				int num2 = Math.Max(0, _layout.TotalHeight - height);
				vScrollBarContinuous.LargeChange = Math.Max(1, height);
				vScrollBarContinuous.Maximum = num2 + vScrollBarContinuous.LargeChange - 1;
				_virtualScrollY = Math.Max(0, Math.Min(_virtualScrollY, num2));
				if (vScrollBarContinuous.Value != _virtualScrollY)
				{
					vScrollBarContinuous.Value = _virtualScrollY;
				}
			}
		}
		else if (_leftImage != null && scrollHost.ClientSize.Width > 0 && scrollHost.ClientSize.Height > 0)
		{
			int num3 = _leftImage.Width + (_rightImage?.Width ?? 0);
			int num4 = ((_rightImage != null) ? Math.Max(_leftImage.Height, _rightImage.Height) : _leftImage.Height);
			int width = scrollHost.ClientSize.Width;
			int height2 = scrollHost.ClientSize.Height;
			int num5;
			int num6;
			switch (_zoomMode)
			{
			case ReaderZoomMode.FitPage:
			{
				float val = (float)width / (float)num3;
				float val2 = (float)height2 / (float)num4;
				float num7 = Math.Min(val, val2);
				num5 = Math.Max(1, (int)((float)num3 * num7));
				num6 = Math.Max(1, (int)((float)num4 * num7));
				break;
			}
			case ReaderZoomMode.FitWidth:
				num5 = width;
				num6 = (int)((float)num4 * (float)width / (float)num3);
				break;
			case ReaderZoomMode.Actual:
				num5 = num3;
				num6 = num4;
				break;
			case ReaderZoomMode.Custom:
				num5 = Math.Max(1, (int)((float)num3 * _customZoom));
				num6 = Math.Max(1, (int)((float)num4 * _customZoom));
				break;
			default:
				num5 = num3;
				num6 = num4;
				break;
			}
			picPage.Size = new Size(num5, num6);
			int x = Math.Max(0, (width - num5) / 2);
			int y = Math.Max(0, (height2 - num6) / 2);
			picPage.Location = new Point(x, y);
		}
		UpdateCanvasMode();   // zoom 模式/尺寸變動 → 更新 GPU↔GDI+ 畫布可見性
	}

	private void OnViewModeChanged(int idx)
	{
		if (1 == 0)
		{
		}
		ReaderViewMode readerViewMode = idx switch
		{
			0 => ReaderViewMode.SinglePage, 
			1 => ReaderViewMode.DualPage, 
			2 => ReaderViewMode.ContinuousScroll, 
			_ => ReaderViewMode.SinglePage, 
		};
		if (1 == 0)
		{
		}
		ReaderViewMode readerViewMode2 = readerViewMode;
		if (readerViewMode2 != _viewMode)
		{
			_viewMode = readerViewMode2;
			bool flag = readerViewMode2 == ReaderViewMode.ContinuousScroll;
			segZoomMode.Visible = !flag;
			sliderZoom.Visible = flag;
			switchSeamless.Visible = flag;
			lblSeamless.Visible = flag;
			if (flag)
			{
				EnterContinuousMode();
			}
			else
			{
				LeaveContinuousMode();
			}
		}
	}

	private void EnterContinuousMode()
	{
		_leftImage = null;
		_rightImage = null;
		scrollHost.AutoScroll = false;
		vScrollBarContinuous.Visible = true;
		_virtualScrollY = 0;
		UpdateCanvasMode();   // 進連續模式 → 退出 GPU 單頁、回 picPage
		_layout = new ContinuousLayout(_source.PageCount, Math.Max(1, scrollHost.ClientSize.Width - vScrollBarContinuous.Width));
		_layout.Gap = ((!switchSeamless.Checked) ? 8 : 0);
		lock (_cacheLock)
		{
			foreach (var (idx, image2) in _cache)
			{
				_layout.SetMeasured(idx, image2.Width, image2.Height);
			}
		}
		int num2 = (sliderZoom.MinValue + sliderZoom.MaxValue) / 2;
		sliderZoom.Value = num2;
		_layout.SetZoom((float)num2 / 100f);
		LayoutPic();
		ScrollContinuousTo(_layout.GetPageY(_currentIndex));
		picPage.Invalidate();
		UpdateStatusBar();
	}

	private void LeaveContinuousMode()
	{
		_layout = null;
		vScrollBarContinuous.Visible = false;
		scrollHost.AutoScroll = true;
		_virtualScrollY = 0;
		_currentIndex = SpreadStart(_currentIndex);
		GoToPage(_currentIndex);
	}

	private void OnSeamlessChanged()
	{
		if (_layout != null)
		{
			ChangeLayoutKeepingFocus(delegate
			{
				_layout.Gap = ((!switchSeamless.Checked) ? 8 : 0);
			});
		}
	}

	private void ChangeLayoutKeepingFocus(Action mutate)
	{
		if (_layout != null)
		{
			int num = _layout.FindPageAtY(_virtualScrollY + picPage.Height / 2);
			mutate();
			LayoutPic();
			if (num >= 0)
			{
				ScrollContinuousTo(_layout.GetPageY(num));
			}
			picPage.Invalidate();
		}
	}

	private int SpreadStart(int pageIndex)
	{
		if (_viewMode != ReaderViewMode.DualPage)
		{
			return pageIndex;
		}
		if (_spreadFromCover)
		{
			if (pageIndex == 0)
			{
				return 0;
			}
			return pageIndex - (pageIndex - 1) % 2;
		}
		return pageIndex - pageIndex % 2;
	}

	private int SpreadSize(int spreadStart)
	{
		if (_viewMode != ReaderViewMode.DualPage)
		{
			return 1;
		}
		if (_spreadFromCover && spreadStart == 0)
		{
			return 1;
		}
		if (spreadStart + 1 >= _source.PageCount)
		{
			return 1;
		}
		return 2;
	}

	private void StepSpread(int direction)
	{
		int num = SpreadStart(_currentIndex);
		if (direction > 0)
		{
			int num2 = num + SpreadSize(num);
			if (num2 < _source.PageCount)
			{
				GoToPage(num2);
			}
		}
		else if (num != 0)
		{
			int index = SpreadStart(num - 1);
			GoToPage(index);
		}
	}

	private void GoToPage(int index)
	{
		if (index < 0 || index >= _source.PageCount)
		{
			return;
		}
		if (IsContinuous && _layout != null)
		{
			_currentIndex = index;
			ScrollContinuousTo(_layout.GetPageY(index));
			return;
		}
		if (UseGpuNow)
		{
			// GPU 輕量翻頁分支：只同步 GPU(解碼/預載/重繪)，跳過整套 GDI+ 解碼/預縮/快取淘汰
			// (那些 GPU 模式用不到、且雙重解碼會拖慢)。切到非 GPU 模式時由 UpdateCanvasMode 惰性補載。
			_currentIndex = SpreadStart(index);
			UpdatePageInfo();
			UpdateCanvasMode();
			SaveProgress();
			_pagesVisited++;
			TryRecordRead();
			TryAutoComplete();
			return;
		}
		_currentIndex = SpreadStart(index);
		UpdatePageInfo();
		int num = SpreadSize(_currentIndex);
		EvictCacheIfNeeded(_currentIndex, _currentIndex + num - 1);
		for (int i = 0; i < num; i++)
		{
			_ = LoadPageAsync(_currentIndex + i);
		}
		for (int j = 1; j <= 6; j++)
		{
			int num2 = _currentIndex + num - 1 + j;
			int num3 = _currentIndex - j;
			if (num2 < _source.PageCount)
			{
				_ = LoadPageAsync(num2);
			}
			if (num3 >= 0)
			{
				_ = LoadPageAsync(num3);
			}
		}
		UpdateCanvasMode();   // 單頁+非譯文+GPU 開 → 切到 GPU 畫布(隱藏 picPage)；否則維持 picPage
		TryRenderCurrentSpread();
		SaveProgress();
		_pagesVisited++;
		TryRecordRead();
		TryAutoComplete();
	}

	private async Task LoadPageAsync(int index)
	{
		lock (_cacheLock)
		{
			if (_cache.ContainsKey(index) || !_loading.Add(index))
			{
				return;
			}
		}
		// 方案2 預熱：單頁模式下，依「該頁解碼尺寸 + 視窗 + zoom」算出顯示尺寸(同 LayoutPic)，背景預縮 → 翻頁直接 blit。
		// (picPage 尺寸會每頁依長寬重設，故不能拿「當前 picPage 尺寸」當目標。)
		bool preScale = !IsContinuous && !_enableTranslation && SpreadSize(SpreadStart(index)) == 1;
		int vw = scrollHost.ClientSize.Width, vh = scrollHost.ClientSize.Height;
		ReaderZoomMode zmode = _zoomMode; float czoom = _customZoom;
		Image img;
		try
		{
			img = await Task.Run(delegate
			{
				using Stream stream = _source.OpenPage(index);
				var dec = ReaderImageLoader.Decode(stream, _source.GetPageExtension(index));
				if (preScale)
				{
					var sz = FitDisplaySize(dec.Width, dec.Height, vw, vh, zmode, czoom);
					if (sz.HasValue) { try { ScaledForPaint(dec, sz.Value.Width, sz.Value.Height); } catch { } }
				}
				return dec;
			});
		}
		catch
		{
			lock (_cacheLock)
			{
				_loading.Remove(index);
			}
			return;
		}
		if (base.IsDisposed)
		{
			img.Dispose();
			return;
		}
		int natW = img.Width;
		int natH = img.Height;
		lock (_cacheLock)
		{
			_loading.Remove(index);
			_cache[index] = img;
		}
		try
		{
			BeginInvoke(delegate
			{
				if (!base.IsDisposed)
				{
					if (IsContinuous && _layout != null)
					{
						int totalHeight = _layout.TotalHeight;
						_layout.SetMeasured(index, natW, natH);
						int totalHeight2 = _layout.TotalHeight;
						int num = Math.Max(0, totalHeight2 - picPage.Height);
						int num2 = num + vScrollBarContinuous.LargeChange - 1;
						if (vScrollBarContinuous.Maximum != num2)
						{
							vScrollBarContinuous.Maximum = num2;
						}
						if (_virtualScrollY > num)
						{
							_virtualScrollY = num;
							vScrollBarContinuous.Value = Math.Min(_virtualScrollY, vScrollBarContinuous.Maximum);
						}
						UpdateContinuousState();
						picPage.Invalidate();
					}
					else
					{
						int num3 = SpreadStart(_currentIndex);
						int num4 = SpreadSize(num3);
						if (index >= num3 && index < num3 + num4)
						{
							TryRenderCurrentSpread();
						}
					}
				}
			});
		}
		catch
		{
		}
	}

	private void TryRenderCurrentSpread()
	{
		int currentIndex = _currentIndex;
		int num = SpreadSize(currentIndex);
		Image? value;
		Image? image;
		lock (_cacheLock)
		{
			_cache.TryGetValue(currentIndex, out value);
			image = ((num != 2) ? null : (_cache.TryGetValue(currentIndex + 1, out Image? value2) ? value2 : null));
		}
		if (value == null || (num == 2 && image == null))
		{
			_leftImage = null;
			_rightImage = null;
			picPage.Invalidate();
			lblStatus.Text = "讀取中…";
			return;
		}
		if (num == 1)
		{
			_leftImage = value;
			_rightImage = null;
		}
		else
		{
			_leftImage = (_rtl ? image : value);
			_rightImage = (_rtl ? value : image);
		}
		LayoutPic();
		picPage.Invalidate();
		UpdateStatusBar();
	}

	private void OnPicPagePaint(object? sender, PaintEventArgs e)
	{
		if (_enableTranslation)
		{
			List<int> visiblePageIndices = GetVisiblePageIndices();
			KeepOnlyVisibleInstantPages(visiblePageIndices);
			CancelNonVisibleActiveInstantPages(visiblePageIndices);
		}
		if (IsContinuous && _layout != null)
		{
			lock (_cacheLock)
			{
				DrawContinuous(e);
				return;
			}
		}
		lock (_cacheLock)
		{
			if (_leftImage == null)
			{
				return;
			}
			Graphics graphics = e.Graphics;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			graphics.InterpolationMode = InterpolationMode.Bilinear;   // 翻頁縮放快 3~5×；fit-to-window 下畫質差異極微
			if (_rightImage == null)
			{
				Rectangle rectangle = new Rectangle(0, 0, picPage.Width, picPage.Height);
				Image img1 = PageImageForPaint(_currentIndex, _leftImage);
				graphics.DrawImage(ScaledForPaint(img1, rectangle.Width, rectangle.Height), rectangle);
				if (_enableTranslation)
				{
					RenderTranslationOverlay(graphics, img1, rectangle, _currentIndex);
				}
				return;
			}
			int num = picPage.Width - 5;
			int num2 = _leftImage.Width + _rightImage.Width;
			int num3 = Math.Max(_leftImage.Height, _rightImage.Height);
			int num4 = (int)((double)_leftImage.Width / (double)num2 * (double)num);
			int width = num - num4;
			int num5 = (int)((double)_leftImage.Height / (double)num3 * (double)picPage.Height);
			int num6 = (int)((double)_rightImage.Height / (double)num3 * (double)picPage.Height);
			int y = (picPage.Height - num5) / 2;
			int y2 = (picPage.Height - num6) / 2;
			Rectangle rectangle2 = new Rectangle(0, y, num4, num5);
			Rectangle rectangle3 = new Rectangle(num4 + 5, y2, width, num6);
			int pageIndexL = (_rtl ? (_currentIndex + 1) : _currentIndex);
			int pageIndexR = (_rtl ? _currentIndex : (_currentIndex + 1));
			Image limg = PageImageForPaint(pageIndexL, _leftImage);
			Image rimg = PageImageForPaint(pageIndexR, _rightImage);
			graphics.DrawImage(ScaledForPaint(limg, rectangle2.Width, rectangle2.Height), rectangle2);
			graphics.FillRectangle(Brushes.White, num4, 0, 5, picPage.Height);
			graphics.DrawImage(ScaledForPaint(rimg, rectangle3.Width, rectangle3.Height), rectangle3);
			if (_enableTranslation)
			{
				RenderTranslationOverlay(graphics, limg, rectangle2, pageIndexL);
				RenderTranslationOverlay(graphics, rimg, rectangle3, pageIndexR);
			}
		}
	}

	private void DrawContinuous(PaintEventArgs e)
	{
		if (_layout == null)
		{
			return;
		}
		Graphics graphics = e.Graphics;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		graphics.InterpolationMode = InterpolationMode.Bilinear;   // 翻頁縮放快 3~5×；fit-to-window 下畫質差異極微
		var (num, num2) = _layout.GetVisibleRange(_virtualScrollY, _virtualScrollY + picPage.Height);
		if (num < 0)
		{
			return;
		}
		int contentWidth = _layout.ContentWidth;
		int x = (picPage.Width - contentWidth) / 2;
		using SolidBrush brush = new SolidBrush(LoadingPlaceholderBg);
		using StringFormat format = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center
		};
		for (int i = num; i <= num2; i++)
		{
			Image? value;
			lock (_cacheLock)
			{
				_cache.TryGetValue(i, out value);
			}
			int y = _layout.GetPageY(i) - _virtualScrollY;
			int pageHeight = _layout.GetPageHeight(i);
			Rectangle rectangle = new Rectangle(x, y, contentWidth, pageHeight);
			if (value == null)
			{
				graphics.FillRectangle(brush, rectangle);
				graphics.DrawString("讀取中…", lblStatus.Font, Brushes.White, rectangle, format);
				continue;
			}
			Image pimg = PageImageForPaint(i, value);
			graphics.DrawImage(ScaledForPaint(pimg, rectangle.Width, rectangle.Height), rectangle);
			if (_enableTranslation)
			{
				RenderTranslationOverlay(graphics, pimg, rectangle, i);
			}
		}
	}

	private void UpdateContinuousState()
	{
		if (_layout != null)
		{
			int virtualScrollY = _virtualScrollY;
			int height = picPage.Height;
			int y = virtualScrollY + height / 2;
			int num = _layout.FindPageAtY(y);
			var (num2, num3) = _layout.GetVisibleRange(virtualScrollY, virtualScrollY + height);
			if (num >= 0 && num != _currentIndex)
			{
				_currentIndex = num;
				_pagesVisited++;
				UpdatePageInfo();
				UpdateStatusBar();
				TryRecordRead();
			}
			if (num2 >= 0)
			{
				UpdateContinuousCacheWindow(num2, num3);
			}
			if (num3 >= _source.PageCount - 1)
			{
				TryAutoComplete();
			}
			_scrollDebounce.Stop();
			_scrollDebounce.Start();
		}
	}

	private void UpdateContinuousCacheWindow(int first, int last)
	{
		int num = Math.Max(0, first - 6);
		int num2 = Math.Min(_source.PageCount - 1, last + 6);
		EvictCacheIfNeeded(first, last);
		for (int i = num; i <= num2; i++)
		{
			_ = LoadPageAsync(i);
		}
	}

	private void EvictCacheIfNeeded(int activeFirst, int activeLast)
	{
		lock (_cacheLock)
		{
			if (_cache.Count <= 24)
			{
				return;
			}
			var list = (from x in _cache.Keys.Select(delegate(int k)
				{
					int distance = 0;
					if (k < activeFirst)
					{
						distance = activeFirst - k;
					}
					else if (k > activeLast)
					{
						distance = k - activeLast;
					}
					return new
					{
						Key = k,
						Distance = distance
					};
				})
				orderby x.Distance descending
				select x).ToList();
			int num = _cache.Count - 24;
			int num2 = 0;
			foreach (var item in list)
			{
				if (num2 >= num)
				{
					break;
				}
				if (item.Distance != 0 && _cache.TryGetValue(item.Key, out Image? value))
				{
					DropScaled(value);
					value.Dispose();
					_cache.Remove(item.Key);
					num2++;
				}
			}
		}
	}

	private void DisposeAllPages()
	{
		_recordCheckTimer.Stop();
		_recordCheckTimer.Dispose();
		_scrollDebounce.Stop();
		_scrollDebounce.Dispose();
		_leftImage = null;
		_rightImage = null;
		lock (_cacheLock)
		{
			foreach (Image value in _cache.Values)
			{
				value.Dispose();
			}
			_cache.Clear();
			_loading.Clear();
		}
		lock (_scaledLock)
		{
			foreach (var e in _scaledCache.Values) e.bmp.Dispose();
			_scaledCache.Clear();
		}
		_source.Dispose();
	}

	private void ToggleFullscreen()
	{
		if (!_isFullscreen)
		{
			_prevBorderStyle = base.FormBorderStyle;
			_prevWindowState = base.WindowState;
			base.FormBorderStyle = FormBorderStyle.None;
			base.WindowState = FormWindowState.Normal;
			base.WindowState = FormWindowState.Maximized;
			windowBar.Visible = false;
			toolbar.Visible = false;
			statusBar.Visible = false;
			_isFullscreen = true;
		}
		else
		{
			base.FormBorderStyle = _prevBorderStyle;
			base.WindowState = _prevWindowState;
			windowBar.Visible = true;
			toolbar.Visible = true;
			statusBar.Visible = true;
			_isFullscreen = false;
		}
	}

	private void UpdatePageInfo()
	{
		int currentIndex = _currentIndex;
		int num = (IsContinuous ? 1 : SpreadSize(currentIndex));
		string text = ((num == 1) ? $"{currentIndex + 1} / {_source.PageCount}" : $"{currentIndex + 1}-{currentIndex + 2} / {_source.PageCount}");
		windowBar.Text = text;
		RefreshBookmarkIcon();
	}

	private void RefreshBookmarkIcon()
	{
		if (_assetId.HasValue && _metaStore != null)
		{
			bool flag = _metaStore.HasBookmarkOnPage(_assetId.Value, _currentIndex);
			btnBookmarkToggle.IconSvg = (flag ? "StarFilled" : "StarOutlined");
		}
	}

	private void ToggleBookmarkOnCurrentPage()
	{
		if (!_assetId.HasValue || _metaStore == null)
		{
			return;
		}
		long value = _assetId.Value;
		if (_metaStore.HasBookmarkOnPage(value, _currentIndex))
		{
			_metaStore.DeleteBookmarkByPage(value, _currentIndex);
			_bookmarkCursor = -1;
		}
		else
		{
			_metaStore.AddBookmark(value, _currentIndex, null);
			IReadOnlyList<Bookmark> source = _metaStore.ListBookmarks(value);
			_bookmarkCursor = source.ToList().FindIndex((Bookmark b) => b.PageIndex == _currentIndex);
		}
		RefreshBookmarkIcon();
	}

	private void GoToNextBookmark()
	{
		if (_assetId.HasValue && _metaStore != null)
		{
			IReadOnlyList<Bookmark> readOnlyList = _metaStore.ListBookmarks(_assetId.Value);
			if (readOnlyList.Count != 0)
			{
				_bookmarkCursor = ((_bookmarkCursor >= 0) ? ((_bookmarkCursor + 1) % readOnlyList.Count) : 0);
				GoToPage(readOnlyList[_bookmarkCursor].PageIndex);
			}
		}
	}

	private void GoToPrevBookmark()
	{
		if (_assetId.HasValue && _metaStore != null)
		{
			IReadOnlyList<Bookmark> readOnlyList = _metaStore.ListBookmarks(_assetId.Value);
			if (readOnlyList.Count != 0)
			{
				_bookmarkCursor = ((_bookmarkCursor < 0) ? (readOnlyList.Count - 1) : ((_bookmarkCursor - 1 + readOnlyList.Count) % readOnlyList.Count));
				GoToPage(readOnlyList[_bookmarkCursor].PageIndex);
			}
		}
	}

	private void OpenBookmarkList()
	{
		if (!_assetId.HasValue || _metaStore == null)
		{
			return;
		}
		long value = _assetId.Value;
		List<Bookmark> list = _metaStore.ListBookmarks(value).ToList();
		string pageFmt = LocalizationManager.GetString("BookmarkPageFmt") ?? "第 {0} 頁";
		string unnamed = LocalizationManager.GetString("BookmarkUnnamed") ?? "(未命名)";
		string text = LocalizationManager.GetString("BookmarkEmptyHint") ?? "(尚無書籤)";
		string renameLbl = LocalizationManager.GetString("BookmarkRename") ?? "重新命名";
		string deleteLbl = LocalizationManager.GetString("BookmarkDelete") ?? "刪除";
		AntdUI.ContextMenuStrip.open(items: (list.Count != 0) ? ((IEnumerable<Bookmark>)list).Select((Func<Bookmark, int, IContextMenuStripItem>)delegate(Bookmark b, int idx)
		{
			string text2 = string.Format(pageFmt, b.PageIndex + 1);
			string text3 = (string.IsNullOrWhiteSpace(b.Label) ? unnamed : b.Label);
			return new ContextMenuStripItem(text2 + "  ·  " + text3)
			{
				IconSvg = "BookOutlined",
				Tag = new BookmarkMenuTag("nav", b, idx),
				Sub = new IContextMenuStripItem[2]
				{
					new ContextMenuStripItem(renameLbl)
					{
						IconSvg = "EditOutlined",
						Tag = new BookmarkMenuTag("rename", b, idx)
					},
					new ContextMenuStripItem(deleteLbl)
					{
						IconSvg = "DeleteOutlined",
						Tag = new BookmarkMenuTag("delete", b, idx)
					}
				}
			};
		}).ToArray() : new IContextMenuStripItem[1]
		{
			new ContextMenuStripItem(text)
			{
				Enabled = false
			}
		}, control: btnBookmarkList, call: delegate(IContextMenuStrip mi)
		{
			if (mi is ContextMenuStripItem { Tag: BookmarkMenuTag tag })
			{
				switch (tag.Action)
				{
				case "nav":
					_bookmarkCursor = tag.CursorIndex;
					GoToPage(tag.Bookmark.PageIndex);
					break;
				case "rename":
					RenameBookmarkInline(tag.Bookmark);
					break;
				case "delete":
					_metaStore.DeleteBookmark(tag.Bookmark.Id);
					_bookmarkCursor = -1;
					RefreshBookmarkIcon();
					break;
				}
			}
		});
	}

	private void RenameBookmarkInline(Bookmark b)
	{
		if (_metaStore != null)
		{
			string title = LocalizationManager.GetString("BookmarkRenamePrompt") ?? "輸入書籤名稱（留白清除）";
			string? text = PromptForText(this, title, b.Label ?? "");
			if (text != null)
			{
				string text2 = text.Trim();
				_metaStore.RenameBookmark(b.Id, string.IsNullOrEmpty(text2) ? null : text2);
			}
		}
	}

	private static string? PromptForText(IWin32Window parent, string title, string defaultValue)
	{
		using Form form = new Form
		{
			Text = title,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			MaximizeBox = false,
			MinimizeBox = false,
			StartPosition = FormStartPosition.CenterParent,
			ClientSize = new Size(360, 92),
			ShowInTaskbar = false
		};
		TextBox textBox = new TextBox
		{
			Text = defaultValue,
			Font = new Font("Microsoft JhengHei UI", 10f),
			Location = new Point(12, 12),
			Width = 336
		};
		System.Windows.Forms.Button button = new System.Windows.Forms.Button
		{
			Text = "OK",
			DialogResult = DialogResult.OK,
			Location = new Point(192, 52),
			Width = 75
		};
		System.Windows.Forms.Button button2 = new System.Windows.Forms.Button
		{
			Text = "Cancel",
			DialogResult = DialogResult.Cancel,
			Location = new Point(273, 52),
			Width = 75
		};
		form.Controls.Add(textBox);
		form.Controls.Add(button);
		form.Controls.Add(button2);
		form.AcceptButton = button;
		form.CancelButton = button2;
		textBox.SelectAll();
		return (form.ShowDialog(parent) == DialogResult.OK) ? textBox.Text : null;
	}

	private List<int> GetVisiblePageIndices()
	{
		List<int> list = new List<int>();
		if (IsContinuous)
		{
			if (_layout != null)
			{
				var (num, num2) = _layout.GetVisibleRange(_virtualScrollY, _virtualScrollY + picPage.Height);
				if (num >= 0)
				{
					for (int i = num; i <= num2; i++)
					{
						list.Add(i);
					}
				}
			}
			else
			{
				list.Add(_currentIndex);
			}
		}
		else
		{
			int currentIndex = _currentIndex;
			int num3 = SpreadSize(currentIndex);
			if (num3 == 1)
			{
				if (currentIndex >= 0 && currentIndex < _source.PageCount)
				{
					list.Add(currentIndex);
				}
			}
			else
			{
				int num4 = (_rtl ? (currentIndex + 1) : currentIndex);
				int num5 = (_rtl ? currentIndex : (currentIndex + 1));
				if (num4 >= 0 && num4 < _source.PageCount)
				{
					list.Add(num4);
				}
				if (num5 >= 0 && num5 < _source.PageCount)
				{
					list.Add(num5);
				}
			}
		}
		return list;
	}

	private void UpdateStatusBar()
	{
		string pageName = _source.GetPageName(_currentIndex);
		string value = (_rtl ? "RTL" : "LTR");
		ReaderViewMode viewMode = _viewMode;
		if (1 == 0)
		{
		}
		string text = viewMode switch
		{
			ReaderViewMode.SinglePage => "單頁", 
			ReaderViewMode.DualPage => "雙頁", 
			ReaderViewMode.ContinuousScroll => "捲動", 
			_ => "", 
		};
		if (1 == 0)
		{
		}
		string value2 = text;
		string text2 = "";
		if (IsContinuous)
		{
			int value3 = sliderZoom.Value;
			text2 = $"{pageName}    {value}    {value2}    {value3}%";
		}
		else
		{
			string value4 = "-";
			lock (_cacheLock)
			{
				if (_leftImage != null)
				{
					value4 = $"{_leftImage.Width}×{_leftImage.Height}";
				}
			}
			ReaderZoomMode zoomMode = _zoomMode;
			if (1 == 0)
			{
			}
			text = zoomMode switch
			{
				ReaderZoomMode.FitPage => "適合頁", 
				ReaderZoomMode.FitWidth => "適合寬", 
				ReaderZoomMode.Actual => "100%", 
				ReaderZoomMode.Custom => $"{(int)(_customZoom * 100f)}%", 
				_ => "", 
			};
			if (1 == 0)
			{
			}
			string value5 = text;
			text2 = $"{pageName}    {value4}    {value}    {value2}    {value5}";
		}
		string text3 = "";
		bool flag = false;
		if (_enableTranslation)
		{
			List<int> visiblePageIndices = GetVisiblePageIndices();
			TranslationStep? translationStep = null;
			int num = -1;
			lock (_stepLock)
			{
				foreach (int item in visiblePageIndices)
				{
					if (_pageTranslationSteps.TryGetValue(item, out var value6) && (value6 == TranslationStep.Ocr || value6 == TranslationStep.Translating || value6 == TranslationStep.Applying || value6 == TranslationStep.Applied))
					{
						translationStep = value6;
						num = item;
						break;
					}
				}
			}
			if (translationStep.HasValue)
			{
				TranslationStep value7 = translationStep.Value;
				if (1 == 0)
				{
				}
				text = value7 switch
				{
					TranslationStep.Ocr => "AI 辨識中...", 
					TranslationStep.Translating => "AI 翻譯中...", 
					TranslationStep.Applying => "譯文套用中...", 
					TranslationStep.Applied => "已套用 AI 翻譯", 
					_ => "", 
				};
				if (1 == 0)
				{
				}
				string text4 = text;
				if (!string.IsNullOrEmpty(text4))
				{
					text3 = " [" + text4 + "]";
				}
				if (translationStep.Value == TranslationStep.Ocr || translationStep.Value == TranslationStep.Translating || translationStep.Value == TranslationStep.Applying)
				{
					flag = true;
				}
			}
			lock (_queueLock)
			{
				if (_translationQueue.Count > 0 || _activeWorks.Count > 0)
				{
					flag = true;
				}
			}
			if (_isAllTranslationActive)
			{
				int num2 = 0;
				lock (_translationLock)
				{
					num2 = _translatedPageCache.Count;
				}
				int pageCount = _source.PageCount;
				int num3 = ((pageCount > 0) ? Math.Clamp(num2 * 100 / pageCount, 0, 100) : 0);
				string text5 = ((num3 == 100) ? "[全部翻譯已完成 100%]" : $"[全部翻譯中 {num3}%]");
				lblStatus.Text = text5 + text3 + "    " + text2;
			}
			else if (_isInstantTranslationActive)
			{
				lblStatus.Text = "[即時翻譯]" + text3 + "    " + text2;
			}
			else
			{
				lblStatus.Text = text2;
			}
		}
		else
		{
			lblStatus.Text = text2;
		}
		btnStatusLoading.Visible = _enableTranslation && flag;
	}

	private void EnsurePageTranslated(int pageIndex)
	{
		if (pageIndex >= 0 && pageIndex < _source.PageCount && (_isInstantTranslationActive || _isAllTranslationActive))
		{
			EnqueueInstantPage(pageIndex);
		}
	}

	private void StartWorkers(int concurrency)
	{
		_queueCts = new CancellationTokenSource();
		for (int i = 0; i < concurrency; i++)
		{
			int workerId = i;
			_workerTasks.Add(Task.Run(() => TranslationWorkerLoopAsync(workerId, _queueCts.Token)));
		}
	}

	private void EnqueueInstantPage(int pageIndex)
	{
		if (pageIndex < 0 || pageIndex >= _source.PageCount)
		{
			return;
		}
		lock (_translationLock)
		{
			if (_translatedPageCache.ContainsKey(pageIndex))
			{
				return;
			}
		}
		lock (_queueLock)
		{
			TranslationWork? translationWork = _translationQueue.FirstOrDefault((TranslationWork w) => w.PageIndex == pageIndex);
			TranslationWork? value;
			if (translationWork != null)
			{
				if (!translationWork.IsHighPriority)
				{
					translationWork.IsHighPriority = true;
					_translationQueue.Remove(translationWork);
					_translationQueue.Insert(0, translationWork);
				}
			}
			else if (!_activeWorks.TryGetValue(pageIndex, out value) || value == null || value.Cts.IsCancellationRequested)
			{
				TranslationWork item = new TranslationWork
				{
					PageIndex = pageIndex,
					IsHighPriority = true
				};
				_translationQueue.Insert(0, item);
				Monitor.PulseAll(_queueLock);
			}
		}
	}

	private void KeepOnlyVisibleInstantPages(List<int> visiblePages)
	{
		lock (_queueLock)
		{
			List<TranslationWork> list = _translationQueue.Where((TranslationWork w) => w.IsHighPriority && !visiblePages.Contains(w.PageIndex)).ToList();
			foreach (TranslationWork item in list)
			{
				item.Cts.Cancel();
				_translationQueue.Remove(item);
			}
		}
	}

	private void CancelNonVisibleActiveInstantPages(List<int> visiblePages)
	{
		lock (_queueLock)
		{
			List<int> list = _activeWorks.Keys.ToList();
			foreach (int item in list)
			{
				if (_activeWorks.TryGetValue(item, out TranslationWork? value) && value != null && value.IsHighPriority && !visiblePages.Contains(item))
				{
					value.Cts.Cancel();
				}
			}
		}
	}

	private void EnqueueAllPages()
	{
		int pageCount = _source.PageCount;
		lock (_queueLock)
		{
			int i;
			for (i = 0; i < pageCount; i++)
			{
				bool flag = false;
				lock (_translationLock)
				{
					flag = _translatedPageCache.ContainsKey(i);
				}
				if (!flag && !_translationQueue.Any((TranslationWork w) => w.PageIndex == i) && !_activeWorks.ContainsKey(i))
				{
					TranslationWork item = new TranslationWork
					{
						PageIndex = i,
						IsHighPriority = false
					};
					_translationQueue.Add(item);
				}
			}
			Monitor.PulseAll(_queueLock);
		}
	}

	private void CancelAllTranslationQueue()
	{
		lock (_queueLock)
		{
			_translationQueue.RemoveAll((TranslationWork w) => !w.IsHighPriority);
			List<int> list = _activeWorks.Keys.ToList();
			foreach (int item in list)
			{
				if (_activeWorks.TryGetValue(item, out TranslationWork? value) && value != null && !value.IsHighPriority)
				{
					value.Cts.Cancel();
				}
			}
		}
	}

	private async Task TranslationWorkerLoopAsync(int workerId, CancellationToken queueToken)
	{
		while (!queueToken.IsCancellationRequested)
		{
			TranslationWork? work = null;
			lock (_queueLock)
			{
				while (_translationQueue.Count == 0)
				{
					if (queueToken.IsCancellationRequested)
					{
						return;
					}
					Monitor.Wait(_queueLock, 1000);
				}
				if (queueToken.IsCancellationRequested)
				{
					break;
				}
				work = _translationQueue.FirstOrDefault((TranslationWork w) => w.IsHighPriority);
				if (work == null)
				{
					work = _translationQueue.FirstOrDefault();
				}
				if (work != null)
				{
					_translationQueue.Remove(work);
					_activeWorks[work.PageIndex] = work;
				}
			}
			if (work == null)
			{
				continue;
			}
			try
			{
				using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(queueToken, work.Cts.Token);
				await TranslatePageCoreAsync(work.PageIndex, work.IsHighPriority, linkedCts.Token);
			}
			catch (OperationCanceledException)
			{
				Debug.WriteLine($"[Translation Worker] Page {work.PageIndex} cancelled.");
			}
			catch (Exception ex2)
			{
				Exception ex3 = ex2;
				Debug.WriteLine($"[Translation Worker] Page {work.PageIndex} error: {ex3.Message}");
			}
			finally
			{
				lock (_queueLock)
				{
					_activeWorks.Remove(work.PageIndex);
				}
				UpdateStatusBarOnMainThread();
			}
		}
	}

	private async Task TranslatePageCoreAsync(int pageIndex, bool isHighPriority, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		Stopwatch sw = Stopwatch.StartNew();
		Debug.WriteLine($"[Translation Flow] === Page {pageIndex} Start (Priority: {(isHighPriority ? "High" : "Low")}) ===");
		UpdatePageStep(pageIndex, TranslationStep.Ocr);
		Stopwatch ocrSw = Stopwatch.StartNew();
		Image? img = null;
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(pageIndex, out Image? cachedImg) && cachedImg != null)
			{
				img = new Bitmap(cachedImg);
			}
		}
		if (img == null)
		{
			using Stream stream = _source.OpenPage(pageIndex);
			string ext = _source.GetPageExtension(pageIndex);
			img = ReaderImageLoader.Decode(stream, ext);
		}
		if (img == null)
		{
			UpdatePageStep(pageIndex, TranslationStep.Failed);
			Debug.WriteLine($"[Translation Flow] Page {pageIndex} Error: Image decode failed.");
			return;
		}
		Debug.WriteLine($"[Translation Flow] Page {pageIndex} waiting for LLM Semaphore...");
		Stopwatch semSw = Stopwatch.StartNew();
		await _llmSemaphore.WaitAsync(token);
		semSw.Stop();
		Debug.WriteLine($"[Translation Flow] Page {pageIndex} acquired Semaphore in {semSw.ElapsedMilliseconds} ms.");
		try
		{
			using (img)
			{
				token.ThrowIfCancellationRequested();
				AppConfig config = new ConfigManager().Load();
				_translationService.Configure(config.TranslationEndpoint, config.TranslationModelName, (LlmApiType)config.TranslationApiType, config.GetTranslationApiKey());
				if (config.OcrEngineType == 1 && _ocrService is not PaddleOcrService)
				{
					_ocrService = new PaddleOcrService();
				}
				else if (config.OcrEngineType != 1 && _ocrService is not WindowsOcrService)
				{
					_ocrService = new WindowsOcrService();
				}
				List<OcrBlock> rawBlocks = await _ocrService.RecognizeAsync(img);
				ocrSw.Stop();
				Debug.WriteLine($"[Translation Flow] Page {pageIndex} OCR raw blocks count: {rawBlocks.Count}. OCR Elapsed: {ocrSw.ElapsedMilliseconds} ms.");
				token.ThrowIfCancellationRequested();
				Stopwatch mergeSw = Stopwatch.StartNew();
				List<OcrBlock> mergedBlocks = _ocrService.MergeBlocks(rawBlocks);
				mergeSw.Stop();
				Debug.WriteLine($"[Translation Flow] Page {pageIndex} OCR merged blocks count: {mergedBlocks.Count}. Merge Elapsed: {mergeSw.ElapsedMilliseconds} ms.");
				if (mergedBlocks.Count > 0)
				{
					Debug.WriteLine($"[Translation Flow] Page {pageIndex} Merged Blocks Texts:");
					for (int i = 0; i < mergedBlocks.Count; i++)
					{
						Debug.WriteLine($"  Block [{i}]: \"{mergedBlocks[i].Text}\" at ({mergedBlocks[i].X}, {mergedBlocks[i].Y}, {mergedBlocks[i].Width}, {mergedBlocks[i].Height})");
					}
				}
				UpdatePageStep(pageIndex, TranslationStep.Translating);
				if (mergedBlocks.Count > 0)
				{
					List<string> textsToTranslate = mergedBlocks.Select((OcrBlock b) => b.Text).ToList();
					int totalLength = textsToTranslate.Sum((string t) => t.Length);
					Debug.WriteLine($"[Translation Flow] Page {pageIndex} calling LLM (Model: {config.TranslationModelName}). Texts count: {textsToTranslate.Count}, Total Length: {totalLength} chars.");
					Stopwatch llmSw = Stopwatch.StartNew();
					List<string> translations = await _translationService.TranslateBatchAsync(textsToTranslate);
					llmSw.Stop();
					Debug.WriteLine($"[Translation Flow] Page {pageIndex} LLM call finished in {llmSw.ElapsedMilliseconds} ms.");
					token.ThrowIfCancellationRequested();
					for (int i2 = 0; i2 < mergedBlocks.Count; i2++)
					{
						if (i2 < translations.Count)
						{
							Debug.WriteLine($"  Translate block [{i2}]: \"{mergedBlocks[i2].Text}\" -> \"{translations[i2]}\"");
							mergedBlocks[i2].Text = translations[i2];
						}
					}
				}
				UpdatePageStep(pageIndex, TranslationStep.Applying);
				Debug.WriteLine($"[Translation Flow] Page {pageIndex} applying translated blocks (clearing original text & rendering)...");
				Stopwatch applySw = Stopwatch.StartNew();
				lock (_translationLock)
				{
					_translatedPageCache[pageIndex] = mergedBlocks;
				}
				// 路線 2：用文字遮罩抹除原文，產生乾淨頁圖（只抹有譯文的氣泡，保留手繪狀聲詞）。
				// 模型缺失/失敗則略過 → overlay 走逐欄白底 fallback。
				if (_maskProvider != null && _maskProvider.IsAvailable && img is Bitmap pageBmp)
				{
					try
					{
						var maskArr = _maskProvider.GetTextMask(pageBmp, mergedBlocks);
						if (maskArr != null)
						{
							GateMaskToTranslatedBlocks(maskArr, pageBmp.Width, pageBmp.Height, mergedBlocks);
							Bitmap erased = TextEraser.Erase(pageBmp, maskArr);
							lock (_translationLock)
							{
								if (_erasedPageCache.TryGetValue(pageIndex, out var oldEr)) { if (oldEr != null) DropScaled(oldEr); oldEr?.Dispose(); }
								_erasedPageCache[pageIndex] = erased;
							}
						}
					}
					catch (Exception exMask) { Debug.WriteLine($"[Mask] page {pageIndex} erase failed: {exMask.Message}"); }
				}
				token.ThrowIfCancellationRequested();
				if (!base.IsDisposed)
				{
					BeginInvoke(delegate
					{
						if (!base.IsDisposed)
						{
							picPage.Invalidate();
							Debug.WriteLine($"[Translation Flow] Page {pageIndex} UI Invalidate triggered.");
						}
					});
				}
				applySw.Stop();
				Debug.WriteLine($"[Translation Flow] Page {pageIndex} apply and redraw trigger elapsed: {applySw.ElapsedMilliseconds} ms.");
				UpdatePageStep(pageIndex, TranslationStep.Applied);
				sw.Stop();
				Debug.WriteLine($"[Translation Flow] === Page {pageIndex} Complete. Total Time: {sw.ElapsedMilliseconds} ms ===");
			}
		}
		finally
		{
			_llmSemaphore.Release();
		}
	}

	private void UpdatePageStep(int pageIndex, TranslationStep step)
	{
		lock (_stepLock)
		{
			_pageTranslationSteps[pageIndex] = step;
		}
		UpdateStatusBarOnMainThread();
		if (step != TranslationStep.Applied)
		{
			return;
		}
		Task.Delay(3000).ContinueWith(delegate
		{
			lock (_stepLock)
			{
				if (_pageTranslationSteps.TryGetValue(pageIndex, out var value) && value == TranslationStep.Applied)
				{
					_pageTranslationSteps.Remove(pageIndex);
				}
			}
			UpdateStatusBarOnMainThread();
		});
	}

	private void UpdateStatusBarOnMainThread()
	{
		if (!base.IsDisposed)
		{
			if (base.InvokeRequired)
			{
				BeginInvoke(UpdateStatusBar);
			}
			else
			{
				UpdateStatusBar();
			}
		}
	}

	private void OnPageSpreadChanged()
	{
		List<int> visiblePageIndices = GetVisiblePageIndices();
		if (_isInstantTranslationActive)
		{
			KeepOnlyVisibleInstantPages(visiblePageIndices);
			CancelNonVisibleActiveInstantPages(visiblePageIndices);
			{
				foreach (int item in visiblePageIndices)
				{
					EnqueueInstantPage(item);
				}
				return;
			}
		}
		if (!_isAllTranslationActive)
		{
			return;
		}
		foreach (int item2 in visiblePageIndices)
		{
			EnqueueInstantPage(item2);
		}
	}

	private void TriggerInstantTranslationForCurrentSpread()
	{
		List<int> visiblePageIndices = GetVisiblePageIndices();
		foreach (int item in visiblePageIndices)
		{
			EnqueueInstantPage(item);
		}
		picPage.Invalidate();
		UpdateStatusBar();
	}

	private void CancelInstantTranslation()
	{
		lock (_queueLock)
		{
			_translationQueue.RemoveAll((TranslationWork w) => w.IsHighPriority);
			List<int> list = _activeWorks.Keys.ToList();
			foreach (int item in list)
			{
				if (_activeWorks.TryGetValue(item, out TranslationWork? value) && value != null && value.IsHighPriority)
				{
					value.Cts.Cancel();
				}
			}
		}
		lock (_stepLock)
		{
			_pageTranslationSteps.Clear();
		}
	}

	private void StartAllTranslation()
	{
		EnqueueAllPages();
		TriggerInstantTranslationForCurrentSpread();
	}

	private void CancelAllTranslation()
	{
		CancelAllTranslationQueue();
		lock (_stepLock)
		{
			_pageTranslationSteps.Clear();
		}
	}

	// 只保留落在「有譯文氣泡(OCR 框)」內的遮罩像素，其餘清 0（狀聲詞/空譯文不抹，保留手繪）。
	private static void GateMaskToTranslatedBlocks(byte[] mask, int W, int H, List<OcrBlock> blocks)
	{
		byte[] allowed = new byte[W * H];
		const int pad = 4;
		foreach (var b in blocks)
		{
			if (string.IsNullOrWhiteSpace(b.Text)) continue;
			int x1 = Math.Max(0, (int)b.X - pad), y1 = Math.Max(0, (int)b.Y - pad);
			int x2 = Math.Min(W - 1, (int)(b.X + b.Width) + pad), y2 = Math.Min(H - 1, (int)(b.Y + b.Height) + pad);
			for (int y = y1; y <= y2; y++)
				for (int x = x1; x <= x2; x++)
					allowed[y * W + x] = 1;
		}
		for (int i = 0; i < mask.Length; i++)
			if (allowed[i] == 0) mask[i] = 0;
	}

	/// <summary>該頁有「抹除頁圖」就回它（路線2），否則回原圖（fallback）。</summary>
	private Image PageImageForPaint(int pageIndex, Image original)
	{
		if (!_enableTranslation) return original;   // 翻譯關閉時用原圖（含日文），避免顯示空白氣泡
		lock (_translationLock)
		{
			if (_erasedPageCache.TryGetValue(pageIndex, out var er) && er != null) return er;
		}
		return original;
	}

	// 把 src 預縮到 (tw,th) 並快取(鍵=來源圖)；尺寸相符直接回 src(免縮)，尺寸變了(resize)自動重建。
	// 回傳的點陣圖恰為目標尺寸 → paint 1:1 blit(不觸發即時縮放)。來源圖 dispose 時須呼叫 DropScaled 避免洩漏。
	private Image ScaledForPaint(Image src, int tw, int th)
	{
		if (tw <= 0 || th <= 0 || (src.Width == tw && src.Height == th)) return src;
		lock (_scaledLock)
		{
			if (_scaledCache.TryGetValue(src, out var e))
			{
				if (e.w == tw && e.h == th) return e.bmp;
				e.bmp.Dispose(); _scaledCache.Remove(src);   // 尺寸變了 → 重建
			}
			var bmp = new Bitmap(tw, th, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (var gg = Graphics.FromImage(bmp))
			{
				gg.PixelOffsetMode = PixelOffsetMode.HighQuality;
				gg.InterpolationMode = InterpolationMode.HighQualityBicubic;   // 預縮只做一次 → 用最高畫質
				gg.DrawImage(src, new Rectangle(0, 0, tw, th));
			}
			_scaledCache[src] = (tw, th, bmp);
			return bmp;
		}
	}

	private void DropScaled(Image src)
	{
		lock (_scaledLock)
		{
			if (_scaledCache.TryGetValue(src, out var e)) { e.bmp.Dispose(); _scaledCache.Remove(src); }
		}
	}

	// 開啟控件雙緩衝(Control.DoubleBuffered 為 protected，用反射設)。消除翻頁時父 Panel 重繪閃爍/頓挫。
	private static void EnableDoubleBuffer(Control c)
	{
		try
		{
			typeof(Control).GetProperty("DoubleBuffered",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				?.SetValue(c, true);
		}
		catch { /* 反射失敗不致命，僅少了雙緩衝 */ }
	}

	// 單頁模式：依該頁長寬 + 視窗 + zoom 算出顯示尺寸(與 LayoutPic 單頁分支同邏輯)，供背景預熱對齊 picPage 尺寸。
	private static Size? FitDisplaySize(int imgW, int imgH, int vw, int vh, ReaderZoomMode mode, float customZoom)
	{
		if (imgW <= 0 || imgH <= 0 || vw <= 0 || vh <= 0) return null;
		switch (mode)
		{
			case ReaderZoomMode.FitPage:
			{
				float s = Math.Min((float)vw / imgW, (float)vh / imgH);
				return new Size(Math.Max(1, (int)(imgW * s)), Math.Max(1, (int)(imgH * s)));
			}
			case ReaderZoomMode.FitWidth:
				return new Size(vw, Math.Max(1, (int)((float)imgH * vw / imgW)));
			case ReaderZoomMode.Custom:
				return new Size(Math.Max(1, (int)(imgW * customZoom)), Math.Max(1, (int)(imgH * customZoom)));
			default: // Actual
				return new Size(imgW, imgH);
		}
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		base.OnFormClosed(e);
		lock (_translationLock)
		{
			foreach (var b in _erasedPageCache.Values) b?.Dispose();
			_erasedPageCache.Clear();
			_envelopeCache.Clear();
		}
		(_maskProvider as System.IDisposable)?.Dispose();
	}

	// 取樣抹除後氣泡中央區的亮度，判斷是否黑底氣泡（→ 譯文畫白字）。座標為原圖座標。
	private static bool IsDarkBubble(Bitmap img, OcrBlock b)
	{
		int cx = (int)(b.X + b.Width / 2), cy = (int)(b.Y + b.Height / 2);
		long lum = 0; int n = 0;
		for (int dy = -3; dy <= 3; dy++)
			for (int dx = -3; dx <= 3; dx++)
			{
				int x = cx + dx * 3, y = cy + dy * 3;
				if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) continue;
				var p = img.GetPixel(x, y);
				lum += (p.R * 299 + p.G * 587 + p.B * 114) / 1000; n++;
			}
		return n > 0 && lum / n < 110;
	}

	private BubbleEnvelope? GetEnvelope(OcrBlock item, Image img, IReadOnlyList<OcrBlock> all)
	{
		lock (_translationLock)
			if (_envelopeCache.TryGetValue(item, out var cached)) return cached;
		BubbleEnvelope? env = null;
		try
		{
			if (img is Bitmap bmp)
			{
				// 其他塊的文字框當 barrier，避免相鄰氣泡 envelope 互相侵入(文字重疊)
				var blockers = new List<Rectangle>();
				foreach (var o in all)
				{
					if (ReferenceEquals(o, item)) continue;
					blockers.Add(new Rectangle((int)o.X, (int)o.Y, (int)o.Width, (int)o.Height));
				}
				var e = BubbleEnvelope.Detect(bmp, new Rectangle((int)item.X, (int)item.Y, (int)item.Width, (int)item.Height), blockers: blockers);
				if (e.Valid) env = e;
			}
		}
		catch { env = null; }
		lock (_translationLock) { _envelopeCache[item] = env; }
		return env;
	}

	// 在氣泡輪廓(來源座標)內排版譯文；用 Graphics transform 把來源座標縮放到顯示位置。
	private void RenderBubble(Graphics g, OcrBlock item, BubbleEnvelope env, Rectangle destRect, float num, float num2, bool erased, Brush whiteBrush, Brush blackBrush, float ppp)
	{
		int bt = int.MaxValue, bb = int.MinValue;
		for (int x = env.Left; x <= env.Right; x++)
			if (env.Top[x] >= 0) { if (env.Top[x] < bt) bt = env.Top[x]; if (env.Bottom[x] > bb) bb = env.Bottom[x]; }
		if (bb < bt) return;
		var st = g.Save();
		g.TranslateTransform(destRect.X, destRect.Y);
		g.ScaleTransform(num, num2);
		using (var clip = new Region())
		{
			clip.MakeEmpty();
			clip.Union(new RectangleF(env.Left - 2, bt - 2, env.Right - env.Left + 5, bb - bt + 5));
			g.SetClip(clip, CombineMode.Replace);
			if (!erased)
				for (int x = env.Left; x <= env.Right; x++)
				{
					int t = env.Top[x], b = env.Bottom[x];
					if (t >= 0 && b >= t) g.FillRectangle(whiteBrush, x, t, 1, b - t + 1);
				}
			Brush tb = env.DarkBubble ? whiteBrush : blackBrush;
			FontSizeOptimizer.DrawVerticalStringInBubble(g, item.Text, tb, env, ppp);
			if (_debugEnvelope)
			{
				using var ep = new Pen(Color.Lime, 1f);
				for (int x = env.Left; x <= env.Right; x++)
				{
					int t = env.Top[x], bbb = env.Bottom[x];
					if (t >= 0 && bbb >= t) { g.DrawLine(ep, x, t, x + 1, t); g.DrawLine(ep, x, bbb, x + 1, bbb); }
				}
			}
		}
		g.Restore(st);
	}
	private void RenderTranslationOverlay(Graphics g, Image originalImage, Rectangle destRect, int pageIndex)
	{
		EnsurePageTranslated(pageIndex);
		List<OcrBlock>? value;
		lock (_translationLock)
		{
			_translatedPageCache.TryGetValue(pageIndex, out value);
		}
		if (value == null || value.Count == 0)
		{
			return;
		}
		bool erased;
		lock (_translationLock) { erased = _erasedPageCache.ContainsKey(pageIndex); }
		float num = (float)destRect.Width / (float)originalImage.Width;
		float num2 = (float)destRect.Height / (float)originalImage.Height;
		using SolidBrush brush = new SolidBrush(Color.White);
		using SolidBrush brush2 = new SolidBrush(Color.Black);
		// 依目前 Graphics 的 DPI 動態計算 Points→Pixels 比率，取代硬編碼的 1.333f (僅適用 96 DPI)
		// 在 96 DPI (100%) 下 = 96/72 = 1.333，在 120 DPI (125%) 下 = 1.667，以此類推
		float pixelsPerPoint = g.DpiY / 72f;

		// 預先計算整頁一致性的字體大小（在原始 100% 圖片尺寸下計算）
		var fontSizes = FontSizeOptimizer.CalculateConsistentFontSizes(
			value,
			pixelsPerPoint,
			(b, ppp) => FontSizeOptimizer.EstimateOptimalFontSize(b.Text, b.Width, b.Height, ppp)
		);

		foreach (OcrBlock item in value)
		{
			if (!string.IsNullOrWhiteSpace(item.Text))
			{
				float x = (float)destRect.X + item.X * num;
				float y = (float)destRect.Y + item.Y * num2;
				float num3 = item.Width * num;
				float num4 = item.Height * num2;
				RectangleF rect = new RectangleF(x, y, num3, num4);

				// 完整解：用氣泡「實際輪廓」排版(flood-fill envelope) → 填滿氣泡、自然斷行、貼合圓/斜形狀；
				// 偵測失敗(漏出/網點底)時 env=null → fallback 走下方 children-based shaped。
				BubbleEnvelope? env = GetEnvelope(item, originalImage, value);
				if (env != null && env.Valid)
				{
					RenderBubble(g, item, env, destRect, num, num2, erased, brush, brush2, pixelsPerPoint);
					continue;
				}

				// 譯文排版區域 = 合併框；多氣泡誤併已在 MergeBlocks 沿縫隙拆開，每 block ≈ 單一氣泡文字區。
				// 子欄(Children)矩形 = 原文各欄實際幾何，供 DrawVerticalStringShaped 追隨氣泡形狀。
				var cols = new List<RectangleF>();
				if (item.Children != null && item.Children.Count > 0)
				{
					foreach (var child in item.Children)
						cols.Add(new RectangleF((float)destRect.X + child.X * num, (float)destRect.Y + child.Y * num2, child.Width * num, child.Height * num2));
				}

				// 有抹除頁圖(路線2)時不再逐欄塗白（原文已精準抹除、無戳出）；否則 fallback 逐欄白底(內縮2px)。
				if (!erased)
				{
					g.FillRectangle(brush, RectangleF.Inflate(rect, -2f, -2f));
				}

				float emSize = fontSizes.TryGetValue(item, out float size) ? size : 14f;
				// 將計算出的原始字體大小乘上寬度縮放比例 num，實現等比例放大縮小
				emSize = emSize * num;
				using Font font = ReaderFonts.MakeFont(emSize);
				// 黑底氣泡(抹除後底色為暗)畫白字，否則黑字
				Brush textBrush = (erased && originalImage is Bitmap ebmp && IsDarkBubble(ebmp, item)) ? brush : brush2;
				var savedState = g.Save();
				// clip：路線2(已抹除)用合併框(+2)，讓「頂端對齊」的均勻版面不被參差子欄裁切；
				// fallback(逐欄白底)維持子欄聯集(+2)，避免畫到未塗白的欄間插圖。
				using (var clipRegion = new Region())
				{
					clipRegion.MakeEmpty();
					clipRegion.Union(RectangleF.Inflate(rect, 2f, 2f));
					g.SetClip(clipRegion, CombineMode.Replace);
					// 追隨原文子欄形狀排版：邊欄短/中欄長/左右基線可不同高 → 貼合圓/斜氣泡不戳出；含縮字防重疊溢出。
					FontSizeOptimizer.DrawVerticalStringShaped(g, item.Text, font, textBrush, rect, cols, pixelsPerPoint);
				}
				g.Restore(savedState);
			}
		}
	}

}
