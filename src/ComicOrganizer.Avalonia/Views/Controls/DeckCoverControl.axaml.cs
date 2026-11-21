using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ComicOrganizer.Avalonia.Views.Controls;

/// <summary>
/// 探索牆卡片雙模式封面元件：支援「🃏 疊牌扇形（預設，含懸停洗牌輪轉動畫）」與「🖼️ 單張精選」。
/// </summary>
public partial class DeckCoverControl : UserControl
{
    private static readonly TimeSpan HoverIntentDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CycleInterval = TimeSpan.FromMilliseconds(1400);

    public static readonly StyledProperty<bool> IsDeckModeProperty =
        AvaloniaProperty.Register<DeckCoverControl, bool>(nameof(IsDeckMode), defaultValue: true);

    public static readonly StyledProperty<bool> IsCardHoveredProperty =
        AvaloniaProperty.Register<DeckCoverControl, bool>(nameof(IsCardHovered), defaultValue: false);

    public static readonly StyledProperty<Bitmap?> Cover1Property =
        AvaloniaProperty.Register<DeckCoverControl, Bitmap?>(nameof(Cover1));

    public static readonly StyledProperty<Bitmap?> Cover2Property =
        AvaloniaProperty.Register<DeckCoverControl, Bitmap?>(nameof(Cover2));

    public static readonly StyledProperty<Bitmap?> Cover3Property =
        AvaloniaProperty.Register<DeckCoverControl, Bitmap?>(nameof(Cover3));

    public static readonly StyledProperty<bool> HasCover1Property =
        AvaloniaProperty.Register<DeckCoverControl, bool>(nameof(HasCover1));

    public static readonly StyledProperty<bool> HasCover2Property =
        AvaloniaProperty.Register<DeckCoverControl, bool>(nameof(HasCover2));

    public static readonly StyledProperty<bool> HasCover3Property =
        AvaloniaProperty.Register<DeckCoverControl, bool>(nameof(HasCover3));

    public static readonly StyledProperty<string> FallbackGlyphProperty =
        AvaloniaProperty.Register<DeckCoverControl, string>(nameof(FallbackGlyph), defaultValue: "📚");

    public static readonly StyledProperty<string> AccentColorHexProperty =
        AvaloniaProperty.Register<DeckCoverControl, string>(nameof(AccentColorHex), defaultValue: "#3B82F6");

    public static readonly StyledProperty<string?> CountBadgeTextProperty =
        AvaloniaProperty.Register<DeckCoverControl, string?>(nameof(CountBadgeText));

    private static readonly Color DefaultAccentColor = Color.Parse("#3B82F6");

    private DispatcherTimer? _cycleTimer;
    private bool _hasValidCover1;
    private bool _hasValidCover2;
    private bool _hasValidCover3;
    private int _rotationStep;
    private TopLevel? _topLevel;

    public DeckCoverControl()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    public bool IsDeckMode
    {
        get => GetValue(IsDeckModeProperty);
        set => SetValue(IsDeckModeProperty, value);
    }

    public bool IsCardHovered
    {
        get => GetValue(IsCardHoveredProperty);
        set => SetValue(IsCardHoveredProperty, value);
    }

    public Bitmap? Cover1
    {
        get => GetValue(Cover1Property);
        set => SetValue(Cover1Property, value);
    }

    public Bitmap? Cover2
    {
        get => GetValue(Cover2Property);
        set => SetValue(Cover2Property, value);
    }

    public Bitmap? Cover3
    {
        get => GetValue(Cover3Property);
        set => SetValue(Cover3Property, value);
    }

    public bool HasCover1
    {
        get => GetValue(HasCover1Property);
        set => SetValue(HasCover1Property, value);
    }

    public bool HasCover2
    {
        get => GetValue(HasCover2Property);
        set => SetValue(HasCover2Property, value);
    }

    public bool HasCover3
    {
        get => GetValue(HasCover3Property);
        set => SetValue(HasCover3Property, value);
    }

    public string FallbackGlyph
    {
        get => GetValue(FallbackGlyphProperty);
        set => SetValue(FallbackGlyphProperty, value);
    }

    public string AccentColorHex
    {
        get => GetValue(AccentColorHexProperty);
        set => SetValue(AccentColorHexProperty, value);
    }

    public string? CountBadgeText
    {
        get => GetValue(CountBadgeTextProperty);
        set => SetValue(CountBadgeTextProperty, value);
    }

    private bool IsHoverActive => IsCardHovered || IsPointerOver;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged += OnTopLevelScalingChanged;
        }

        ApplyDeckBitmapCache();
    }

    /// <summary>
    /// 疊牌（3 張旋轉封面 + BoxShadow）整組快取成點陣圖，捲動時直接重用，不必每幀重畫陰影與旋轉圖片。
    /// 實測 4K 最大化探索牆捲動 15.9 → 30.7 FPS（遠端桌面 32 FPS 上限）。
    /// 倍率跟著螢幕縮放（150% → 1.5），避免快取圖在高 DPI 下變模糊；懸停動畫期間只有該張卡片會重繪快取。
    /// 「本數 / 未讀」徽章與輪播圓點放在快取範圍外，維持 ClearType 文字清晰度。
    /// </summary>
    private void ApplyDeckBitmapCache()
    {
        double scale = _topLevel?.RenderScaling ?? 1.0;
        if (DeckContainer.CacheMode is BitmapCache cache && Math.Abs(cache.RenderAtScale - scale) < 0.001)
        {
            return;
        }

        DeckContainer.CacheMode = new BitmapCache { RenderAtScale = scale };
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs e)
    {
        // 視窗移到不同縮放比例的螢幕時，快取倍率跟著調整
        ApplyDeckBitmapCache();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopCycleAndReset();

        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged -= OnTopLevelScalingChanged;
            _topLevel = null;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCardHoveredProperty || change.Property == IsPointerOverProperty)
        {
            UpdateHoverAndCycleState();
            return;
        }

        if (change.Property == IsDeckModeProperty ||
            change.Property == Cover1Property ||
            change.Property == Cover2Property ||
            change.Property == Cover3Property ||
            change.Property == HasCover1Property ||
            change.Property == HasCover2Property ||
            change.Property == HasCover3Property ||
            change.Property == FallbackGlyphProperty ||
            change.Property == AccentColorHexProperty ||
            change.Property == CountBadgeTextProperty)
        {
            UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        bool deckMode = IsDeckMode;
        DeckContainer.IsVisible = deckMode;
        SingleContainer.IsVisible = !deckMode;

        _hasValidCover1 = Cover1 is not null && (!IsSet(HasCover1Property) || HasCover1);
        _hasValidCover2 = Cover2 is not null && (!IsSet(HasCover2Property) || HasCover2);
        _hasValidCover3 = Cover3 is not null && (!IsSet(HasCover3Property) || HasCover3);

        FrontImage.Source = _hasValidCover1 ? Cover1 : null;
        FrontImage.Opacity = _hasValidCover1 ? 1.0 : 0.0;
        FrontPlaceholder.Opacity = _hasValidCover1 ? 0.0 : 1.0;

        BackLeftImage.Source = _hasValidCover2 ? Cover2 : null;
        BackLeftImage.Opacity = _hasValidCover2 ? 1.0 : 0.0;
        BackLeftPlaceholder.Opacity = _hasValidCover2 ? 0.0 : 0.72;

        BackRightImage.Source = _hasValidCover3 ? Cover3 : null;
        BackRightImage.Opacity = _hasValidCover3 ? 1.0 : 0.0;
        BackRightPlaceholder.Opacity = _hasValidCover3 ? 0.0 : 0.72;

        SingleImage.Source = _hasValidCover1 ? Cover1 : null;
        SingleImage.Opacity = _hasValidCover1 ? 1.0 : 0.0;
        SinglePlaceholder.Opacity = _hasValidCover1 ? 0.0 : 1.0;

        string glyph = string.IsNullOrWhiteSpace(FallbackGlyph) ? "📚" : FallbackGlyph;
        FrontGlyphText.Text = glyph;
        BackLeftGlyphText.Text = glyph;
        BackRightGlyphText.Text = glyph;
        SingleGlyphText.Text = glyph;

        var accent = ParseAccentColor(AccentColorHex);
        ApplyAccentBrushes(accent);

        string? badge = string.IsNullOrWhiteSpace(CountBadgeText) ? null : CountBadgeText.Trim();
        bool hasBadge = badge is not null;
        DeckBadgeText.Text = badge ?? string.Empty;
        DeckBadgeBorder.IsVisible = hasBadge && IsDeckMode;
        SingleBadgeText.Text = badge ?? string.Empty;
        SingleBadgeBorder.IsVisible = hasBadge;

        UpdateHoverAndCycleState();
    }

    private int GetValidCoverCount()
        => (_hasValidCover1 ? 1 : 0) + (_hasValidCover2 ? 1 : 0) + (_hasValidCover3 ? 1 : 0);

    private void UpdateHoverAndCycleState()
    {
        bool active = IsDeckMode && IsHoverActive;
        DeckContainer.Classes.Set("hovered", active);

        int validCount = GetValidCoverCount();
        if (!active || validCount < 2)
        {
            StopCycleAndReset();
            return;
        }

        // 啟動停留緩衝計時器（若尚未啟動）
        if (_cycleTimer is null)
        {
            _cycleTimer = new DispatcherTimer
            {
                Interval = HoverIntentDelay,
            };
            _cycleTimer.Tick += OnCycleTimerTick;
            _cycleTimer.Start();
        }
        else if (!_cycleTimer.IsEnabled)
        {
            _cycleTimer.Interval = HoverIntentDelay;
            _cycleTimer.Start();
        }

        UpdateDotsVisibility(validCount, visible: true);
    }

    private void OnCycleTimerTick(object? sender, EventArgs e)
    {
        if (!IsDeckMode || !IsHoverActive)
        {
            StopCycleAndReset();
            return;
        }

        int validCount = GetValidCoverCount();
        if (validCount < 2)
        {
            StopCycleAndReset();
            return;
        }

        // 首次緩衝觸發後切換為固定輪轉週期
        if (_cycleTimer is not null && _cycleTimer.Interval != CycleInterval)
        {
            _cycleTimer.Interval = CycleInterval;
        }

        _rotationStep = (_rotationStep + 1) % validCount;
        ApplyDeckRoles(validCount);
        UpdateDotsVisibility(validCount, visible: true);
    }

    private void StopCycleAndReset()
    {
        if (_cycleTimer is not null)
        {
            _cycleTimer.Stop();
            _cycleTimer.Tick -= OnCycleTimerTick;
            _cycleTimer = null;
        }

        _rotationStep = 0;
        ApplyDeckRoles(GetValidCoverCount());
        UpdateDotsVisibility(GetValidCoverCount(), visible: false);
    }

    private void ApplyDeckRoles(int validCount)
    {
        if (validCount >= 3)
        {
            // 3 張實圖洗牌輪轉：
            // step 0: A=Front, B=Left,  C=Right
            // step 1: C=Front, A=Left,  B=Right（右後卡抽至最前，原前卡退至左後）
            // step 2: B=Front, C=Left,  A=Right
            switch (_rotationStep % 3)
            {
                case 1:
                    SetCardRole(CardCFrame, "role-front");
                    SetCardRole(CardAFrame, "role-left");
                    SetCardRole(CardBFrame, "role-right");
                    break;
                case 2:
                    SetCardRole(CardBFrame, "role-front");
                    SetCardRole(CardCFrame, "role-left");
                    SetCardRole(CardAFrame, "role-right");
                    break;
                default:
                    SetCardRole(CardAFrame, "role-front");
                    SetCardRole(CardBFrame, "role-left");
                    SetCardRole(CardCFrame, "role-right");
                    break;
            }
            return;
        }

        if (validCount == 2 && _hasValidCover1)
        {
            // 僅有 2 張實圖時，只在兩張實圖之間互換，絕不把空白佔位卡輪轉至正中央
            if (_hasValidCover2)
            {
                if (_rotationStep % 2 == 1)
                {
                    SetCardRole(CardBFrame, "role-front");
                    SetCardRole(CardAFrame, "role-left");
                    SetCardRole(CardCFrame, "role-right");
                }
                else
                {
                    SetCardRole(CardAFrame, "role-front");
                    SetCardRole(CardBFrame, "role-left");
                    SetCardRole(CardCFrame, "role-right");
                }
                return;
            }

            if (_hasValidCover3)
            {
                if (_rotationStep % 2 == 1)
                {
                    SetCardRole(CardCFrame, "role-front");
                    SetCardRole(CardBFrame, "role-left");
                    SetCardRole(CardAFrame, "role-right");
                }
                else
                {
                    SetCardRole(CardAFrame, "role-front");
                    SetCardRole(CardBFrame, "role-left");
                    SetCardRole(CardCFrame, "role-right");
                }
                return;
            }
        }

        // 預設歸位（<= 1 張圖）
        SetCardRole(CardAFrame, "role-front");
        SetCardRole(CardBFrame, "role-left");
        SetCardRole(CardCFrame, "role-right");
    }

    private static void SetCardRole(Border cardFrame, string activeRole)
    {
        cardFrame.Classes.Set("role-front", activeRole == "role-front");
        cardFrame.Classes.Set("role-left", activeRole == "role-left");
        cardFrame.Classes.Set("role-right", activeRole == "role-right");
    }

    private void UpdateDotsVisibility(int validCount, bool visible)
    {
        bool show = visible && validCount >= 2;
        DeckDotsPill.IsVisible = show;
        if (!show)
        {
            return;
        }

        Dot0.IsVisible = true;
        Dot1.IsVisible = true;
        Dot2.IsVisible = validCount >= 3;

        int activeIdx = _rotationStep % validCount;
        Dot0.Classes.Set("active", activeIdx == 0);
        Dot1.Classes.Set("active", activeIdx == 1);
        Dot2.Classes.Set("active", activeIdx == 2);
    }

    private void ApplyAccentBrushes(Color accent)
    {
        var frontGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(68, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(24, accent.R, accent.G, accent.B), 1),
            ],
        };

        var backGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(46, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(18, accent.R, accent.G, accent.B), 1),
            ],
        };

        var orbBrush = new SolidColorBrush(Color.FromArgb(56, accent.R, accent.G, accent.B));
        var frameBorder = new SolidColorBrush(Color.FromArgb(110, accent.R, accent.G, accent.B));

        FrontPlaceholder.Background = frontGradient;
        SinglePlaceholder.Background = frontGradient;
        BackLeftPlaceholder.Background = backGradient;
        BackRightPlaceholder.Background = backGradient;

        FrontAccentOrb.Background = orbBrush;
        SingleAccentOrb.Background = orbBrush;
        CardAFrame.BorderBrush = frameBorder;
    }

    private static Color ParseAccentColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var parsed))
        {
            return parsed;
        }

        return DefaultAccentColor;
    }
}
