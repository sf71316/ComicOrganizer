using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 閱讀器譯文字型：內嵌「獅尾黑體 Medium(SweiSansCJKtc-Medium, OFL/思源衍生)」並以 PrivateFontCollection 載入，
/// 漫畫對白用中等字重(縮小後仍清楚、不致過粗糊成一團)。載入失敗時 fallback 回系統 Microsoft JhengHei UI。
/// 字型檔以 EmbeddedResource 內嵌 → 單檔發行/別台電腦免安裝。
/// </summary>
public static class ReaderFonts
{
    private const string ResourceName = "ComicOrganizer.App.Resources.fonts.SweiSansCJKtc-Medium.ttf";

    private static readonly object _lock = new();
    private static readonly PrivateFontCollection _pfc = new();
    private static IntPtr _fontMem = IntPtr.Zero;   // AddMemoryFont 的記憶體需在字型生命週期內保留 → 不釋放
    private static FontFamily? _family;

    /// <summary>譯文字型家族（內嵌獅尾黑體；失敗則 JhengHei）。</summary>
    public static FontFamily TranslationFamily
    {
        get
        {
            if (_family != null) return _family;
            lock (_lock)
            {
                if (_family != null) return _family;
                try
                {
                    var asm = typeof(ReaderFonts).Assembly;
                    using Stream? s = asm.GetManifestResourceStream(ResourceName);
                    if (s != null)
                    {
                        byte[] data = new byte[s.Length];
                        int read = 0;
                        while (read < data.Length)
                        {
                            int n = s.Read(data, read, data.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        _fontMem = Marshal.AllocCoTaskMem(data.Length);
                        Marshal.Copy(data, 0, _fontMem, data.Length);
                        _pfc.AddMemoryFont(_fontMem, data.Length);
                        if (_pfc.Families.Length > 0) _family = _pfc.Families[0];
                    }
                }
                catch { /* fallback 如下 */ }

                _family ??= new FontFamily("Microsoft JhengHei UI");
                return _family;
            }
        }
    }

    /// <summary>建立譯文字型（內嵌單一字重檔，家族只含一種樣式 → 取家族可用樣式即可）。</summary>
    public static Font MakeFont(float sizePoints)
    {
        var fam = TranslationFamily;
        FontStyle style = fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
            : fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
            : FontStyle.Regular;
        return new Font(fam, sizePoints, style);
    }
}
