using System.Runtime.InteropServices;
using SkyScope.Core;
using SkyScope.Native;

namespace SkyScope.Ui;

/// <summary>
/// Полноэкранное затемнение с «дыркой» под лупу. Layered-окно, обновляется
/// только при смене позиции/размера лупы, а не каждый кадр.
/// </summary>
public class DimWindow
{
    private IntPtr _hwnd;
    private IntPtr _screenDc;
    private IntPtr _memDc;
    private IntPtr _bitmap = IntPtr.Zero;
    private IntPtr _old;
    private IntPtr _bits;
    private int _w;
    private int _h;

    public DimWindow()
    {
        var hInstance = GdiApi.GetModuleHandle(null);
        _screenDc = GdiApi.CreateDC("DISPLAY", "", null, IntPtr.Zero);
        _memDc = GdiApi.CreateCompatibleDC(IntPtr.Zero);

        var wc = new GdiApi.WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_proc ??= DefProc),
            hInstance = hInstance,
            lpszClassName = "SkyScopeDim",
        };
        GdiApi.RegisterClass(ref wc);
        _hwnd = GdiApi.CreateWindowEx(
            GdiApi.WS_EX_LAYERED | GdiApi.WS_EX_TOOLWINDOW | GdiApi.WS_EX_TRANSPARENT,
            "SkyScopeDim", "",
            GdiApi.WS_POPUP,
            0, 0, 100, 100, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }

    private static WndProcDelegate? s_proc;
    private static IntPtr DefProc(IntPtr h, uint m, IntPtr w, IntPtr l) => LoupeInterop.DefWindowProc(h, m, w, l);

    public unsafe void Update(NativeMethods.RECT monitorPhysical, ScopeStyle scope,
        int loupeX, int loupeY, int loupeW, int loupeH)
    {
        var w = monitorPhysical.Width;
        var h = monitorPhysical.Height;
        if (_bitmap == IntPtr.Zero || _w != w || _h != h)
        {
            if (_bitmap != IntPtr.Zero)
            {
                GdiApi.SelectObject(_memDc, _old);
                GdiApi.DeleteObject(_bitmap);
            }
            _w = w;
            _h = h;
            var bmi = new GdiApi.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<GdiApi.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = GdiApi.BI_RGB,
                biSizeImage = (uint)(w * h * 4),
            };
            _bitmap = GdiApi.CreateDIBSection(_memDc, ref bmi, GdiApi.DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
            _old = GdiApi.SelectObject(_memDc, _bitmap);
        }

        var alpha = (byte)(scope.DimOpacity * 255);
        var circle = scope.Shape == ScopeShape.Circle;
        var radius = scope.Shape is ScopeShape.Rectangle or ScopeShape.RoundedSquare ? (scope.Shape == ScopeShape.Rectangle ? 10 : 18) * 2 : 0;
        var lcx = loupeX + loupeW / 2.0;
        var lcy = loupeY + loupeH / 2.0;
        var lrx = loupeW / 2.0;
        var lry = loupeH / 2.0;

        byte* px = (byte*)_bits;
        for (var y = 0; y < h; y++)
        {
            var row = px + (long)y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var inside = circle
                    ? Math.Pow((x + 0.5 - lcx) / lrx, 2) + Math.Pow((y + 0.5 - lcy) / lry, 2) <= 1
                    : radius == 0
                        ? Math.Abs(x + 0.5 - lcx) <= lrx && Math.Abs(y + 0.5 - lcy) <= lry
                        : Math.Pow(Math.Max(0, Math.Abs(x + 0.5 - lcx) - (lrx - radius)) / radius, 2)
                          + Math.Pow(Math.Max(0, Math.Abs(y + 0.5 - lcy) - (lry - radius)) / radius, 2) <= 1;
                if (inside) continue;
                row[x * 4] = 0;
                row[x * 4 + 1] = 0;
                row[x * 4 + 2] = 0;
                row[x * 4 + 3] = alpha;
            }
        }

        GdiApi.SetWindowPos(_hwnd, new IntPtr(-1), monitorPhysical.Left, monitorPhysical.Top, w, h,
            GdiApi.SWP_NOACTIVATE | GdiApi.SWP_SHOWWINDOW);
        var size = new NativeMethods.POINT { X = w, Y = h };
        var src = new NativeMethods.POINT { X = 0, Y = 0 };
        var dest = new NativeMethods.POINT { X = monitorPhysical.Left, Y = monitorPhysical.Top };
        var blend = GdiApi.SourceAlphaBlend;
        GdiApi.UpdateLayeredWindow(_hwnd, _screenDc, ref dest, ref size, _memDc, ref src, 0, ref blend, GdiApi.ULW_ALPHA);
    }

    public void Hide()
    {
        if (_hwnd != IntPtr.Zero)
            GdiApi.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                GdiApi.SWP_NOACTIVATE | GdiApi.SWP_HIDEWINDOW);
    }

    public void Dispose()
    {
        if (_bitmap != IntPtr.Zero)
        {
            GdiApi.SelectObject(_memDc, _old);
            GdiApi.DeleteObject(_bitmap);
        }
        if (_memDc != IntPtr.Zero) GdiApi.DeleteDC(_memDc);
        if (_screenDc != IntPtr.Zero) GdiApi.DeleteDC(_screenDc);
        if (_hwnd != IntPtr.Zero) GdiApi.DestroyWindow(_hwnd);
    }
}
