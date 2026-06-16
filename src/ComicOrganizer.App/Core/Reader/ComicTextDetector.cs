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
            try { _session = new InferenceSession(_modelPath); }
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

        var input = new DenseTensor<float>(new[] { 1, 3, S, S });
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
                for (int x = 0; x < S; x++)
                {
                    int o = y * cd.Stride + x * 4;
                    input[0, 0, y, x] = cb[o + 2] / 255f; // R
                    input[0, 1, y, x] = cb[o + 1] / 255f; // G
                    input[0, 2, y, x] = cb[o] / 255f;     // B
                }
        }

        byte[] mask = new byte[W * H];
        using (var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor("images", input) }))
        {
            var seg = results.First(r => r.Name == "seg").AsTensor<float>();
            for (int oy = 0; oy < H; oy++)
                for (int ox = 0; ox < W; ox++)
                {
                    int cx = (int)Math.Round(ox * scale) + padx, cy = (int)Math.Round(oy * scale) + pady;
                    if (cx < 0 || cy < 0 || cx >= S || cy >= S) continue;
                    if (seg[0, 0, cy, cx] > threshold) mask[oy * W + ox] = 1;
                }
        }

        return dilate > 0 ? DilateMask(mask, W, H, dilate) : mask;
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
