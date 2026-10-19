using System.IO;
using System.IO.Pipes;
using System.Text;
using Microsoft.Win32;

namespace ClawdPet.Hosting;

static class Log
{
    static string? _file;
    static readonly object Gate = new();

    public static void Init(string file)
    {
        _file = file;
        try { if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Delete(file); } catch { }
    }

    public static void Write(string message)
    {
        if (_file is null) return;
        lock (Gate)
        {
            try { File.AppendAllText(_file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}"); }
            catch { }
        }
    }
}

/// <summary>
/// One pet per data folder. Later launches hand their command line to the running pet over a
/// named pipe (current user only) and exit — that is how <c>ClawdPet.exe work</c> drives it.
/// </summary>
sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;
    readonly string _pipeName;
    readonly CancellationTokenSource _cts = new();

    public SingleInstance(string key)
    {
        _mutex = new Mutex(true, $@"Local\ClawdPet-{key}", out bool created);
        IsFirst = created;
        _pipeName = $"ClawdPet-{key}";
    }

    public bool IsFirst { get; }

    /// <summary>Whether a pet already runs for this data folder (without taking ownership).</summary>
    public static bool IsRunning(string key)
    {
        if (!Mutex.TryOpenExisting($@"Local\ClawdPet-{key}", out var m)) return false;
        m.Dispose();
        return true;
    }

    public string? Send(string command, int timeoutMs = 3000) => Send(_pipeName, command, timeoutMs, raw: true);

    public static string? Send(string key, string command, int timeoutMs = 3000, bool raw = false)
    {
        string pipeName = raw ? key : $"ClawdPet-{key}";
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeoutMs);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine(command);
            return reader.ReadLine();
        }
        catch (Exception ex)
        {
            Log.Write($"send '{(command.Length > 60 ? command[..60] + "…" : command)}' failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Serves one command line per connection. Each connection is handled on its own task so
    /// several hooks firing at once don't queue behind each other; <paramref name="handle"/> runs
    /// on a background thread.
    /// </summary>
    public void StartServer(Func<string, string> handle) => Task.Run(async () =>
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cts.Token);
                var connected = server;
                server = null;
                _ = Task.Run(() => Serve(connected, handle));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Write($"pipe: {ex.Message}"); await Task.Delay(200); }
            finally { server?.Dispose(); }
        }
    });

    async Task Serve(NamedPipeServerStream server, Func<string, string> handle)
    {
        await using (server)
        {
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(_cts.Token);
                string reply = string.IsNullOrWhiteSpace(line) ? "error: empty command" : handle(line);
                await writer.WriteLineAsync(reply.ReplaceLineEndings(" "));   // one line per reply
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.Write($"pipe client: {ex.Message}"); }
            catch (OperationCanceledException) { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirst) { try { _mutex.ReleaseMutex(); } catch { } }
        _mutex.Dispose();
    }
}

/// <summary>Start-with-Windows via the per-user Run key.</summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "ClawdPet";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string v && string.Equals(v, Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>Lets this windowed exe print command replies: to a redirected stdout, or the parent console.</summary>
static class ConsoleOut
{
    public static bool Quiet { get; set; }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    public static void WriteLine(string text)
    {
        if (Quiet) return;
        try
        {
            IntPtr h = GetStdHandle(-11);
            if ((h == IntPtr.Zero || h == new IntPtr(-1)) && !AttachConsole(-1)) return;
            using var stdout = Console.OpenStandardOutput();
            var bytes = new UTF8Encoding(false).GetBytes(text + Environment.NewLine);
            stdout.Write(bytes, 0, bytes.Length);
        }
        catch { }
    }
}
