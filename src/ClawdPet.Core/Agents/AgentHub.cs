using System.Text;
using System.Text.Json;

namespace ClawdPet.Core.Agents;

/// <summary>
/// Keeps track of Claude Code sessions: hook payloads come in through <see cref="ApplyHook"/>,
/// and every known transcript is tailed on <see cref="Poll"/>. Sessions can also be discovered
/// by watching ~/.claude/projects for transcripts being written, so it works without hooks too.
/// </summary>
public sealed class AgentHub
{
    /// <summary>How much of an existing transcript to read when first attaching to it.</summary>
    const int InitialTailBytes = 512 * 1024;
    static readonly TimeSpan DiscoveryWindow = TimeSpan.FromMinutes(3);
    static readonly TimeSpan StaleWorking = TimeSpan.FromMinutes(15);
    static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(2);

    readonly Dictionary<string, AgentSession> _sessions = [];
    readonly Dictionary<string, Tail> _tails = new(StringComparer.OrdinalIgnoreCase);
    DateTime _lastDiscovery;

    /// <summary>Folder holding &lt;project&gt;\&lt;session&gt;.jsonl transcripts; null disables discovery.</summary>
    public string? ProjectsDir { get; set; }
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public event Action<AgentSession, AgentSignal>? Signal;

    public IReadOnlyCollection<AgentSession> Sessions => _sessions.Values;

    public void ApplyHook(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        if (!e.TryGetProperty("session_id", out var idEl) || idEl.GetString() is not { Length: > 0 } id) return;
        var s = Get(id);
        // Read whatever the transcript already says first, so the hook's state wins.
        if (e.TryGetProperty("transcript_path", out var tp) && tp.GetString() is { Length: > 0 } path)
        {
            s.TranscriptPath = path;
            Attach(s, path);
            ReadTail(s);
        }
        var signal = AgentEvents.ApplyHook(s, e, Clock());
        if (signal == AgentSignal.SessionEnded) _sessions.Remove(id);
        if (signal is { } sig) Signal?.Invoke(s, sig);
    }

    /// <summary>Reads new transcript lines, discovers new sessions and expires old ones.</summary>
    public void Poll()
    {
        var now = Clock();
        if (ProjectsDir != null && now - _lastDiscovery > TimeSpan.FromSeconds(3))
        {
            _lastDiscovery = now;
            Discover(now);
        }
        foreach (var s in _sessions.Values.ToList())
        {
            ReadTail(s);
            if (s.IsWorking && now - s.LastEventUtc > StaleWorking)
            {
                s.Activity = AgentActivity.Idle;
                s.Detail = null;
            }
            if (!s.IsWorking && now - s.LastEventUtc > ForgetAfter) Forget(s);
        }
    }

    /// <summary>The session most worth showing: busy ones first, then the latest finished turn.</summary>
    public AgentSession? Primary() => Ranked().FirstOrDefault();

    public IEnumerable<AgentSession> Ranked() => _sessions.Values
        .OrderByDescending(s => s.Activity == AgentActivity.Waiting)
        .ThenByDescending(s => s.IsWorking)
        .ThenByDescending(s => s.Activity is AgentActivity.Done or AgentActivity.Failed && !s.Seen)
        .ThenByDescending(s => s.LastEventUtc);

    AgentSession Get(string id)
    {
        if (!_sessions.TryGetValue(id, out var s))
            _sessions[id] = s = new AgentSession(id) { LastEventUtc = Clock() };
        return s;
    }

    void Forget(AgentSession s)
    {
        _sessions.Remove(s.Id);
        if (s.TranscriptPath != null) _tails.Remove(s.TranscriptPath);
    }

    void Discover(DateTime now)
    {
        try
        {
            if (!Directory.Exists(ProjectsDir)) return;
            foreach (var dir in Directory.EnumerateDirectories(ProjectsDir!))
                foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
                {
                    if (_tails.ContainsKey(file)) continue;
                    var written = File.GetLastWriteTimeUtc(file);
                    if (now - written > DiscoveryWindow) continue;
                    var s = Get(Path.GetFileNameWithoutExtension(file));
                    s.TranscriptPath = file;
                    s.LastEventUtc = written;
                    Attach(s, file);
                }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void Attach(AgentSession s, string path)
    {
        if (_tails.ContainsKey(path)) return;
        var tail = new Tail(path);
        _tails[path] = tail;
        // Catch up on recent history silently (no signals), then follow live.
        foreach (var line in tail.ReadNew(InitialTailBytes)) Apply(s, line, live: false);
        if (s.IsWorking && Clock() - s.LastEventUtc > StaleWorking) s.Activity = AgentActivity.Idle;
    }

    void ReadTail(AgentSession s)
    {
        if (s.TranscriptPath is null || !_tails.TryGetValue(s.TranscriptPath, out var tail)) return;
        foreach (var line in tail.ReadNew()) Apply(s, line, live: true);
    }

    void Apply(AgentSession s, string line, bool live)
    {
        AgentSignal? signal;
        try
        {
            using var doc = JsonDocument.Parse(line);
            signal = AgentEvents.ApplyTranscript(s, doc.RootElement);
        }
        catch (JsonException) { return; }
        if (live && signal is { } sig) Signal?.Invoke(s, sig);
    }

    /// <summary>Follows a growing JSONL file, returning only complete new lines.</summary>
    sealed class Tail(string path)
    {
        long _offset = -1;
        bool _skipFirstLine;

        /// <remarks>Works on bytes and only consumes up to the last newline, so a line (or a UTF-8
        /// character) that is still being written is picked up whole on the next call.</remarks>
        public IEnumerable<string> ReadNew(int initialBytes = 0)
        {
            var lines = new List<string>();
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (_offset < 0)
                {
                    _offset = Math.Max(0, fs.Length - initialBytes);
                    _skipFirstLine = _offset > 0;
                }
                if (fs.Length < _offset) _offset = 0;   // truncated or rewritten
                if (fs.Length == _offset) return lines;
                fs.Position = _offset;
                var buffer = new byte[fs.Length - _offset];
                int read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                int end = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
                if (end < 0) return lines;
                _offset += end + 1;
                int start = 0;
                for (int i = 0; i <= end; i++)
                {
                    if (buffer[i] != (byte)'\n') continue;
                    if (_skipFirstLine) _skipFirstLine = false;
                    else if (i > start) lines.Add(Encoding.UTF8.GetString(buffer, start, i - start).TrimEnd('\r'));
                    start = i + 1;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return lines;
        }
    }
}
