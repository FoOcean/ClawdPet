# ClawdPet — Claude Code 的 Clawd 像素桌宠

Windows 桌面宠物。Clawd 站在任务栏上走来走去、眨眼、思考（头顶是 Claude Code 的 ✻ spinner）、掏出笔记本敲代码、打瞌睡；可以点它、拖它、把它甩出去。

| 待机 | 走路 | 敲代码 | 思考 |
| :---: | :---: | :---: | :---: |
| ![idle](docs/idle.gif) | ![walk](docs/walk_right.gif) | ![typing](docs/typing.gif) | ![think](docs/think.gif) |
| **开心** | **睡觉** | **被拎起来** | **挥手** |
| ![happy](docs/happy.gif) | ![sleep](docs/sleep.gif) | ![drag](docs/drag.gif) | ![wave](docs/wave.gif) |

> 非官方粉丝作品，与 Anthropic 无关。Clawd、Claude 及 Claude Code 相关名称与形象归 Anthropic 所有。

- **一张像素图**：整个角色（包括笔记本、Zzz、爱心等特效）都画在同一张精灵图 `clawd.png` 里，逐帧播放，运行时不拆部件、不做补间。放大只用整数倍最近邻，像素始终是清晰的方块。
- **还原原版**：比例取自 Claude Code 欢迎界面的 Clawd（`▐▛███▜▌` 象限块字符画）：身体 12×4、眼睛 1×1 个字符象限，换算成方形像素后放大 2 倍即身体 24×16、眼睛 2×4、手 4×4、腿 2×4，颜色 `rgb(215,119,87)`。图标就是 1:1 的原版 16×10 Clawd。敲电脑动作按 Claude 应用里的样子画：Clawd 转成 3/4 侧身（右侧面深一号）、左手搭在键盘上，电脑在左侧只露出侧面的屏幕线，不遮挡身体。

## 下载

到 [Releases](https://github.com/FoOcean/ClawdPet/releases) 下载，解压后运行 `ClawdPet.exe`（Windows 10/11 x64）：

- `ClawdPet-<版本>-win-x64.zip`：自带运行时，解压即用。
- `ClawdPet-<版本>-win-x64-net10.zip`：体积小，需要先装 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)。

从源码运行：

```bash
dotnet run --project src/ClawdPet
```

| 操作 | 效果 |
| --- | --- |
| 单击 | 开心地跳一下（睡觉时点它会醒；敲代码时点它会收起电脑再跳） |
| 拖动 | 拎起来，腿在空中晃；松手会按甩出的速度飞出去、撞墙反弹、落地压扁 |
| 鼠标停在 Clawd 上 | 头顶显示 Claude 当前的进度（见下） |
| 右键 / 托盘右键 | 设置、动作、大小、皮肤、自由活动、总在最前、开机启动、Agent 接入、召回、隐藏、退出 |
| 托盘双击 | 召回到鼠标所在屏幕 |
| `Ctrl+Alt+Shift+C` | 显示 / 隐藏 Clawd（可在设置里改） |

全屏玩游戏、看视频、放幻灯片时，同一屏幕上的 Clawd 会自动躲起来。

## 设置

右键 → **设置…** 打开设置窗口（跟随 Windows 浅色 / 深色）：

| 页面 | 内容 |
| --- | --- |
| 通用 | 大小、活泼程度、自由活动、多久打瞌睡、总在最前、全屏时隐藏、快捷键、开机启动 |
| Claude Code | 一键接入、进度气泡什么时候显示 / 显示哪些内容、Clawd 的反应、完成提示音、当前会话列表 |
| 动作 | 逐个开关 Clawd 会自己做的事，每个都能"试试" |
| 外观 | 皮肤选择与预览、皮肤文件夹 |
| 关于 | 版本、数据文件夹、日志 |

## 命令行 / 遥控

已经在运行时，再次调用 `ClawdPet.exe <命令>` 会通过命名管道（仅当前用户）把命令交给正在运行的桌宠并打印回复，约 0.1 秒返回（不加载界面）；没在运行时会在后台启动一个再执行该命令。

```bash
ClawdPet.exe work          # 掏出电脑敲代码，直到下一条命令（work 30 = 敲 30 秒）
ClawdPet.exe think         # 盯着头顶的 ✻ 思考
ClawdPet.exe done          # 收起手头的事，然后开心地跳一下
ClawdPet.exe attention     # 头顶冒 "!"，然后挥手
ClawdPet.exe sleep | wake | wave | happy | walk left | look
ClawdPet.exe play typing 5 # 播放精灵图里任意动画标签（给秒数则循环）
ClawdPet.exe status | sessions | summon | scale 4 | reload | quit
ClawdPet.exe settings claude # 打开设置的某一页
ClawdPet.exe agent install   # 一键接入 Claude Code（见下）
```

## Claude Code 实时进度

Claude 在工作、而你把它最小化（或切到别的窗口，看设置）时，Clawd 头顶会出现一个 Claude Code 风格的气泡：

- 正在做什么：`✻ 思考中…` / 正在运行的命令 / 正在编辑的文件 / `! 等你确认`，以及本轮耗时和输出 token
- 计划进度（Claude 列待办时）、你的提问或它最新的回复
- 工具调用次数、上下文大小、花费；同时开着几个会话时列出其它会话
- **点气泡直接跳回那个 Claude 窗口**；右键可以让这一轮不再显示
- 完成后会一直显示"完成"，直到你回到 Claude 窗口

Clawd 也会跟着动：你发消息它掏出电脑敲代码，Claude 需要你确认时冒 "!"，完成时收工跳一下；还可以开完成提示音。

数据来自两处：

1. **对话记录**：Claude Code 把每个会话实时写进 `~/.claude/projects/*/*.jsonl`，ClawdPet 只读地跟踪最近在写的那些。不接入 hooks 也能用，Claude 桌面版的会话能自动对上窗口。
2. **hooks（推荐）**：接入后事件来得更及时，hook 还会沿着进程树找到会话所在的窗口（Claude 桌面版、Windows Terminal、VS Code…），"最小化时显示"就能对上任何终端里的会话。

### 接入 Claude Code

**一键接入**：设置 → Claude Code → **一键接入**（或右键 → Agent 接入 → Claude Code），然后重新打开 Claude Code 会话。命令行：`ClawdPet.exe agent install`（`agent status` 查看、`agent uninstall` 移除）。

接入会在 `SessionStart`、`UserPromptSubmit`、`Notification`、`PreCompact`、`Stop`、`StopFailure`、`SessionEnd` 上各加一个 hook：`"…/ClawdPet.exe" --quiet hook`，并且是 `async`（后台运行，不会拖慢 Claude）。它把 hook 的输入 JSON 加上窗口信息转给正在运行的 Clawd，怎么反应由 Clawd 和设置决定。

它会修改 Claude Code 的用户配置 `~/.claude/settings.json`（设置了 `CLAUDE_CONFIG_DIR` 时用那个目录）：

- 只增删命令里带 `ClawdPet.exe` 的 hook，你自己的 hooks 和其它设置原样保留；重复接入不会重复添加。
- 每次修改前把原文件备份为 `settings.json.clawdpet-backup-<时间>`。
- 移动了 ClawdPet 的位置、或是旧版本的接入，设置里会显示"需要更新"，点一下就好。
- 配置文件里有注释时不会自动修改（重写会丢掉注释），请参照下面手动添加。

手动配置等价于（每个事件一条，路径换成你的）：

```json
{
  "hooks": {
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "\"C:/path/to/ClawdPet.exe\" --quiet hook", "async": true, "timeout": 15 }] }],
    "Stop":             [{ "hooks": [{ "type": "command", "command": "\"C:/path/to/ClawdPet.exe\" --quiet hook", "async": true, "timeout": 15 }] }]
  }
}
```

要接入其它 agent，参照 `src/ClawdPet.Core/Integrations/ClaudeCodeHooks.cs`（纯 JSON 变换，有单测）和 `src/ClawdPet/Hosting/ClaudeCodeIntegration.cs`（读写文件、备份）再写一个；会话状态的解析在 `src/ClawdPet.Core/Agents/`。

## 结构

```
tools/SpriteGen/          精灵图生成器：按姿势参数逐帧画出整张 clawd.png + 图集 JSON + 图标 + 预览
  ClawdAnimations.cs        ← 所有动画的帧、时长、特效（美术在这里改）
  Clawd.cs                  画师：正面 / 3/4 侧身 Clawd、笔记本、字形
src/ClawdPet.Core/        与 UI 无关的桌宠引擎（可单测，可移植到别的 UI）
  Sprites/SpriteAtlas.cs    读 Aseprite 格式的图集（frames + frameTags + "anchor" 切片的 pivot = 脚底点）
  Animation/Animator.cs     按每帧自己的时长播放；FrameEntered / Completed 事件
  Behaviors/                行为状态机：PetBrain 一次只跑一个 PetBehavior
    Builtin/                idle / walk / work / think / sleep / drag / fall / 通用 ClipBehavior …
  PetCommands.cs            文本命令 → 行为
  Agents/                   Claude Code 会话跟踪：transcript 增量读取 + hook 事件 → 会话状态
  Integrations/             Claude Code hooks 的增删（纯 JSON 变换）
src/ClawdPet/             WPF 宿主：透明置顶窗口、整数倍放大、拖拽、菜单、托盘、单实例 + 命名管道
  Program.cs                入口；命令行和 hook 走不加载 WPF 的快速路径
  UI/ProgressBubble.cs      头顶的进度气泡；UI/AgentPresenter.cs 决定什么时候显示
  UI/Settings/              设置窗口：SettingsPages（页面注册表）+ SettingsKit（行、开关、滑块…）
  Assets/Skins/clawd/       clawd.png + clawd.json（唯一的角色图）
tests/ClawdPet.Core.Tests/ 引擎测试（用真实精灵图）
```

行走是"踩帧移动"：每出现一帧走路帧就整像素地挪一步，所以是像素游戏那种一步一步的走，而不是平滑滑动。

## 扩展

**加一个动画**（两种方式，都只改那一张图）

1. 在 `tools/SpriteGen/ClawdAnimations.cs` 里加一行 `new("dance", Dance())` 并写出帧，然后
   `dotnet run --project tools/SpriteGen` 重新生成 `clawd.png / clawd.json`，打开 `artifacts/sprite-preview/preview.html` 看效果。
2. 或者用 Aseprite 直接画：导出 Sprite Sheet 时选 JSON Data（Array 或 Hash）、勾选 Tags 和 Slices；加一个名为 `anchor` 的切片，pivot 放在脚底中点。

新标签不用写代码就能播放：右键 → 动作 → 全部动画，或 `ClawdPet.exe play dance`。

**加一个行为**：继承 `PetBehavior`，在 `OnEnter` 里 `Play(...)`，结束时 `Complete()`；可重写 `OnFrame`（按帧移动）、`OnClipCompleted`（串联阶段）、`OnInput`（点击/拖拽）、`OnFinishRequested`（优雅收尾）。然后在 `BuiltinBehaviors.CreateRegistry()` 里注册：

```csharp
.Register(new("dance", () => new ClipBehavior("dance"))
{
    MenuLabel = "跳舞",          // 出现在右键菜单
    AutoWeight = _ => 1,         // 自由活动时被随机选中的权重
    Aliases = ["party"],         // 命令别名
})
```

**加一页设置**：在 `UI/Settings/SettingsPages.cs` 的 `All` 里加一项，用 `SettingsKit` 拼出来，改动会自动应用并保存：

```csharp
new("fun", "好玩的", "", k => k.Page("好玩的",
    k.Section("彩蛋", k.Row("开启彩蛋", "说明文字", k.Toggle(k.Settings.Fun, v => k.Settings.Fun = v)))))
```

**换皮肤**：把 `<名字>\<名字>.json + .png` 放到 `%APPDATA%\ClawdPet\skins\`，设置 → 外观里会出现。

**接入别的程序**：任何能写命名管道或调用命令行的东西（脚本、IDE 插件、CI 通知）都能用上面的命令驱动它。

## 开发

```bash
dotnet run --project tools/SpriteGen     # 重新生成精灵图、图标、预览
dotnet test tests/ClawdPet.Core.Tests    # 引擎测试
```

设置 `CLAWDPET_HOME=<目录>` 可以把配置、日志、单实例锁都隔离到别的目录，测试实例不会碰你正在用的桌宠和开机启动项（也不会改真正的 Claude Code 配置，而是写 `<目录>\claude\settings.json`）。数据默认在 `%APPDATA%\ClawdPet`（`settings.json`、`clawdpet.log`）。

- `CLAWDPET_CLAUDE_SETTINGS` / `CLAWDPET_CLAUDE_PROJECTS`：指定接入时改哪个 settings.json、去哪里找对话记录
- `ClawdPet.exe snapshot settings <页面> <png路径>`：把设置页渲染成图片，方便检查界面

后续可做：坐在窗口标题栏上、跟随鼠标、更多皮肤（帽子、节日装）、接入更多 agent。
