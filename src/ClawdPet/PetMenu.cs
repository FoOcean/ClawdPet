using System.Windows.Controls;
using ClawdPet.Core.Behaviors;
using ClawdPet.Core.Integrations;
using ClawdPet.Hosting;

namespace ClawdPet;

/// <summary>The right-click / tray menu. Rebuilt every time it opens so checks are always current.</summary>
static class PetMenu
{
    public static ContextMenu Build(App app)
    {
        var engine = app.Pet.Engine;
        var user = new BehaviorArgs { Source = BehaviorSource.User };
        var menu = new ContextMenu();

        var actions = new MenuItem { Header = "动作" };
        foreach (var b in engine.Context.Registry.All.Where(b => b.MenuLabel != null))
            actions.Items.Add(Item(b.MenuLabel!, () => engine.Brain.Request(b.Name, user, force: true)));
        actions.Items.Add(new Separator());
        var clips = new MenuItem { Header = "全部动画" };
        foreach (var clip in engine.Atlas.Clips.Keys)
            clips.Items.Add(Item(clip, () => engine.Brain.Request("play", user with { Clip = clip }, force: true)));
        actions.Items.Add(clips);
        menu.Items.Add(actions);

        var size = new MenuItem { Header = "大小" };
        for (int s = 1; s <= 8; s++)
        {
            int scale = s;
            size.Items.Add(Item($"{s}×", () => app.SetScale(scale), app.Settings.Scale == s));
        }
        menu.Items.Add(size);

        var skins = app.Paths.SkinNames().ToList();
        if (skins.Count > 1)
        {
            var skinMenu = new MenuItem { Header = "皮肤" };
            foreach (var name in skins)
                skinMenu.Items.Add(Item(name, () => app.SetSkin(name),
                    string.Equals(app.Settings.Skin, name, StringComparison.OrdinalIgnoreCase)));
            menu.Items.Add(skinMenu);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("自由活动", () => app.SetAutonomous(!app.Settings.Autonomous), app.Settings.Autonomous));
        menu.Items.Add(Item("总在最前", () => app.SetTopmost(!app.Settings.Topmost), app.Settings.Topmost));
        if (app.Paths.Sandboxed)
            menu.Items.Add(Item("开机启动（测试实例不可用）", () => { }, false, enabled: false));
        else
            menu.Items.Add(Item("开机启动", () => app.SetAutostart(!Autostart.IsEnabled()), Autostart.IsEnabled()));

        var agents = new MenuItem { Header = "Agent 接入" };
        var claudeState = app.Claude.State();
        agents.Items.Add(Item(claudeState switch
        {
            HookState.Installed => "Claude Code（已接入，点击移除）",
            HookState.Outdated => "Claude Code（需要更新，点击重新接入）",
            _ => "Claude Code（点击一键接入）",
        }, app.ToggleClaudeCode, claudeState == HookState.Installed));
        agents.Items.Add(Item("打开 Claude Code 配置位置", app.OpenClaudeSettings));
        menu.Items.Add(agents);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("召回到鼠标处", app.Summon));
        menu.Items.Add(Item(app.Pet.IsVisible ? "隐藏" : "显示", app.ToggleVisible));
        menu.Items.Add(Item("重新加载皮肤", app.ReloadSkin));
        menu.Items.Add(Item("打开数据文件夹", app.OpenDataFolder));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("退出", app.Quit));
        return menu;
    }

    static MenuItem Item(string header, Action click, bool? check = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (check is { } c) { item.IsCheckable = true; item.IsChecked = c; }
        item.Click += (_, _) => click();
        return item;
    }
}
