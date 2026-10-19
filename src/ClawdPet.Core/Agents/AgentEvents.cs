using System.Globalization;
using System.Text.Json;

namespace ClawdPet.Core.Agents;

/// <summary>
/// Folds Claude Code transcript lines (JSONL) and hook payloads into an <see cref="AgentSession"/>.
/// Both sources describe the same turns; prompt ids and a once-per-turn flag keep the signals single.
/// </summary>
public static class AgentEvents
{
    /// <summary>Applies one transcript line. Returns a signal for live lines that start or end a turn.</summary>
    public static AgentSignal? ApplyTranscript(AgentSession s, JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        string? type = Str(e, "type");
        var at = Time(e) ?? s.LastEventUtc;

        switch (type)
        {
            case "custom-title":
                s.Title = Str(e, "customTitle") ?? s.Title;
                return null;
            case "summary":
                s.Title ??= Str(e, "summary");
                return null;
            case "cost-state":
                if (e.TryGetProperty("totalCostUSD", out var cost) && cost.TryGetDouble(out var c)) s.CostUsd = c;
                if (e.TryGetProperty("totalLinesAdded", out var la) && la.TryGetInt32(out var a)) s.LinesAdded = a;
                if (e.TryGetProperty("totalLinesRemoved", out var lr) && lr.TryGetInt32(out var r)) s.LinesRemoved = r;
                return null;
            case "user" or "assistant":
                break;
            default:
                return null;
        }

        if (e.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) return null;
        s.Entrypoint = Str(e, "entrypoint") ?? s.Entrypoint;
        s.Cwd = Str(e, "cwd") ?? s.Cwd;
        if (at > s.LastEventUtc) s.LastEventUtc = at;
        if (!e.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return null;
        msg.TryGetProperty("content", out var content);

        if (type == "user") return ApplyUser(s, e, content, at);

        s.Model = Str(msg, "model") ?? s.Model;
        ApplyUsage(s, msg);
        if (content.ValueKind != JsonValueKind.Array) return null;
        foreach (var block in content.EnumerateArray())
        {
            switch (Str(block, "type"))
            {
                case "thinking":
                    s.Activity = AgentActivity.Thinking;
                    s.Detail = null;
                    break;
                case "text":
                    if (Str(block, "text") is { Length: > 0 } text)
                    {
                        s.LastReply = text.Trim();
                        s.RepliedThisTurn = true;
                        s.Activity = AgentActivity.Responding;
                        s.Detail = null;
                    }
                    break;
                case "tool_use":
                    string name = Str(block, "name") ?? "tool";
                    block.TryGetProperty("input", out var input);
                    string label = ToolLabels.Describe(name, input);
                    if (Str(block, "id") is { } id) s.PendingTools[id] = label;
                    s.TurnToolCalls++;
                    s.Activity = AgentActivity.Tool;
                    s.Detail = label;
                    if (PlanReader.TryRead(input) is { } plan && (name == "TodoWrite" || name.Contains("plan", StringComparison.OrdinalIgnoreCase)))
                        s.Plan = plan;
                    break;
            }
        }
        if (Str(msg, "stop_reason") == "end_turn")
            return s.EndTurn(at);
        return null;
    }

    static AgentSignal? ApplyUser(AgentSession s, JsonElement e, JsonElement content, DateTime at)
    {
        if (e.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) return null;

        string? prompt = null;
        bool toolResult = false;
        if (content.ValueKind == JsonValueKind.String) prompt = content.GetString();
        else if (content.ValueKind == JsonValueKind.Array)
            foreach (var block in content.EnumerateArray())
            {
                if (Str(block, "type") == "tool_result")
                {
                    toolResult = true;
                    if (Str(block, "tool_use_id") is { } id) s.PendingTools.Remove(id);
                }
                else if (Str(block, "type") == "text") prompt ??= Str(block, "text");
            }

        if (toolResult)
        {
            if (s.Activity is AgentActivity.Done or AgentActivity.Failed or AgentActivity.Idle) return null;
            s.Activity = s.PendingTools.Count == 0 ? AgentActivity.Thinking : AgentActivity.Tool;
            s.Detail = s.PendingTools.Values.LastOrDefault();
            return null;
        }
        if (string.IsNullOrWhiteSpace(prompt)) return null;
        prompt = prompt.Trim();
        if (prompt.StartsWith("[Request interrupted", StringComparison.Ordinal))
        {
            s.Activity = AgentActivity.Idle;
            s.Detail = "已中断";
            s.PendingTools.Clear();
            s.TurnEndedUtc = at;
            s.TurnCompletedSignaled = true;
            return null;
        }
        // Slash-command plumbing and command output are not prompts.
        if (prompt.StartsWith('<')) return null;
        string? promptId = Str(e, "promptId");
        if (promptId != null && promptId == s.PromptId && s.IsWorking) return null;
        s.BeginTurn(at, promptId, FirstLine(prompt));
        return AgentSignal.TurnStarted;
    }

    /// <summary>Applies a hook payload (stdin JSON of a Claude Code hook, plus "clawdpet_host").</summary>
    public static AgentSignal? ApplyHook(AgentSession s, JsonElement e, DateTime nowUtc)
    {
        s.TranscriptPath = Str(e, "transcript_path") ?? s.TranscriptPath;
        s.Cwd = Str(e, "cwd") ?? s.Cwd;
        s.LastEventUtc = nowUtc;
        if (e.TryGetProperty("clawdpet_host", out var h) && h.ValueKind == JsonValueKind.Object
            && h.TryGetProperty("hwnd", out var hw) && hw.TryGetInt64(out long hwnd) && hwnd != 0)
            s.Host = new AgentHost(h.TryGetProperty("pid", out var p) && p.TryGetInt32(out int pid) ? pid : 0, hwnd, Str(h, "process") ?? "");

        string? promptId = Str(e, "prompt_id");
        switch (Str(e, "hook_event_name"))
        {
            case "SessionStart":
                s.Title = Str(e, "session_title") ?? s.Title;
                s.Model = Str(e, "model") ?? s.Model;
                if (!s.IsWorking) { s.Activity = AgentActivity.Idle; s.Detail = null; }
                return AgentSignal.SessionStarted;

            case "UserPromptSubmit":
                if (promptId != null && promptId == s.PromptId && s.IsWorking) return null;
                s.BeginTurn(nowUtc, promptId, Str(e, "prompt") is { } pr ? FirstLine(pr.Trim()) : null);
                return AgentSignal.TurnStarted;

            case "PreToolUse":
                if (Str(e, "tool_name") is { } tool)
                {
                    e.TryGetProperty("tool_input", out var input);
                    s.Activity = AgentActivity.Tool;
                    s.Detail = ToolLabels.Describe(tool, input);
                }
                return null;

            case "Notification":
                string? kind = Str(e, "notification_type") ?? Str(e, "matcher");
                string? message = Str(e, "message");
                if (kind is "idle_prompt" or "auth_success" or "elicitation_complete" or "elicitation_response"
                    || (kind == null && message != null && message.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase)))
                    return null;
                s.Activity = AgentActivity.Waiting;
                s.Detail = message is { Length: > 0 } ? ToolLabels.Clip(message, 80) : "需要你的确认";
                return AgentSignal.NeedsUser;

            case "PreCompact":
                s.Activity = AgentActivity.Compacting;
                s.Detail = null;
                return null;

            case "Stop":
                if (Str(e, "last_assistant_message") is { Length: > 0 } last) { s.LastReply = last.Trim(); s.RepliedThisTurn = true; }
                return s.EndTurn(nowUtc);

            case "StopFailure":
                s.Detail = Str(e, "error") ?? Str(e, "message");
                var signal = s.EndTurn(nowUtc, failed: true);
                s.Detail ??= "出错了";
                return signal;

            case "SessionEnd":
                return AgentSignal.SessionEnded;
        }
        return null;
    }

    static void ApplyUsage(AgentSession s, JsonElement msg)
    {
        if (!msg.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return;
        long Num(string k) => u.TryGetProperty(k, out var v) && v.TryGetInt64(out long n) ? n : 0;
        long context = Num("input_tokens") + Num("cache_read_input_tokens") + Num("cache_creation_input_tokens");
        if (context > 0) s.ContextTokens = context;
        if (Str(msg, "id") is { } id)
        {
            s.TurnOutputByMessage[id] = Num("output_tokens");
            s.TurnOutputTokens = s.TurnOutputByMessage.Values.Sum();
        }
    }

    static string FirstLine(string s) => ToolLabels.Clip(s.Split('\n', 2)[0], 80);

    static string? Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static DateTime? Time(JsonElement e) =>
        Str(e, "timestamp") is { } t && DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d : null;
}
