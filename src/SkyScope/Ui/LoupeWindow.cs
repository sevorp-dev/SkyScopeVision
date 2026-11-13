using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SkyScope.Core;
using SkyScope.Native;

namespace SkyScope.Ui;

/// <summary>
/// Живая лупа: каждый кадр BitBlt региона экрана → StretchBlt → 32bpp кадр
/// с альфа-маской формы → UpdateLayeredWindow. Рамка/перекрестие/бейдж рисуются
/// поверх кадра. Классика ZoomIt. Layered-окно не попадает в собственный захват
/// и скрыто из записей через WDA.
/// </summary>
public class LoupeWindow
{
    private const uint WM_MOVE = 0x0003;
    private const uint WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;

    private readonly IntPtr _hwnd;
    private IntPtr _screenDc;
    private IntPtr _memDc;
    private IntPtr _memBitmap = IntPtr.Zero;
    private IntPtr _memOld;
    private int _memW, _memH;
    private IntPtr _frameDc;
    private IntPtr _frameBitmap = IntPtr.Zero;
    private IntPtr _frameOld;
    private IntPtr _frameBits;
    private int _frameW, _frameH;
    private Bitmap? _chrome;
    private Graphics? _chromeG;
    private int _winW = 100;
    private int _winH = 100;
    private int _winX;
    private int _winY;
    private NativeMethods.POINT _windowCenter;
    private bool _moveMode;
    private bool _disposed;
    private readonly IntPtr _exStyleOriginal;

    public IntPtr Handle => _hwnd;
    public int WindowX => _winX;
    public int WindowY => _winY;
    public int WindowW => _winW;
    public int WindowH => _winH;
    public bool MoveMode
    {
        get => _moveMode;
        set
        {
            _moveMode = value;
            var ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
            var next = value
                ? new IntPtr(ex.ToInt64() & ~(long)GdiApi.WS_EX_TRANSPARENT)
                : _exStyleOriginal;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, next);
        }
    }

    public event Action<int, int>? Moved;

    private static LoupeWindow? s_current;
    private static WndProcDelegate? s_wndProc;

    public LoupeWindow()
    {
        s_current = this;
        var hInstance = GdiApi.GetModuleHandle(null);
        s_wndProc = WndProc;

        var wc = new GdiApi.WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = hInstance,
            lpszClassName = "SkyScopeLoupe",
        };
        GdiApi.RegisterClass(ref wc);

        _hwnd = GdiApi.CreateWindowEx(
            GdiApi.WS_EX_LAYERED | GdiApi.WS_EX_TOOLWINDOW | GdiApi.WS_EX_TRANSPARENT,
            "SkyScopeLoupe", "",
            GdiApi.WS_POPUP | GdiApi.WS_VISIBLE,
            0, 0, 100, 100, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"LoupeWindow failed: {Marshal.GetLastWin32Error()}");

        _exStyleOriginal = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

        _screenDc = GdiApi.CreateDC("DISPLAY", "", null, IntPtr.Zero);
        _memDc = GdiApi.CreateCompatibleDC(IntPtr.Zero);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = s_current;
        if (self is not null && hWnd == self._hwnd)
        {
            if (msg == WM_NCHITTEST && self._moveMode)
                return new IntPtr(HTCAPTION);
            if (msg == WM_MOVE)
            {
                var info = NativeMethods.MONITORINFO.Create();
                if (NativeMethods.GetMonitorInfo(
                        NativeMethods.MonitorFromPoint(self._windowCenter, NativeMethods.MONITOR_DEFAULTTONEAREST),
                        ref info))
                {
                    var cx = (NativeMethods.LOWORD(lParam) + self._winW / 2) - (info.rcMonitor.Left + info.rcMonitor.Width / 2);
                    var cy = (NativeMethods.HIWORD(lParam) + self._winH / 2) - (info.rcMonitor.Top + info.rcMonitor.Height / 2);
                    self.Moved?.Invoke(cx, cy);
                }
            }
        }
        return LoupeInterop.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    internal static (int w, int h) WindowSize(ScopeStyle scope, double scale)
    {
        var R = scope.Radius * scale;
        return scope.Shape switch
        {
            ScopeShape.Rectangle => ((int)Math.Round(R * 2.6), (int)Math.Round(R * 1.5)),
            _ => ((int)Math.Round(R * 2), (int)Math.Round(R * 2)),
        };
    }

    private static (int x, int y) PositionFor(ScopeStyle scope, int cx, int cy, int w, int h, double scale)
    {
        var margin = (int)(50 * scale);
        return scope.Position switch
        {
            ScopePosition.Top => (cx - w / 2, cy - h / 2 - margin),
            ScopePosition.Bottom => (cx - w / 2, cy + h / 2 + margin),
            ScopePosition.Left => (cx - w / 2 - margin, cy - h / 2),
            ScopePosition.Right => (cx + w / 2 + margin, cy - h / 2),
            ScopePosition.Custom => (cx + (int)(scope.CustomOffsetX * scale) - w / 2, cy + (int)(scope.CustomOffsetY * scale) - h / 2),
            _ => (cx - w / 2, cy - h / 2),
        };
    }

    public void Update(int cx, int cy, double scale, Profile profile)
    {
        if (_disposed) return;
        var scope = profile.Scope;
        var (w, h) = WindowSize(scope, scale);
        _winW = w;
        _winH = h;
        _windowCenter = new NativeMethods.POINT { X = cx, Y = cy };

        var (x, y) = PositionFor(scope, cx, cy, w, h, scale);
        _winX = x;
        _winY = y;
        GdiApi.SetWindowPos(_hwnd, new IntPtr(-1), x, y, w, h, GdiApi.SWP_NOACTIVATE);
    }

    public void Render(double scale, double zoom, Profile profile)
    {
        if (_disposed) return;
        var scope = profile.Scope;
        var srcW = Math.Max(8, (int)Math.Round(_winW / zoom));
        var srcH = Math.Max(8, (int)Math.Round(_winH / zoom));
        var srcX = _windowCenter.X - srcW / 2;
        var srcY = _windowCenter.Y - srcH / 2;

        EnsureCaptureBuffer(srcW, srcH);
        EnsureFrameBuffer(_winW, _winH);
        if (_memBitmap == IntPtr.Zero || _frameBitmap == IntPtr.Zero) return;

        GdiApi.BitBlt(_memDc, 0, 0, srcW, srcH, _screenDc, srcX, srcY, GdiApi.SRCCOPY);
        GdiApi.SetStretchBltMode(_frameDc, GdiApi.HALFTONE);
        GdiApi.SetBrushOrgEx(_frameDc, 0, 0, out _);
        GdiApi.StretchBlt(_frameDc, 0, 0, _winW, _winH, _memDc, 0, 0, srcW, srcH, GdiApi.SRCCOPY);

        ApplyShapeAlpha(scope);
        DrawChrome(profile);
        PremultiplyFrame();
        MaybeDumpFrame();

        var size = new NativeMethods.POINT { X = _winW, Y = _winH };
        var srcPoint = new NativeMethods.POINT { X = 0, Y = 0 };
        var dest = new NativeMethods.POINT { X = _winX, Y = _winY };
        var blend = GdiApi.SourceAlphaBlend;
        var ok = GdiApi.UpdateLayeredWindow(_hwnd, _screenDc, ref dest, ref size, _frameDc, ref srcPoint, 0, ref blend, GdiApi.ULW_ALPHA);
        if (!ok && _ulwLogCount < 5)
        {
            _ulwLogCount++;
            Services.OverlayService.Log($"ULW failed err={Marshal.GetLastWin32Error()} w={_winW} h={_winH} x={_winX} y={_winY}");
        }
    }

    private int _ulwLogCount;

    // Маска формы: кэшируется по размеру/форме, со сглаживанием края (SDF-покрытие)
    private byte[]? _mask;
    private int _maskW, _maskH;
    private ScopeShape _maskShape = (ScopeShape)(-1);

    public static bool DumpFrameEnabled;

    private unsafe void ApplyShapeAlpha(ScopeStyle scope)
    {
        if (_frameBits == IntPtr.Zero) return;
        var w = _winW;
        var h = _winH;
        EnsureMask(w, h, scope);

        byte* px = (byte*)_frameBits;
        for (var y = 0; y < h; y++)
        {
            var row = px + (long)y * w * 4;
            var mRow = _mask!.AsSpan(y * w, w);
            for (var x = 0; x < w; x++)
                row[x * 4 + 3] = mRow[x];
        }
    }

    private void EnsureMask(int w, int h, ScopeStyle scope)
    {
        if (_mask is not null && _maskW == w && _maskH == h && _maskShape == scope.Shape) return;
        _maskW = w;
        _maskH = h;
        _maskShape = scope.Shape;
        _mask = new byte[w * h];

        var corner = (scope.Shape == ScopeShape.Rectangle ? 10 : 18) * 2;
        var rx = w / 2.0;
        var ry = h / 2.0;
        var minR = Math.Min(rx, ry);

        for (var y = 0; y < h; y++)
        {
            var py = y + 0.5 - ry;
            for (var x = 0; x < w; x++)
            {
                var px = x + 0.5 - rx;
                // SDF: >0 внутри формы, в пикселях
                double sd;
                switch (scope.Shape)
                {
                    case ScopeShape.Circle:
                        sd = minR - Math.Sqrt(px * px + py * py);
                        break;
                    case ScopeShape.RoundedSquare:
                    case ScopeShape.Rectangle:
                    {
                        var qx = Math.Abs(px) - (rx - corner);
                        var qy = Math.Abs(py) - (ry - corner);
                        var ox = Math.Max(qx, 0);
                        var oy = Math.Max(qy, 0);
                        sd = Math.Min(Math.Max(qx, qy), 0) + Math.Sqrt(ox * ox + oy * oy) - corner;
                        break;
                    }
                    default:
                        sd = Math.Min(rx - Math.Abs(px), ry - Math.Abs(py));
                        break;
                }
                _mask[y * w + x] = (byte)(Math.Clamp(sd + 0.5, 0, 1) * 255);
            }
        }
    }

    // UpdateLayeredWindow с AC_SRC_ALPHA ждёт премультиплицированный BGRA:
    // где alpha=0, RGB обязан быть 0, иначе «прозрачная» область проступает.
    private unsafe void PremultiplyFrame()
    {
        if (_frameBits == IntPtr.Zero) return;
        byte* px = (byte*)_frameBits;
        var count = (long)_winW * _winH;
        for (var i = 0; i < count; i++)
        {
            var p = px + i * 4;
            var a = p[3];
            if (a == 255) continue;
            if (a == 0) { p[0] = p[1] = p[2] = 0; continue; }
            p[0] = (byte)(p[0] * a / 255);
            p[1] = (byte)(p[1] * a / 255);
            p[2] = (byte)(p[2] * a / 255);
        }
    }

    private int _dumpCounter;

    private unsafe void MaybeDumpFrame()
    {
        if (!DumpFrameEnabled || _frameBits == IntPtr.Zero) return;
        if (++_dumpCounter % 60 != 1) return;
        try
        {
            var dir = Path.GetTempPath();
            using (var bmp = new Bitmap(_winW, _winH, PixelFormat.Format32bppArgb))
            {
                var bits = bmp.LockBits(new Rectangle(0, 0, _winW, _winH),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                byte* src = (byte*)_frameBits;
                for (var y = 0; y < _winH; y++)
                {
                    byte* s = src + (long)y * _winW * 4;
                    byte* d = (byte*)bits.Scan0 + (long)y * bits.Stride;
                    for (var x = 0; x < _winW * 4; x++) d[x] = s[x];
                }
                bmp.UnlockBits(bits);
                bmp.Save(Path.Combine(dir, "skyscope-frame.png"));
            }
            using (var alpha = new Bitmap(_winW, _winH, PixelFormat.Format24bppRgb))
            {
                var bits = alpha.LockBits(new Rectangle(0, 0, _winW, _winH),
                    ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                byte* src = (byte*)_frameBits;
                for (var y = 0; y < _winH; y++)
                {
                    byte* s = src + (long)y * _winW * 4;
                    byte* d = (byte*)bits.Scan0 + (long)y * bits.Stride;
                    for (var x = 0; x < _winW; x++)
                    {
                        var a = s[x * 4 + 3];
                        d[x * 3] = d[x * 3 + 1] = d[x * 3 + 2] = a;
                    }
                }
                alpha.UnlockBits(bits);
                alpha.Save(Path.Combine(dir, "skyscope-alpha.png"));
            }
        }
        catch { }
    }

    private void DrawChrome(Profile profile)
    {
        var scope = profile.Scope;
        if (_chrome is null || _chrome.Width != _winW || _chrome.Height != _winH)
        {
            _chromeG?.Dispose();
            _chrome?.Dispose();
            _chrome = new Bitmap(_winW, _winH, PixelFormat.Format32bppArgb);
            _chromeG = Graphics.FromImage(_chrome);
            _chromeG.SmoothingMode = SmoothingMode.AntiAlias;
        }
        var g = _chromeG!;
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var accent = ColorTranslator.FromHtml(scope.BorderColor);
        var borderAlpha = (int)(scope.BorderOpacity * 255);
        var penColor = Color.FromArgb(borderAlpha, accent);

        if (scope.BorderWidth > 0)
        {
            using var pen = new Pen(penColor, (float)scope.BorderWidth);
            var inset = (float)scope.BorderWidth / 2;
            var rect = new RectangleF(inset, inset, (float)(_winW - scope.BorderWidth), (float)(_winH - scope.BorderWidth));
            switch (scope.Shape)
            {
                case ScopeShape.Circle:
                    g.DrawEllipse(pen, rect);
                    break;
                case ScopeShape.Rectangle:
                case ScopeShape.RoundedSquare:
                    g.DrawPath(pen, RoundedPath(rect, scope.Shape == ScopeShape.Rectangle ? 10 : 18));
                    break;
                default:
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    break;
            }
        }

        if (scope.ShowZoomBadge)
        {
            var label = $"{profile.Zoom.Level:0.0}×";
            using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
            var size = g.MeasureString(label, font);
            var bw = size.Width + 18;
            var bh = 24;
            var bx = (_winW - bw) / 2;
            var by = _winH - bh - 12;
            g.FillRoundedRectangle(new SolidBrush(Color.FromArgb(166, 16, 16, 18)), bx, by, bw, bh, 11f);
            g.DrawString(label, font, new SolidBrush(Color.FromArgb(255, 0xFF, 0x7D, 0x1F)), bx + 9, by + 2);
        }

        BlendChromeOntoFrame();
    }

    private unsafe void BlendChromeOntoFrame()
    {
        if (_chrome is null || _frameBits == IntPtr.Zero) return;
        var rect = new Rectangle(0, 0, _winW, _winH);
        var data = _chrome.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte* src0 = (byte*)data.Scan0;
        byte* dst0 = (byte*)_frameBits;
        for (var y = 0; y < _winH; y++)
        {
            byte* sRow = src0 + (long)y * data.Stride;
            byte* dRow = dst0 + (long)y * _winW * 4;
            for (var x = 0; x < _winW; x++)
            {
                var ca = sRow[x * 4 + 3];
                if (ca == 0) continue;
                var maskA = _mask is not null ? _mask[y * _winW + x] : (byte)255;
                if (maskA == 0) continue;
                if (maskA < 255) ca = (byte)(ca * (maskA / 255.0));
                var d = dRow + x * 4;
                if (ca == 255)
                {
                    d[0] = sRow[x * 4];
                    d[1] = sRow[x * 4 + 1];
                    d[2] = sRow[x * 4 + 2];
                    d[3] = 255;
                    continue;
                }
                var fa = d[3];
                var caF = ca / 255.0;
                var faF = fa / 255.0;
                var outA = caF + faF * (1 - caF);
                if (outA <= 0) { d[3] = 0; continue; }
                d[0] = (byte)((sRow[x * 4] * caF + d[0] * faF * (1 - caF)) / outA);
                d[1] = (byte)((sRow[x * 4 + 1] * caF + d[1] * faF * (1 - caF)) / outA);
                d[2] = (byte)((sRow[x * 4 + 2] * caF + d[2] * faF * (1 - caF)) / outA);
                d[3] = (byte)(outA * 255);
            }
        }
        _chrome.UnlockBits(data);
    }

    private static GraphicsPath RoundedPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void EnsureCaptureBuffer(int w, int h)
    {
        if (_memBitmap != IntPtr.Zero && _memW == w && _memH == h) return;
        if (_memBitmap != IntPtr.Zero)
        {
            GdiApi.SelectObject(_memDc, _memOld);
            GdiApi.DeleteObject(_memBitmap);
        }
        _memW = w;
        _memH = h;
        _memBitmap = GdiApi.CreateCompatibleBitmap(_screenDc, w, h);
        _memOld = GdiApi.SelectObject(_memDc, _memBitmap);
    }

    private void EnsureFrameBuffer(int w, int h)
    {
        if (_frameBitmap != IntPtr.Zero && _frameW == w && _frameH == h) return;
        if (_frameBitmap != IntPtr.Zero)
        {
            GdiApi.SelectObject(_frameDc, _frameOld);
            GdiApi.DeleteObject(_frameBitmap);
            _frameBits = IntPtr.Zero;
        }
        _frameW = w;
        _frameH = h;
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
        _frameDc = _frameDc == IntPtr.Zero ? GdiApi.CreateCompatibleDC(IntPtr.Zero) : _frameDc;
        _frameBitmap = GdiApi.CreateDIBSection(_frameDc, ref bmi, GdiApi.DIB_RGB_COLORS, out _frameBits, IntPtr.Zero, 0);
        _frameOld = GdiApi.SelectObject(_frameDc, _frameBitmap);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (s_current == this) s_current = null;
        _chromeG?.Dispose();
        _chrome?.Dispose();
        if (_memBitmap != IntPtr.Zero)
        {
            GdiApi.SelectObject(_memDc, _memOld);
            GdiApi.DeleteObject(_memBitmap);
        }
        if (_frameBitmap != IntPtr.Zero)
        {
            GdiApi.SelectObject(_frameDc, _frameOld);
            GdiApi.DeleteObject(_frameBitmap);
        }
        if (_memDc != IntPtr.Zero) GdiApi.DeleteDC(_memDc);
        if (_frameDc != IntPtr.Zero) GdiApi.DeleteDC(_frameDc);
        if (_screenDc != IntPtr.Zero) GdiApi.DeleteDC(_screenDc);
        if (_hwnd != IntPtr.Zero) GdiApi.DestroyWindow(_hwnd);
    }
}

internal static class LoupeChromeExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float w, float h, float radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}

[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

internal static class LoupeInterop
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
