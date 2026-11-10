using System.Runtime.InteropServices;

namespace SkyScope.Native;

public static class MagnificationApi
{
    public const string MagnifierClassName = "Magnifier";

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern short RegisterClass(ref WNDCLASS wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateEllipticRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr handle);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MagInitialize();

    [DllImport("Magnification.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MagSetWindowSource(IntPtr hwnd, NativeMethods.RECT rect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM transform);

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    public struct MAGTRANSFORM
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public float[] v;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }
}
