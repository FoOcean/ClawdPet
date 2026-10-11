using ClawdPet.Core.Sprites;

namespace ClawdPet.Core.Animation;

public readonly record struct ClipFrame(int AtlasIndex, int DurationMs);

/// <summary>A named run of atlas frames, already expanded for its playback direction.</summary>
public sealed class AnimationClip
{
    public string Name { get; }
    public IReadOnlyList<ClipFrame> Frames { get; }
    public int TotalMs { get; }

    public AnimationClip(string name, IReadOnlyList<ClipFrame> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("A clip needs at least one frame", nameof(frames));
        Name = name;
        Frames = frames;
        TotalMs = frames.Sum(f => f.DurationMs);
    }

    /// <param name="direction">Aseprite tag direction: forward, reverse, pingpong, pingpong_reverse.</param>
    public static AnimationClip FromRange(string name, IReadOnlyList<AtlasFrame> atlas, int from, int to, string direction)
    {
        var forward = Enumerable.Range(from, to - from + 1).ToList();
        List<int> order = direction.ToLowerInvariant() switch
        {
            "reverse" => Enumerable.Reverse(forward).ToList(),
            "pingpong" => PingPong(forward),
            "pingpong_reverse" => PingPong(Enumerable.Reverse(forward).ToList()),
            _ => forward,
        };
        return new AnimationClip(name, order.Select(i => new ClipFrame(i, atlas[i].DurationMs)).ToList());
    }

    static List<int> PingPong(List<int> seq)
    {
        var r = new List<int>(seq);
        for (int i = seq.Count - 2; i >= 1; i--) r.Add(seq[i]);
        return r;
    }
}
