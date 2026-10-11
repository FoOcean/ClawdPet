using System.Diagnostics;
using ClawdPet.Core.World;
using ClawdPet.Native;

namespace ClawdPet.Hosting;

/// <summary>
/// The desktop as the pet sees it: one surface per monitor, floored at the bottom of the work area
/// (i.e. standing on the taskbar). Physical pixels throughout.
/// </summary>
sealed class DesktopWorld : IPetWorld
{
    readonly record struct Monitor(Win32.RECT Bounds, Win32.RECT Work);

    List<Monitor> _monitors = [];
    readonly Stopwatch _sinceRefresh = new();

    public DesktopWorld()
    {
        Refresh();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var list = new List<Monitor>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
            if (Win32.GetMonitorInfo(h, ref info)) list.Add(new Monitor(info.rcMonitor, info.rcWork));
            return true;
        }, IntPtr.Zero);
        if (list.Count > 0) _monitors = list;
        _sinceRefresh.Restart();
    }

    public Surface SurfaceAt(double x, double y)
    {
        // Work areas change without a display event (taskbar auto-hide, docking), so re-poll now and then.
        if (_sinceRefresh.ElapsedMilliseconds > 2000) Refresh();
        if (_monitors.Count == 0) return new Surface(0, 0, 1920, 1040);

        // A pet standing on a floor sits exactly on the work-area bottom, so test a point just above it.
        double probeY = y - 1;
        foreach (var m in _monitors)
            if (x >= m.Bounds.Left && x < m.Bounds.Right && probeY >= m.Bounds.Top && probeY < m.Bounds.Bottom)
                return ToSurface(m.Work);

        var nearest = _monitors.MinBy(m => Distance(m.Bounds, x, probeY));
        return ToSurface(nearest.Work);
    }

    public bool IsOnScreen(double x, double y) =>
        _monitors.Any(m => x >= m.Bounds.Left && x < m.Bounds.Right && y >= m.Bounds.Top && y < m.Bounds.Bottom);

    public Surface SurfaceUnderCursor()
    {
        Win32.GetCursorPos(out var p);
        return SurfaceAt(p.X, p.Y + 1);
    }

    static Surface ToSurface(Win32.RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    static double Distance(Win32.RECT r, double x, double y)
    {
        double dx = Math.Max(Math.Max(r.Left - x, 0), x - r.Right);
        double dy = Math.Max(Math.Max(r.Top - y, 0), y - r.Bottom);
        return dx * dx + dy * dy;
    }
}
