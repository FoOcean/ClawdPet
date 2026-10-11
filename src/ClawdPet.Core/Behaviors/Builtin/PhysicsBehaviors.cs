using ClawdPet.Core.Animation;

namespace ClawdPet.Core.Behaviors.Builtin;

/// <summary>Held by the mouse: follows the cursor, remembers recent motion so a release can throw.</summary>
public sealed class DragBehavior : PetBehavior
{
    readonly Queue<(double T, double X, double Y)> _samples = new();
    double _offX, _offY;
    bool _released;

    public override bool NeedsGround => false;
    public override bool Interruptible => false;

    protected override void OnEnter()
    {
        _offX = Body.X - Ctx.Pointer.X;
        _offY = Body.Y - Ctx.Pointer.Y;
        Body.Vx = Body.Vy = 0;
        Play("drag");
    }

    protected override void OnUpdate(double dtMs)
    {
        Body.X = Ctx.Pointer.X + _offX;
        Body.Y = Ctx.Pointer.Y + _offY;
        _samples.Enqueue((ElapsedMs, Body.X, Body.Y));
        while (_samples.Count > 2 && ElapsedMs - _samples.Peek().T > 90) _samples.Dequeue();
        if (!Ctx.Pointer.IsDown) Release();
    }

    public override bool OnInput(PetInput input)
    {
        if (input.Kind == PetInputKind.DragEnd) Release();
        return true;
    }

    void Release()
    {
        if (_released) return;
        _released = true;
        double vx = 0, vy = 0;
        if (_samples.Count >= 2)
        {
            var a = _samples.Peek();
            var b = _samples.Last();
            double dt = (b.T - a.T) / 1000.0;
            if (dt > 0.005) { vx = (b.X - a.X) / dt; vy = (b.Y - a.Y) / dt; }
        }
        Ctx.Brain.Request("fall", new BehaviorArgs { Vx = vx, Vy = vy, Source = BehaviorSource.User }, force: true);
    }
}

/// <summary>Falls (or flies, after a throw) under gravity, bounces off screen edges, lands with a squash.</summary>
public sealed class FallBehavior : PetBehavior
{
    // In sprite pixels per second (squared); multiplied by the body scale.
    const double Gravity = 900, Terminal = 700, MaxThrow = 1400;
    /// <summary>How far above the anchor the sprite's head reaches, for the ceiling.</summary>
    const double HeadroomArt = 26;

    bool _landed;

    public override bool NeedsGround => false;
    public override bool Interruptible => _landed;

    protected override void OnEnter()
    {
        double max = MaxThrow * Body.Scale;
        Body.Vx = Math.Clamp(Args.Vx, -max, max);
        Body.Vy = Math.Clamp(Args.Vy, -max, max);
        Play("fall");
    }

    protected override void OnUpdate(double dtMs)
    {
        if (_landed) return;
        double s = Math.Min(dtMs, 50) / 1000.0, k = Body.Scale;
        Body.Vy = Math.Min(Body.Vy + Gravity * k * s, Terminal * k);
        Body.Vx *= Math.Pow(0.4, s);
        Body.X += Body.Vx * s;
        Body.Y += Body.Vy * s;

        var surf = Ctx.World.SurfaceAt(Body.X, Math.Min(Body.Y, Ctx.World.SurfaceAt(Body.X, Body.Y).Floor - 1));
        double half = Body.HalfWidth;
        if (Body.X < surf.Left + half) { Body.X = surf.Left + half; Body.Vx = Math.Abs(Body.Vx) * 0.5; }
        if (Body.X > surf.Right - half) { Body.X = surf.Right - half; Body.Vx = -Math.Abs(Body.Vx) * 0.5; }
        if (Body.Y - HeadroomArt * k < surf.Top) { Body.Y = surf.Top + HeadroomArt * k; Body.Vy = Math.Abs(Body.Vy) * 0.3; }

        if (Body.Y >= surf.Floor)
        {
            Body.Y = surf.Floor;
            Body.Vx = Body.Vy = 0;
            _landed = true;
            if (!Play("land", loop: false, restart: true)) Complete();
        }
    }

    public override void OnClipCompleted(AnimationClip clip)
    {
        if (_landed) Complete();
    }

    public override bool OnInput(PetInput input) => !_landed && input.Kind == PetInputKind.Click;
}
