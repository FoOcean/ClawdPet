using System.IO;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ClawdPet;

/// <summary>Notification-area icon: right-click opens the pet menu, double-click summons the pet.</summary>
sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;

    public TrayIcon(string iconPath, Action showMenu, Action summon)
    {
        _icon = new Forms.NotifyIcon
        {
            Icon = File.Exists(iconPath)
                ? new Drawing.Icon(iconPath, Forms.SystemInformation.SmallIconSize)
                : Drawing.SystemIcons.Application,
            Text = "Clawd",
            Visible = true,
        };
        _icon.MouseUp += (_, e) => { if (e.Button == Forms.MouseButtons.Right) showMenu(); };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) summon(); };
    }

    public void SetStatus(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
