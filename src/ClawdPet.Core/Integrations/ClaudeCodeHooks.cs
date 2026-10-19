using System.Text.Json.Nodes;

namespace ClawdPet.Core.Integrations;

public enum HookState
{
    /// <summary>No ClawdPet hooks in the settings.</summary>
    NotInstalled,
    /// <summary>Every hook is present and points at this exe.</summary>
    Installed,
    /// <summary>Some hooks are missing or point at another ClawdPet.exe (moved / older copy).</summary>
    Outdated,
}

/// <summary>
/// Adds / removes ClawdPet's hooks in a Claude Code settings.json object. Only hook entries whose
/// command mentions ClawdPet.exe are ever touched; everything else in the file is left as it was.
/// </summary>
public static class ClaudeCodeHooks
{
    /// <summary>
    /// Events forwarded to the pet. Each runs <c>ClawdPet.exe --quiet hook</c>, which hands the hook's
    /// stdin JSON (plus the window the session lives in) to the running pet; the pet decides how to
    /// react and tails the transcript for live progress.
    /// </summary>
    public static readonly IReadOnlyList<string> Events =
    [
        "SessionStart", "UserPromptSubmit", "Notification", "PreCompact", "Stop", "StopFailure", "SessionEnd",
    ];

    public const string PetCommand = "hook";

    const string Marker = "clawdpet.exe";

    /// <summary>
    /// The hook command for a pet command. <c>--quiet</c> matters: Claude Code feeds the stdout of
    /// SessionStart / UserPromptSubmit hooks into the conversation.
    /// </summary>
    public static string CommandLine(string exePath, string petCommand) =>
        $"\"{exePath.Replace('\\', '/')}\" --quiet {petCommand}";

    public static HookState Inspect(JsonObject settings, string exePath)
    {
        var ours = OurCommands(settings).ToList();
        if (ours.Count == 0) return HookState.NotInstalled;
        string expected = CommandLine(exePath, PetCommand);
        bool complete = Events.All(ev => ours.Any(o => o.Event == ev && o.Command == expected && o.Async));
        return complete && ours.Count == Events.Count ? HookState.Installed : HookState.Outdated;
    }

    /// <summary>Removes any previous ClawdPet hooks, then adds the current set.</summary>
    public static void Install(JsonObject settings, string exePath)
    {
        Uninstall(settings);
        if (settings["hooks"] is not JsonObject hooks)
            settings["hooks"] = hooks = new JsonObject();
        foreach (var ev in Events)
        {
            if (hooks[ev] is not JsonArray groups)
                hooks[ev] = groups = new JsonArray();
            groups.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = CommandLine(exePath, PetCommand),
                    // Runs in the background, so it never slows Claude down.
                    ["async"] = true,
                    ["timeout"] = 15,
                }),
            });
        }
    }

    /// <summary>Removes ClawdPet hook entries; drops groups / events / "hooks" that end up empty because of it.</summary>
    public static bool Uninstall(JsonObject settings)
    {
        if (settings["hooks"] is not JsonObject hooks) return false;
        bool changed = false;
        foreach (var ev in hooks.Select(p => p.Key).ToList())
        {
            if (hooks[ev] is not JsonArray groups) continue;
            bool touchedEvent = false;
            for (int g = groups.Count - 1; g >= 0; g--)
            {
                if (groups[g]?["hooks"] is not JsonArray inner) continue;
                bool touchedGroup = false;
                for (int h = inner.Count - 1; h >= 0; h--)
                    if (IsOurs(inner[h])) { inner.RemoveAt(h); touchedGroup = true; }
                if (inner.Count == 0 && touchedGroup) groups.RemoveAt(g);
                touchedEvent |= touchedGroup;
            }
            if (groups.Count == 0 && touchedEvent) hooks.Remove(ev);
            changed |= touchedEvent;
        }
        if (hooks.Count == 0 && changed) settings.Remove("hooks");
        return changed;
    }

    static IEnumerable<(string Event, string Command, bool Async)> OurCommands(JsonObject settings)
    {
        if (settings["hooks"] is not JsonObject hooks) yield break;
        foreach (var (ev, node) in hooks)
            if (node is JsonArray groups)
                foreach (var group in groups)
                    if (group?["hooks"] is JsonArray inner)
                        foreach (var h in inner)
                            if (IsOurs(h)) yield return (ev, h!["command"]!.GetValue<string>(), h["async"] is JsonValue a && a.TryGetValue<bool>(out bool b) && b);
    }

    static bool IsOurs(JsonNode? hook) =>
        hook?["command"] is JsonValue v && v.TryGetValue<string>(out var s)
        && s.Contains(Marker, StringComparison.OrdinalIgnoreCase);
}
