using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ClawdPet.Core.Agents;
using ClawdPet.Native;

namespace ClawdPet.UI;

/// <summary>What the bubble shows; built from an <see cref="AgentSession"/> by the presenter.</summary>
sealed record BubbleView(
    string Title,
    string? Corner,
    AgentActivity Activity,
    string Status,
    string? Elapsed,
    IReadOnlyList<PlanItem>? Plan,
    string? Text,
    string? Stats,
    IReadOnlyList<string> Others,
    bool CanJump);

/// <summary>
/// Speech bubble above Clawd with a Claude Code-style status line (✻ spinner, elapsed time),
/// plan progress, the latest prompt/reply and a few numbers. Square corners, a 2-px Claude-orange
/// frame and a stepped pixel tail, to match the sprite.
/// </summary>
sealed class ProgressBubble : Window
{
    static readonly Brush Bg = Frozen(0xF21F1E1D);
    static readonly Brush Frame = Frozen(0xFFD77757);
    static readonly Brush Fg = Frozen(0xFFECE9E1);
    static readonly Brush Muted = Frozen(0xFF9A968C);
    static readonly Brush Ok = Frozen(0xFF7DC27D);
    static readonly Brush Warn = Frozen(0xFFF2B53A);
    static readonly Brush Err = Frozen(0xFFE5484D);
    static readonly Brush Info = Frozen(0xFF8DBDE6);
    static readonly Brush PlanTodo = Frozen(0xFF3A3836);
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Microsoft YaHei UI");
    static readonly string[] SpinnerGlyphs = ["·", "✢", "✳", "✶", "✻", "✽", "✻", "✶", "✳", "✢"];

    readonly TextBlock _title = new() { Foreground = Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _corner = new() { Foreground = Muted, FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
    readonly Run _glyph = new(), _status = new(), _elapsed = new() { Foreground = Muted };
    readonly TextBlock _statusLine = new() { FontSize = 13, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly StackPanel _planBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock _planText = new() { Foreground = Muted, FontSize = 11, Margin = new Thickness(6, -2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _text = new() { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock _stats = new() { Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel _others = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly Canvas _tail = new() { Height = 6, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _card;
    readonly DockPanel _planRow;
    IntPtr _hwnd;
    int _spin;
    AgentActivity _activity;
    double _tailX = 40;

    public ProgressBubble()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Top = -10000;
        FontFamily = Mono;
        Foreground = Fg;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var header = new DockPanel();
        DockPanel.SetDock(_corner, Dock.Right);
        header.Children.Add(_corner);
        header.Children.Add(_title);
        _statusLine.Inlines.Add(_glyph);
        _statusLine.Inlines.Add(new Run(" "));
        _statusLine.Inlines.Add(_status);
        _statusLine.Inlines.Add(new Run("  "));
        _statusLine.Inlines.Add(_elapsed);

        _planRow = new DockPanel();
        DockPanel.SetDock(_planBar, Dock.Left);
        _planRow.Children.Add(_planBar);
        _planRow.Children.Add(_planText);

        var body = new StackPanel();
        foreach (var child in new UIElement[] { header, _statusLine, _planRow, _text, _stats, _others }) body.Children.Add(child);
        _card = new Border
        {
            Background = Bg,
            BorderBrush = Frame,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(10, 7, 10, 8),
            MinWidth = 230,
            MaxWidth = 340,
            Child = body,
            Cursor = Cursors.Hand,
        };
        var root = new StackPanel();
        root.Children.Add(_card);
        root.Children.Add(_tail);
        Content = root;

        _card.MouseLeftButtonUp += (_, e) => { e.Handled = true; Clicked?.Invoke(); };
        _card.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            var dismiss = new MenuItem { Header = "这一轮不再显示" };
            dismiss.Click += (_, _) => DismissRequested?.Invoke();
            var settings = new MenuItem { Header = "气泡设置…" };
            settings.Click += (_, _) => SettingsRequested?.Invoke();
            menu.Items.Add(dismiss);
            menu.Items.Add(settings);
            menu.IsOpen = true;
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)(ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE));
        };
    }

    public event Action? Clicked;
    public event Action? DismissRequested;
    public event Action? SettingsRequested;

    public void Show(BubbleView v)
    {
        _activity = v.Activity;
        _title.Text = v.Title;
        _corner.Text = v.Corner ?? "";
        _status.Text = v.Status;
        _status.Foreground = v.Activity switch
        {
            AgentActivity.Waiting => Warn,
            AgentActivity.Done => Ok,
            AgentActivity.Failed => Err,
            AgentActivity.Idle => Muted,
            _ => Fg,
        };
        _elapsed.Text = v.Elapsed ?? "";
        UpdateGlyph();

        _planRow.Visibility = v.Plan is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        if (v.Plan is { Count: > 0 } plan)
        {
            _planBar.Children.Clear();
            foreach (var item in plan.Take(12))
                _planBar.Children.Add(new Rectangle
                {
                    Width = 6, Height = 6, Margin = new Thickness(0, 0, 2, 0),
                    Fill = item.IsDone ? Frame : item.IsActive ? Warn : PlanTodo,
                });
            var active = plan.FirstOrDefault(p => p.IsActive) ?? plan.FirstOrDefault(p => !p.IsDone);
            _planText.Text = $"{plan.Count(p => p.IsDone)}/{plan.Count}" + (active != null ? $" · {active.Text}" : "");
        }
        Set(_text, v.Text);
        Set(_stats, v.Stats);
        _others.Children.Clear();
        foreach (var o in v.Others)
            _others.Children.Add(new TextBlock { Text = o, Foreground = Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        _others.Visibility = v.Others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _card.ToolTip = v.CanJump ? "点击切换到 Claude" : null;
        if (!IsVisible) Show();
    }

    /// <summary>Advances the spinner; call every ~120 ms while visible.</summary>
    public void Animate()
    {
        _spin = (_spin + 1) % SpinnerGlyphs.Length;
        UpdateGlyph();
    }

    void UpdateGlyph()
    {
        (_glyph.Text, _glyph.Foreground) = _activity switch
        {
            AgentActivity.Waiting => ("!", Warn),
            AgentActivity.Done => ("✓", Ok),
            AgentActivity.Failed => ("✗", Err),
            AgentActivity.Idle => ("·", Muted),
            AgentActivity.Compacting => (SpinnerGlyphs[_spin], Info),
            _ => (SpinnerGlyphs[_spin], Frame),
        };
    }

    /// <summary>
    /// Puts the tail tip at (<paramref name="tipX"/>, <paramref name="tipY"/>) in physical pixels,
    /// keeping the card inside [<paramref name="left"/>, <paramref name="right"/>].
    /// </summary>
    public void PlaceTip(int tipX, int tipY, double left, double right)
    {
        if (_hwnd == IntPtr.Zero) return;
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        double w = ActualWidth * dpi.DpiScaleX, h = ActualHeight * dpi.DpiScaleY;
        double x = Math.Clamp(tipX - w / 2, left + 4, Math.Max(left + 4, right - w - 4));
        double tail = Math.Clamp((tipX - x) / dpi.DpiScaleX, 12, ActualWidth - 12);
        if (Math.Abs(tail - _tailX) > 0.5) DrawTail(tail);
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(tipY - h), 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    // Stepped pixel tail under the card: two rows of frame + fill, then a 2-px point.
    void DrawTail(double center)
    {
        _tailX = center;
        _tail.Children.Clear();
        _tail.Width = ActualWidth;
        void Px(double x, double y, double w, Brush b)
        {
            var r = new Rectangle { Width = w, Height = 2, Fill = b };
            Canvas.SetLeft(r, Math.Round(center + x));
            Canvas.SetTop(r, y);
            _tail.Children.Add(r);
        }
        Px(-5, -2, 10, Bg);              // open the card's bottom border
        Px(-7, 0, 2, Frame); Px(-5, 0, 10, Bg); Px(5, 0, 2, Frame);
        Px(-5, 2, 2, Frame); Px(-3, 2, 6, Bg); Px(3, 2, 2, Frame);
        Px(-3, 4, 6, Frame);
    }

    static void Set(TextBlock t, string? s)
    {
        t.Text = s ?? "";
        t.Visibility = string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
    }

    static SolidColorBrush Frozen(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }
}
