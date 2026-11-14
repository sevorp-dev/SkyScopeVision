using System.Runtime.InteropServices;

namespace SkyScope.Native;

/// <summary>
/// Однократный выбор точки на экране: ловит первый клик мышью (клик глотается,
/// чтобы не ушёл в игру), Esc — отмена. Хуки ставятся на поток с message loop.
/// </summary>
public static class CapturePointPicker
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_LBUTTONDOWN = 0x0201;

    private static IntPtr _mouseHook;
    private static IntPtr _keyboardHook;
    private static HookProc? _mouseProc;
    private static HookProc? _keyboardProc;
    private static Action<int, int>? _onPick;
    private static Action? _onCancel;
    private static bool _busy;

    public static bool IsPicking => _busy;

    public static bool Start(Action<int, int> onPick, Action onCancel)
    {
        if (_busy) return false;
        _onPick = onPick;
        _onCancel = onCancel;
        _mouseProc = MouseHook;
        _keyboardProc = KeyboardHook;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            Stop();
            return false;
        }
        _busy = true;
        return true;
    }

    private static IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_LBUTTONDOWN && _busy)
        {
            var pt = Marshal.PtrToStructure<NativeMethods.POINT>(lParam);
            var cb = _onPick;
            Stop();
            cb?.Invoke(pt.X, pt.Y);
            return new IntPtr(1);
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_KEYDOWN && _busy)
        {
            var vk = Marshal.ReadInt32(lParam);
            if (vk == NativeMethods.VK_ESCAPE)
            {
                var cb = _onCancel;
                Stop();
                cb?.Invoke();
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static void Stop()
    {
        _busy = false;
        _onPick = null;
        _onCancel = null;
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        _mouseProc = null;
        _keyboardProc = null;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
