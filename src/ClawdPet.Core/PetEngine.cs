using ClawdPet.Core.Animation;
using ClawdPet.Core.Behaviors;
using ClawdPet.Core.Behaviors.Builtin;
using ClawdPet.Core.Sprites;
using ClawdPet.Core.World;

namespace ClawdPet.Core;

public sealed class PetOptions
{
    /// <summary>Wander, think, nap… on its own. When off the pet idles until told otherwise.</summary>
    public bool Autonomous { get; set; } = true;
    /// <summary>After this long without interaction, napping becomes likely.</summary>
    public double SleepAfterMs { get; set; } = 3 * 60_000;
}

/// <summary>Everything a behavior can see and touch.</summary>
public sealed class PetContext
{
    internal PetContext(SpriteAtlas atlas, IPetWorld world, PetOptions options, BehaviorRegistry registry)
    {
        Atlas = atlas;
        World = world;
        Options = options;
        Registry = registry;
        Animator = new Animator(atlas);
        Brain = new PetBrain(this);
    }

    public SpriteAtlas Atlas { get; }
    public Animator Animator { get; }
    public PetBody Body { get; } = new();
    public PetPointer Pointer { get; } = new();
    public IPetWorld World { get; }
    public PetOptions Options { get; }
    public BehaviorRegistry Registry { get; }
    public PetBrain Brain { get; }
    public Random Random { get; } = new();
    public double SinceInteractionMs { get; set; }
}

/// <summary>UI-agnostic pet: sprite atlas + animator + behavior brain. The host calls <see cref="Tick"/>.</summary>
public sealed class PetEngine
{
    public PetEngine(SpriteAtlas atlas, IPetWorld world, PetOptions? options = null, BehaviorRegistry? registry = null)
    {
        Context = new PetContext(atlas, world, options ?? new PetOptions(), registry ?? BuiltinBehaviors.CreateRegistry());
        Context.Animator.FrameEntered += (clip, i) => Context.Brain.Current?.OnFrame(clip, i);
        Context.Animator.Completed += clip => Context.Brain.Current?.OnClipCompleted(clip);
    }

    public PetContext Context { get; }
    public PetBrain Brain => Context.Brain;
    public Animator Animator => Context.Animator;
    public PetBody Body => Context.Body;
    public SpriteAtlas Atlas => Context.Atlas;

    /// <summary>Switches to a behavior right away (later non-forced requests queue behind it if it is non-interruptible).</summary>
    public void Start(string behavior, BehaviorArgs? args = null)
    {
        Brain.Request(behavior, args, force: true);
        Brain.ApplyNow();
    }

    public void Tick(double dtMs)
    {
        Context.SinceInteractionMs += dtMs;
        Brain.Update(dtMs);
        Animator.Update(dtMs);
    }

    public void Input(PetInputKind kind) => Brain.HandleInput(new PetInput(kind));

    /// <summary>Runs a text command such as "work", "think 30", "play wave", "done".</summary>
    public string Command(string line) => PetCommands.Execute(this, line);
}
