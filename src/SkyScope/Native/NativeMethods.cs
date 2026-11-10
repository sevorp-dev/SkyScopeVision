using System.Runtime.InteropServices;

namespace SkyScope.Native;

public static class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    public const uint VK_SPACE = 0x20;
    public const uint VK_TAB = 0x09;
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_CAPITAL = 0x14;
    public const uint VK_INSERT = 0x2D;
    public const uint VK_DELETE = 0x2E;
    public const uint VK_HOME = 0x24;
    public const uint VK_END = 0x23;
    public const uint VK_PRIOR = 0x21;
    public const uint VK_NEXT = 0x22;
    public const uint VK_UP = 0x26;
    public const uint VK_DOWN = 0x28;
    public const uint VK_LEFT = 0x25;
    public const uint VK_RIGHT = 0x27;
    public const uint VK_F1 = 0x70;
    public const uint VK_F24 = 0x87;

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

    public delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern int GetDpiForSystem();

    [DllImport("user32.dll")]
    public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateMutex(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);

    [DllImport("kernel32.dll")]
    public static extern uint GetLastError();

    public static int LOWORD(IntPtr l) => (short)(l.ToInt64() & 0xFFFF);
    public static int HIWORD(IntPtr l) => (short)((l.ToInt64() >> 16) & 0xFFFF);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        public static MONITORINFO Create() => new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
    }

    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("Shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static IEnumerable<(IntPtr Handle, RECT Bounds)> AllMonitors()
    {
        var result = new List<(IntPtr, RECT)>();
        var handle = GCHandle.Alloc(result);
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumCallback, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return result;
    }

    private static bool MonitorEnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
    {
        var handle = GCHandle.FromIntPtr(dwData);
        if (handle.Target is List<(IntPtr, RECT)> list)
            list.Add((hMonitor, lprcMonitor));
        return true;
    }
}
