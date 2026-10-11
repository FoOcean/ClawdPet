using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClawdPet.Hosting;

/// <summary>
/// Where things live. Data goes to %APPDATA%\ClawdPet, or to CLAWDPET_HOME when set — a test
/// instance pointed elsewhere never touches the real settings, autostart entry or running pet.
/// </summary>
sealed class AppPaths
{
    public const string HomeVariable = "CLAWDPET_HOME";

    AppPaths(string home, bool sandboxed)
    {
        Home = home;
        Sandboxed = sandboxed;
        Directory.CreateDirectory(home);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(home).ToLowerInvariant()));
        InstanceKey = Convert.ToHexString(hash, 0, 6);
    }

    public string Home { get; }
    public bool Sandboxed { get; }
    /// <summary>Distinguishes instances by data folder (mutex + pipe names).</summary>
    public string InstanceKey { get; }
    public string AppDir => AppContext.BaseDirectory;
    public string SettingsFile => Path.Combine(Home, "settings.json");
    public string LogFile => Path.Combine(Home, "clawdpet.log");
    /// <summary>Drop-in skins: skins\&lt;name&gt;\&lt;name&gt;.json + its PNG. Checked before the built-in ones.</summary>
    public string UserSkinsDir => Path.Combine(Home, "skins");
    public string BuiltinSkinsDir => Path.Combine(AppDir, "Assets", "Skins");
    public string IconFile => Path.Combine(AppDir, "Assets", "clawd.ico");

    public static AppPaths Resolve()
    {
        string? custom = Environment.GetEnvironmentVariable(HomeVariable);
        return string.IsNullOrWhiteSpace(custom)
            ? new AppPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClawdPet"), false)
            : new AppPaths(Path.GetFullPath(custom), true);
    }

    public IEnumerable<string> SkinNames() =>
        new[] { UserSkinsDir, BuiltinSkinsDir }
            .Where(Directory.Exists)
            .SelectMany(Directory.GetDirectories)
            .Where(d => Directory.GetFiles(d, "*.json").Length > 0)
            .Select(d => Path.GetFileName(d)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>The atlas JSON of a skin: &lt;dir&gt;\&lt;name&gt;.json, else the first JSON in the folder.</summary>
    public string? FindSkin(string name)
    {
        foreach (var root in new[] { UserSkinsDir, BuiltinSkinsDir })
        {
            var dir = Path.Combine(root, name);
            if (!Directory.Exists(dir)) continue;
            var named = Path.Combine(dir, name + ".json");
            if (File.Exists(named)) return named;
            var any = Directory.GetFiles(dir, "*.json").Order().FirstOrDefault();
            if (any != null) return any;
        }
        return null;
    }
}
