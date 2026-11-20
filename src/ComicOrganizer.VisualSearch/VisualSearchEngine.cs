using System.Diagnostics;
using System.Numerics.Tensors;
using SkiaSharp;

namespace ComicOrganizer.VisualSearch;

public sealed record VisualSearchHit(
    float Similarity,
    VisualVectorMeta Meta);

public sealed record VisualSearchReport(
    List<VisualSearchHit> Hits,
    double PreprocessMs,
    double EmbedMs,
    double VectorSearchMs,
    double DbLookupMs,
    double RerankMs,
    bool RerankTriggered)
{
    public double TotalMs => PreprocessMs + EmbedMs + VectorSearchMs + DbLookupMs + RerankMs;
}

/// <summary>
/// 純 CPU + MMap 視覺查詢引擎（0 VRAM、低記憶體佔用），支援自動去黑/白邊與低信心 Multi-Crop 精排。
/// </summary>
public sealed class VisualSearchEngine : IDisposable
{
    private readonly IVisualEmbedder _cpuEmbedder;
    private readonly ShardedVectorStore _mmapStore;
    private readonly VisualIndexDatabase _db;

    public VisualSearchEngine(VisualSearchPaths paths)
        : this(
            paths,
            new SscdFeatureEmbedder(paths.EmbedModelPath, useDirectMl: false))
    {
    }

    /// <summary>
    /// 供單元測試注入 Mock Embedder 的建構子。
    /// </summary>
    public VisualSearchEngine(VisualSearchPaths paths, IVisualEmbedder cpuEmbedder)
    {
        if (!File.Exists(paths.DatabasePath))
            throw new FileNotFoundException("尚未建立視覺搜尋索引資料庫。", paths.DatabasePath);

        _cpuEmbedder = cpuEmbedder;
        _mmapStore = new ShardedVectorStore(paths, cpuEmbedder.Dimensions, readOnlyMmap: true);
        _db = new VisualIndexDatabase(paths.DatabasePath, readOnly: true);
    }

    public VisualSearchReport SearchImage(
        SKBitmap queryBitmap,
        int topK = 10,
        bool trimBorders = true,
        bool enableMultiCropRerank = true,
        float rerankThreshold = 0.72f)
    {
        var swPre = Stopwatch.StartNew();
        using var processed = trimBorders ? TrimSolidBorders(queryBitmap) : queryBitmap.Copy();
        using var processedImg = SKImage.FromBitmap(processed);
        var chw = VisualImagePreprocess.CreateCropNormalizedChw(
            processedImg,
            new SKRect(0, 0, processedImg.Width, processedImg.Height),
            _cpuEmbedder.InputSize);
        swPre.Stop();

        var swEmb = Stopwatch.StartNew();
        var queryVec = _cpuEmbedder.EmbedPreprocessedBatch([chw])[0];
        swEmb.Stop();

        var swVec = Stopwatch.StartNew();
        int candidateLimit = enableMultiCropRerank ? Math.Max(topK, 10) : topK;
        int fetchK = Math.Max(candidateLimit * 5, 32);
        var rawHits = _mmapStore.SearchAllShards(queryVec, fetchK);
        swVec.Stop();

        var swDb = Stopwatch.StartNew();
        var metaMap = _db.GetVectorsByIds(rawHits.Select(h => (long)h.VecId));

        var candidates = new List<VisualSearchHit>(candidateLimit);
        var seenPages = new HashSet<(long MangaId, int PageNo)>();
        foreach (var (vecId, dist) in rawHits)
        {
            if (candidates.Count >= candidateLimit) break;
            if (!metaMap.TryGetValue((long)vecId, out var meta) || meta.IsDeleted) continue;
            if (!seenPages.Add((meta.MangaId, meta.PageNo))) continue;

            candidates.Add(new VisualSearchHit(1f - dist, meta));
        }
        swDb.Stop();

        double rerankMs = 0;
        bool rerankTriggered = false;
        if (enableMultiCropRerank && candidates.Count > 1 && candidates[0].Similarity < rerankThreshold)
        {
            rerankTriggered = true;
            var swRr = Stopwatch.StartNew();
            candidates = RerankWithMultiCrop(queryVec, candidates);
            swRr.Stop();
            rerankMs = swRr.Elapsed.TotalMilliseconds;
        }

        return new VisualSearchReport(
            candidates.Take(topK).ToList(),
            swPre.Elapsed.TotalMilliseconds,
            swEmb.Elapsed.TotalMilliseconds,
            swVec.Elapsed.TotalMilliseconds,
            swDb.Elapsed.TotalMilliseconds,
            rerankMs,
            rerankTriggered);
    }

    private List<VisualSearchHit> RerankWithMultiCrop(float[] queryVec, List<VisualSearchHit> candidates)
    {
        var rescored = new List<VisualSearchHit>(candidates.Count);
        foreach (var cand in candidates)
        {
            float bestSim = cand.Similarity;
            using var pageImg = VisualComicReader.LoadSinglePageImage(cand.Meta.MangaPath, cand.Meta.EntryName);
            if (pageImg != null)
            {
                float bx = cand.Meta.IsFullPage ? 0f : cand.Meta.X;
                float by = cand.Meta.IsFullPage ? 0f : cand.Meta.Y;
                float bw = cand.Meta.IsFullPage ? 1f : cand.Meta.W;
                float bh = cand.Meta.IsFullPage ? 1f : cand.Meta.H;

                float px = bx * pageImg.Width;
                float py = by * pageImg.Height;
                float pw = bw * pageImg.Width;
                float ph = bh * pageImg.Height;

                SKRect[] subRects =
                [
                    new(px, py, px + pw * 0.60f, py + ph * 0.60f),
                    new(px + pw * 0.40f, py, px + pw, py + ph * 0.60f),
                    new(px, py + ph * 0.40f, px + pw * 0.60f, py + ph),
                    new(px + pw * 0.40f, py + ph * 0.40f, px + pw, py + ph),
                    new(px + pw * 0.20f, py + ph * 0.20f, px + pw * 0.80f, py + ph * 0.80f),
                ];

                var chwList = new List<float[]>(subRects.Length);
                foreach (var r in subRects)
                {
                    if (r.Width >= 24 && r.Height >= 24)
                    {
                        chwList.Add(VisualImagePreprocess.CreateCropNormalizedChw(pageImg, r, _cpuEmbedder.InputSize));
                    }
                }
                if (chwList.Count > 0)
                {
                    var vecs = _cpuEmbedder.EmbedPreprocessedBatch(chwList);
                    foreach (var v in vecs)
                    {
                        float s = TensorPrimitives.CosineSimilarity(queryVec, v);
                        if (s > bestSim) bestSim = s;
                    }
                }
            }
            rescored.Add(cand with { Similarity = bestSim });
        }
        return rescored.OrderByDescending(h => h.Similarity).ToList();
    }

    /// <summary>
    /// 產生命中原頁的縮圖 JPEG（若是分鏡命中則疊加醒目高亮框）。
    /// </summary>
    public static byte[]? RenderHitPreviewJpeg(VisualVectorMeta meta, int maxWidth = 360)
    {
        using var pageImg = VisualComicReader.LoadSinglePageImage(meta.MangaPath, meta.EntryName);
        if (pageImg == null) return null;

        float scale = Math.Min(1f, (float)maxWidth / pageImg.Width);
        int w = Math.Max(1, (int)Math.Round(pageImg.Width * scale));
        int h = Math.Max(1, (int)Math.Round(pageImg.Height * scale));

        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.DrawImage(pageImg, new SKRect(0, 0, w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            if (!meta.IsFullPage)
            {
                var box = new SKRect(meta.X * w, meta.Y * h, (meta.X + meta.W) * w, (meta.Y + meta.H) * h);
                using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(34, 197, 94, 48) };
                using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(34, 197, 94, 240), StrokeWidth = 3 };
                canvas.DrawRect(box, fill);
                canvas.DrawRect(box, stroke);
            }
        }

        using var outImg = SKImage.FromBitmap(bmp);
        using var encoded = outImg.Encode(SKEncodedImageFormat.Jpeg, 85);
        return encoded.ToArray();
    }

    /// <summary>
    /// 自動偵測並裁切截圖四周純黑／純白留白邊框。
    /// </summary>
    public static SKBitmap TrimSolidBorders(SKBitmap src)
    {
        int w = src.Width;
        int h = src.Height;
        if (w < 40 || h < 40) return src.Copy();

        var pixels = src.Pixels;
        bool IsUniformBorderRow(int y)
        {
            int bw = 0, samples = 0;
            int step = Math.Max(1, w / 64);
            for (int x = 0; x < w; x += step)
            {
                var c = pixels[y * w + x];
                if ((c.Red < 18 && c.Green < 18 && c.Blue < 18) || (c.Red > 240 && c.Green > 240 && c.Blue > 240))
                    bw++;
                samples++;
            }
            return samples > 0 && (double)bw / samples >= 0.98;
        }

        bool IsUniformBorderCol(int x, int top, int bottom)
        {
            int bw = 0, samples = 0;
            int step = Math.Max(1, (bottom - top + 1) / 64);
            for (int y = top; y <= bottom; y += step)
            {
                var c = pixels[y * w + x];
                if ((c.Red < 18 && c.Green < 18 && c.Blue < 18) || (c.Red > 240 && c.Green > 240 && c.Blue > 240))
                    bw++;
                samples++;
            }
            return samples > 0 && (double)bw / samples >= 0.98;
        }

        int top = 0, bottom = h - 1, left = 0, right = w - 1;
        while (top < bottom - 32 && IsUniformBorderRow(top)) top++;
        while (bottom > top + 32 && IsUniformBorderRow(bottom)) bottom--;
        while (left < right - 32 && IsUniformBorderCol(left, top, bottom)) left++;
        while (right > left + 32 && IsUniformBorderCol(right, top, bottom)) right--;

        int cw = right - left + 1;
        int ch = bottom - top + 1;
        if ((cw >= w * 0.98 && ch >= h * 0.98) || cw < 32 || ch < 32)
            return src.Copy();

        var dst = new SKBitmap(cw, ch, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(dst);
        using var img = SKImage.FromBitmap(src);
        canvas.DrawImage(img, new SKRect(left, top, left + cw, top + ch), new SKRect(0, 0, cw, ch), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return dst;
    }

    public void Dispose()
    {
        _cpuEmbedder.Dispose();
        _mmapStore.Dispose();
        _db.Dispose();
    }
}
