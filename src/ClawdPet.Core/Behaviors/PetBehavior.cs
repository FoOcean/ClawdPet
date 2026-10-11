using ClawdPet.Core.Animation;
using ClawdPet.Core.World;

namespace ClawdPet.Core.Behaviors;

public enum BehaviorSource { Auto, User, Command }

public enum PetInputKind { Click, DragStart, DragEnd }

public readonly record struct PetInput(PetInputKind Kind);

/// <summary>Parameters a behavior is started with. Behaviors read what they understand and ignore the rest.</summary>
public sealed record BehaviorArgs
{
    public static readonly BehaviorArgs Default = new();

    public BehaviorSource Source { get; init; } = BehaviorSource.Auto;
    /// <summary>How long a looping behavior should last; null = the behavior's own default.</summary>
    public double? DurationMs { get; init; }
    /// <summary>-1 left, +1 right, 0 = let the behavior decide.</summary>
    public int Direction { get; init; }
    /// <summary>How far to go, in sprite pixels (walk).</summary>
    public double? DistanceArt { get; init; }
    /// <summary>Animation tag for generic clip behaviors.</summary>
    public string? Clip { get; init; }
    /// <summary>Initial velocity in screen px/s (used by "fall" after a throw).</summary>
    public double Vx { get; init; }
    public double Vy { get; init; }
    /// <summary>Behavior to run after this one completes (instead of the default rule).</summary>
    public string? Then { get; init; }
    public BehaviorArgs? ThenArgs { get; init; }
}

/// <summary>
/// One thing the pet can be doing. The brain runs exactly one at a time; a behavior drives the
/// animator and the body and calls <see cref="Complete"/> when it is finished.
/// </summary>
public abstract class PetBehavior
{
    bool _done;

    /// <summary>Registry name, assigned when the behavior is created.</summary>
    public string Name { get; internal set; } = "";
    public BehaviorArgs Args { get; private set; } = BehaviorArgs.Default;
    /// <summary>What runs next; starts as <see cref="BehaviorArgs.Then"/> and may be overridden.</summary>
    public string? Then { get; protected set; }
    public bool IsDone => _done;

    /// <summary>When true the brain keeps the pet on the floor and starts "fall" if it is in the air.</summary>
    public virtual bool NeedsGround => true;
    /// <summary>When false, non-forced requests wait in a queue until this behavior completes.</summary>
    public virtual bool Interruptible => true;

    protected PetContext Ctx { get; private set; } = null!;
    protected PetBody Body => Ctx.Body;
    protected double ElapsedMs { get; private set; }

    internal void Enter(PetContext ctx, BehaviorArgs args)
    {
        Ctx = ctx;
        Args = args;
        Then = args.Then;
        ElapsedMs = 0;
        _done = false;
        OnEnter();
    }

    internal bool Tick(double dtMs)
    {
        ElapsedMs += dtMs;
        if (!_done) OnUpdate(dtMs);
        return _done;
    }

    internal void Exit() => OnExit();

    /// <summary>Asks the behavior to wrap up gracefully (e.g. put the laptop away first).</summary>
    public void RequestFinish(string? then = null)
    {
        if (then != null) Then = then;
        OnFinishRequested();
    }

    protected virtual void OnEnter() { }
    protected virtual void OnUpdate(double dtMs) { }
    protected virtual void OnExit() { }
    protected virtual void OnFinishRequested() => Complete();

    /// <summary>A frame of the current clip became visible.</summary>
    public virtual void OnFrame(AnimationClip clip, int frameIndex) { }
    /// <summary>A non-looping clip finished.</summary>
    public virtual void OnClipCompleted(AnimationClip clip) { }
    /// <summary>Return true to swallow the input; otherwise the brain's default reaction applies.</summary>
    public virtual bool OnInput(PetInput input) => false;

    protected void Complete() => _done = true;

    /// <summary>Plays a clip; falls back to "idle" when the skin doesn't have it.</summary>
    protected bool Play(string clip, bool loop = true, bool restart = false)
    {
        if (Ctx.Animator.Play(clip, loop, restart)) return true;
        Ctx.Animator.Play("idle");
        return false;
    }

    protected double Rand(double min, double max) => min + Ctx.Random.NextDouble() * (max - min);
}
