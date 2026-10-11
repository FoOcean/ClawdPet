using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClawdPet.Core;
using ClawdPet.Core.Behaviors;
using ClawdPet.Core.Sprites;
using ClawdPet.Hosting;
using ClawdPet.Native;
using ClawdPet.Rendering;

namespace ClawdPet;

/// <summary>
/// The pet: a borderless, transparent, topmost window that shows one whole sprite frame at a time.
/// Transparent pixels are click-through (layered window), so only Clawd itself catches the mouse.
/// The window is moved in physical pixels so the anchor (feet) lands exactly on the floor.
/// </summary>
sealed class PetWindow : Window
{
    readonly Image _image = new() { Stretch = Stretch.None };
    readonly PetSettings _settings;
    readonly DesktopWorld _world;
    readonly DispatcherTimer _timer;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    SpriteFrames _frames = null!;
    long _lastTick;
    IntPtr _hwnd;
    double _dpi = 1;
    int _shownFrame = -1;
    (int X, int Y) _placed = (int.MinValue, int.MinValue);
    bool _pressed, _dragging;
    Win32.POINT _pressAt;

    public PetWindow(SpriteAtlas atlas, PetSettings settings, DesktopWorld world)
    {
        _settings = settings;
        _world = world;

        Title = "Clawd";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = settings.Topmost;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Top = -10000;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        Content = _image;

        UseSkin(atlas);

        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += (_, _) => Step();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)(ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE));
            _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            ApplyScale();
        };
        DpiChanged += (_, e) =>
        {
            _dpi = e.NewDpi.DpiScaleX;
            ApplyScale();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { _lastTick = _clock.ElapsedTicks; _timer.Start(); }
            else _timer.Stop();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Win32.GetCursorPos(out _pressAt);
            _pressed = true;
            _dragging = false;
            CaptureMouse();
        };
        MouseLeftButtonUp += (_, e) => { e.Handled = true; ReleasePointer(); };
        LostMouseCapture += (_, _) => ReleasePointer();
        MouseRightButtonUp += (_, e) => { e.Handled = true; MenuRequested?.Invoke(); };
    }

    public PetEngine Engine { get; private set; } = null!;
    public IntPtr Handle => _hwnd;

    public event Action? MenuRequested;
    public event Action<PetBehavior>? BehaviorChanged;

    /// <summary>Swaps in a (re)loaded sprite sheet, keeping the pet where it is.</summary>
    public void UseSkin(SpriteAtlas atlas)
    {
        var old = Engine;
        _frames = new SpriteFrames(atlas);
        Engine = new PetEngine(atlas, _world, new PetOptions
        {
            Autonomous = _settings.Autonomous,
            SleepAfterMs = _settings.SleepAfterMinutes * 60_000,
        });
        int idle = atlas.Clips.TryGetValue("idle", out var clip) ? clip.Frames[0].AtlasIndex : 0;
        Engine.Body.HalfWidthArt = _frames.OpaqueHalfWidth(idle);
        Engine.Brain.BehaviorChanged += b => BehaviorChanged?.Invoke(b);
        if (old != null)
        {
            Engine.Body.X = old.Body.X;
            Engine.Body.Y = old.Body.Y;
            Engine.Start("idle");
        }
        _shownFrame = -1;
        if (_hwnd != IntPtr.Zero) ApplyScale();
    }

    public void SetScale(int scale)
    {
        _settings.Scale = Math.Clamp(scale, 1, 12);
        ApplyScale();
    }

    /// <summary>First appearance: drop in where it was last time, else toward the right of the primary screen.</summary>
    public void Enter(double? x, double? y)
    {
        if (x is { } px && y is { } py && _world.IsOnScreen(px, py - 1))
        {
            DropAt(px, py);
            return;
        }
        var primary = _world.SurfaceAt(1, 2);
        DropAt(primary.Left + (primary.Right - primary.Left) * 0.8, primary.Floor);
    }

    /// <summary>Drops the pet onto the floor below (x, y) from a little height, then it waves.</summary>
    public void DropAt(double x, double y)
    {
        var surface = _world.SurfaceAt(x, y);
        int s = Engine.Body.Scale;
        Engine.Body.X = surface.ClampX(x, Engine.Body.HalfWidth);
        Engine.Body.Y = Math.Max(surface.Top + 40 * s, surface.Floor - 110 * s);
        Engine.Start("fall", new BehaviorArgs { Then = "wave" });
        Render();
    }

    void ApplyScale()
    {
        int scale = Math.Max(1, (int)Math.Round(_settings.Scale * _dpi, MidpointRounding.AwayFromZero));
        Engine.Body.Scale = scale;
        _frames.Configure(scale, _dpi);
        _shownFrame = -1;
        _placed = (int.MinValue, int.MinValue);
        Render();
    }

    void Step()
    {
        long now = _clock.ElapsedTicks;
        double dt = Math.Clamp((now - _lastTick) * 1000.0 / Stopwatch.Frequency, 0, 100);
        _lastTick = now;

        Win32.GetCursorPos(out var p);
        var pointer = Engine.Context.Pointer;
        pointer.X = p.X;
        pointer.Y = p.Y;
        pointer.IsDown = _pressed;

        if (_pressed && !_dragging)
        {
            double threshold = 4 * _dpi;
            if (Math.Abs(p.X - _pressAt.X) > threshold || Math.Abs(p.Y - _pressAt.Y) > threshold)
            {
                _dragging = true;
                Engine.Input(PetInputKind.DragStart);
            }
        }

        Engine.Tick(dt);
        Render();
    }

    void Render()
    {
        if (_hwnd == IntPtr.Zero) return;
        int frame = Engine.Animator.AtlasFrame;
        if (frame != _shownFrame)
        {
            _image.Source = _frames.Get(frame);
            _shownFrame = frame;
        }

        var anchor = Engine.Atlas.Anchor;
        int s = Engine.Body.Scale;
        int x = (int)Math.Round(Engine.Body.X) - anchor.X * s;
        int y = (int)Math.Round(Engine.Body.Y) - anchor.Y * s;
        if ((x, y) == _placed) return;
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        _placed = (x, y);
    }

    void ReleasePointer()
    {
        if (!_pressed) return;
        _pressed = false;
        bool wasDragging = _dragging;
        _dragging = false;
        Engine.Context.Pointer.IsDown = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Engine.Input(wasDragging ? PetInputKind.DragEnd : PetInputKind.Click);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
