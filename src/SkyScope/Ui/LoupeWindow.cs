using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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

    private unsafe void ApplyShapeAlpha(ScopeStyle scope)
    {
        if (_frameBits == IntPtr.Zero) return;
        var w = _winW;
        var h = _winH;
        byte* px = (byte*)_frameBits;
        switch (scope.Shape)
        {
            case ScopeShape.Circle:
            {
                var rx = w / 2.0;
                var ry = h / 2.0;
                for (var y = 0; y < h; y++)
                {
                    var dy = (y + 0.5 - ry) / ry;
                    var row = px + (long)y * w * 4;
                    for (var x = 0; x < w; x++)
                    {
                        var dx = (x + 0.5 - rx) / rx;
                        row[x * 4 + 3] = dx * dx + dy * dy <= 1 ? (byte)255 : (byte)0;
                    }
                }
                break;
            }
            case ScopeShape.RoundedSquare:
            case ScopeShape.Rectangle:
            {
                var radius = (scope.Shape == ScopeShape.Rectangle ? 10 : 18) * 2;
                var rx = w / 2.0;
                var ry = h / 2.0;
                for (var y = 0; y < h; y++)
                {
                    var row = px + (long)y * w * 4;
                    for (var x = 0; x < w; x++)
                    {
                        var inside = x >= radius || y >= radius || x < w - radius || y < h - radius
                            ? Math.Pow(Math.Max(0, Math.Abs(x + 0.5 - rx) - (rx - radius)) / radius, 2)
                              + Math.Pow(Math.Max(0, Math.Abs(y + 0.5 - ry) - (ry - radius)) / radius, 2) <= 1
                            : true;
                        row[x * 4 + 3] = inside ? (byte)255 : (byte)0;
                    }
                }
                break;
            }
            default:
            {
                for (var y = 0; y < h; y++)
                {
                    var row = px + (long)y * w * 4;
                    for (var x = 0; x < w; x++)
                        row[x * 4 + 3] = 255;
                }
                break;
            }
        }
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
        var strokeColor = profile.Accessibility.HighContrastReticle ? Color.FromArgb(borderAlpha, Color.White) : penColor;

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

        if (scope.Reticle)
        {
            using var pen = new Pen(strokeColor, (float)Math.Max(1.5, scope.BorderWidth * 0.6));
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            var cx = _winW / 2f;
            var cy = _winH / 2f;
            var gap = 8f;
            var len = Math.Min(_winW, _winH) * 0.45f;
            g.DrawLine(pen, cx, cy - gap - len, cx, cy - gap);
            g.DrawLine(pen, cx, cy + gap, cx, cy + gap + len);
            g.DrawLine(pen, cx - gap - len, cy, cx - gap, cy);
            g.DrawLine(pen, cx + gap, cy, cx + gap + len, cy);
            var dot = (float)Math.Max(2, scope.BorderWidth * 0.8);
            g.FillEllipse(new SolidBrush(strokeColor), cx - dot, cy - dot, dot * 2, dot * 2);
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
