using ClawdPet.Core.Sprites;

namespace ClawdPet.Core.Animation;

/// <summary>Plays one clip at a time, frame by frame, honouring each frame's own duration.</summary>
public sealed class Animator
{
    readonly SpriteAtlas _atlas;
    double _elapsedMs;

    public Animator(SpriteAtlas atlas) => _atlas = atlas;

    public AnimationClip? Clip { get; private set; }
    public bool Loop { get; private set; }
    /// <summary>Index into <see cref="AnimationClip.Frames"/>.</summary>
    public int FrameIndex { get; private set; }
    public int AtlasFrame => Clip?.Frames[FrameIndex].AtlasIndex ?? 0;
    public bool IsFinished { get; private set; }
    public double Speed { get; set; } = 1.0;

    /// <summary>Raised whenever a frame becomes visible (including frame 0 on <see cref="Play"/>).</summary>
    public event Action<AnimationClip, int>? FrameEntered;
    /// <summary>Raised when a looping clip wraps around.</summary>
    public event Action<AnimationClip>? Looped;
    /// <summary>Raised when a non-looping clip has shown its last frame for its full duration.</summary>
    public event Action<AnimationClip>? Completed;

    public bool Has(string clip) => _atlas.HasClip(clip);

    /// <summary>Starts a clip. Re-playing the clip that is already running is a no-op unless <paramref name="restart"/>.</summary>
    /// <returns>false when the atlas has no such clip.</returns>
    public bool Play(string clip, bool loop = true, bool restart = false)
    {
        if (!_atlas.Clips.TryGetValue(clip, out var c)) return false;
        if (!restart && ReferenceEquals(c, Clip) && Loop == loop && !IsFinished) return true;
        Clip = c;
        Loop = loop;
        FrameIndex = 0;
        _elapsedMs = 0;
        IsFinished = false;
        FrameEntered?.Invoke(c, 0);
        return true;
    }

    public bool IsPlaying(string clip) =>
        Clip is not null && !IsFinished && string.Equals(Clip.Name, clip, StringComparison.OrdinalIgnoreCase);

    public void Update(double dtMs)
    {
        if (Clip is null || IsFinished) return;
        _elapsedMs += dtMs * Speed;
        for (int guard = 0; guard < 256; guard++)
        {
            var clip = Clip;
            int duration = clip.Frames[FrameIndex].DurationMs;
            if (_elapsedMs < duration) return;
            _elapsedMs -= duration;

            if (FrameIndex + 1 < clip.Frames.Count)
            {
                FrameIndex++;
                FrameEntered?.Invoke(clip, FrameIndex);
            }
            else if (Loop)
            {
                FrameIndex = 0;
                Looped?.Invoke(clip);
                if (!ReferenceEquals(clip, Clip)) return;
                FrameEntered?.Invoke(clip, 0);
            }
            else
            {
                IsFinished = true;
                _elapsedMs = 0;
                Completed?.Invoke(clip);
                return;
            }
            // A handler switched clips; the new clip starts fresh.
            if (!ReferenceEquals(clip, Clip)) return;
        }
        _elapsedMs = 0;
    }
}
