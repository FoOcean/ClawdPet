namespace ClawdPet.Core.Agents;

public enum AgentActivity
{
    Idle,
    Thinking,
    Responding,
    Tool,
    /// <summary>Waiting for the user (permission prompt or a question).</summary>
    Waiting,
    Compacting,
    Done,
    Failed,
}

/// <summary>Things the pet may react to.</summary>
public enum AgentSignal { SessionStarted, TurnStarted, TurnCompleted, NeedsUser, Failed, SessionEnded }

public sealed record PlanItem(string Text, string Status)
{
    public bool IsDone => Status is "completed" or "done";
    public bool IsActive => Status is "in_progress" or "active";
}

/// <summary>The window an agent session lives in (found by the hook forwarder from its process tree).</summary>
public sealed record AgentHost(int Pid, long Hwnd, string Process);

/// <summary>Live state of one Claude Code session, built from hook events and its transcript.</summary>
public sealed class AgentSession(string id)
{
    public string Id { get; } = id;
    public string? TranscriptPath { get; set; }
    public string? Cwd { get; set; }
    public string? Title { get; set; }
    /// <summary>claude-desktop, cli, … (from the transcript).</summary>
    public string? Entrypoint { get; set; }
    public string? Model { get; set; }
    public AgentHost? Host { get; set; }

    public AgentActivity Activity { get; set; }
    /// <summary>What exactly is happening: tool label, permission message…</summary>
    public string? Detail { get; set; }
    public DateTime LastEventUtc { get; set; }
    public DateTime? TurnStartedUtc { get; set; }
    public DateTime? TurnEndedUtc { get; set; }
    public string? PromptId { get; set; }
    public string? LastPrompt { get; set; }
    public string? LastReply { get; set; }
    /// <summary>Whether <see cref="LastReply"/> belongs to the current turn.</summary>
    public bool RepliedThisTurn { get; set; }
    public int TurnToolCalls { get; set; }
    public IReadOnlyList<PlanItem> Plan { get; set; } = [];
    /// <summary>Tokens in the context window as of the latest model call.</summary>
    public long ContextTokens { get; set; }
    public long TurnOutputTokens { get; set; }
    public double? CostUsd { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }

    /// <summary>Set once the user has looked at the finished turn (its window came to the front).</summary>
    public bool Seen { get; set; }

    internal Dictionary<string, string> PendingTools { get; } = [];
    internal Dictionary<string, long> TurnOutputByMessage { get; } = [];
    internal bool TurnCompletedSignaled { get; set; }

    public bool IsWorking => Activity is AgentActivity.Thinking or AgentActivity.Responding
        or AgentActivity.Tool or AgentActivity.Waiting or AgentActivity.Compacting;

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Title) ? Title!
        : !string.IsNullOrWhiteSpace(Cwd) ? Path.GetFileName(Cwd!.TrimEnd('\\', '/')) is { Length: > 0 } f ? f : Cwd!
        : "Claude";

    public TimeSpan? Elapsed(DateTime nowUtc) =>
        TurnStartedUtc is { } start ? ((IsWorking ? nowUtc : TurnEndedUtc ?? nowUtc) - start) : null;

    internal void BeginTurn(DateTime at, string? promptId, string? prompt)
    {
        PromptId = promptId;
        if (prompt != null) LastPrompt = prompt;
        TurnStartedUtc = at;
        TurnEndedUtc = null;
        TurnToolCalls = 0;
        RepliedThisTurn = false;
        TurnOutputTokens = 0;
        TurnOutputByMessage.Clear();
        PendingTools.Clear();
        TurnCompletedSignaled = false;
        Seen = false;
        Activity = AgentActivity.Thinking;
        Detail = null;
    }

    internal AgentSignal? EndTurn(DateTime at, bool failed = false)
    {
        Activity = failed ? AgentActivity.Failed : AgentActivity.Done;
        Detail = null;
        PendingTools.Clear();
        TurnEndedUtc = at;
        if (TurnCompletedSignaled) return null;
        TurnCompletedSignaled = true;
        return failed ? AgentSignal.Failed : AgentSignal.TurnCompleted;
    }
}
