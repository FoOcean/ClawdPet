using System.Windows.Input;
using System.Windows.Interop;
using ClawdPet.Native;

namespace ClawdPet.Hosting;

/// <summary>One global hotkey ("Ctrl+Alt+C" style) bound to a window.</summary>
sealed class Hotkey : IDisposable
{
    const int Id = 0xC1AD;
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    readonly IntPtr _hwnd;
    readonly HwndSource _source;
    readonly Action _pressed;
    bool _registered;

    public Hotkey(IntPtr hwnd, Action pressed)
    {
        _hwnd = hwnd;
        _pressed = pressed;
        _source = HwndSource.FromHwnd(hwnd);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers <paramref name="spec"/>; returns false when it is invalid or taken by another app.</summary>
    public bool Set(string? spec)
    {
        if (_registered) { Win32.UnregisterHotKey(_hwnd, Id); _registered = false; }
        if (string.IsNullOrWhiteSpace(spec) || !TryParse(spec, out uint mods, out uint vk)) return string.IsNullOrWhiteSpace(spec);
        _registered = Win32.RegisterHotKey(_hwnd, Id, mods | MOD_NOREPEAT, vk);
        return _registered;
    }

    public static bool TryParse(string spec, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        foreach (var raw in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": mods |= MOD_WIN; break;
                default:
                    if (!Enum.TryParse<Key>(raw, true, out var key)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0 && mods != 0;
    }

    /// <summary>Formats a key press as a spec, or null while only modifiers are down.</summary>
    public static string? Describe(ModifierKeys mods, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None) return null;
        if (mods == ModifierKeys.None) return null;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            _pressed();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered) Win32.UnregisterHotKey(_hwnd, Id);
        _source.RemoveHook(WndProc);
    }
}
