using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClawdPet.Hosting;

namespace ClawdPet.UI.Settings;

/// <summary>
/// Building blocks for settings pages, so a new feature can add its page in a few lines:
/// <code>kit.Page("标题", kit.Section("分组", kit.Row("名称", "说明", kit.Toggle(v, set))))</code>
/// Every change goes through <see cref="Changed"/>, which applies and saves the settings.
/// </summary>
sealed class SettingsKit(App app, Window owner)
{
    public App App { get; } = app;
    public PetSettings Settings => App.Settings;
    public Window Owner { get; } = owner;

    /// <summary>Apply + save after a setting changed.</summary>
    public void Changed() => App.ApplySettings();

    public UIElement Page(string title, params UIElement[] sections)
    {
        var panel = new StackPanel { Margin = new Thickness(28, 20, 28, 28), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        foreach (var s in sections) panel.Children.Add(s);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public UIElement Section(string? header, params UIElement[] rows)
    {
        var stack = new StackPanel();
        if (header != null)
            stack.Children.Add(new TextBlock { Text = header, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(2, 10, 0, 8) });
        var list = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            var row = new Border { Padding = new Thickness(16, 12, 16, 12), Child = rows[i] };
            if (i > 0)
            {
                row.BorderThickness = new Thickness(0, 1, 0, 0);
                row.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            }
            list.Children.Add(row);
        }
        var card = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = list, Margin = new Thickness(0, 0, 0, 6) };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        stack.Children.Add(card);
        return stack;
    }

    public UIElement Row(string label, string? description, UIElement? control)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = label, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (description != null) text.Children.Add(Note(description));
        grid.Children.Add(text);
        if (control is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(fe, 1);
            grid.Children.Add(fe);
        }
        return grid;
    }

    public TextBlock Note(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return t;
    }

    public CheckBox Toggle(bool value, Action<bool> set, bool enabled = true)
    {
        var box = new CheckBox { IsChecked = value, IsEnabled = enabled, MinWidth = 0 };
        box.Click += (_, _) => { set(box.IsChecked == true); Changed(); };
        return box;
    }

    public UIElement Slider(int min, int max, int value, Action<int> set, Func<int, string> label)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 180, IsSnapToTickEnabled = true, TickFrequency = 1 };
        var text = new TextBlock { Text = label(value), Width = 72, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        slider.ValueChanged += (_, e) =>
        {
            int v = (int)Math.Round(e.NewValue);
            text.Text = label(v);
            set(v);
            Changed();
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(text);
        panel.Children.Add(new Border { Width = 10 });
        panel.Children.Add(slider);
        return panel;
    }

    public ComboBox Choice<T>(IEnumerable<(T Value, string Label)> options, T current, Action<T> set)
    {
        var items = options.ToList();
        var box = new ComboBox { MinWidth = 180 };
        foreach (var (_, label) in items) box.Items.Add(label);
        box.SelectedIndex = Math.Max(0, items.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, current)));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            set(items[box.SelectedIndex].Value);
            Changed();
        };
        return box;
    }

    public Button Button(string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text, MinWidth = 96, Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(6, 0, 0, 0) };
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        b.Click += (_, _) => click();
        return b;
    }

    public StackPanel Buttons(params UIElement[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    /// <summary>A text block refreshed every <paramref name="every"/> while the window is open.</summary>
    public TextBlock Live(Func<string> text, TimeSpan every, bool secondary = false)
    {
        var t = new TextBlock { Text = text(), TextWrapping = TextWrapping.Wrap, FontSize = secondary ? 12 : 14 };
        if (secondary) t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        var timer = new DispatcherTimer { Interval = every };
        timer.Tick += (_, _) => t.Text = text();
        t.Loaded += (_, _) => timer.Start();
        t.Unloaded += (_, _) => timer.Stop();
        return t;
    }

    /// <summary>Segoe Fluent Icons glyph (falls back to MDL2 on Windows 10).</summary>
    public static TextBlock Glyph(string glyph, double size = 16) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
