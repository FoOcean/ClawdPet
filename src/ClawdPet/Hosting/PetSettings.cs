using System.IO;
using System.Text;
using System.Text.Json;

namespace ClawdPet.Hosting;

sealed class PetSettings
{
    /// <summary>Screen pixels per sprite pixel at 100% Windows scaling (multiplied by the monitor's DPI).</summary>
    public int Scale { get; set; } = 3;
    public string Skin { get; set; } = "clawd";
    public bool Topmost { get; set; } = true;
    public bool Autonomous { get; set; } = true;
    public double SleepAfterMinutes { get; set; } = 3;
    /// <summary>Last anchor position in physical pixels.</summary>
    public double? X { get; set; }
    public double? Y { get; set; }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static PetSettings Load(string path)
    {
        if (!File.Exists(path)) return new PetSettings();
        try
        {
            return JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(path), Json) ?? new PetSettings();
        }
        catch (Exception ex)
        {
            // Keep the unreadable file instead of silently overwriting it with defaults.
            var keep = Path.ChangeExtension(path, $".invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Copy(path, keep, overwrite: true); } catch { }
            Log.Write($"settings unreadable, kept as {keep}: {ex.Message}");
            return new PetSettings();
        }
    }

    public void Save(string path)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write($"settings save failed: {ex.Message}");
        }
    }
}
