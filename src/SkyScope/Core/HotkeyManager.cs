using System.ComponentModel;
using System.Windows.Interop;
using SkyScope.Native;

namespace SkyScope.Core;

public enum HotkeyAction
{
    Toggle,
    ZoomIn,
    ZoomOut,
    Reset,
}

public class HotkeyManager
{
    private readonly Dictionary<HotkeyAction, int> _registered = new();
    private readonly Dictionary<HotkeyAction, (MouseButtonKind Button, uint Mods)> _mouse = new();
    private Action<MouseButtonKind, uint>? _captureCallback;

    public IntPtr Hwnd { get; set; }

    public event Action<HotkeyAction>? Triggered;
    public event Action<HotkeyAction, string>? RegistrationFailed;

    public void RegisterAll(HotkeyMap map)
    {
        UnregisterAll();
        Register(HotkeyAction.Toggle, map.Toggle);
        Register(HotkeyAction.ZoomIn, map.ZoomIn);
        Register(HotkeyAction.ZoomOut, map.ZoomOut);
        Register(HotkeyAction.Reset, map.Reset);
        SyncMouseHook();
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Values)
            NativeMethods.UnregisterHotKey(Hwnd, id);
        _registered.Clear();
        _mouse.Clear();
        _captureCallback = null;
        MouseButtonHook.Uninstall();
    }

    private void Register(HotkeyAction action, string combo)
    {
        if (!HotkeyParser.TryParse(combo, out var mods, out var key, out var mouse)) return;
        if (mouse != MouseButtonKind.None)
        {
            _mouse[action] = (mouse, mods);
            return;
        }
        var id = (int)action + 1;
        if (NativeMethods.RegisterHotKey(Hwnd, id, mods, key))
        {
            _registered[action] = id;
        }
        else
        {
            RegistrationFailed?.Invoke(action, combo);
        }
    }

    // Пока открыт захват бинда, любая кнопка мыши уходит в callback и глотается.
    public void BeginMouseCapture(Action<MouseButtonKind, uint> callback)
    {
        _captureCallback = callback;
        SyncMouseHook();
    }

    public void EndMouseCapture()
    {
        _captureCallback = null;
        SyncMouseHook();
    }

    private void SyncMouseHook()
    {
        if (_mouse.Count > 0 || _captureCallback is not null)
            MouseButtonHook.Install(OnMouseButton);
        else
            MouseButtonHook.Uninstall();
    }

    private bool OnMouseButton(MouseButtonKind button, uint mods)
    {
        if (CapturePointPicker.IsPicking) return false;
        if (_captureCallback is { } cb)
        {
            cb(button, mods);
            return true;
        }
        foreach (var kv in _mouse)
        {
            if (kv.Value.Button == button && kv.Value.Mods == mods)
            {
                Triggered?.Invoke(kv.Key);
                return true;
            }
        }
        return false;
    }

    public void Attach(WindowInteropHelper helper)
    {
        if (helper.Handle == IntPtr.Zero) return;
        HwndSource.FromHwnd(helper.Handle)!.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _registered.ContainsValue((int)wParam))
        {
            var action = _registered.First(kv => kv.Value == (int)wParam).Key;
            Triggered?.Invoke(action);
            handled = true;
        }
        return IntPtr.Zero;
    }
}

public static class HotkeyParser
{
    public static bool TryParse(string combo, out uint modifiers, out uint key, out MouseButtonKind mouse)
    {
        modifiers = 0;
        key = 0;
        mouse = MouseButtonKind.None;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "ALT": modifiers |= NativeMethods.MOD_ALT; break;
                case "SHIFT": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "WIN": modifiers |= NativeMethods.MOD_WIN; break;
                default:
                    if (TryMouseToken(part, out mouse)) break;
                    key = KeyToVirtualKey(part);
                    if (key == 0) return false;
                    break;
            }
        }
        if (mouse != MouseButtonKind.None) return true;
        var hasModifier = (modifiers & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN)) != 0;
        return key != 0 && (hasModifier || key >= NativeMethods.VK_F1 && key <= NativeMethods.VK_F24);
    }

    public static bool TryMouseToken(string part, out MouseButtonKind button)
    {
        button = part.ToUpperInvariant() switch
        {
            "ПКМ" or "RBUTTON" or "RIGHT" => MouseButtonKind.Right,
            "СКМ" or "MBUTTON" or "MIDDLE" => MouseButtonKind.Middle,
            "БОКОВАЯ 1" or "XBUTTON1" or "MOUSE4" or "X1" => MouseButtonKind.X1,
            "БОКОВАЯ 2" or "XBUTTON2" or "MOUSE5" or "X2" => MouseButtonKind.X2,
            _ => MouseButtonKind.None,
        };
        return button != MouseButtonKind.None;
    }

    public static string MouseDisplay(MouseButtonKind button) => button switch
    {
        MouseButtonKind.Right => "ПКМ",
        MouseButtonKind.Middle => "СКМ",
        MouseButtonKind.X1 => "Боковая 1",
        MouseButtonKind.X2 => "Боковая 2",
        _ => "",
    };

    private static uint KeyToVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
            return key switch
            {
                " " => NativeMethods.VK_SPACE,
                "-" => 0xBD,
                "=" => 0xBB,
                "[" => 0xDB,
                "]" => 0xDD,
                ";" => 0xBA,
                "'" => 0xDE,
                "," => 0xBC,
                "." => 0xBE,
                "/" => 0xBF,
                "`" => 0xC0,
                _ => 0,
            };
        }
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24)
            return (uint)(NativeMethods.VK_F1 + f - 1);
        return key.ToUpperInvariant() switch
        {
            "SPACE" => NativeMethods.VK_SPACE,
            "TAB" => NativeMethods.VK_TAB,
            "CAPSLOCK" => NativeMethods.VK_CAPITAL,
            "ESCAPE" => NativeMethods.VK_ESCAPE,
            "INSERT" => NativeMethods.VK_INSERT,
            "DELETE" => NativeMethods.VK_DELETE,
            "HOME" => NativeMethods.VK_HOME,
            "END" => NativeMethods.VK_END,
            "PAGEUP" => NativeMethods.VK_PRIOR,
            "PAGEDOWN" => NativeMethods.VK_NEXT,
            "UP" => NativeMethods.VK_UP,
            "DOWN" => NativeMethods.VK_DOWN,
            "LEFT" => NativeMethods.VK_LEFT,
            "RIGHT" => NativeMethods.VK_RIGHT,
            "NUMPAD0" => 0x60, "NUMPAD1" => 0x61, "NUMPAD2" => 0x62, "NUMPAD3" => 0x63,
            "NUMPAD4" => 0x64, "NUMPAD5" => 0x65, "NUMPAD6" => 0x66, "NUMPAD7" => 0x67,
            "NUMPAD8" => 0x68, "NUMPAD9" => 0x69,
            _ => 0,
        };
    }

    public static string Format(uint mods, uint key)
    {
        var parts = new List<string>();
        if ((mods & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(VirtualKeyToDisplay(key));
        return string.Join("+", parts);
    }

    public static string Format(uint mods, MouseButtonKind mouse)
    {
        var parts = new List<string>();
        if ((mods & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(MouseDisplay(mouse));
        return string.Join("+", parts);
    }

    private static string VirtualKeyToDisplay(uint vk) => vk switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)vk).ToString(),
        NativeMethods.VK_SPACE => "Space",
        0xBD => "-", 0xBB => "=", 0xDB => "[", 0xDD => "]", 0xBA => ";",
        0xDE => "'", 0xBC => ",", 0xBE => ".", 0xBF => "/", 0xC0 => "`",
        >= NativeMethods.VK_F1 and <= NativeMethods.VK_F24 => $"F{vk - NativeMethods.VK_F1 + 1}",
        NativeMethods.VK_TAB => "Tab",
        NativeMethods.VK_CAPITAL => "CapsLock",
        NativeMethods.VK_ESCAPE => "Esc",
        NativeMethods.VK_INSERT => "Insert",
        NativeMethods.VK_DELETE => "Delete",
        NativeMethods.VK_HOME => "Home",
        NativeMethods.VK_END => "End",
        NativeMethods.VK_PRIOR => "PageUp",
        NativeMethods.VK_NEXT => "PageDown",
        NativeMethods.VK_UP => "Up",
        NativeMethods.VK_DOWN => "Down",
        NativeMethods.VK_LEFT => "Left",
        NativeMethods.VK_RIGHT => "Right",
        >= 0x60 and <= 0x69 => $"NumPad{vk - 0x60}",
        _ => $"0x{vk:X}",
    };
}
