using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using ClawdPet.Hosting;

namespace ClawdPet;

/// <summary>
/// Entry point. Command-line calls and Claude Code hooks take a fast path that never loads WPF:
///
///   ClawdPet.exe                  start the pet (or show the running one)
///   ClawdPet.exe work | done | …  send a command to the running pet and print its reply;
///                                 if none is running, start one in the background with it
///   ClawdPet.exe hook             (Claude Code hooks) forward the hook's stdin JSON to the pet
///   --quiet / -q                  print nothing
/// </summary>
static class Program
{
    public const string RunFlag = "--run";

    [STAThread]
    static int Main(string[] args)
    {
        var paths = AppPaths.Resolve();
        Log.Init(paths.LogFile);
        ConsoleOut.Quiet = args.Any(a => a is "--quiet" or "-q");
        args = args.Where(a => a is not ("--quiet" or "-q")).ToArray();
        bool resident = args.FirstOrDefault() == RunFlag;
        var argv = args.SkipWhile(a => a is RunFlag or "--cmd" or "send").ToArray();
        string? command = argv.Length > 0 ? string.Join(' ', argv) : null;

        if (!resident && argv.FirstOrDefault() == "hook") return ForwardHook(paths);
        if (!resident && command != null) return Forward(paths, command);

        var instance = new SingleInstance(paths.InstanceKey);
        if (!instance.IsFirst)
        {
            instance.Send("show");
            instance.Dispose();
            return 0;
        }
        var app = new App(paths, instance, command);
        return app.Run();
    }

    /// <summary>Hands a command to the running pet, or starts one detached to run it.</summary>
    static int Forward(AppPaths paths, string command)
    {
        if (SingleInstance.IsRunning(paths.InstanceKey))
        {
            ConsoleOut.WriteLine(SingleInstance.Send(paths.InstanceKey, command) ?? "error: no reply from the running pet");
            return 0;
        }
        if (command is "quit" or "exit" or "status" or "ping")
        {
            ConsoleOut.WriteLine("error: ClawdPet is not running");
            return 1;
        }
        StartDetached(command);
        ConsoleOut.WriteLine("ok (started)");
        return 0;
    }

    /// <summary>
    /// ShellExecute, so the new pet does not inherit the caller's stdout handles — otherwise a
    /// Claude Code hook would wait for the pet to exit.
    /// </summary>
    static void StartDetached(string? command)
    {
        var args = command is null ? RunFlag : $"{RunFlag} {command}";
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = true });
    }

    /// <summary>
    /// <c>ClawdPet.exe hook</c>: reads a Claude Code hook payload from stdin, adds the window the
    /// session runs in (found from our own process ancestry) and forwards it as one line.
    /// </summary>
    static int ForwardHook(AppPaths paths)
    {
        try
        {
            string input;
            using (var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
                input = stdin.ReadToEnd();
            if (JsonNode.Parse(input) is not JsonObject payload) return 0;

            foreach (var key in new[] { "prompt", "last_assistant_message", "message", "tool_input", "tool_response" })
                if (payload[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 2000)
                    payload[key] = s[..2000];
            if (payload["clawdpet_host"] is null && WindowTools.FindHostWindow(Environment.ProcessId) is { } host)
                payload["clawdpet_host"] = new JsonObject { ["pid"] = host.Pid, ["hwnd"] = host.Hwnd.ToInt64(), ["process"] = host.Process };

            string line = "hook " + payload.ToJsonString();
            if (!SingleInstance.IsRunning(paths.InstanceKey))
            {
                // Only a new session or a new prompt is worth waking the pet for.
                string? ev = payload["hook_event_name"]?.GetValue<string>();
                if (ev is not ("SessionStart" or "UserPromptSubmit")) return 0;
                StartDetached(null);
                for (int i = 0; i < 40 && !SingleInstance.IsRunning(paths.InstanceKey); i++) Thread.Sleep(150);
            }
            SingleInstance.Send(paths.InstanceKey, line, timeoutMs: 5000);
        }
        catch (Exception ex)
        {
            Log.Write($"hook forward failed: {ex.Message}");
        }
        return 0;
    }
}
