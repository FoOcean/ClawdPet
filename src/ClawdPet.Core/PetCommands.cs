using System.Globalization;
using ClawdPet.Core.Behaviors;

namespace ClawdPet.Core;

/// <summary>
/// Text commands, e.g. from <c>ClawdPet.exe work</c> or a Claude Code hook:
/// <list type="bullet">
/// <item><c>&lt;behavior&gt; [seconds]</c> — any registered behavior or alias: work, think, sleep, walk, wave…</item>
/// <item><c>walk left|right</c></item>
/// <item><c>play &lt;tag&gt; [seconds]</c> — any animation tag in the sheet (looped when seconds is given)</item>
/// <item><c>done</c> — finish the current activity gracefully, then hop happily</item>
/// <item><c>stop</c> / <c>wake</c> — finish the current activity gracefully</item>
/// <item><c>attention</c> — "!" then a wave</item>
/// </list>
/// Returns "ok" or "error: …".
/// </summary>
public static class PetCommands
{
    public static string Execute(PetEngine engine, string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "error: empty command";

        string verb = parts[0].ToLowerInvariant();
        double? seconds = parts.Length > 1 && double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0
            ? s : null;
        var args = new BehaviorArgs { Source = BehaviorSource.Command, DurationMs = seconds * 1000 };
        var brain = engine.Brain;

        switch (verb)
        {
            case "done" or "finish":
                brain.FinishCurrent(then: "happy");
                return "ok";

            case "stop" or "wake":
                brain.FinishCurrent();
                return "ok";

            case "attention" or "notify":
                return brain.Request("surprised", args with { Then = "wave" }) ? "ok" : "error: no 'surprised' behavior";

            case "walk" when parts.Length > 1 && parts[1] is "left" or "right":
                brain.Request("walk", args with { Direction = parts[1] == "left" ? -1 : 1, DurationMs = null });
                return "ok";

            case "play":
                if (parts.Length < 2) return "error: usage: play <tag> [seconds]";
                if (!engine.Atlas.HasClip(parts[1])) return $"error: no animation '{parts[1]}'";
                brain.Request("play", args with { Clip = parts[1] });
                return "ok";
        }

        if (brain.Request(verb, args)) return "ok";
        if (engine.Atlas.HasClip(verb))
        {
            brain.Request("play", args with { Clip = verb });
            return "ok";
        }
        return $"error: unknown command '{verb}'";
    }
}
