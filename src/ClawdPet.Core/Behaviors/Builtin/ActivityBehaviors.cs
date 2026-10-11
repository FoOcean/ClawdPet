using ClawdPet.Core.Animation;

namespace ClawdPet.Core.Behaviors.Builtin;

/// <summary>Watches the Claude Code spinner (✻) twinkle above its head.</summary>
public sealed class ThinkBehavior : PetBehavior
{
    double _duration;

    protected override void OnEnter()
    {
        Play("think");
        _duration = Args.DurationMs ?? (Args.Source == BehaviorSource.Command ? 30 * 60_000 : Rand(4000, 9000));
    }

    protected override void OnUpdate(double dtMs)
    {
        if (ElapsedMs >= _duration) Complete();
    }
}

/// <summary>
/// Pulls out a laptop on its left, turns 3/4 toward it and types for a while, then puts it away.
/// laptop_out → typing (loop) → laptop_away.
/// </summary>
public sealed class WorkBehavior : PetBehavior
{
    /// <summary>Sprite pixels the laptop sticks out to the left of the anchor.</summary>
    public const int LaptopReachArt = 30;

    enum Phase { Out, Typing, Away }
    Phase _phase;
    double _duration, _typingSince;

    protected override void OnEnter()
    {
        // Make room for the laptop on the left before getting to work.
        var s = Ctx.World.SurfaceAt(Body.X, Body.Y);
        double shortBy = s.Left - (Body.X - LaptopReachArt * Body.Scale);
        if (shortBy > 0 && Ctx.Brain.Request("walk", new BehaviorArgs
            {
                Direction = 1,
                DistanceArt = shortBy / Body.Scale + 4,
                Then = Name,
                ThenArgs = Args,
                Source = Args.Source,
            }, force: true))
        {
            Then = null;
            Complete();
            return;
        }

        _duration = Args.DurationMs ?? (Args.Source == BehaviorSource.Command ? 30 * 60_000 : Rand(8000, 20000));
        _phase = Phase.Out;
        if (!Play("laptop_out", loop: false, restart: true)) StartTyping();
    }

    void StartTyping()
    {
        _phase = Phase.Typing;
        _typingSince = ElapsedMs;
        Play("typing");
    }

    void PutAway()
    {
        if (_phase == Phase.Away) return;
        _phase = Phase.Away;
        if (!Play("laptop_away", loop: false, restart: true)) Complete();
    }

    protected override void OnUpdate(double dtMs)
    {
        if (_phase == Phase.Typing && ElapsedMs - _typingSince >= _duration) PutAway();
    }

    public override void OnClipCompleted(AnimationClip clip)
    {
        if (_phase == Phase.Out) StartTyping();
        else if (_phase == Phase.Away) Complete();
    }

    protected override void OnFinishRequested() => PutAway();

    /// <summary>A click asks for attention: tidy the laptop away, then hop.</summary>
    public override bool OnInput(PetInput input)
    {
        if (input.Kind != PetInputKind.Click) return false;
        Then ??= "happy";
        PutAway();
        return true;
    }
}

/// <summary>Dozes off (sleep_start), sleeps with floating z's, wakes on click / command / timeout.</summary>
public sealed class SleepBehavior : PetBehavior
{
    enum Phase { Dozing, Sleeping, Waking }
    Phase _phase;
    double _duration, _sleepingSince;

    protected override void OnEnter()
    {
        _duration = Args.DurationMs ?? (Args.Source == BehaviorSource.Auto ? Rand(30_000, 90_000) : 60 * 60_000);
        _phase = Phase.Dozing;
        if (!Play("sleep_start", loop: false, restart: true)) StartSleeping();
    }

    void StartSleeping()
    {
        _phase = Phase.Sleeping;
        _sleepingSince = ElapsedMs;
        Play("sleep");
    }

    void Wake()
    {
        if (_phase == Phase.Waking) return;
        _phase = Phase.Waking;
        if (!Play("wake", loop: false, restart: true)) Complete();
    }

    protected override void OnUpdate(double dtMs)
    {
        if (_phase == Phase.Sleeping && ElapsedMs - _sleepingSince >= _duration) Wake();
    }

    public override void OnClipCompleted(AnimationClip clip)
    {
        if (_phase == Phase.Dozing) StartSleeping();
        else if (_phase == Phase.Waking) Complete();
    }

    protected override void OnFinishRequested() => Wake();

    public override bool OnInput(PetInput input)
    {
        if (input.Kind != PetInputKind.Click) return false;
        Wake();
        return true;
    }
}
