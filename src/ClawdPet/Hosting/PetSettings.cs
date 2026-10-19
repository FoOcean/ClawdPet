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
    /// <summary>Minutes without interaction before naps get likely; 0 = never nap on its own.</summary>
    public double SleepAfterMinutes { get; set; } = 3;
    /// <summary>1 (calm) … 5 (restless).</summary>
    public int Liveliness { get; set; } = 3;
    /// <summary>Behaviors the pet should not pick on its own.</summary>
    public List<string> DisabledBehaviors { get; set; } = [];
    public bool HideInFullscreen { get; set; } = true;
    /// <summary>Show/hide hotkey such as "Ctrl+Alt+C"; empty = none.</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+C";
    public ClaudeOptions Claude { get; set; } = new();
    /// <summary>Last anchor position in physical pixels.</summary>
    public double? X { get; set; }
    public double? Y { get; set; }

    public double Calmness => Liveliness switch { 1 => 3.0, 2 => 1.8, 4 => 0.6, 5 => 0.35, _ => 1.0 };

    /// <summary>Claude Code live progress and reactions.</summary>
    public sealed class ClaudeOptions
    {
        /// <summary>When the progress bubble shows: minimized | background | always | never.</summary>
        public string BubbleWhen { get; set; } = "minimized";
        public bool HoverPeek { get; set; } = true;
        public bool ShowPlan { get; set; } = true;
        public bool ShowText { get; set; } = true;
        public bool ShowStats { get; set; } = true;
        public bool ShowOtherSessions { get; set; } = true;
        public bool ReactTyping { get; set; } = true;
        public bool ReactDone { get; set; } = true;
        public bool ReactAttention { get; set; } = true;
        public bool CompletionSound { get; set; }
        /// <summary>Also follow sessions found in ~/.claude/projects (works without hooks).</summary>
        public bool DiscoverSessions { get; set; } = true;
    }

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
