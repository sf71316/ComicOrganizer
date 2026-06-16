using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 依文字遮罩抹除原文：遮罩內像素以鄰近背景色由邊界往內擴散填補（平底氣泡：白→白、黑→黑），
/// 取代逐欄矩形白底，因此不受氣泡形狀限制、無戳出。搭配 <see cref="ComicTextDetector"/> 的遮罩使用。
/// </summary>
public static class TextEraser
{
    /// <summary>回傳抹除後的新點陣圖（不改動 source）。mask 長度需為 W*H，1=要抹除的文字像素。</summary>
    public static Bitmap Erase(Bitmap source, byte[] mask)
    {
        int W = source.Width, H = source.Height;
        var result = new Bitmap(source);
        var data = result.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        byte[] px = new byte[stride * H];
        Marshal.Copy(data.Scan0, px, 0, px.Length);

        DiffusionFill(px, stride, W, H, mask);

        Marshal.Copy(px, 0, data.Scan0, px.Length);
        result.UnlockBits(data);
        return result;
    }

    /// <summary>BFS 由遮罩邊界往內填：每個遮罩像素取已知鄰居的平均色，逐圈推進。O(遮罩像素)。</summary>
    private static void DiffusionFill(byte[] px, int stride, int W, int H, byte[] mask)
    {
        bool[] known = new bool[W * H];
        bool[] queued = new bool[W * H];
        for (int i = 0; i < known.Length; i++) known[i] = mask[i] == 0;

        var q = new Queue<int>();
        // 邊界：遮罩內、且有已知鄰居的像素
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (known[i]) continue;
                if ((x > 0 && known[i - 1]) || (x < W - 1 && known[i + 1]) ||
                    (y > 0 && known[i - W]) || (y < H - 1 && known[i + W]))
                { q.Enqueue(i); queued[i] = true; }
            }

        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int x = i % W, y = i / W;
            int rs = 0, gs = 0, bs = 0, c = 0;
            if (x > 0 && known[i - 1]) { int o = y * stride + (x - 1) * 4; rs += px[o + 2]; gs += px[o + 1]; bs += px[o]; c++; }
            if (x < W - 1 && known[i + 1]) { int o = y * stride + (x + 1) * 4; rs += px[o + 2]; gs += px[o + 1]; bs += px[o]; c++; }
            if (y > 0 && known[i - W]) { int o = (y - 1) * stride + x * 4; rs += px[o + 2]; gs += px[o + 1]; bs += px[o]; c++; }
            if (y < H - 1 && known[i + W]) { int o = (y + 1) * stride + x * 4; rs += px[o + 2]; gs += px[o + 1]; bs += px[o]; c++; }
            if (c > 0)
            {
                int o = y * stride + x * 4;
                px[o + 2] = (byte)(rs / c); px[o + 1] = (byte)(gs / c); px[o] = (byte)(bs / c);
            }
            known[i] = true;
            // 將尚未填的遮罩鄰居入列
            if (x > 0 && !known[i - 1] && !queued[i - 1]) { q.Enqueue(i - 1); queued[i - 1] = true; }
            if (x < W - 1 && !known[i + 1] && !queued[i + 1]) { q.Enqueue(i + 1); queued[i + 1] = true; }
            if (y > 0 && !known[i - W] && !queued[i - W]) { q.Enqueue(i - W); queued[i - W] = true; }
            if (y < H - 1 && !known[i + W] && !queued[i + W]) { q.Enqueue(i + W); queued[i + W] = true; }
        }
    }
}
