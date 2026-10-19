using System.Text.Json.Nodes;
using ClawdPet.Core.Integrations;

namespace ClawdPet.Core.Tests;

public class ClaudeCodeHooksTests
{
    const string Exe = @"C:\Apps\ClawdPet\ClawdPet.exe";

    static JsonObject Existing() => JsonNode.Parse("""
        {
          "model": "opus",
          "permissions": { "allow": ["Bash(git status)"] },
          "hooks": {
            "Stop": [ { "hooks": [ { "type": "command", "command": "notify-send done" } ] } ],
            "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "audit.sh" } ] } ]
          }
        }
        """)!.AsObject();

    [Fact]
    public void Install_adds_every_hook_and_keeps_the_users_own()
    {
        var s = Existing();
        ClaudeCodeHooks.Install(s, Exe);

        Assert.Equal(HookState.Installed, ClaudeCodeHooks.Inspect(s, Exe));
        Assert.Equal("opus", s["model"]!.GetValue<string>());
        Assert.Equal("audit.sh", s["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
        var stop = s["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("notify-send done", stop[0]!["hooks"]![0]!["command"]!.GetValue<string>());
        var ours = stop[1]!["hooks"]![0]!;
        Assert.Equal("\"C:/Apps/ClawdPet/ClawdPet.exe\" --quiet hook", ours["command"]!.GetValue<string>());
        Assert.True(ours["async"]!.GetValue<bool>());
        Assert.All(ClaudeCodeHooks.Events, ev => Assert.NotNull(s["hooks"]![ev]));
    }

    [Fact]
    public void Hooks_from_the_previous_version_count_as_outdated()
    {
        var s = JsonNode.Parse("""
            { "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "\"C:/Apps/ClawdPet/ClawdPet.exe\" --quiet done", "timeout": 10 } ] } ] } }
            """)!.AsObject();
        Assert.Equal(HookState.Outdated, ClaudeCodeHooks.Inspect(s, Exe));
        ClaudeCodeHooks.Install(s, Exe);
        Assert.Equal(HookState.Installed, ClaudeCodeHooks.Inspect(s, Exe));
        Assert.DoesNotContain("--quiet done", s.ToJsonString());
    }

    [Fact]
    public void Installing_twice_does_not_duplicate()
    {
        var s = Existing();
        ClaudeCodeHooks.Install(s, Exe);
        string once = s.ToJsonString();
        ClaudeCodeHooks.Install(s, Exe);
        Assert.Equal(once, s.ToJsonString());
    }

    [Fact]
    public void A_moved_exe_shows_as_outdated_and_reinstall_replaces_the_old_path()
    {
        var s = new JsonObject();
        ClaudeCodeHooks.Install(s, @"D:\old\ClawdPet.exe");
        Assert.Equal(HookState.Outdated, ClaudeCodeHooks.Inspect(s, Exe));
        ClaudeCodeHooks.Install(s, Exe);
        Assert.Equal(HookState.Installed, ClaudeCodeHooks.Inspect(s, Exe));
        Assert.DoesNotContain("D:/old", s.ToJsonString());
    }

    [Fact]
    public void Uninstall_restores_the_original_settings()
    {
        var s = Existing();
        string before = s.ToJsonString();
        ClaudeCodeHooks.Install(s, Exe);
        Assert.True(ClaudeCodeHooks.Uninstall(s));
        Assert.Equal(before, s.ToJsonString());
        Assert.Equal(HookState.NotInstalled, ClaudeCodeHooks.Inspect(s, Exe));
        Assert.False(ClaudeCodeHooks.Uninstall(s));
    }

    [Fact]
    public void Uninstall_from_empty_settings_leaves_no_hooks_key()
    {
        var s = new JsonObject();
        ClaudeCodeHooks.Install(s, Exe);
        ClaudeCodeHooks.Uninstall(s);
        Assert.Equal("{}", s.ToJsonString());
    }
}
