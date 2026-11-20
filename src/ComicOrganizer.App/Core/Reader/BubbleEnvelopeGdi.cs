using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// GDI+ <see cref="Bitmap"/> 端的 <see cref="BubbleEnvelope"/> 轉接器（共用於 WinForms 閱讀器）。
/// </summary>
public static class BubbleEnvelopeGdi
{
    public static BubbleEnvelope Detect(
        Bitmap img,
        Rectangle textRegion,
        float lumThreshold = 165f,
        float marginRatio = 0.5f,
        IReadOnlyList<Rectangle>? blockers = null,
        int erodeRadius = 3,
        float clampMargin = 0.30f)
    {
        int W = img.Width, H = img.Height;
        if (W <= 0 || H <= 0) return BubbleEnvelope.DetectFromBgra32(ReadOnlySpan<byte>.Empty, 0, 0, 0, default);

        var data = img.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] buf = new byte[data.Stride * H];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        int stride = data.Stride;
        img.UnlockBits(data);

        IReadOnlyList<SKRectI>? skBlockers = null;
        if (blockers != null && blockers.Count > 0)
        {
            var list = new List<SKRectI>(blockers.Count);
            foreach (var r in blockers)
                list.Add(new SKRectI(r.Left, r.Top, r.Right, r.Bottom));
            skBlockers = list;
        }

        return BubbleEnvelope.DetectFromBgra32(
            buf,
            stride,
            W,
            H,
            new SKRectI(textRegion.Left, textRegion.Top, textRegion.Right, textRegion.Bottom),
            lumThreshold,
            marginRatio,
            skBlockers,
            erodeRadius,
            clampMargin);
    }
}
