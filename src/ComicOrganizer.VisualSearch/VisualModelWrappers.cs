using System.Numerics.Tensors;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace ComicOrganizer.VisualSearch;

public readonly record struct VisualPanelBox(float X, float Y, float W, float H, float Confidence, int ClassId)
{
    public float Area => W * H;
}

public readonly record struct PreparedYoloFrame(float[] Data, float Scale, float PadX, float PadY, int SrcWidth, int SrcHeight);

public interface IPanelDetector : IDisposable
{
    List<VisualPanelBox> DetectPreprocessed(PreparedYoloFrame input, float confThreshold = 0.35f, float iouThreshold = 0.45f, float minAreaRatio = 0.02f);
}

public interface IVisualEmbedder : IDisposable
{
    int InputSize { get; }
    int Dimensions { get; }
    float[][] EmbedPreprocessedBatch(IReadOnlyList<float[]> chwItems);
}

public static class VisualImagePreprocess
{
    private static readonly float[] ImageNetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] ImageNetStd = [0.229f, 0.224f, 0.225f];
    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>
    /// 一步完成「裁切 + 縮放 + 零配置 GetPixelSpan CHW 正規化」。
    /// </summary>
    public static float[] CreateCropNormalizedChw(SKImage srcImage, SKRect srcRect, int targetSize)
    {
        var info = new SKImageInfo(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var dst = new SKBitmap(info);
        using (var canvas = new SKCanvas(dst))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(srcImage, srcRect, new SKRect(0, 0, targetSize, targetSize), LinearSampling);
        }

        var span = dst.GetPixelSpan();
        int hw = targetSize * targetSize;
        var outBuf = new float[3 * hw];
        float inv255 = 1f / 255f;
        float m0 = ImageNetMean[0], m1 = ImageNetMean[1], m2 = ImageNetMean[2];
        float invS0 = 1f / ImageNetStd[0], invS1 = 1f / ImageNetStd[1], invS2 = 1f / ImageNetStd[2];

        for (int i = 0; i < hw; i++)
        {
            int p = i * 4;
            outBuf[i] = (span[p] * inv255 - m0) * invS0;
            outBuf[hw + i] = (span[p + 1] * inv255 - m1) * invS1;
            outBuf[2 * hw + i] = (span[p + 2] * inv255 - m2) * invS2;
        }
        return outBuf;
    }

    public static PreparedYoloFrame CreateYoloLetterbox(SKImage srcImg, int size = 640)
    {
        int srcW = srcImg.Width;
        int srcH = srcImg.Height;
        float scale = Math.Min((float)size / srcW, (float)size / srcH);
        int newW = (int)Math.Round(srcW * scale);
        int newH = (int)Math.Round(srcH * scale);
        float padX = (size - newW) / 2f;
        float padY = (size - newH) / 2f;

        using var canvasBmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(canvasBmp))
        {
            canvas.Clear(new SKColor(114, 114, 114));
            var dstRect = new SKRect(padX, padY, padX + newW, padY + newH);
            canvas.DrawImage(srcImg, dstRect, LinearSampling);
        }

        var span = canvasBmp.GetPixelSpan();
        int hw = size * size;
        var data = new float[3 * hw];
        float inv255 = 1f / 255f;
        for (int i = 0; i < hw; i++)
        {
            int p = i * 4;
            data[i] = span[p] * inv255;
            data[hw + i] = span[p + 1] * inv255;
            data[2 * hw + i] = span[p + 2] * inv255;
        }
        return new PreparedYoloFrame(data, scale, padX, padY, srcW, srcH);
    }

    public static SessionOptions CreateSessionOptions(bool useDirectMl)
    {
        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        if (useDirectMl)
        {
            opts.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            opts.EnableMemoryPattern = false;
            opts.AppendExecutionProvider_DML(0);
        }
        return opts;
    }
}

public sealed class YoloPanelDetector : IPanelDetector
{
    public const int InputSize = 640;

    private readonly InferenceSession _session;
    private readonly string _inputName;

    public YoloPanelDetector(string modelPath, bool useDirectMl)
    {
        using var opts = VisualImagePreprocess.CreateSessionOptions(useDirectMl);
        _session = new InferenceSession(modelPath, opts);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public List<VisualPanelBox> DetectPreprocessed(
        PreparedYoloFrame input,
        float confThreshold = 0.35f,
        float iouThreshold = 0.45f,
        float minAreaRatio = 0.02f)
    {
        var tensor = new DenseTensor<float>(input.Data, [1, 3, InputSize, InputSize]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        var output = results[0].AsTensor<float>();

        int numChannels = output.Dimensions[1];
        int numAnchors = output.Dimensions[2];
        int numClasses = numChannels - 4;

        var raw = new List<VisualPanelBox>();
        for (int a = 0; a < numAnchors; a++)
        {
            int bestCls = 0;
            float bestConf = output[0, 4, a];
            for (int c = 1; c < numClasses; c++)
            {
                float score = output[0, 4 + c, a];
                if (score > bestConf)
                {
                    bestConf = score;
                    bestCls = c;
                }
            }
            if (bestConf < confThreshold) continue;

            float cx = output[0, 0, a];
            float cy = output[0, 1, a];
            float bw = output[0, 2, a];
            float bh = output[0, 3, a];

            float x1 = Math.Clamp((cx - bw / 2f - input.PadX) / input.Scale / input.SrcWidth, 0f, 1f);
            float y1 = Math.Clamp((cy - bh / 2f - input.PadY) / input.Scale / input.SrcHeight, 0f, 1f);
            float x2 = Math.Clamp((cx + bw / 2f - input.PadX) / input.Scale / input.SrcWidth, 0f, 1f);
            float y2 = Math.Clamp((cy + bh / 2f - input.PadY) / input.Scale / input.SrcHeight, 0f, 1f);

            float nw = x2 - x1;
            float nh = y2 - y1;
            if (nw <= 0.01f || nh <= 0.01f) continue;
            if (bestCls == 0 && nw * nh < minAreaRatio) continue;

            raw.Add(new VisualPanelBox(x1, y1, nw, nh, bestConf, bestCls));
        }

        return ApplyNms(raw, iouThreshold);
    }

    private static List<VisualPanelBox> ApplyNms(List<VisualPanelBox> boxes, float iouThreshold)
    {
        var kept = new List<VisualPanelBox>();
        foreach (var group in boxes.GroupBy(b => b.ClassId))
        {
            var sorted = group.OrderByDescending(b => b.Confidence).ToList();
            var suppressed = new bool[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                if (suppressed[i]) continue;
                var a = sorted[i];
                kept.Add(a);
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (!suppressed[j] && ComputeIoU(a, sorted[j]) > iouThreshold)
                        suppressed[j] = true;
                }
            }
        }
        return kept;
    }

    private static float ComputeIoU(VisualPanelBox a, VisualPanelBox b)
    {
        float x1 = Math.Max(a.X, b.X);
        float y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(a.X + a.W, b.X + b.W);
        float y2 = Math.Min(a.Y + a.H, b.Y + b.H);
        float inter = Math.Max(0f, x2 - x1) * Math.Max(0f, y2 - y1);
        float union = a.Area + b.Area - inter;
        return union > 0f ? inter / union : 0f;
    }

    public void Dispose() => _session.Dispose();
}

public sealed class SscdFeatureEmbedder : IVisualEmbedder
{
    public const int DefaultInputSize = 288;
    public const int DefaultDimensions = 512;

    private readonly InferenceSession _session;
    private readonly string _inputName;

    public int InputSize { get; }
    public int Dimensions { get; }

    public SscdFeatureEmbedder(
        string modelPath,
        bool useDirectMl,
        int inputSize = DefaultInputSize,
        int dimensions = DefaultDimensions)
    {
        InputSize = inputSize;
        Dimensions = dimensions;
        using var opts = VisualImagePreprocess.CreateSessionOptions(useDirectMl);
        _session = new InferenceSession(modelPath, opts);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public float[][] EmbedPreprocessedBatch(IReadOnlyList<float[]> chwItems)
    {
        int n = chwItems.Count;
        if (n == 0) return [];

        int singleLen = 3 * InputSize * InputSize;
        var buf = new float[n * singleLen];
        for (int i = 0; i < n; i++)
        {
            Buffer.BlockCopy(chwItems[i], 0, buf, i * singleLen * sizeof(float), singleLen * sizeof(float));
        }

        var tensor = new DenseTensor<float>(buf, [n, 3, InputSize, InputSize]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        var output = results[0].AsTensor<float>();

        var embeddings = new float[n][];
        for (int i = 0; i < n; i++)
        {
            var vec = new float[Dimensions];
            for (int d = 0; d < Dimensions; d++)
            {
                vec[d] = output[i, d];
            }
            float norm = TensorPrimitives.Norm(vec);
            if (norm > 1e-6f)
            {
                TensorPrimitives.Divide(vec, norm, vec);
            }
            embeddings[i] = vec;
        }
        return embeddings;
    }

    public void Dispose() => _session.Dispose();
}
