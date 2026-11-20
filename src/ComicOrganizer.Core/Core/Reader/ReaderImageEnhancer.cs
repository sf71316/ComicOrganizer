using System.Reflection;
using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 閱讀器即時圖片畫質優化預設模式（組合 B：即時 Skia 濾鏡 / 色階 + 高品質重取樣 + 插件擴充）。
/// </summary>
public enum ReaderImageEnhancementPreset
{
    /// <summary>原圖關閉（僅保留高品質縮放取樣）。</summary>
    Off = 0,

    /// <summary>✨ 漫畫清晰增強（輕量 Unsharp Mask 銳化 + 網點/線條黑階壓實 + 去灰霧）。</summary>
    MangaCrisp = 1,

    /// <summary>📜 老舊掃描漂白（強化去泛黃白點裁切 + 強化對比 + 文字線條銳化）。</summary>
    VintageBleach = 2,

    /// <summary>🎨 彩漫生動增艷（適度提升彩度 + 對比與 Gamma 最佳化 + 輕微銳化）。</summary>
    ColorVivid = 3,

    /// <summary>🌙 夜間護眼降亮（降低純白底刺眼亮度並加入暖色紙張色溫，套用於譯文重繪之後使對話框完全無色差）。</summary>
    NightEyeCare = 4,

    /// <summary>🎛️ 自訂參數（由使用者微調各項滑桿）。</summary>
    Custom = 5
}

/// <summary>
/// 閱讀器即時圖片畫質優化參數。
/// </summary>
public sealed record ReaderImageEnhancementOptions
{
    public ReaderImageEnhancementPreset Preset { get; init; } = ReaderImageEnhancementPreset.Off;

    /// <summary>是否啟用高品質重取樣（Catmull-Rom Bicubic / Anisotropic Mipmap），消除縮放鋸齒與網點摩爾紋。</summary>
    public bool HighQualitySampling { get; init; } = true;

    /// <summary>Unsharp Mask 銳化強度（0.0 ~ 1.5）。</summary>
    public float SharpenAmount { get; init; } = 0f;

    /// <summary>Anime4K 風格暗部線條收斂強度（0.0 ~ 1.0），使模糊黑線更俐落。</summary>
    public float LineCrispAmount { get; init; } = 0f;

    /// <summary>黑點裁切門檻（0.0 ~ 0.30），低於此亮度的灰黑壓實為純黑。</summary>
    public float BlackPoint { get; init; } = 0f;

    /// <summary>白點裁切門檻（0.70 ~ 1.0），高於此亮度的泛黃/灰底漂白為純白。</summary>
    public float WhitePoint { get; init; } = 1f;

    /// <summary>對比度調整（-0.5 ~ +0.8，0 為不變）。</summary>
    public float Contrast { get; init; } = 0f;

    /// <summary>Gamma 調整（0.5 ~ 2.0，1.0 為不變；大於 1 加深中間調墨線）。</summary>
    public float Gamma { get; init; } = 1f;

    /// <summary>色彩飽和度倍率（0.0 ~ 1.8，1.0 為不變）。</summary>
    public float Saturation { get; init; } = 1f;

    /// <summary>夜間護眼最大亮度上限（0.50 ~ 1.0，1.0 為不降亮）。</summary>
    public float BrightnessCap { get; init; } = 1f;

    /// <summary>夜間護眼暖色紙張色溫強度（0.0 ~ 0.35，0 為不變）。</summary>
    public float WarmPaperTint { get; init; } = 0f;

    /// <summary>是否需要執行像素/Shader 濾鏡處理（若為 false 則只需高品質取樣直接繪製）。</summary>
    public bool HasActiveFilter =>
        Preset != ReaderImageEnhancementPreset.Off ||
        SharpenAmount > 0.001f ||
        LineCrispAmount > 0.001f ||
        BlackPoint > 0.001f ||
        WhitePoint < 0.999f ||
        Math.Abs(Contrast) > 0.001f ||
        Math.Abs(Gamma - 1f) > 0.001f ||
        Math.Abs(Saturation - 1f) > 0.001f ||
        BrightnessCap < 0.999f ||
        WarmPaperTint > 0.001f;

    public SKSamplingOptions GetSamplingOptions() =>
        HighQualitySampling
            ? new SKSamplingOptions(SKCubicResampler.CatmullRom)
            : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

    public static string GetPresetDisplayName(ReaderImageEnhancementPreset preset) => preset switch
    {
        ReaderImageEnhancementPreset.Off => "原圖關閉",
        ReaderImageEnhancementPreset.MangaCrisp => "✨ 漫畫清晰增強",
        ReaderImageEnhancementPreset.VintageBleach => "📜 老舊掃描漂白",
        ReaderImageEnhancementPreset.ColorVivid => "🎨 彩漫生動增艷",
        ReaderImageEnhancementPreset.NightEyeCare => "🌙 夜間護眼降亮",
        ReaderImageEnhancementPreset.Custom => "🎛️ 自訂參數",
        _ => "原圖關閉"
    };

    public static ReaderImageEnhancementOptions FromPreset(ReaderImageEnhancementPreset preset, bool highQualitySampling = true) =>
        preset switch
        {
            ReaderImageEnhancementPreset.MangaCrisp => new ReaderImageEnhancementOptions
            {
                Preset = ReaderImageEnhancementPreset.MangaCrisp,
                HighQualitySampling = highQualitySampling,
                SharpenAmount = 0.55f,
                LineCrispAmount = 0.35f,
                BlackPoint = 0.06f,
                WhitePoint = 0.94f,
                Contrast = 0.12f,
                Gamma = 1.08f,
                Saturation = 1.0f,
                BrightnessCap = 1.0f,
                WarmPaperTint = 0f
            },
            ReaderImageEnhancementPreset.VintageBleach => new ReaderImageEnhancementOptions
            {
                Preset = ReaderImageEnhancementPreset.VintageBleach,
                HighQualitySampling = highQualitySampling,
                SharpenAmount = 0.65f,
                LineCrispAmount = 0.45f,
                BlackPoint = 0.12f,
                WhitePoint = 0.84f,
                Contrast = 0.22f,
                Gamma = 1.15f,
                Saturation = 0.85f,
                BrightnessCap = 1.0f,
                WarmPaperTint = 0f
            },
            ReaderImageEnhancementPreset.ColorVivid => new ReaderImageEnhancementOptions
            {
                Preset = ReaderImageEnhancementPreset.ColorVivid,
                HighQualitySampling = highQualitySampling,
                SharpenAmount = 0.35f,
                LineCrispAmount = 0.15f,
                BlackPoint = 0.03f,
                WhitePoint = 0.97f,
                Contrast = 0.10f,
                Gamma = 1.03f,
                Saturation = 1.28f,
                BrightnessCap = 1.0f,
                WarmPaperTint = 0f
            },
            ReaderImageEnhancementPreset.NightEyeCare => new ReaderImageEnhancementOptions
            {
                Preset = ReaderImageEnhancementPreset.NightEyeCare,
                HighQualitySampling = highQualitySampling,
                SharpenAmount = 0.25f,
                LineCrispAmount = 0.15f,
                BlackPoint = 0.02f,
                WhitePoint = 0.96f,
                Contrast = 0.04f,
                Gamma = 1.02f,
                Saturation = 0.92f,
                BrightnessCap = 0.82f,
                WarmPaperTint = 0.16f
            },
            _ => new ReaderImageEnhancementOptions
            {
                Preset = preset == ReaderImageEnhancementPreset.Custom
                    ? ReaderImageEnhancementPreset.Custom
                    : ReaderImageEnhancementPreset.Off,
                HighQualitySampling = highQualitySampling
            }
        };
}

/// <summary>
/// 外部或內建單頁超解析度 / 深度重繪插件介面（如 Waifu2x / Real-CUGAN / Anime4K 多重精細重繪）。
/// </summary>
public interface IImageEnhancerPlugin
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    bool IsAvailable { get; }

    /// <summary>
    /// 對單頁影像執行深度超解析度或線條重繪（回傳新的 <see cref="SKImage"/>，由呼叫端負責管理生命週期）。
    /// </summary>
    Task<SKImage?> EnhancePageAsync(SKImage source, CancellationToken ct = default);
}

/// <summary>
/// 內建離線「Anime4K 漫畫線條超解析重繪」插件（免外部 GPU 驅動或額外下載即可直接對單頁進行 1.5x~2x 雙三次升頻 + 邊緣梯度線條收斂與去噪重繪）。
/// </summary>
public sealed class BuiltInAnimeLineSuperResPlugin : IImageEnhancerPlugin
{
    public string Id => "builtin-anime4k-superres";
    public string DisplayName => "⚡ Anime4K 漫畫線條超解析重繪 (內建離線)";
    public string Description => "針對低解析度漫畫單頁進行 Catmull-Rom 升頻、暗部線條收斂與網點清晰化重繪。";
    public bool IsAvailable => true;

    public Task<SKImage?> EnhancePageAsync(SKImage source, CancellationToken ct = default)
    {
        if (source == null) return Task.FromResult<SKImage?>(null);

        return Task.Run<SKImage?>(() =>
        {
            ct.ThrowIfCancellationRequested();

            // 限制最大邊長避免超大圖爆記憶體（若原圖已很大則維持原尺寸精細重繪，若偏小則 1.5x 升頻）
            int maxDim = Math.Max(source.Width, source.Height);
            float scale = maxDim < 1800 ? 1.5f : 1.0f;
            int targetW = Math.Max(1, (int)Math.Round(source.Width * scale));
            int targetH = Math.Max(1, (int)Math.Round(source.Height * scale));

            var info = new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface == null) return null;

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            var superOptions = new ReaderImageEnhancementOptions
            {
                Preset = ReaderImageEnhancementPreset.Custom,
                HighQualitySampling = true,
                SharpenAmount = 0.85f,
                LineCrispAmount = 0.65f,
                BlackPoint = 0.08f,
                WhitePoint = 0.92f,
                Contrast = 0.16f,
                Gamma = 1.10f,
                Saturation = 1.05f
            };

            ReaderImageEnhancer.DrawEnhancedImage(
                canvas,
                source,
                new SKRect(0, 0, targetW, targetH),
                superOptions);

            canvas.Flush();
            return surface.Snapshot();
        }, ct);
    }
}

/// <summary>
/// 管理漫畫單頁深度畫質優化插件（支援動態載入 <c>plugins/ImageEnhancer/*.dll</c> 與內建離線超解析插件）。
/// </summary>
public static class ImageEnhancerPluginManager
{
    private static readonly object SyncRoot = new();
    private static List<IImageEnhancerPlugin>? _plugins;

    public static IReadOnlyList<IImageEnhancerPlugin> GetPlugins()
    {
        lock (SyncRoot)
        {
            if (_plugins != null)
                return _plugins;

            var list = new List<IImageEnhancerPlugin>
            {
                new BuiltInAnimeLineSuperResPlugin()
            };

            try
            {
                var pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "ImageEnhancer");
                if (Directory.Exists(pluginDir))
                {
                    foreach (var dll in Directory.GetFiles(pluginDir, "*.dll"))
                    {
                        try
                        {
                            var asm = Assembly.LoadFrom(dll);
                            var types = asm.GetTypes()
                                .Where(t => typeof(IImageEnhancerPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                            foreach (var type in types)
                            {
                                if (Activator.CreateInstance(type) is IImageEnhancerPlugin plugin &&
                                    list.All(p => !string.Equals(p.Id, plugin.Id, StringComparison.OrdinalIgnoreCase)))
                                {
                                    list.Add(plugin);
                                }
                            }
                        }
                        catch
                        {
                            // 忽略損壞或不相容的外部插件 DLL
                        }
                    }
                }
            }
            catch
            {
                // 忽略目錄存取例外
            }

            _plugins = list;
            return _plugins;
        }
    }

    public static void RegisterPlugin(IImageEnhancerPlugin plugin)
    {
        if (plugin == null) throw new ArgumentNullException(nameof(plugin));
        lock (SyncRoot)
        {
            _plugins ??= new List<IImageEnhancerPlugin> { new BuiltInAnimeLineSuperResPlugin() };
            _plugins.RemoveAll(p => string.Equals(p.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));
            _plugins.Add(plugin);
        }
    }
}

/// <summary>
/// 漫畫閱讀器即時影像畫質優化引擎（SkSL Runtime Shader + CPU 像素回退雙引擎）。
/// </summary>
public static class ReaderImageEnhancer
{
    private const string SkslSource = @"
uniform shader u_image;
uniform float2 u_invSize;
uniform float u_sharpen;
uniform float u_lineCrisp;
uniform float u_blackPoint;
uniform float u_whitePoint;
uniform float u_contrast;
uniform float u_gamma;
uniform float u_saturation;
uniform float u_brightnessCap;
uniform float u_warmTint;

half4 main(float2 coord) {
    half4 center = u_image.eval(coord);
    half3 rgb = center.rgb;

    if (u_sharpen > 0.001 || u_lineCrisp > 0.001) {
        half3 n = u_image.eval(coord + float2(0.0, -1.0)).rgb;
        half3 s = u_image.eval(coord + float2(0.0,  1.0)).rgb;
        half3 w = u_image.eval(coord + float2(-1.0, 0.0)).rgb;
        half3 e = u_image.eval(coord + float2( 1.0, 0.0)).rgb;
        half3 blur = (n + s + w + e) * 0.25;

        // Unsharp Mask 邊緣銳化
        rgb = clamp(rgb + (rgb - blur) * half(u_sharpen), 0.0, 1.0);

        // Anime4K 風格暗部線條收斂（取鄰域最暗值收緊模糊墨線邊緣）
        if (u_lineCrisp > 0.001) {
            half3 darkest = min(rgb, min(min(n, s), min(w, e)));
            half luma = dot(rgb, half3(0.299, 0.587, 0.114));
            half edgeWeight = clamp((0.75 - luma) * 1.5, 0.0, 1.0);
            rgb = mix(rgb, darkest, half(u_lineCrisp) * edgeWeight * 0.45);
        }
    }

    // 黑白點色階裁切（老舊掃描去黃底 / 墨線黑階壓實）
    float denom = max(0.05, u_whitePoint - u_blackPoint);
    rgb = clamp((rgb - half(u_blackPoint)) / half(denom), 0.0, 1.0);

    // 對比度調整
    if (abs(u_contrast) > 0.001) {
        rgb = clamp((rgb - 0.5) * half(1.0 + u_contrast) + 0.5, 0.0, 1.0);
    }

    // Gamma 調整（> 1.0 加深中間調漫畫線條）
    if (abs(u_gamma - 1.0) > 0.001) {
        rgb = pow(max(rgb, half3(0.0001)), half3(u_gamma));
    }

    // 彩度調整
    if (abs(u_saturation - 1.0) > 0.001) {
        half luma = dot(rgb, half3(0.299, 0.587, 0.114));
        rgb = clamp(mix(half3(luma), rgb, half(u_saturation)), 0.0, 1.0);
    }

    // 夜間護眼降亮與暖色紙張色溫
    if (u_brightnessCap < 0.999) {
        rgb = rgb * half(u_brightnessCap);
    }
    if (u_warmTint > 0.001) {
        half3 warmPaper = half3(1.0, 0.94, 0.83);
        rgb = clamp(mix(rgb, rgb * warmPaper, half(u_warmTint)), 0.0, 1.0);
    }

    return half4(rgb, center.a);
}
";

    private static readonly Lazy<SKRuntimeEffect?> CachedEffect = new(() =>
    {
        try
        {
            var effect = SKRuntimeEffect.CreateShader(SkslSource, out var errors);
            return effect;
        }
        catch
        {
            return null;
        }
    });

    /// <summary>
    /// 在 <paramref name="canvas"/> 的 <paramref name="destRect"/> 範圍內即時繪製套用畫質優化後的 <paramref name="source"/>。
    /// </summary>
    public static void DrawEnhancedImage(
        SKCanvas canvas,
        SKImage source,
        SKRect destRect,
        ReaderImageEnhancementOptions? options)
    {
        if (canvas == null) throw new ArgumentNullException(nameof(canvas));
        if (source == null) throw new ArgumentNullException(nameof(source));

        var opts = options ?? ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.Off);
        var sampling = opts.GetSamplingOptions();

        if (!opts.HasActiveFilter || destRect.Width <= 0 || destRect.Height <= 0)
        {
            canvas.DrawImage(source, destRect, sampling);
            return;
        }

        var effect = CachedEffect.Value;
        if (effect != null)
        {
            float scaleX = destRect.Width / Math.Max(1f, source.Width);
            float scaleY = destRect.Height / Math.Max(1f, source.Height);

            using var imageShader = source.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
            if (imageShader != null)
            {
                var uniforms = new SKRuntimeEffectUniforms(effect)
                {
                    ["u_invSize"] = new[] { 1f / Math.Max(1, source.Width), 1f / Math.Max(1, source.Height) },
                    ["u_sharpen"] = opts.SharpenAmount,
                    ["u_lineCrisp"] = opts.LineCrispAmount,
                    ["u_blackPoint"] = opts.BlackPoint,
                    ["u_whitePoint"] = opts.WhitePoint,
                    ["u_contrast"] = opts.Contrast,
                    ["u_gamma"] = opts.Gamma,
                    ["u_saturation"] = opts.Saturation,
                    ["u_brightnessCap"] = opts.BrightnessCap,
                    ["u_warmTint"] = opts.WarmPaperTint
                };

                var children = new SKRuntimeEffectChildren(effect)
                {
                    ["u_image"] = imageShader
                };

                using var runtimeShader = effect.ToShader(uniforms, children);
                if (runtimeShader != null)
                {
                    using var paint = new SKPaint { Shader = runtimeShader, IsAntialias = true };
                    canvas.Save();
                    canvas.Translate(destRect.Left, destRect.Top);
                    canvas.Scale(scaleX, scaleY);
                    canvas.DrawRect(0, 0, source.Width, source.Height, paint);
                    canvas.Restore();
                    return;
                }
            }
        }

        // 若平台不支援 SkSL RuntimeEffect，退回 CPU 產出優化影像後繪製
        using var fallback = RenderEnhancedImageCpu(source, opts);
        canvas.DrawImage(fallback, destRect, sampling);
    }

    /// <summary>
    /// 將 <paramref name="source"/> 套用 <paramref name="options"/> 後渲染為全新的 <see cref="SKImage"/>（供測試、匯出或單頁重繪快取使用）。
    /// </summary>
    public static SKImage RenderEnhancedImage(SKImage source, ReaderImageEnhancementOptions? options)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var opts = options ?? ReaderImageEnhancementOptions.FromPreset(ReaderImageEnhancementPreset.Off);

        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface != null)
        {
            DrawEnhancedImage(surface.Canvas, source, new SKRect(0, 0, source.Width, source.Height), opts);
            surface.Canvas.Flush();
            var snap = surface.Snapshot();
            if (snap != null) return snap;
        }

        return RenderEnhancedImageCpu(source, opts);
    }

    private static SKImage RenderEnhancedImageCpu(SKImage source, ReaderImageEnhancementOptions opts)
    {
        using var srcBmp = SKBitmap.FromImage(source);
        if (srcBmp == null) return source;

        int w = srcBmp.Width;
        int h = srcBmp.Height;
        using var dstBmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

        float denom = Math.Max(0.05f, opts.WhitePoint - opts.BlackPoint);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = srcBmp.GetPixel(x, y);
                float r = c.Red / 255f;
                float g = c.Green / 255f;
                float b = c.Blue / 255f;

                if (opts.SharpenAmount > 0.001f || opts.LineCrispAmount > 0.001f)
                {
                    var n = srcBmp.GetPixel(x, Math.Max(0, y - 1));
                    var s = srcBmp.GetPixel(x, Math.Min(h - 1, y + 1));
                    var wPx = srcBmp.GetPixel(Math.Max(0, x - 1), y);
                    var ePx = srcBmp.GetPixel(Math.Min(w - 1, x + 1), y);

                    float br = (n.Red + s.Red + wPx.Red + ePx.Red) / (4f * 255f);
                    float bg = (n.Green + s.Green + wPx.Green + ePx.Green) / (4f * 255f);
                    float bb = (n.Blue + s.Blue + wPx.Blue + ePx.Blue) / (4f * 255f);

                    r = Math.Clamp(r + (r - br) * opts.SharpenAmount, 0f, 1f);
                    g = Math.Clamp(g + (g - bg) * opts.SharpenAmount, 0f, 1f);
                    b = Math.Clamp(b + (b - bb) * opts.SharpenAmount, 0f, 1f);
                }

                r = Math.Clamp((r - opts.BlackPoint) / denom, 0f, 1f);
                g = Math.Clamp((g - opts.BlackPoint) / denom, 0f, 1f);
                b = Math.Clamp((b - opts.BlackPoint) / denom, 0f, 1f);

                if (Math.Abs(opts.Contrast) > 0.001f)
                {
                    r = Math.Clamp((r - 0.5f) * (1f + opts.Contrast) + 0.5f, 0f, 1f);
                    g = Math.Clamp((g - 0.5f) * (1f + opts.Contrast) + 0.5f, 0f, 1f);
                    b = Math.Clamp((b - 0.5f) * (1f + opts.Contrast) + 0.5f, 0f, 1f);
                }

                if (Math.Abs(opts.Gamma - 1f) > 0.001f)
                {
                    r = MathF.Pow(Math.Max(0.0001f, r), opts.Gamma);
                    g = MathF.Pow(Math.Max(0.0001f, g), opts.Gamma);
                    b = MathF.Pow(Math.Max(0.0001f, b), opts.Gamma);
                }

                if (Math.Abs(opts.Saturation - 1f) > 0.001f)
                {
                    float luma = 0.299f * r + 0.587f * g + 0.114f * b;
                    r = Math.Clamp(luma + (r - luma) * opts.Saturation, 0f, 1f);
                    g = Math.Clamp(luma + (g - luma) * opts.Saturation, 0f, 1f);
                    b = Math.Clamp(luma + (b - luma) * opts.Saturation, 0f, 1f);
                }

                if (opts.BrightnessCap < 0.999f)
                {
                    r *= opts.BrightnessCap;
                    g *= opts.BrightnessCap;
                    b *= opts.BrightnessCap;
                }

                if (opts.WarmPaperTint > 0.001f)
                {
                    r = Math.Clamp(r * (1f - opts.WarmPaperTint) + (r * 1.00f) * opts.WarmPaperTint, 0f, 1f);
                    g = Math.Clamp(g * (1f - opts.WarmPaperTint) + (g * 0.94f) * opts.WarmPaperTint, 0f, 1f);
                    b = Math.Clamp(b * (1f - opts.WarmPaperTint) + (b * 0.83f) * opts.WarmPaperTint, 0f, 1f);
                }

                dstBmp.SetPixel(x, y, new SKColor(
                    (byte)Math.Round(r * 255f),
                    (byte)Math.Round(g * 255f),
                    (byte)Math.Round(b * 255f),
                    c.Alpha));
            }
        }

        return SKImage.FromBitmap(dstBmp);
    }
}
