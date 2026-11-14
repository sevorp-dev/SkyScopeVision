using System.Runtime.InteropServices;

namespace SkyScope.Native;

public enum MouseButtonKind
{
    None,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>
/// Глобальный хук мыши (WH_MOUSE_LL) для биндов на кнопки мыши —
/// RegisterHotKey мышь не умеет. Заглатывает только назначенные кнопки.
/// </summary>
public static class MouseButtonHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;

    private static IntPtr _hook;
    private static HookProc? _proc;
    private static Func<MouseButtonKind, uint, bool>? _handler;
    private static readonly HashSet<MouseButtonKind> _suppressed = new();

    public static void Install(Func<MouseButtonKind, uint, bool> handler)
    {
        if (_hook != IntPtr.Zero) return;
        _handler = handler;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            _proc = null;
            _handler = null;
        }
    }

    public static void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
        _handler = null;
        _suppressed.Clear();
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _handler is not null)
        {
            var msg = (int)wParam;
            uint xBtn = 0;
            if (msg is WM_XBUTTONDOWN or WM_XBUTTONUP)
                xBtn = (Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).mouseData >> 16) & 0xFFFF;
            var button = msg switch
            {
                WM_RBUTTONDOWN or WM_RBUTTONUP => MouseButtonKind.Right,
                WM_MBUTTONDOWN or WM_MBUTTONUP => MouseButtonKind.Middle,
                WM_XBUTTONDOWN or WM_XBUTTONUP => xBtn == 2 ? MouseButtonKind.X2 : MouseButtonKind.X1,
                _ => MouseButtonKind.None,
            };
            if (button != MouseButtonKind.None)
            {
                var isDown = msg is WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN;
                if (isDown)
                {
                    if (_handler(button, CurrentModifiers()))
                    {
                        _suppressed.Add(button);
                        return new IntPtr(1);
                    }
                }
                else if (_suppressed.Contains(button))
                {
                    _suppressed.Remove(button);
                    return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static uint CurrentModifiers()
    {
        uint mods = 0;
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= NativeMethods.MOD_CONTROL;
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= NativeMethods.MOD_ALT;
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= NativeMethods.MOD_SHIFT;
        if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0) mods |= NativeMethods.MOD_WIN;
        return mods;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public NativeMethods.POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
