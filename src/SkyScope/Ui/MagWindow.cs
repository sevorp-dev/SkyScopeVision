using System.Runtime.InteropServices;
using SkyScope.Core;
using SkyScope.Native;

namespace SkyScope.Ui;

/// <summary>
/// Окно вывода лупы. Честное Win32-окно с Magnification API: DWM рендерит
/// увеличенный центр экрана, форма задаётся регионом, окно click-through
/// и исключено из захвата. Работает в физических пикселях.
/// </summary>
public class MagWindow
{
    private const uint WM_MOVE = 0x0003;
    private const uint WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;

    private readonly IntPtr _hwnd;
    private int _rgnW = -1;
    private int _rgnH = -1;
    private ScopeShape _rgnShape;
    private bool _disposed;
    private bool _moveMode;
    private IntPtr _exStyleOriginal;

    public IntPtr Handle => _hwnd;
    public IntPtr MagnifierHwnd { get; private set; }
    public bool MoveMode
    {
        get => _moveMode;
        set
        {
            _moveMode = value;
            var ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
            var next = value
                ? new IntPtr(ex.ToInt64() & ~NativeMethods.WS_EX_TRANSPARENT)
                : _exStyleOriginal;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, next);
        }
    }

    /// Центр окна сместился (в физических px относительно центра монитора).
    public event Action<int, int>? Moved;

    private static MagWindow? s_current;
    private static WndProcDelegate? s_wndProc;

    public static bool InitializeApi() => MagnificationApi.MagInitialize();

    public MagWindow()
    {
        s_current = this;
        var hInstance = MagnificationApi.GetModuleHandle(null);
        s_wndProc = WndProc;

        var wc = new MagnificationApi.WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = hInstance,
            lpszClassName = "SkyScopeMagHost",
        };
        MagnificationApi.RegisterClass(ref wc);

        _hwnd = MagnificationApi.CreateWindowEx(
            MagnificationApi.WS_EX_LAYERED | MagnificationApi.WS_EX_TOOLWINDOW | MagnificationApi.WS_EX_TRANSPARENT,
            "SkyScopeMagHost", "",
            MagnificationApi.WS_POPUP | MagnificationApi.WS_VISIBLE,
            0, 0, 100, 100, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"MagWindow host failed: {Marshal.GetLastWin32Error()}");

        _exStyleOriginal = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);

        MagnifierHwnd = MagnificationApi.CreateWindowEx(
            0, MagnificationApi.MagnifierClassName, "",
            MagnificationApi.WS_CHILD | MagnificationApi.WS_VISIBLE,
            0, 0, 100, 100, _hwnd, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (MagnifierHwnd == IntPtr.Zero)
            throw new InvalidOperationException($"Magnifier failed: {Marshal.GetLastWin32Error()}");

        NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        NativeMethods.SetWindowDisplayAffinity(MagnifierHwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
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
                if (NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromPoint(self._windowCenter, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info))
                {
                    var cx = (NativeMethods.LOWORD(lParam) + self._windowW / 2) - (info.rcMonitor.Left + info.rcMonitor.Width / 2);
                    var cy = (NativeMethods.HIWORD(lParam) + self._windowH / 2) - (info.rcMonitor.Top + info.rcMonitor.Height / 2);
                    self.Moved?.Invoke(cx, cy);
                }
            }
        }
        return MagWindowInterop.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private NativeMethods.POINT _windowCenter;
    private int _windowW = 100;
    private int _windowH = 100;

    /// <summary>
    /// Обновляет окно. cx/cy — центр монитора (физические px), scale — DPI.
    /// Зона захвата — всегда центр монитора; положение окна — по якорю профиля.
    /// </summary>
    public void Update(int cx, int cy, double scale, Profile profile)
    {
        if (_disposed || MagnifierHwnd == IntPtr.Zero) return;

        var scope = profile.Scope;
        var zoom = Math.Clamp(profile.Zoom.Level, profile.Zoom.Min, profile.Zoom.Max);
        var (w, h) = WindowSize(scope, scale);
        _windowW = w;
        _windowH = h;
        _windowCenter = new NativeMethods.POINT { X = cx, Y = cy };

        var (x, y) = PositionFor(scope, cx, cy, w, h, scale);
        MagnificationApi.SetWindowPos(_hwnd, new IntPtr(-1), x, y, w, h, MagnificationApi.SWP_NOACTIVATE);

        if (_rgnW != w || _rgnH != h || _rgnShape != scope.Shape)
        {
            _rgnW = w;
            _rgnH = h;
            _rgnShape = scope.Shape;
            var rgn = scope.Shape switch
            {
                ScopeShape.Circle => MagnificationApi.CreateEllipticRgn(0, 0, w + 1, h + 1),
                ScopeShape.Rectangle => MagnificationApi.CreateRoundRectRgn(0, 0, w + 1, h + 1, (int)(10 * scale), (int)(10 * scale)),
                ScopeShape.RoundedSquare => MagnificationApi.CreateRoundRectRgn(0, 0, w + 1, h + 1, (int)(18 * scale), (int)(18 * scale)),
                _ => MagnificationApi.CreateRoundRectRgn(0, 0, w + 1, h + 1, 0, 0),
            };
            MagnificationApi.SetWindowRgn(_hwnd, rgn, true);
        }

        var srcW = Math.Max(8, (int)Math.Round(w / zoom));
        var srcH = Math.Max(8, (int)Math.Round(h / zoom));
        var src = new NativeMethods.RECT
        {
            Left = cx - srcW / 2,
            Top = cy - srcH / 2,
            Right = cx + srcW / 2,
            Bottom = cy + srcH / 2,
        };
        MagnificationApi.MagSetWindowSource(MagnifierHwnd, src);

        var transform = new MagnificationApi.MAGTRANSFORM
        {
            v = [(float)zoom, 0, 0, 0, (float)zoom, 0, 0, 0, 1],
        };
        MagnificationApi.MagSetWindowTransform(MagnifierHwnd, ref transform);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (s_current == this) s_current = null;
        if (MagnifierHwnd != IntPtr.Zero)
        {
            MagnificationApi.DestroyWindow(MagnifierHwnd);
            MagnifierHwnd = IntPtr.Zero;
        }
        if (_hwnd != IntPtr.Zero)
            MagnificationApi.DestroyWindow(_hwnd);
    }
}

[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

internal static class MagWindowInterop
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
