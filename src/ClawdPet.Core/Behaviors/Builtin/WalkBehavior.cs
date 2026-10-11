using ClawdPet.Core.Animation;

namespace ClawdPet.Core.Behaviors.Builtin;

/// <summary>
/// Walks along the floor. Movement is tied to animation frames — each new walk frame moves the pet
/// a whole number of sprite pixels — so it steps like a sprite instead of sliding.
/// </summary>
public sealed class WalkBehavior : PetBehavior
{
    const int StepArt = 2;
    int _dir;
    double _remainingArt;

    protected override void OnEnter()
    {
        var s = Ctx.World.SurfaceAt(Body.X, Body.Y);
        double roomLeft = Body.X - (s.Left + Body.HalfWidth);
        double roomRight = s.Right - Body.HalfWidth - Body.X;
        double minRoom = 24 * Body.Scale;

        _dir = Args.Direction != 0 ? Math.Sign(Args.Direction)
            : roomLeft < minRoom ? 1
            : roomRight < minRoom ? -1
            : Ctx.Random.Next(2) == 0 ? -1 : 1;
        _remainingArt = Args.DistanceArt ?? Rand(40, 180);
        if (!Play(_dir > 0 ? "walk_right" : "walk_left")) Complete();
    }

    public override void OnFrame(AnimationClip clip, int frameIndex)
    {
        if (!clip.Name.StartsWith("walk", StringComparison.OrdinalIgnoreCase)) return;
        var s = Ctx.World.SurfaceAt(Body.X, Body.Y);
        double next = Body.X + _dir * StepArt * Body.Scale;
        if (next < s.Left + Body.HalfWidth || next > s.Right - Body.HalfWidth)
        {
            Complete();
            return;
        }
        Body.X = next;
        if ((_remainingArt -= StepArt) <= 0) Complete();
    }
}
