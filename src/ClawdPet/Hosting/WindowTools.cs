using System.Diagnostics;
using System.Text;
using ClawdPet.Native;

namespace ClawdPet.Hosting;

/// <summary>Finding and inspecting other apps' windows (Claude's, fullscreen ones).</summary>
static class WindowTools
{
    /// <summary>
    /// Walks up the parent-process chain from <paramref name="pid"/> and returns the first ancestor
    /// that owns a real top-level window: the Claude desktop app, Windows Terminal, VS Code…
    /// </summary>
    public static (int Pid, IntPtr Hwnd, string Process)? FindHostWindow(int pid)
    {
        var parents = new Dictionary<int, (int Parent, string Exe)>();
        IntPtr snap = Win32.CreateToolhelp32Snapshot(Win32.TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return null;
        try
        {
            var e = new Win32.PROCESSENTRY32 { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.PROCESSENTRY32>() };
            for (bool ok = Win32.Process32First(snap, ref e); ok; ok = Win32.Process32Next(snap, ref e))
                parents[(int)e.th32ProcessID] = ((int)e.th32ParentProcessID, e.szExeFile);
        }
        finally { Win32.CloseHandle(snap); }

        var windows = TopLevelWindowsByProcess();
        var seen = new HashSet<int>();
        for (int cur = pid, depth = 0; depth < 24 && parents.TryGetValue(cur, out var info) && seen.Add(cur); depth++)
        {
            int parent = info.Parent;
            if (parent == 0 || !parents.TryGetValue(parent, out var pinfo)) break;
            if (windows.TryGetValue(parent, out var hwnd))
                return (parent, hwnd, System.IO.Path.GetFileNameWithoutExtension(pinfo.Exe));
            cur = parent;
        }
        return null;
    }

    /// <summary>The first visible, unowned, titled, non-tool top-level window of each process.</summary>
    static Dictionary<int, IntPtr> TopLevelWindowsByProcess()
    {
        var map = new Dictionary<int, IntPtr>();
        Win32.EnumWindows((h, _) =>
        {
            if (IsAppWindow(h))
            {
                Win32.GetWindowThreadProcessId(h, out uint p);
                map.TryAdd((int)p, h);
            }
            return true;
        }, IntPtr.Zero);
        return map;
    }

    static bool IsAppWindow(IntPtr h)
    {
        if (!Win32.IsWindowVisible(h) || Win32.GetWindow(h, Win32.GW_OWNER) != IntPtr.Zero) return false;
        if (Win32.GetWindowTextLength(h) == 0) return false;
        long ex = (long)Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE);
        return (ex & Win32.WS_EX_TOOLWINDOW) == 0;
    }

    static (DateTime At, IntPtr Hwnd) _desktopCache;

    /// <summary>Main window of the Claude desktop app (for sessions we only know from transcripts).</summary>
    public static IntPtr ClaudeDesktopWindow()
    {
        if (DateTime.UtcNow - _desktopCache.At < TimeSpan.FromSeconds(5) && Win32.IsWindow(_desktopCache.Hwnd))
            return _desktopCache.Hwnd;
        IntPtr found = IntPtr.Zero;
        foreach (var p in Process.GetProcessesByName("claude"))
        {
            using (p)
                if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0) found = p.MainWindowHandle;
            if (found != IntPtr.Zero) break;
        }
        _desktopCache = (DateTime.UtcNow, found);
        return found;
    }

    public static bool IsMinimized(IntPtr hwnd) => Win32.IsIconic(hwnd);

    /// <summary>Active and not minimized (a minimized window can still be reported as the foreground one).</summary>
    public static bool IsForeground(IntPtr hwnd)
    {
        if (Win32.IsIconic(hwnd)) return false;
        var fg = Win32.GetForegroundWindow();
        return fg == hwnd || Win32.GetAncestor(fg, 3 /* GA_ROOTOWNER */) == hwnd;
    }

    /// <summary>Restores (if minimized) and activates another app's window.</summary>
    public static void BringToFront(IntPtr hwnd)
    {
        if (!Win32.IsWindow(hwnd)) return;
        if (Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        Win32.SetForegroundWindow(hwnd);
    }

    static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>
    /// The monitor handle a fullscreen app currently covers (game, video, presentation), or zero.
    /// The desktop, the taskbar and our own windows don't count.
    /// </summary>
    public static IntPtr FullscreenMonitor()
    {
        var fg = Win32.GetForegroundWindow();
        if (fg == IntPtr.Zero) return IntPtr.Zero;
        Win32.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == Environment.ProcessId) return IntPtr.Zero;
        var cls = new StringBuilder(64);
        Win32.GetClassName(fg, cls, cls.Capacity);
        if (ShellClasses.Contains(cls.ToString())) return IntPtr.Zero;
        // A normal maximized window still has a caption; with an auto-hiding taskbar it covers the
        // whole monitor too, so only captionless windows (games, video, F11) count as fullscreen.
        const long WS_CAPTION = 0x00C00000;
        if (((long)Win32.GetWindowLongPtr(fg, Win32.GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return IntPtr.Zero;
        if (!Win32.GetWindowRect(fg, out var r)) return IntPtr.Zero;
        var mon = Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(mon, ref info)) return IntPtr.Zero;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom ? mon : IntPtr.Zero;
    }

    public static IntPtr MonitorOf(IntPtr hwnd) => Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
}
