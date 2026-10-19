using System.Windows.Threading;
using ClawdPet.Core.Agents;
using ClawdPet.Hosting;
using ClawdPet.Native;

namespace ClawdPet.UI;

/// <summary>
/// Shows Claude Code progress above Clawd while Claude itself is out of sight (minimized or in the
/// background, per settings), or while the mouse rests on Clawd. Clicking the bubble brings Claude back.
/// </summary>
sealed class AgentPresenter
{
    readonly App _app;
    readonly AgentHub _hub;
    readonly ProgressBubble _bubble = new();
    readonly DispatcherTimer _timer;
    DateTime _peekUntil;
    int _ticks;
    AgentSession? _shown;
    (string Session, string? Prompt)? _dismissed;
    string? _lastKey;

    /// <summary>Session currently shown in the bubble (for status / tests).</summary>
    public AgentSession? Shown => _shown;

    public AgentPresenter(App app, AgentHub hub)
    {
        _app = app;
        _hub = hub;
        _bubble.Clicked += JumpToClaude;
        _bubble.DismissRequested += () =>
        {
            if (_shown != null) _dismissed = (_shown.Id, _shown.PromptId ?? _shown.TurnStartedUtc?.ToString("O"));
            _peekUntil = default;
        };
        _bubble.SettingsRequested += () => _app.OpenSettings("claude");
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    PetSettings.ClaudeOptions Options => _app.Settings.Claude;

    /// <summary>Window the session lives in: from hooks, else the Claude desktop app for desktop sessions.</summary>
    public static IntPtr HostOf(AgentSession s)
    {
        if (s.Host is { } h && Win32.IsWindow(new IntPtr(h.Hwnd))) return new IntPtr(h.Hwnd);
        return s.Entrypoint is "claude-desktop" or null ? WindowTools.ClaudeDesktopWindow() : IntPtr.Zero;
    }

    /// <summary>Whether Claude is out of sight under the current setting (unknown window counts as away).</summary>
    public bool IsAway(AgentSession s)
    {
        var hwnd = HostOf(s);
        if (hwnd == IntPtr.Zero) return true;
        return Options.BubbleWhen switch
        {
            "minimized" => WindowTools.IsMinimized(hwnd),
            "background" => WindowTools.IsMinimized(hwnd) || !WindowTools.IsForeground(hwnd),
            _ => true,
        };
    }

    /// <summary>The user has seen the window: back on screen (minimized mode) or in front (other modes).</summary>
    bool LookedAt(IntPtr hwnd) =>
        Options.BubbleWhen == "minimized" ? !WindowTools.IsMinimized(hwnd) : WindowTools.IsForeground(hwnd);

    void Tick()
    {
        if (++_ticks % 3 == 0) _hub.Poll();
        var now = DateTime.UtcNow;
        var pet = _app.Pet;
        if (pet.IsMouseOver || (_bubble.IsVisible && _bubble.IsMouseOver)) _peekUntil = now.AddSeconds(1.5);

        foreach (var s in _hub.Sessions)
            if (s.Activity is AgentActivity.Done or AgentActivity.Failed && !s.Seen && HostOf(s) is var h && h != IntPtr.Zero && LookedAt(h))
                s.Seen = true;

        // While peeking show the top session; otherwise the first one whose window is out of sight.
        bool peek = Options.HoverPeek && now < _peekUntil;
        var session = peek ? _hub.Primary() : _hub.Ranked().FirstOrDefault(s => ShouldShow(s, now));
        bool show = session != null && pet.IsVisible && pet.Engine.Brain.Current?.Name is not ("drag" or "fall");
        if (!show)
        {
            if (_bubble.IsVisible) _bubble.Hide();
            _shown = null;
            return;
        }
        _shown = session;
        var view = View(session!, now);
        string key = Key(view);
        if (key != _lastKey || !_bubble.IsVisible) { _bubble.Show(view); _lastKey = key; }
        _bubble.Animate();

        var body = pet.Engine.Body;
        var surface = _app.World.SurfaceAt(body.X, body.Y);
        _bubble.PlaceTip((int)Math.Round(body.X), (int)Math.Round(body.Y - 24 * body.Scale), surface.Left, surface.Right);
    }

    bool ShouldShow(AgentSession s, DateTime now)
    {
        if (Options.BubbleWhen == "never") return false;
        bool relevant = s.IsWorking
            || (s.Activity is AgentActivity.Done or AgentActivity.Failed && !s.Seen && now - (s.TurnEndedUtc ?? now) < TimeSpan.FromMinutes(30));
        if (!relevant) return false;
        if (_dismissed is { } d && d.Session == s.Id && d.Prompt == (s.PromptId ?? s.TurnStartedUtc?.ToString("O"))) return false;
        return IsAway(s);
    }

    BubbleView View(AgentSession s, DateTime now)
    {
        string status = s.Activity switch
        {
            AgentActivity.Thinking => "思考中…",
            AgentActivity.Responding => "回复中…",
            AgentActivity.Tool => (s.Detail ?? "使用工具") + "…",
            AgentActivity.Waiting => "等你确认 · " + (s.Detail ?? ""),
            AgentActivity.Compacting => "整理上下文…",
            AgentActivity.Done => "完成",
            AgentActivity.Failed => "出错了" + (s.Detail is { } d ? " · " + d : ""),
            _ => s.Detail ?? "空闲",
        };
        string? elapsed = s.Elapsed(now) is { } t && s.Activity != AgentActivity.Idle ? Format(t) : null;
        if (elapsed != null && s.IsWorking && s.TurnOutputTokens > 0) elapsed += $" · ↓ {Tokens(s.TurnOutputTokens)}";

        string? text = null;
        if (Options.ShowText)
            text = s.RepliedThisTurn || !s.IsWorking ? s.LastReply : s.LastPrompt is { } p ? "› " + p : null;
        if (text != null) text = ToolLabels.Clip(text, 120);

        string? stats = null;
        if (Options.ShowStats)
        {
            var parts = new List<string>();
            if (s.TurnToolCalls > 0) parts.Add($"{s.TurnToolCalls} 次工具");
            if (s.ContextTokens > 0) parts.Add($"上下文 {Tokens(s.ContextTokens)}");
            if (s.CostUsd is { } c && c > 0) parts.Add($"${c:0.00}");
            stats = parts.Count > 0 ? string.Join(" · ", parts) : null;
        }

        var others = new List<string>();
        if (Options.ShowOtherSessions)
            foreach (var o in _hub.Ranked().Where(o => o != s && (o.IsWorking || (o.Activity == AgentActivity.Done && !o.Seen))).Take(2))
                others.Add($"• {ToolLabels.Clip(o.DisplayName, 16)}  {(o.IsWorking ? o.Detail ?? "工作中" : "完成")}");

        int count = _hub.Sessions.Count(o => o.IsWorking);
        return new BubbleView(
            Title: s.DisplayName,
            Corner: count > 1 ? $"{count} 个进行中" : null,
            Activity: s.Activity,
            Status: ToolLabels.Clip(status, 44),
            Elapsed: elapsed,
            Plan: Options.ShowPlan && s.Plan.Count > 0 && s.Plan.Any(p => !p.IsDone) ? s.Plan : null,
            Text: text,
            Stats: stats,
            Others: others,
            CanJump: HostOf(s) != IntPtr.Zero);
    }

    static string Key(BubbleView v) => string.Join("|", v.Title, v.Corner, v.Activity, v.Status, v.Elapsed, v.Text, v.Stats, v.CanJump,
        v.Plan is null ? "" : string.Concat(v.Plan.Select(p => p.Status[0] + p.Text)), string.Join(";", v.Others));

    void JumpToClaude()
    {
        if (_shown == null) return;
        var hwnd = HostOf(_shown);
        if (hwnd == IntPtr.Zero) return;
        _shown.Seen = true;
        _peekUntil = default;
        WindowTools.BringToFront(hwnd);
    }

    public static string Format(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{(int)t.TotalSeconds}s"
        : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s"
        : $"{(int)t.TotalHours}h {t.Minutes:00}m";

    static string Tokens(long n) => n >= 100_000 ? $"{n / 1000}k" : n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString();

    public void Close()
    {
        _timer.Stop();
        _bubble.Close();
    }
}
