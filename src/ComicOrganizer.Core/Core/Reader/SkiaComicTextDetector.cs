using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 跨平台 Skia 文字遮罩偵測介面：輸入頁面影像與 OCR 區塊，輸出 W * H 位元組遮罩（1 = 原文筆畫）。
/// </summary>
public interface ISkiaTextMaskProvider
{
    bool IsAvailable { get; }

    byte[]? GetTextMask(SKImage page, IReadOnlyList<OcrBlock> ocrBlocks);
}

/// <summary>
/// comic-text-detector（ONNX）Skia 版文字偵測器。
/// 輸入 [1, 3, 1024, 1024] letterbox RGB 張量，輸出像素級文字機率圖並二值化、膨脹後交給 <see cref="SkiaTextEraser"/> 使用。
/// 優先嘗試 DirectML GPU 推論，失敗時自動降級至 CPU ExecutionProvider。
/// </summary>
public sealed class SkiaComicTextDetector : ISkiaTextMaskProvider, IDisposable
{
    private const int InputSize = 1024;
    private readonly string? _explicitModelPath;
    private readonly string? _dataDirectory;
    private readonly object _lock = new();
    private InferenceSession? _session;
    private string? _loadedModelPath;
    private bool _initFailed;
    private bool _disposed;

    public SkiaComicTextDetector(string? modelPath = null, string? dataDirectory = null)
    {
        _explicitModelPath = string.IsNullOrWhiteSpace(modelPath) ? null : modelPath;
        _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory;
    }

    /// <summary>目前解析到的模型檔路徑。</summary>
    public string ModelPath => _explicitModelPath ?? ModelDownloader.ResolveComicTextDetectorPath(_dataDirectory);

    /// <summary>模型檔是否已就緒（若未就緒，呼叫端應自動降級回矩形遮罩）。</summary>
    public bool IsAvailable => !_disposed && File.Exists(ModelPath);

    /// <summary><see cref="ISkiaTextMaskProvider"/> 實作。</summary>
    public byte[]? GetTextMask(SKImage page, IReadOnlyList<OcrBlock> ocrBlocks) => DetectTextMask(page);

    private InferenceSession? EnsureSession()
    {
        if (_disposed)
        {
            return null;
        }

        string currentPath = ModelPath;
        if (!File.Exists(currentPath))
        {
            return null;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return null;
            }

            if (_session != null && string.Equals(_loadedModelPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return _session;
            }

            if (_initFailed && string.Equals(_loadedModelPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            _session?.Dispose();
            _session = null;
            _loadedModelPath = currentPath;
            _initFailed = false;

            try
            {
                var so = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    EnableMemoryPattern = false,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                };
                so.AppendExecutionProvider_DML(0);
                _session = new InferenceSession(currentPath, so);
                return _session;
            }
            catch
            {
                // DirectML 不可用或顯卡不支援時自動降級至 CPU EP
            }

            try
            {
                _session = new InferenceSession(currentPath);
                return _session;
            }
            catch
            {
                _initFailed = true;
                return null;
            }
        }
    }

    /// <summary>
    /// 偵測頁面文字遮罩，回傳長度 W * H 的 byte 陣列（1 = 文字、0 = 非文字）；
    /// 模型不可用或推論失敗時回傳 <see langword="null"/>。
    /// </summary>
    public byte[]? DetectTextMask(SKImage image, float threshold = 0.5f, int dilate = 2)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        var session = EnsureSession();
        if (session == null)
        {
            return null;
        }

        int w = image.Width;
        int h = image.Height;
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        float scale = (float)InputSize / Math.Max(w, h);
        int nw = Math.Max(1, (int)Math.Round(w * scale));
        int nh = Math.Max(1, (int)Math.Round(h * scale));
        int padX = (InputSize - nw) / 2;
        int padY = (InputSize - nh) / 2;

        var input = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
        var inputSpan = input.Buffer.Span;
        const int plane = InputSize * InputSize;

        using (var letterbox = new SKBitmap(new SKImageInfo(InputSize, InputSize, SKColorType.Bgra8888, SKAlphaType.Premul)))
        {
            using (var canvas = new SKCanvas(letterbox))
            {
                canvas.Clear(new SKColor(114, 114, 114));
                var dstRect = new SKRect(padX, padY, padX + nw, padY + nh);
                var srcRect = new SKRect(0, 0, w, h);
                canvas.DrawImage(image, srcRect, dstRect, new SKSamplingOptions(SKFilterMode.Linear));
                canvas.Flush();
            }

            var pixels = letterbox.GetPixelSpan();
            int stride = letterbox.RowBytes;
            for (int y = 0; y < InputSize; y++)
            {
                int row = y * stride;
                int outRow = y * InputSize;
                for (int x = 0; x < InputSize; x++)
                {
                    int o = row + x * 4;
                    int p = outRow + x;
                    inputSpan[p] = pixels[o + 2] / 255f;             // R 平面
                    inputSpan[plane + p] = pixels[o + 1] / 255f;     // G 平面
                    inputSpan[2 * plane + p] = pixels[o] / 255f;     // B 平面
                }
            }
        }

        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor("images", input) });
        var segOutput = results.FirstOrDefault(r => r.Name == "seg") ?? results.First();
        float[] seg = segOutput.AsTensor<float>().ToArray();

        var mask = new byte[w * h];
        for (int oy = 0; oy < h; oy++)
        {
            int outRow = oy * w;
            for (int ox = 0; ox < w; ox++)
            {
                int cx = (int)Math.Round(ox * scale) + padX;
                int cy = (int)Math.Round(oy * scale) + padY;
                if (cx < 0 || cy < 0 || cx >= InputSize || cy >= InputSize)
                {
                    continue;
                }

                if (seg[cy * InputSize + cx] > threshold)
                {
                    mask[outRow + ox] = 1;
                }
            }
        }

        return dilate > 0 ? DilateMask(mask, w, h, dilate) : mask;
    }

    private static byte[] DilateMask(byte[] sourceMask, int width, int height, int iterations)
    {
        var cur = sourceMask;
        for (int k = 0; k < iterations; k++)
        {
            var next = (byte[])cur.Clone();
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    if (cur[i] != 0)
                    {
                        continue;
                    }

                    if ((x > 0 && cur[i - 1] != 0) ||
                        (x < width - 1 && cur[i + 1] != 0) ||
                        (y > 0 && cur[i - width] != 0) ||
                        (y < height - 1 && cur[i + width] != 0))
                    {
                        next[i] = 1;
                    }
                }
            }

            cur = next;
        }

        return cur;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
    }
}
