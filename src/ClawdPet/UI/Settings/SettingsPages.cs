using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClawdPet.Core.Agents;
using ClawdPet.Core.Behaviors;
using ClawdPet.Core.Integrations;
using ClawdPet.Hosting;

namespace ClawdPet.UI.Settings;

/// <summary>The settings pages, in navigation order. Add new features' pages here.</summary>
static class SettingsPages
{
    public static readonly IReadOnlyList<SettingsPage> All =
    [
        new("general", "通用", "", General),
        new("claude", "Claude Code", "", Claude),
        new("behaviors", "动作", "", Behaviors),
        new("appearance", "外观", "", Appearance),
        new("about", "关于", "", About),
    ];

    static readonly string[] LivelinessNames = ["", "很安静", "安静", "正常", "活泼", "很活泼"];

    static UIElement General(SettingsKit k)
    {
        var s = k.Settings;
        bool sandbox = k.App.Paths.Sandboxed;
        return k.Page("通用",
            k.Section("Clawd",
                k.Row("大小", "每个像素放大的倍数（会再乘上 Windows 缩放比例）",
                    k.Slider(1, 8, s.Scale, v => s.Scale = v, v => $"{v}×")),
                k.Row("活泼程度", "影响它闲着多久才去做别的事",
                    k.Slider(1, 5, s.Liveliness, v => s.Liveliness = v, v => LivelinessNames[v])),
                k.Row("自由活动", "自己走来走去、敲代码、东张西望；关闭后只在原地待机",
                    k.Toggle(s.Autonomous, v => s.Autonomous = v)),
                k.Row("没人理它时打瞌睡", null,
                    k.Choice([(1.0, "1 分钟后"), (3.0, "3 分钟后"), (5.0, "5 分钟后"), (10.0, "10 分钟后"), (30.0, "30 分钟后"), (0.0, "从不")],
                        s.SleepAfterMinutes, v => s.SleepAfterMinutes = v))),
            k.Section("窗口",
                k.Row("总在最前", null, k.Toggle(s.Topmost, v => s.Topmost = v)),
                k.Row("全屏时自动隐藏", "玩游戏、看视频、放幻灯片时，同一屏幕上的 Clawd 暂时躲起来",
                    k.Toggle(s.HideInFullscreen, v => s.HideInFullscreen = v)),
                k.Row("显示 / 隐藏快捷键", "点击后按下新的组合键；Esc 取消，Backspace 清除", HotkeyBox(k)),
                k.Row("开机启动", sandbox ? "测试实例（设置了 CLAWDPET_HOME）不会修改开机启动" : null,
                    k.Toggle(!sandbox && Autostart.IsEnabled(), v => k.App.SetAutostart(v), enabled: !sandbox))));
    }

    static UIElement HotkeyBox(SettingsKit k)
    {
        var s = k.Settings;
        var status = k.Note("");
        var button = new Button { MinWidth = 150, Padding = new Thickness(12, 5, 12, 6) };
        bool recording = false;
        void Refresh()
        {
            button.Content = recording ? "请按下组合键…" : string.IsNullOrEmpty(s.Hotkey) ? "未设置" : s.Hotkey;
            status.Text = !string.IsNullOrEmpty(s.Hotkey) && !k.App.HotkeyRegistered ? "已被其它程序占用，换一个吧" : "";
        }
        button.Click += (_, _) => { recording = true; Refresh(); button.Focus(); };
        button.LostKeyboardFocus += (_, _) => { recording = false; Refresh(); };
        button.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { recording = false; Refresh(); return; }
            if (key is Key.Back or Key.Delete) { s.Hotkey = ""; recording = false; k.Changed(); Refresh(); return; }
            if (Hotkey.Describe(Keyboard.Modifiers, key) is { } spec)
            {
                s.Hotkey = spec;
                recording = false;
                k.Changed();
                Refresh();
            }
        };
        Refresh();
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(button);
        panel.Children.Add(status);
        return panel;
    }

    static UIElement Claude(SettingsKit k)
    {
        var c = k.Settings.Claude;
        var claude = k.App.Claude;
        var result = k.Note("");

        string StateText() => claude.State() switch
        {
            HookState.Installed => $"已接入 · {ClaudeCodeHooks.Events.Count} 个事件 · 后台运行，不会拖慢 Claude",
            HookState.Outdated => "需要更新（ClawdPet 换了位置，或是旧版本的接入）",
            _ => "未接入",
        };
        var actionButton = k.Button("", () => { }, accent: true);
        void RefreshButton() => actionButton.Content = claude.State() switch
        {
            HookState.Installed => "移除",
            HookState.Outdated => "更新",
            _ => "一键接入",
        };
        actionButton.Click += (_, _) =>
        {
            try { result.Text = claude.State() == HookState.Installed ? claude.Uninstall() : claude.Install(); }
            catch (Exception ex) { result.Text = "没有修改配置：" + ex.Message; }
            RefreshButton();
        };
        RefreshButton();

        var hookRow = new StackPanel();
        hookRow.Children.Add(k.Row("Claude Code hooks", null, k.Buttons(actionButton, k.Button("打开配置位置", k.App.OpenClaudeSettings))));
        hookRow.Children.Add(k.Live(StateText, TimeSpan.FromSeconds(1), secondary: true));
        hookRow.Children.Add(k.Note($"修改 {claude.SettingsPath}：只增删 ClawdPet 自己的 hook，其它配置原样保留，修改前自动备份。接入后重新打开 Claude Code 会话生效。"));
        hookRow.Children.Add(result);

        return k.Page("Claude Code",
            k.Section("接入", hookRow,
                k.Row("没接入时也读取对话记录", "直接跟踪 ~/.claude/projects 里正在写入的会话；接入 hooks 后更及时，还能知道会话在哪个窗口",
                    k.Toggle(c.DiscoverSessions, v => c.DiscoverSessions = v))),
            k.Section("进度气泡",
                k.Row("什么时候显示", "Claude 工作时，在 Clawd 头顶显示它正在做什么；点气泡回到 Claude",
                    k.Choice([("minimized", "Claude 最小化时"), ("background", "Claude 不在前台时"), ("always", "一直显示"), ("never", "从不")],
                        c.BubbleWhen, v => c.BubbleWhen = v)),
                k.Row("鼠标停在 Clawd 上时显示", null, k.Toggle(c.HoverPeek, v => c.HoverPeek = v)),
                k.Row("计划进度", "Claude 列出待办时显示完成情况", k.Toggle(c.ShowPlan, v => c.ShowPlan = v)),
                k.Row("最新的提问 / 回复", null, k.Toggle(c.ShowText, v => c.ShowText = v)),
                k.Row("统计", "工具调用次数、上下文大小、花费", k.Toggle(c.ShowStats, v => c.ShowStats = v)),
                k.Row("其它会话", "同时有多个会话时列出其余的", k.Toggle(c.ShowOtherSessions, v => c.ShowOtherSessions = v))),
            k.Section("Clawd 的反应",
                k.Row("发消息时掏出电脑敲代码", null, k.Toggle(c.ReactTyping, v => c.ReactTyping = v)),
                k.Row("完成时开心地跳一下", null, k.Toggle(c.ReactDone, v => c.ReactDone = v)),
                k.Row("需要你确认时冒出 \"!\"", null, k.Toggle(c.ReactAttention, v => c.ReactAttention = v)),
                k.Row("完成提示音", "只在 Claude 不在眼前时响", k.Toggle(c.CompletionSound, v => c.CompletionSound = v))),
            k.Section("当前会话", k.Live(() => SessionsText(k.App), TimeSpan.FromSeconds(1))));
    }

    static string SessionsText(App app)
    {
        var now = DateTime.UtcNow;
        var lines = app.Hub.Ranked().Take(8).Select(s =>
        {
            string state = s.Activity switch
            {
                AgentActivity.Thinking => "思考中",
                AgentActivity.Responding => "回复中",
                AgentActivity.Tool => s.Detail ?? "使用工具",
                AgentActivity.Waiting => "等你确认",
                AgentActivity.Compacting => "整理上下文",
                AgentActivity.Done => "完成",
                AgentActivity.Failed => "出错",
                _ => "空闲",
            };
            string time = s.Elapsed(now) is { } t && s.Activity != AgentActivity.Idle ? " · " + AgentPresenter.Format(t) : "";
            string where = s.Host is { } h ? $" · {h.Process}" : s.Entrypoint is { } e ? $" · {e}" : "";
            return $"{(s.IsWorking ? "●" : "○")} {s.DisplayName} — {state}{time}{where}";
        }).ToList();
        return lines.Count > 0 ? string.Join("\n", lines) : "最近没有 Claude Code 会话";
    }

    static UIElement Behaviors(SettingsKit k)
    {
        var s = k.Settings;
        var engine = k.App.Pet.Engine;
        var rows = new List<UIElement>();
        foreach (var b in engine.Context.Registry.All.Where(b => b.MenuLabel != null && b.AutoWeight != null))
        {
            string name = b.Name;
            var toggle = k.Toggle(!s.DisabledBehaviors.Contains(name, StringComparer.OrdinalIgnoreCase), on =>
            {
                s.DisabledBehaviors.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                if (!on) s.DisabledBehaviors.Add(name);
            });
            var tryIt = k.Button("试试", () => k.App.Pet.Engine.Brain.Request(name, new BehaviorArgs { Source = BehaviorSource.User }, force: true));
            rows.Add(k.Row(b.MenuLabel!, null, k.Buttons(tryIt, new Border { Width = 12 }, toggle)));
        }
        return k.Page("动作",
            k.Section("它会自己做的事", [k.Note("关掉的动作不会自己做，但右键菜单、命令行和 Claude 联动里仍然可以触发。"), .. rows]));
    }

    static UIElement Appearance(SettingsKit k)
    {
        var s = k.Settings;
        var preview = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        void Preview(string skin) => preview.Source = k.App.SkinPreview(skin);
        Preview(s.Skin);

        var skins = k.App.Paths.SkinNames().Select(n => (n, n)).ToList();
        var picker = k.Choice(skins, s.Skin, v => { k.App.SetSkin(v); Preview(v); });
        var skinRow = new StackPanel();
        skinRow.Children.Add(k.Row("皮肤", "放进皮肤文件夹的 Aseprite 图集（.json + .png）会出现在这里", picker));
        skinRow.Children.Add(preview);
        return k.Page("外观",
            k.Section("角色", skinRow,
                k.Row("皮肤文件夹", null, k.Buttons(
                    k.Button("打开", () => { Directory.CreateDirectory(k.App.Paths.UserSkinsDir); Open(k.App.Paths.UserSkinsDir); }),
                    k.Button("重新加载", k.App.ReloadSkin)))));
    }

    static UIElement About(SettingsKit k)
    {
        var version = typeof(SettingsPages).Assembly.GetName().Version?.ToString(3) ?? "?";
        var link = new Hyperlink(new Run("github.com/FoOcean/ClawdPet")) { NavigateUri = new Uri("https://github.com/FoOcean/ClawdPet") };
        link.RequestNavigate += (_, e) => Open(e.Uri.AbsoluteUri);
        var linkText = new TextBlock(link);
        return k.Page("关于",
            k.Section(null,
                k.Row($"ClawdPet {version}", "Claude Code 的 Clawd 像素桌宠", linkText),
                k.Row("数据文件夹", k.App.Paths.Home, k.Buttons(k.Button("打开", k.App.OpenDataFolder))),
                k.Row("日志", null, k.Buttons(k.Button("打开", () => Open(k.App.Paths.LogFile))))),
            k.Note("非官方粉丝作品，与 Anthropic 无关。Clawd、Claude 及 Claude Code 相关名称与形象归 Anthropic 所有。"));
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"open {target}: {ex.Message}"); }
    }
}
