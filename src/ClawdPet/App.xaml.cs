using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using ClawdPet.Core.Sprites;
using ClawdPet.Hosting;
using ClawdPet.Native;

namespace ClawdPet;

/// <summary>
/// Host: single instance, settings, skin loading, tray and the command pipe.
///
///   ClawdPet.exe                 start (or show the running pet)
///   ClawdPet.exe work            send a command to the running pet and print its reply (see PetCommands);
///                                if no pet is running, one is started in the background with that command
///   ClawdPet.exe think 30 | done | status | quit | scale 4 | reload | summon | ping
/// </summary>
public partial class App : Application
{
    SingleInstance? _instance;
    TrayIcon? _tray;
    DesktopWorld _world = null!;
    bool _quitting;

    internal AppPaths Paths { get; private set; } = null!;
    internal PetSettings Settings { get; private set; } = null!;
    internal PetWindow Pet { get; private set; } = null!;
    internal ClaudeCodeIntegration Claude { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Paths = AppPaths.Resolve();
        Log.Init(Paths.LogFile);

        // --quiet: print nothing (Claude Code adds some hooks' stdout to the conversation).
        ConsoleOut.Quiet = e.Args.Any(a => a is "--quiet" or "-q");
        var args0 = e.Args.Where(a => a is not ("--quiet" or "-q")).ToArray();
        bool resident = args0.FirstOrDefault() == RunFlag;
        var argv = args0.SkipWhile(a => a is RunFlag or "--cmd" or "send").ToArray();
        string? command = argv.Length > 0 ? string.Join(' ', argv) : null;

        _instance = new SingleInstance(Paths.InstanceKey);
        if (!_instance.IsFirst || (command != null && !resident))
        {
            ForwardAndExit(command);
            return;
        }

        DispatcherUnhandledException += (_, ex) => { Log.Write($"unhandled: {ex.Exception}"); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write($"fatal: {ex.ExceptionObject}");
        SessionEnding += (_, _) => Quit();

        Settings = PetSettings.Load(Paths.SettingsFile);
        Claude = new ClaudeCodeIntegration(Paths);
        var atlas = LoadAtlas(Settings.Skin) ?? LoadAtlas("clawd");
        if (atlas is null)
        {
            MessageBox.Show("找不到 Clawd 的精灵图（Assets\\Skins\\clawd）。", "ClawdPet", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _world = new DesktopWorld();
        Pet = new PetWindow(atlas, Settings, _world);
        Pet.MenuRequested += ShowMenu;
        Pet.BehaviorChanged += b => _tray?.SetStatus($"Clawd · {b.Name}");
        Pet.Show();
        Pet.Enter(Settings.X, Settings.Y);

        _tray = new TrayIcon(Paths.IconFile, ShowMenu, Summon);
        _instance.StartServer(line => Dispatcher.Invoke(() => Handle(line)));
        Log.Write($"started home={Paths.Home} sandboxed={Paths.Sandboxed} skin={Settings.Skin}");

        if (command != null && command != "show") Handle(command);
    }

    const string RunFlag = "--run";

    /// <summary>
    /// A command-line call (e.g. from a Claude Code hook) must return quickly. Hand the command to the
    /// running pet; if there is none, start one detached (ShellExecute, so it does not inherit the
    /// caller's stdout and keep a hook waiting) and return at once.
    /// </summary>
    void ForwardAndExit(string? command)
    {
        var instance = _instance!;
        _instance = null;
        if (!instance.IsFirst)
        {
            string? reply = instance.Send(command ?? "show");
            if (command != null) ConsoleOut.WriteLine(reply ?? "error: no reply from the running pet");
            instance.Dispose();
        }
        else
        {
            instance.Dispose();
            if (command is "quit" or "exit" or "status" or "ping")
            {
                ConsoleOut.WriteLine("error: ClawdPet is not running");
            }
            else
            {
                var args = new[] { RunFlag }.Concat(command!.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Join(' ', args)) { UseShellExecute = true });
                ConsoleOut.WriteLine("ok (started)");
            }
        }
        Shutdown();
    }

    /// <summary>Host-level commands first, then the pet's own (<see cref="Core.PetCommands"/>).</summary>
    string Handle(string line)
    {
        Log.Write($"command: {line}");
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "error: empty command";
        switch (parts[0].ToLowerInvariant())
        {
            case "quit" or "exit":
                Dispatcher.BeginInvoke(Quit);
                return "ok";
            case "ping":
                return "pong";
            case "status":
                var b = Pet.Engine.Body;
                return $"behavior={Pet.Engine.Brain.Current?.Name} clip={Pet.Engine.Animator.Clip?.Name} frame={Pet.Engine.Animator.AtlasFrame} x={b.X:0} y={b.Y:0} scale={b.Scale} visible={Pet.IsVisible}";
            case "show":
                if (!Pet.IsVisible) Pet.Show();
                return "ok";
            case "hide":
                Pet.Hide();
                return "ok";
            case "summon":
                Summon();
                return "ok";
            case "menu":
                ShowMenu();
                return "ok";
            case "reload":
                ReloadSkin();
                return "ok";
            case "scale" when parts.Length > 1 && int.TryParse(parts[1], out int s):
                SetScale(s);
                return "ok";
            case "agent":
                return AgentCommand(parts.Skip(1).FirstOrDefault()?.ToLowerInvariant() ?? "status");
        }
        return Pet.Engine.Command(line);
    }

    SpriteAtlas? LoadAtlas(string skin)
    {
        var path = Paths.FindSkin(skin);
        if (path is null) return null;
        try { return SpriteAtlas.Load(path); }
        catch (Exception ex) { Log.Write($"skin '{skin}' failed to load: {ex.Message}"); return null; }
    }

    void ShowMenu()
    {
        var menu = PetMenu.Build(this);
        if (Pet.IsVisible) Win32.SetForegroundWindow(Pet.Handle);
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    internal void SetScale(int scale)
    {
        Pet.SetScale(scale);
        Settings.Save(Paths.SettingsFile);
    }

    internal void SetTopmost(bool on)
    {
        Settings.Topmost = Pet.Topmost = on;
        Settings.Save(Paths.SettingsFile);
    }

    internal void SetAutonomous(bool on)
    {
        Settings.Autonomous = Pet.Engine.Context.Options.Autonomous = on;
        Settings.Save(Paths.SettingsFile);
    }

    internal void SetAutostart(bool on)
    {
        if (Paths.Sandboxed) return;
        try { Autostart.Set(on); }
        catch (Exception ex) { Log.Write($"autostart: {ex.Message}"); }
    }

    internal void SetSkin(string name)
    {
        var atlas = LoadAtlas(name);
        if (atlas is null) return;
        Settings.Skin = name;
        Pet.UseSkin(atlas);
        Settings.Save(Paths.SettingsFile);
    }

    internal void ReloadSkin()
    {
        var atlas = LoadAtlas(Settings.Skin);
        if (atlas != null) Pet.UseSkin(atlas);
    }

    internal void Summon()
    {
        if (!Pet.IsVisible) Pet.Show();
        Win32.GetCursorPos(out var p);
        Pet.DropAt(p.X, p.Y);
    }

    internal void ToggleVisible()
    {
        if (Pet.IsVisible) Pet.Hide();
        else Pet.Show();
    }

    /// <summary><c>agent status | install | uninstall</c> — Claude Code hook-up without dialogs.</summary>
    string AgentCommand(string action)
    {
        try
        {
            return action switch
            {
                "install" => Claude.Install(),
                "uninstall" or "remove" => Claude.Uninstall(),
                _ => $"claude-code={Claude.State()} settings={Claude.SettingsPath}",
            };
        }
        catch (Exception ex) { return $"error: {ex.Message}"; }
    }

    /// <summary>Menu: confirm, then add or remove the Claude Code hooks.</summary>
    internal void ToggleClaudeCode()
    {
        bool remove = Claude.State() == Core.Integrations.HookState.Installed;
        string question = remove
            ? $"从 Claude Code 配置中移除 ClawdPet 的 hooks？\n\n{Claude.SettingsPath}"
            : $"在 Claude Code 配置中加入 ClawdPet 的 hooks？\n\n{Claude.SettingsPath}\n\n"
              + "会话开始 → 挥手\n发送消息 → 掏出电脑敲代码\n回复完成 → 收工并开心地跳\n需要你处理 → 冒出 \"!\"\n\n"
              + "已有配置会保留，修改前会自动备份。";
        if (MessageBox.Show(question, "ClawdPet · Agent 接入", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        try
        {
            MessageBox.Show(remove ? Claude.Uninstall() : Claude.Install(), "ClawdPet · Agent 接入", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Write($"claude code hook-up failed: {ex}");
            MessageBox.Show($"没有修改配置：{ex.Message}", "ClawdPet · Agent 接入", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal void OpenClaudeSettings()
    {
        var path = Claude.SettingsPath;
        if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"") { UseShellExecute = true });
    }

    internal void OpenDataFolder() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.Home}\"") { UseShellExecute = true });

    internal void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        if (Pet != null)
        {
            Settings.X = Pet.Engine.Body.X;
            Settings.Y = Pet.Engine.Body.Y;
            Settings.Save(Paths.SettingsFile);
        }
        _tray?.Dispose();
        _tray = null;
        _instance?.Dispose();
        _instance = null;
        Log.Write("quit");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
