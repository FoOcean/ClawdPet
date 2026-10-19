using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdPet.Core.Integrations;

namespace ClawdPet.Hosting;

/// <summary>
/// One-click hook-up with Claude Code: edits its user settings.json (backing it up first) so that
/// sessions, prompts, replies and notifications drive the pet. See <see cref="ClaudeCodeHooks"/>.
/// </summary>
sealed class ClaudeCodeIntegration(AppPaths paths)
{
    public const string SettingsVariable = "CLAWDPET_CLAUDE_SETTINGS";

    public string Name => "Claude Code";

    /// <summary>
    /// CLAWDPET_CLAUDE_SETTINGS, else (sandboxed instance) a file inside its own home so tests never
    /// touch the real config, else $CLAUDE_CONFIG_DIR or ~/.claude.
    /// </summary>
    public string SettingsPath
    {
        get
        {
            var explicitPath = Environment.GetEnvironmentVariable(SettingsVariable);
            if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);
            if (paths.Sandboxed) return Path.Combine(paths.Home, "claude", "settings.json");
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            return Path.Combine(dir, "settings.json");
        }
    }

    static string ExePath => Environment.ProcessPath!;

    public HookState State()
    {
        try { return ClaudeCodeHooks.Inspect(Read(), ExePath); }
        catch { return HookState.NotInstalled; }
    }

    /// <returns>A short description of what happened (for a message box or the CLI).</returns>
    public string Install()
    {
        var settings = Read();
        ClaudeCodeHooks.Install(settings, ExePath);
        string? backup = Write(settings);
        return $"已接入 Claude Code（{ClaudeCodeHooks.Events.Count} 个 hooks）：{SettingsPath}"
            + (backup != null ? $"\n原文件已备份为 {Path.GetFileName(backup)}" : "")
            + "\n重新打开 Claude Code 会话后生效。";
    }

    public string Uninstall()
    {
        var settings = Read();
        if (!ClaudeCodeHooks.Uninstall(settings)) return "Claude Code 里没有 ClawdPet 的 hooks。";
        string? backup = Write(settings);
        return $"已从 Claude Code 移除 ClawdPet 的 hooks：{SettingsPath}"
            + (backup != null ? $"\n原文件已备份为 {Path.GetFileName(backup)}" : "");
    }

    JsonObject Read()
    {
        var path = SettingsPath;
        if (!File.Exists(path)) return new JsonObject();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            // Rewriting would silently drop comments, so leave such files for the user to edit.
            JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            throw new InvalidDataException($"{path} 里有注释，为避免丢失注释没有自动修改，请按 README 手动添加 hooks。");
        }
        return node as JsonObject ?? throw new InvalidDataException($"{path} 不是 JSON 对象。");
    }

    /// <returns>The backup path, or null when there was no previous file.</returns>
    string? Write(JsonObject settings)
    {
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string? backup = null;
        if (File.Exists(path))
        {
            backup = $"{path}.clawdpet-backup-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backup, overwrite: true);
        }
        var json = settings.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        var tmp = path + ".clawdpet-tmp";
        File.WriteAllText(tmp, json + Environment.NewLine, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
        Log.Write($"claude code settings updated: {path} (backup {backup ?? "none"})");
        return backup;
    }
}
