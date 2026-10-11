namespace ClawdPet.Core.Behaviors;

/// <summary>
/// Runs one behavior at a time. Requests are applied at the next tick so behaviors never get
/// swapped out from under themselves mid-callback.
/// </summary>
public sealed class PetBrain
{
    readonly PetContext _ctx;
    (BehaviorInfo Info, BehaviorArgs Args)? _pending;
    (BehaviorInfo Info, BehaviorArgs Args)? _queued;

    internal PetBrain(PetContext ctx) => _ctx = ctx;

    public PetBehavior? Current { get; private set; }
    public event Action<PetBehavior>? BehaviorChanged;

    /// <summary>
    /// Asks for a behavior. Unless <paramref name="force"/>, a non-interruptible current behavior
    /// (being dragged, falling) keeps running and the request waits until it completes.
    /// </summary>
    public bool Request(string name, BehaviorArgs? args = null, bool force = false)
    {
        if (!_ctx.Registry.TryGet(name, out var info)) return false;
        var entry = (info, args ?? BehaviorArgs.Default);
        if (!force && Current is { Interruptible: false, IsDone: false }) _queued = entry;
        else _pending = entry;
        return true;
    }

    /// <summary>Gracefully ends the current behavior (e.g. "done" from a command).</summary>
    public void FinishCurrent(string? then = null)
    {
        if (Current is null) return;
        if (!Current.Interruptible) { if (then != null) Request(then); return; }
        Current.RequestFinish(then);
    }

    public void HandleInput(PetInput input)
    {
        _ctx.SinceInteractionMs = 0;
        if (Current?.OnInput(input) == true) return;
        var user = new BehaviorArgs { Source = BehaviorSource.User };
        switch (input.Kind)
        {
            case PetInputKind.DragStart: Request("drag", user, force: true); break;
            case PetInputKind.Click: Request("happy", user, force: true); break;
        }
    }

    public void Update(double dtMs)
    {
        ApplyPending();
        if (Current != null && (Current.IsDone || Current.Tick(dtMs)))
            ChooseNext();
        ApplyPending();
        KeepOnGround();
    }

    /// <summary>Weighted random pick among behaviors that have an auto weight.</summary>
    public string PickAutonomous()
    {
        if (!_ctx.Options.Autonomous) return "idle";
        var weighted = _ctx.Registry.All
            .Select(b => (b.Name, W: b.AutoWeight?.Invoke(_ctx) ?? 0))
            .Where(x => x.W > 0)
            .ToList();
        double total = weighted.Sum(x => x.W);
        if (total <= 0) return "idle";
        double r = _ctx.Random.NextDouble() * total;
        foreach (var (name, w) in weighted)
            if ((r -= w) < 0) return name;
        return weighted[^1].Name;
    }

    void ChooseNext()
    {
        var finished = Current!;
        if (_pending != null) return;
        if (_queued is { } q) { _queued = null; _pending = q; return; }
        if (finished.Then is { } then && Request(then, finished.Args.Then == then ? finished.Args.ThenArgs : null, force: true))
            return;
        string next = string.Equals(finished.Name, "idle", StringComparison.OrdinalIgnoreCase) ? PickAutonomous() : "idle";
        if (!Request(next, force: true)) Request("idle", force: true);
    }

    internal void ApplyNow() => ApplyPending();

    void ApplyPending()
    {
        if (_pending is not { } p) return;
        _pending = null;
        var next = _ctx.Registry.Create(p.Info);
        Current?.Exit();
        Current = next;
        next.Enter(_ctx, p.Args);
        BehaviorChanged?.Invoke(next);
    }

    void KeepOnGround()
    {
        if (Current is not { NeedsGround: true } || _pending != null) return;
        var body = _ctx.Body;
        var surface = _ctx.World.SurfaceAt(body.X, body.Y);
        body.X = surface.ClampX(body.X, body.HalfWidth);
        if (body.Y < surface.Floor - 1)
            Request("fall", force: true);
        else
            body.Y = surface.Floor;
    }
}
