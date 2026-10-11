using ClawdPet.Core.Animation;

namespace ClawdPet.Core.Behaviors.Builtin;

/// <summary>Breathes and blinks; after a few seconds hands over to an autonomous pick.</summary>
public sealed class IdleBehavior : PetBehavior
{
    double _duration, _nextBlink;

    protected override void OnEnter()
    {
        Play("idle");
        _duration = Args.DurationMs ?? Rand(3000, 8000);
        _nextBlink = Rand(1200, 4500);
    }

    protected override void OnUpdate(double dtMs)
    {
        bool resting = Ctx.Animator.IsPlaying("idle");
        if (resting && ElapsedMs >= _nextBlink)
        {
            Play("blink", loop: false, restart: true);
            _nextBlink = ElapsedMs + Rand(1800, 5200);
        }
        else if (resting && ElapsedMs >= _duration && Ctx.Options.Autonomous)
        {
            Complete();
        }
    }

    public override void OnClipCompleted(AnimationClip clip) => Play("idle", restart: true);
}

/// <summary>
/// Plays one animation tag once (or loops it for <see cref="BehaviorArgs.DurationMs"/>) and finishes.
/// Any new tag drawn into the sheet can be played through this with no code changes.
/// </summary>
public sealed class ClipBehavior(string? clip = null, bool swallowClicks = false) : PetBehavior
{
    bool _loop;

    protected override void OnEnter()
    {
        _loop = Args.DurationMs is not null;
        if (!Play(Args.Clip ?? clip ?? "idle", loop: _loop, restart: true)) Complete();
    }

    protected override void OnUpdate(double dtMs)
    {
        if (_loop && ElapsedMs >= Args.DurationMs) Complete();
    }

    public override void OnClipCompleted(AnimationClip c) => Complete();

    public override bool OnInput(PetInput input) => swallowClicks && input.Kind == PetInputKind.Click;
}
