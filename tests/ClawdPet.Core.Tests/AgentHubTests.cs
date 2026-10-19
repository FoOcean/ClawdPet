using System.Text;
using System.Text.Json;
using ClawdPet.Core.Agents;

namespace ClawdPet.Core.Tests;

public sealed class AgentHubTests : IDisposable
{
    readonly string _projects = Path.Combine(Path.GetTempPath(), "clawdpet-tests-" + Guid.NewGuid().ToString("N"));
    readonly string _transcript;
    readonly AgentHub _hub;
    readonly List<(string Session, AgentSignal Signal)> _signals = [];
    DateTime _now = new(2026, 10, 19, 6, 0, 0, DateTimeKind.Utc);

    public AgentHubTests()
    {
        Directory.CreateDirectory(Path.Combine(_projects, "C--work-demo"));
        _transcript = Path.Combine(_projects, "C--work-demo", "sess-1.jsonl");
        _hub = new AgentHub { ProjectsDir = _projects, Clock = () => _now };
        _hub.Signal += (s, sig) => _signals.Add((s.Id, sig));
    }

    public void Dispose()
    {
        try { Directory.Delete(_projects, true); } catch { }
    }

    static string Ts(int sec) => new DateTime(2026, 10, 19, 6, 0, sec, DateTimeKind.Utc).ToString("O");

    static string User(string text, string promptId, int sec) =>
        JsonSerializer.Serialize(new { type = "user", promptId, cwd = @"C:\work\demo", entrypoint = "cli", timestamp = Ts(sec), message = new { role = "user", content = text } });

    static string ToolUse(string id, string name, object input, int sec, string msgId = "m1") =>
        JsonSerializer.Serialize(new
        {
            type = "assistant", timestamp = Ts(sec),
            message = new { id = msgId, model = "claude-opus-5-5", role = "assistant", stop_reason = "tool_use",
                usage = new { input_tokens = 10, cache_read_input_tokens = 40000, cache_creation_input_tokens = 2000, output_tokens = 120 },
                content = new object[] { new { type = "tool_use", id, name, input } } },
        });

    static string ToolResult(string id, int sec) =>
        JsonSerializer.Serialize(new { type = "user", timestamp = Ts(sec), message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = id, content = "ok" } } } });

    static string Reply(string text, int sec, string stop = "end_turn") =>
        JsonSerializer.Serialize(new
        {
            type = "assistant", timestamp = Ts(sec),
            message = new { id = "m2", role = "assistant", stop_reason = stop, usage = new { input_tokens = 5, output_tokens = 80 },
                content = new object[] { new { type = "text", text } } },
        });

    void Append(params string[] lines) => File.AppendAllText(_transcript, string.Join("\n", lines) + "\n", new UTF8Encoding(false));

    AgentSession Session => _hub.Sessions.Single();

    [Fact]
    public void Discovers_a_transcript_and_follows_a_turn_live()
    {
        // History: a finished turn. Catching up must not fire signals.
        Append(User("old question", "p0", 1), Reply("old answer", 2));
        _hub.Poll();
        Assert.Equal(AgentActivity.Done, Session.Activity);
        Assert.Equal("demo", Session.DisplayName);
        Assert.Empty(_signals);

        _now = _now.AddSeconds(10);
        Append(User("run the tests please\nsecond line", "p1", 10));
        _hub.Poll();
        Assert.Equal([("sess-1", AgentSignal.TurnStarted)], _signals);
        Assert.Equal(AgentActivity.Thinking, Session.Activity);
        Assert.Equal("run the tests please", Session.LastPrompt);

        Append(ToolUse("t1", "Bash", new { command = "dotnet test", description = "Run tests" }, 12));
        _hub.Poll();
        Assert.Equal(AgentActivity.Tool, Session.Activity);
        Assert.Equal("Run tests", Session.Detail);
        Assert.Equal(42010, Session.ContextTokens);

        Append(ToolUse("t2", "TodoWrite", new { todos = new[]
        {
            new { content = "Write tests", status = "completed", activeForm = "Writing tests" },
            new { content = "Fix bug", status = "in_progress", activeForm = "Fixing bug" },
        } }, 13, "m3"));
        Append(ToolResult("t1", 14), ToolResult("t2", 15));
        _hub.Poll();
        Assert.Equal(AgentActivity.Thinking, Session.Activity);
        Assert.Equal(2, Session.Plan.Count);
        Assert.True(Session.Plan[1].IsActive);
        Assert.Equal(2, Session.TurnToolCalls);

        Append(Reply("All 18 tests pass.", 20));
        _hub.Poll();
        Assert.Equal(AgentActivity.Done, Session.Activity);
        Assert.Equal("All 18 tests pass.", Session.LastReply);
        Assert.Equal(TimeSpan.FromSeconds(10), Session.Elapsed(_now));
        Assert.Equal([AgentSignal.TurnStarted, AgentSignal.TurnCompleted], _signals.Select(x => x.Signal));
    }

    [Fact]
    public void A_line_still_being_written_is_read_once_complete()
    {
        Append(User("hello", "p0", 1));
        _hub.Poll();
        var line = Reply("你好，世界", 2);
        var bytes = Encoding.UTF8.GetBytes(line);
        using (var f = new FileStream(_transcript, FileMode.Append)) f.Write(bytes, 0, bytes.Length - 7);   // cut mid-character
        _hub.Poll();
        Assert.NotEqual(AgentActivity.Done, Session.Activity);
        using (var f = new FileStream(_transcript, FileMode.Append)) { f.Write(bytes, bytes.Length - 7, 7); f.WriteByte((byte)'\n'); }
        _hub.Poll();
        Assert.Equal("你好，世界", Session.LastReply);
        Assert.Equal(AgentActivity.Done, Session.Activity);
    }

    [Fact]
    public void Hooks_and_transcript_describe_one_turn_without_double_signals()
    {
        Append(User("old", "p0", 1), Reply("old", 2));
        string Hook(string ev, string extra = "") =>
            $$$"""{"session_id":"sess-1","transcript_path":{{{JsonSerializer.Serialize(_transcript)}}},"cwd":"C:\\work\\demo","prompt_id":"p1","hook_event_name":"{{{ev}}}"{{{extra}}},"clawdpet_host":{"pid":42,"hwnd":4242,"process":"claude"}}""";

        _hub.ApplyHook(Hook("UserPromptSubmit", ",\"prompt\":\"do it\""));
        Assert.Equal(new AgentHost(42, 4242, "claude"), Session.Host);
        Append(User("do it", "p1", 10));
        _hub.Poll();

        _hub.ApplyHook(Hook("Notification", ",\"notification_type\":\"permission_prompt\",\"message\":\"Claude needs your permission to use Bash\""));
        Assert.Equal(AgentActivity.Waiting, Session.Activity);
        Assert.Contains("permission", Session.Detail);

        _hub.ApplyHook(Hook("Notification", ",\"notification_type\":\"idle_prompt\",\"message\":\"Claude is waiting for your input\""));
        _hub.ApplyHook(Hook("Stop", ",\"last_assistant_message\":\"Done!\""));
        Assert.Equal(AgentActivity.Done, Session.Activity);
        Append(Reply("Done!", 20));
        _hub.Poll();

        Assert.Equal([AgentSignal.TurnStarted, AgentSignal.NeedsUser, AgentSignal.TurnCompleted], _signals.Select(x => x.Signal));
        Assert.Equal("Done!", Session.LastReply);
    }

    [Fact]
    public void An_interrupted_turn_goes_idle_without_a_completion()
    {
        Append(User("go", "p1", 1));
        _hub.Poll();
        _now = _now.AddSeconds(5);
        Append(User("go again", "p2", 3), User("[Request interrupted by user]", "p2", 4));
        _hub.Poll();
        Assert.Equal(AgentActivity.Idle, Session.Activity);
        Assert.DoesNotContain(_signals, x => x.Signal == AgentSignal.TurnCompleted);
    }

    [Fact]
    public void SessionEnd_forgets_the_session()
    {
        _hub.ApplyHook("""{"session_id":"s9","cwd":"C:\\x","hook_event_name":"SessionStart","session_title":"Refactor"}""");
        Assert.Equal("Refactor", _hub.Primary()!.DisplayName);
        _hub.ApplyHook("""{"session_id":"s9","hook_event_name":"SessionEnd"}""");
        Assert.Empty(_hub.Sessions);
    }

    [Theory]
    [InlineData("Read", """{"file_path":"C:\\src\\App.xaml.cs"}""", "读取 App.xaml.cs")]
    [InlineData("Edit", """{"file_path":"/home/me/a.py"}""", "编辑 a.py")]
    [InlineData("Bash", """{"command":"git status\ngit diff"}""", "运行 git status")]
    [InlineData("WebFetch", """{"url":"https://example.com/docs"}""", "浏览 example.com")]
    [InlineData("mcp__github__create_issue", "{}", "create issue · github")]
    public void Tool_labels(string name, string input, string expected)
    {
        using var doc = JsonDocument.Parse(input);
        Assert.Equal(expected, ToolLabels.Describe(name, doc.RootElement));
    }
}
