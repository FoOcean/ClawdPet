using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClawdPet.Core.Agents;
using ClawdPet.Core.Behaviors;
using ClawdPet.Core.Sprites;
using ClawdPet.Hosting;
using ClawdPet.Native;
using ClawdPet.Rendering;
using ClawdPet.UI;
using ClawdPet.UI.Settings;

namespace ClawdPet;

/// <summary>
/// The running pet: settings, skin, tray, command pipe, Claude Code session tracking with its
/// progress bubble, fullscreen auto-hide and the show/hide hotkey. Started by <see cref="Program"/>.
/// </summary>
sealed class App : Application
{
    readonly SingleInstance _instance;
    readonly string? _initialCommand;
    TrayIcon? _tray;
    Hotkey? _hotkey;
    AgentPresenter? _presenter;
    SettingsWindow? _settingsWindow;
    DispatcherTimer? _watch;
    bool _quitting, _userHidden, _fullscreenHidden;

    public App(AppPaths paths, SingleInstance instance, string? initialCommand)
    {
        Paths = paths;
        _instance = instance;
        _initialCommand = initialCommand;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    internal AppPaths Paths { get; }
    internal PetSettings Settings { get; private set; } = null!;
    internal PetWindow Pet { get; private set; } = null!;
    internal DesktopWorld World { get; private set; } = null!;
    internal ClaudeCodeIntegration Claude { get; private set; } = null!;
    internal AgentHub Hub { get; } = new();
    internal bool HotkeyRegistered { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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

        World = new DesktopWorld();
        Pet = new PetWindow(atlas, Settings, World);
        Pet.MenuRequested += ShowMenu;
        Pet.BehaviorChanged += b => _tray?.SetStatus($"Clawd · {b.Name}");
        Pet.Show();
        Pet.Enter(Settings.X, Settings.Y);

        _hotkey = new Hotkey(Pet.Handle, ToggleVisible);
        Hub.Signal += OnAgentSignal;
        ApplySettings(save: false);
        _presenter = new AgentPresenter(this, Hub);

        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _watch.Tick += (_, _) => CheckFullscreen();
        _watch.Start();

        _tray = new TrayIcon(Paths.IconFile, ShowMenu, Summon);
        _instance.StartServer(line => Dispatcher.Invoke(() => Handle(line)));
        Log.Write($"started home={Paths.Home} sandboxed={Paths.Sandboxed} skin={Settings.Skin}");

        if (_initialCommand != null && _initialCommand != "show") Handle(_initialCommand);
    }

    /// <summary>Claude's projects folder (transcripts): CLAWDPET_CLAUDE_PROJECTS, $CLAUDE_CONFIG_DIR or ~/.claude.</summary>
    static string ClaudeProjectsDir()
    {
        var custom = Environment.GetEnvironmentVariable("CLAWDPET_CLAUDE_PROJECTS");
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(dir, "projects");
    }

    /// <summary>Pushes the settings into the pet, hotkey and session tracking, then saves them.</summary>
    internal void ApplySettings(bool save = true)
    {
        var o = Pet.Engine.Context.Options;
        o.Autonomous = Settings.Autonomous;
        o.Calmness = Settings.Calmness;
        o.SleepAfterMs = Settings.SleepAfterMinutes > 0 ? Settings.SleepAfterMinutes * 60_000 : double.PositiveInfinity;
        o.DisabledAutonomous = new HashSet<string>(Settings.DisabledBehaviors, StringComparer.OrdinalIgnoreCase);
        if (Settings.SleepAfterMinutes <= 0) o.DisabledAutonomous.Add("sleep");
        Pet.Topmost = Settings.Topmost;
        Pet.SetScale(Settings.Scale);
        HotkeyRegistered = _hotkey?.Set(Settings.Hotkey) ?? false;
        Hub.ProjectsDir = Settings.Claude.DiscoverSessions ? ClaudeProjectsDir() : null;
        if (save) Settings.Save(Paths.SettingsFile);
    }

    /// <summary>Host-level commands first, then the pet's own (<see cref="Core.PetCommands"/>).</summary>
    string Handle(string line)
    {
        var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "error: empty command";
        string verb = parts[0].ToLowerInvariant();
        if (verb != "hook") Log.Write($"command: {line}");
        switch (verb)
        {
            case "hook":
                try { Hub.ApplyHook(parts.Length > 1 ? parts[1] : "{}"); return "ok"; }
                catch (Exception ex) { Log.Write($"hook payload: {ex.Message}"); return "error: bad hook payload"; }
            case "quit" or "exit":
                Dispatcher.BeginInvoke(Quit);
                return "ok";
            case "ping":
                return "pong";
            case "status":
                var b = Pet.Engine.Body;
                return $"behavior={Pet.Engine.Brain.Current?.Name} clip={Pet.Engine.Animator.Clip?.Name} frame={Pet.Engine.Animator.AtlasFrame} x={b.X:0} y={b.Y:0} scale={b.Scale} visible={Pet.IsVisible} bubble={(_presenter?.Shown is { } shown ? $"{shown.DisplayName}/{shown.Activity}" : "-")}";
            case "sessions":
                return string.Join(" | ", Hub.Ranked().Select(s => $"{s.DisplayName}:{s.Activity}:{s.Detail}:{(s.Host != null ? s.Host.Process : s.Entrypoint)}"));
            case "show":
                _userHidden = false;
                UpdateVisibility();
                return "ok";
            case "hide":
                _userHidden = true;
                UpdateVisibility();
                return "ok";
            case "summon":
                Summon();
                return "ok";
            case "menu":
                ShowMenu();
                return "ok";
            case "settings":
                OpenSettings(parts.Length > 1 ? parts[1] : null);
                return "ok";
            case "reload":
                ReloadSkin();
                return "ok";
            case "snapshot" when parts.Length > 1 && parts[1].Split(' ', 3) is [var what, var page, var path] && what == "settings":
                // Debug aid: render a settings page to PNG, e.g. "snapshot settings claude C:\tmp\claude.png".
                OpenSettings(page);
                _settingsWindow!.Snapshot(path);
                return "ok";
            case "scale" when parts.Length > 1 && int.TryParse(parts[1], out int s):
                SetScale(s);
                return "ok";
            case "agent":
                return AgentCommand(parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "status");
        }
        return Pet.Engine.Command(line);
    }

    /// <summary>Clawd reacts to what Claude Code is doing.</summary>
    void OnAgentSignal(AgentSession session, AgentSignal signal)
    {
        Log.Write($"agent {signal}: {session.DisplayName} ({session.Activity})");
        var c = Settings.Claude;
        var brain = Pet.Engine.Brain;
        var command = new BehaviorArgs { Source = BehaviorSource.Command };
        switch (signal)
        {
            case AgentSignal.TurnStarted when c.ReactTyping:
                if (brain.Current?.Name != "work") brain.Request("work", command);
                break;
            case AgentSignal.TurnCompleted:
                if (c.ReactDone && !Hub.Sessions.Any(s => s.IsWorking)) brain.FinishCurrent(then: "happy");
                if (c.CompletionSound && _presenter?.IsAway(session) == true) SystemSounds.Asterisk.Play();
                break;
            case AgentSignal.NeedsUser when c.ReactAttention:
                // "!" and a wave, then back to the keyboard if Claude is still busy.
                brain.Request("surprised", command with
                {
                    Then = "wave",
                    ThenArgs = command with { Then = c.ReactTyping ? "work" : null, ThenArgs = command },
                });
                break;
            case AgentSignal.Failed when c.ReactAttention:
                brain.Request("surprised", command);
                break;
            case AgentSignal.SessionStarted when c.ReactDone:
                if (brain.Current?.Name == "idle") brain.Request("wave", command);
                break;
        }
    }

    void CheckFullscreen()
    {
        bool hide = Settings.HideInFullscreen && Pet.Handle != IntPtr.Zero
            && WindowTools.FullscreenMonitor() is var mon && mon != IntPtr.Zero && mon == WindowTools.MonitorOf(Pet.Handle);
        if (hide == _fullscreenHidden) return;
        _fullscreenHidden = hide;
        UpdateVisibility();
    }

    void UpdateVisibility()
    {
        bool visible = !_userHidden && !_fullscreenHidden;
        if (visible && !Pet.IsVisible) Pet.Show();
        else if (!visible && Pet.IsVisible) Pet.Hide();
    }

    internal void ToggleVisible()
    {
        _userHidden = Pet.IsVisible;
        if (!_userHidden) _fullscreenHidden = false;
        UpdateVisibility();
    }

    SpriteAtlas? LoadAtlas(string skin)
    {
        var path = Paths.FindSkin(skin);
        if (path is null) return null;
        try { return SpriteAtlas.Load(path); }
        catch (Exception ex) { Log.Write($"skin '{skin}' failed to load: {ex.Message}"); return null; }
    }

    /// <summary>First idle frame of a skin, 3× (for the settings page).</summary>
    internal BitmapSource? SkinPreview(string skin)
    {
        if (LoadAtlas(skin) is not { } atlas) return null;
        try
        {
            var frames = new SpriteFrames(atlas);
            frames.Configure(3, 1);
            return frames.Get(atlas.Clips.TryGetValue("idle", out var idle) ? idle.Frames[0].AtlasIndex : 0);
        }
        catch (Exception ex) { Log.Write($"preview '{skin}': {ex.Message}"); return null; }
    }

    void ShowMenu()
    {
        var menu = PetMenu.Build(this);
        if (Pet.IsVisible) Win32.SetForegroundWindow(Pet.Handle);
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    internal void OpenSettings(string? page = null)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.ShowPage(page);
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    internal void SetScale(int scale)
    {
        Settings.Scale = Math.Clamp(scale, 1, 12);
        ApplySettings();
    }

    internal void SetTopmost(bool on)
    {
        Settings.Topmost = on;
        ApplySettings();
    }

    internal void SetAutonomous(bool on)
    {
        Settings.Autonomous = on;
        ApplySettings();
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
        ApplySettings();
    }

    internal void ReloadSkin()
    {
        var atlas = LoadAtlas(Settings.Skin);
        if (atlas == null) return;
        Pet.UseSkin(atlas);
        ApplySettings(save: false);
    }

    internal void Summon()
    {
        _userHidden = false;
        _fullscreenHidden = false;
        UpdateVisibility();
        Win32.GetCursorPos(out var p);
        Pet.DropAt(p.X, p.Y);
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
              + "Clawd 会跟着 Claude 干活，Claude 不在眼前时在头顶显示进度。\n"
              + "hooks 在后台运行，不会拖慢 Claude；已有配置会保留，修改前会自动备份。";
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
        _watch?.Stop();
        _presenter?.Close();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _tray = null;
        _instance.Dispose();
        Log.Write("quit");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
