using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 氣泡實際輪廓：對影像從文字區往外 flood-fill 氣泡內部（白底或黑底），
/// 得到每一影像欄(X)的上下邊界(envelope)。用於讓譯文在「氣泡真實範圍」內排版（填滿、貼合圓/斜形狀），
/// 不再受原文 OCR 文字框/欄結構限制。座標皆為「來源影像」像素座標。
/// </summary>
public sealed class BubbleEnvelope
{
    public int Left { get; private set; }
    public int Right { get; private set; }
    public int[] Top { get; private set; } = Array.Empty<int>();    // 以影像 X 為索引；-1 = 該欄無填充
    public int[] Bottom { get; private set; } = Array.Empty<int>();
    public bool Valid { get; private set; }
    public bool DarkBubble { get; private set; }

    private BubbleEnvelope() { }

    /// <summary>
    /// 偵測氣泡輪廓。textRegion 提供 flood-fill 種子來源與外擴上限（防漏出爆炸）。
    /// 偵測失敗（漏出、找不到合理內部）時回傳 Valid=false（呼叫端 fallback）。
    /// </summary>
    public static BubbleEnvelope Detect(Bitmap img, Rectangle textRegion, float lumThreshold = 165f, float marginRatio = 0.5f)
    {
        var env = new BubbleEnvelope();
        int W = img.Width, H = img.Height;
        if (W <= 0 || H <= 0) return env;

        var data = img.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] buf = new byte[data.Stride * H];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        int stride = data.Stride;
        img.UnlockBits(data);

        float Lum(int x, int y) { int o = y * stride + x * 4; return 0.114f * buf[o] + 0.587f * buf[o + 1] + 0.299f * buf[o + 2]; }

        int bx = textRegion.X, by = textRegion.Y, bw = textRegion.Width, bh = textRegion.Height;
        int mx = (int)(bw * marginRatio), my = (int)(bh * marginRatio);
        int x0 = Math.Max(0, bx - mx), y0 = Math.Max(0, by - my);
        int x1 = Math.Min(W - 1, bx + bw + mx), y1 = Math.Min(H - 1, by + bh + my);
        if (x1 <= x0 || y1 <= y0) return env;

        // 判斷氣泡內部是亮(白底)還是暗(黑底)：取文字框四邊中點附近的中位亮度
        float[] samp = { Lum(Math.Clamp(bx + bw / 2, 0, W - 1), Math.Clamp(by + 2, 0, H - 1)),
                         Lum(Math.Clamp(bx + bw / 2, 0, W - 1), Math.Clamp(by + bh - 2, 0, H - 1)),
                         Lum(Math.Clamp(bx + 2, 0, W - 1), Math.Clamp(by + bh / 2, 0, H - 1)),
                         Lum(Math.Clamp(bx + bw - 2, 0, W - 1), Math.Clamp(by + bh / 2, 0, H - 1)) };
        Array.Sort(samp);
        float mid = (samp[1] + samp[2]) / 2f;
        bool bright = mid >= 128f;
        env.DarkBubble = !bright;
        // 填充條件：亮底填亮像素、暗底填暗像素
        bool IsInside(int x, int y) { float l = Lum(x, y); return bright ? l > lumThreshold : l < (255f - lumThreshold); }

        // 種子：文字框中心附近找符合內部條件的點
        int sx = bx + bw / 2, sy = by + bh / 2;
        if (!IsInside(sx, sy))
        {
            bool found = false;
            for (int r = 1; r < Math.Max(bw, bh) / 2 && !found; r++)
                foreach (var (dx, dy) in new[] { (r, 0), (-r, 0), (0, r), (0, -r) })
                {
                    int nx = Math.Clamp(sx + dx, x0, x1), ny = Math.Clamp(sy + dy, y0, y1);
                    if (IsInside(nx, ny)) { sx = nx; sy = ny; found = true; break; }
                }
            if (!found) return env;
        }

        var filled = new bool[W * H];
        var stack = new System.Collections.Generic.Stack<int>();
        filled[sy * W + sx] = true; stack.Push(sy * W + sx);
        long cnt = 0;
        while (stack.Count > 0)
        {
            int p = stack.Pop(); cnt++;
            int cx = p % W, cy = p / W;
            void Try(int nx, int ny)
            {
                if (nx < x0 || nx > x1 || ny < y0 || ny > y1) return;
                int np = ny * W + nx;
                if (filled[np] || !IsInside(nx, ny)) return;
                filled[np] = true; stack.Push(np);
            }
            Try(cx - 1, cy); Try(cx + 1, cy); Try(cx, cy - 1); Try(cx, cy + 1);
        }

        // 漏出判定：填充佔外擴框比例過高 → 視為漏出(無封閉輪廓)，無效
        double boxArea = (double)(x1 - x0 + 1) * (y1 - y0 + 1);
        if (cnt / boxArea > 0.85) return env;          // 幾乎填滿外擴框 = 漏出
        if (cnt < bw * bh * 0.15) return env;          // 填得太少 = 沒抓到氣泡

        // 每欄上下邊界
        var top = new int[W]; var bot = new int[W];
        for (int x = 0; x < W; x++) { top[x] = -1; bot[x] = -1; }
        int left = int.MaxValue, right = int.MinValue;
        for (int x = x0; x <= x1; x++)
        {
            int baseIdx = x;
            int tt = -1, bb = -1;
            for (int y = y0; y <= y1; y++)
                if (filled[y * W + baseIdx]) { if (tt < 0) tt = y; bb = y; }
            top[x] = tt; bot[x] = bb;
            if (tt >= 0) { if (x < left) left = x; if (x > right) right = x; }
        }
        if (right <= left) return env;

        env.Top = top; env.Bottom = bot; env.Left = left; env.Right = right; env.Valid = true;
        return env;
    }

    /// <summary>取影像欄 x 的上下邊界（會 clamp 到 [Left,Right]）。該欄無填充時回 (-1,-1)。</summary>
    public (int top, int bottom) At(int x)
    {
        if (!Valid) return (-1, -1);
        x = Math.Clamp(x, Left, Right);
        return (Top[x], Bottom[x]);
    }
}
