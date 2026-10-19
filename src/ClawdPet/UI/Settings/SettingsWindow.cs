using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClawdPet.UI.Settings;

/// <summary>A settings page: key (for deep links), nav title, Segoe Fluent Icons glyph, and its builder.</summary>
sealed record SettingsPage(string Key, string Title, string Glyph, Func<SettingsKit, UIElement> Build);

/// <summary>
/// Settings window: a navigation list on the left and the selected page on the right. Pages come
/// from <see cref="SettingsPages.All"/>; adding a feature's settings = adding one entry there.
/// Uses WPF's built-in Fluent theme so it follows Windows light/dark mode.
/// </summary>
sealed class SettingsWindow : Window
{
    readonly SettingsKit _kit;
    readonly ListBox _nav = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(8, 8, 8, 8) };
    readonly ContentControl _content = new();

    public SettingsWindow(App app)
    {
#pragma warning disable WPF0001 // Fluent theme
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        Title = "ClawdPet 设置";
        Width = 880;
        Height = 640;
        MinWidth = 680;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (File.Exists(app.Paths.IconFile)) Icon = BitmapFrame.Create(new Uri(app.Paths.IconFile));
        _kit = new SettingsKit(app, this);

        foreach (var page in SettingsPages.All)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6), Tag = page };
            item.Children.Add(SettingsKit.Glyph(page.Glyph));
            item.Children.Add(new TextBlock { Text = page.Title, Margin = new Thickness(12, 0, 0, 0), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            _nav.Items.Add(item);
        }
        _nav.SelectionChanged += (_, _) =>
        {
            if (_nav.SelectedItem is FrameworkElement { Tag: SettingsPage page }) _content.Content = page.Build(_kit);
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(_nav);
        Grid.SetColumn(_content, 1);
        grid.Children.Add(_content);
        Content = grid;
        _nav.SelectedIndex = 0;
    }

    /// <summary>Renders the window's content to a PNG (DPI-independent; for visual checks).</summary>
    public void Snapshot(string path)
    {
        UpdateLayout();
        if (Content is not FrameworkElement root) return;
        int w = (int)Math.Ceiling(root.ActualWidth), h = (int)Math.Ceiling(root.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // The Fluent theme paints the window with a system backdrop, so use its base fill instead.
            var bg = new[] { "SolidBackgroundFillColorBaseBrush", "ApplicationBackgroundBrush" }
                .Select(TryFindResource).OfType<Brush>().FirstOrDefault() ?? Background ?? Brushes.Gray;
            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        png.Save(fs);
    }

    public void ShowPage(string? key)
    {
        if (key == null) return;
        for (int i = 0; i < _nav.Items.Count; i++)
            if (_nav.Items[i] is FrameworkElement { Tag: SettingsPage p } && p.Key == key) _nav.SelectedIndex = i;
    }
}
