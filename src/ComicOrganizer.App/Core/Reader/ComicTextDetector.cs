using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ComicOrganizer.App.Core;

/// <summary>
/// comic-text-detector（dmMaze，ONNX）文字偵測。輸出「像素級文字遮罩」，用於精準抹除原文
/// （不受氣泡形狀限制、無矩形戳出）。模型輸入 [1,3,1024,1024] RGB，輸出 seg [1,1,1024,1024] 機率圖。
/// 模型檔（comic-text-detector.onnx, ~95MB）預設放 exe 同層的 inference/；缺檔則 IsAvailable=false。
/// InferenceSession.Run 為 thread-safe；本類別 lazy 初始化 session。
/// </summary>
public sealed class ComicTextDetector : ITextMaskProvider, IDisposable
{
    private const int S = 1024;
    private readonly string _modelPath;
    private readonly object _lock = new();
    private InferenceSession? _session;
    private bool _initFailed;

    public ComicTextDetector(string? modelPath = null)
    {
        _modelPath = modelPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inference", "comic-text-detector.onnx");
    }

    /// <summary>模型檔是否存在（不存在時呼叫端應 fallback 回原行為）。</summary>
    public bool IsAvailable => File.Exists(_modelPath);

    /// <summary><see cref="ITextMaskProvider"/>：CTD 只用整頁圖，忽略 ocrBlocks。</summary>
    public byte[]? GetTextMask(Image page, IReadOnlyList<OcrBlock> ocrBlocks) => DetectTextMask(page);

    private InferenceSession? EnsureSession()
    {
        if (_session != null) return _session;
        if (_initFailed || !IsAvailable) return null;
        lock (_lock)
        {
            if (_session != null) return _session;
            // 先試 DirectML(GPU)：CPU 推論此模型 ~50~80s，GPU 通常 <2s。
            // DML EP 要求關 memory pattern + 循序執行。失敗(無 DX12/驅動)退回 CPU。
            try
            {
                var so = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    EnableMemoryPattern = false,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                };
                so.AppendExecutionProvider_DML(0);
                _session = new InferenceSession(_modelPath, so);
                System.Diagnostics.Debug.WriteLine("[CTD] InferenceSession on DirectML(GPU) EP");
                return _session;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CTD] DirectML init failed → CPU fallback: {ex.Message}");
            }
            try { _session = new InferenceSession(_modelPath); System.Diagnostics.Debug.WriteLine("[CTD] InferenceSession on CPU EP"); }
            catch { _initFailed = true; return null; }
            return _session;
        }
    }

    /// <summary>
    /// 偵測整頁文字遮罩，回傳長度 W*H 的 byte 陣列（1=文字、0=非文字），已 dilate。
    /// 模型不可用/失敗時回 null（呼叫端 fallback）。
    /// </summary>
    public byte[]? DetectTextMask(Image image, float threshold = 0.5f, int dilate = 2)
    {
        var session = EnsureSession();
        if (session == null) return null;

        int W = image.Width, H = image.Height;
        float scale = (float)S / Math.Max(W, H);
        int nw = (int)Math.Round(W * scale), nh = (int)Math.Round(H * scale);
        int padx = (S - nw) / 2, pady = (S - nh) / 2;

        // [1,3,S,S] 連續記憶體 → 直接寫 Buffer.Span(線性索引)，避開 tensor[a,b,c,d] 多維索引每次 new int[4] 的配置風暴。
        var input = new DenseTensor<float>(new[] { 1, 3, S, S });
        var ispan = input.Buffer.Span;
        const int plane = S * S;
        using (var canvas = new Bitmap(S, S, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.FromArgb(114, 114, 114));   // letterbox 灰邊
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(image, new Rectangle(padx, pady, nw, nh));
            }
            var cd = canvas.LockBits(new Rectangle(0, 0, S, S), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] cb = new byte[cd.Stride * S];
            Marshal.Copy(cd.Scan0, cb, 0, cb.Length);
            canvas.UnlockBits(cd);
            for (int y = 0; y < S; y++)
            {
                int row = y * cd.Stride, orow = y * S;
                for (int x = 0; x < S; x++)
                {
                    int o = row + x * 4, p = orow + x;
                    ispan[p] = cb[o + 2] / 255f;             // R 平面
                    ispan[plane + p] = cb[o + 1] / 255f;     // G 平面
                    ispan[2 * plane + p] = cb[o] / 255f;     // B 平面
                }
            }
        }

        byte[] mask = new byte[W * H];
        using (var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor("images", input) }))
        {
            // seg [1,1,S,S] → 取連續陣列一次，後處理線性索引(同樣避開多維索引配置)。
            float[] seg = results.First(r => r.Name == "seg").AsTensor<float>().ToArray();
            for (int oy = 0; oy < H; oy++)
            {
                int orow = oy * W;
                for (int ox = 0; ox < W; ox++)
                {
                    int cx = (int)Math.Round(ox * scale) + padx, cy = (int)Math.Round(oy * scale) + pady;
                    if (cx < 0 || cy < 0 || cx >= S || cy >= S) continue;
                    if (seg[cy * S + cx] > threshold) mask[orow + ox] = 1;
                }
            }
            return dilate > 0 ? DilateMask(mask, W, H, dilate) : mask;
        }
    }

    private static byte[] DilateMask(byte[] m, int W, int H, int d)
    {
        var cur = m;
        for (int k = 0; k < d; k++)
        {
            var nx = (byte[])cur.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (cur[i] == 1) continue;
                    if ((x > 0 && cur[i - 1] == 1) || (x < W - 1 && cur[i + 1] == 1) ||
                        (y > 0 && cur[i - W] == 1) || (y < H - 1 && cur[i + W] == 1)) nx[i] = 1;
                }
            cur = nx;
        }
        return cur;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
