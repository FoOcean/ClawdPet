using System.Text.Json;
using ClawdPet.Core.Animation;

namespace ClawdPet.Core.Sprites;

public readonly record struct PixelRect(int X, int Y, int W, int H);

public readonly record struct PixelPoint(int X, int Y);

/// <param name="Source">Where the frame's pixels live in the sheet.</param>
/// <param name="Placement">Where those pixels land inside the untrimmed frame (Aseprite's spriteSourceSize).</param>
public sealed record AtlasFrame(string Name, PixelRect Source, PixelRect Placement, int DurationMs);

/// <summary>
/// A sprite sheet described by an Aseprite-style JSON atlas ("Array" or "Hash" export, frame tags,
/// optional "anchor" slice whose pivot marks the ground point under the character's feet).
/// The whole character lives in one image; animations are just ranges of frames.
/// </summary>
public sealed class SpriteAtlas
{
    public string JsonPath { get; }
    public string ImagePath { get; }
    public int FrameWidth { get; }
    public int FrameHeight { get; }
    /// <summary>Ground point in frame coordinates; the pet's position is this point.</summary>
    public PixelPoint Anchor { get; }
    public IReadOnlyList<AtlasFrame> Frames { get; }
    public IReadOnlyDictionary<string, AnimationClip> Clips { get; }

    SpriteAtlas(string jsonPath, string imagePath, int fw, int fh, PixelPoint anchor,
        IReadOnlyList<AtlasFrame> frames, IReadOnlyDictionary<string, AnimationClip> clips)
    {
        JsonPath = jsonPath; ImagePath = imagePath; FrameWidth = fw; FrameHeight = fh;
        Anchor = anchor; Frames = frames; Clips = clips;
    }

    public bool HasClip(string name) => Clips.ContainsKey(name);

    public static SpriteAtlas Load(string jsonPath)
    {
        jsonPath = Path.GetFullPath(jsonPath);
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath), new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;
        var meta = root.GetProperty("meta");

        var frames = new List<AtlasFrame>();
        var framesEl = root.GetProperty("frames");
        if (framesEl.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var f in framesEl.EnumerateArray())
                frames.Add(ReadFrame(f.TryGetProperty("filename", out var n) ? n.GetString() ?? $"{i}" : $"{i}", f, i++));
        }
        else
        {
            int i = 0;
            foreach (var p in framesEl.EnumerateObject())
                frames.Add(ReadFrame(p.Name, p.Value, i++));
        }
        if (frames.Count == 0) throw new InvalidDataException($"{jsonPath}: atlas has no frames");

        int fw = frames.Max(f => f.Placement.X + f.Placement.W);
        int fh = frames.Max(f => f.Placement.Y + f.Placement.H);
        if (framesEl.ValueKind == JsonValueKind.Array && framesEl[0].TryGetProperty("sourceSize", out var ss))
        {
            fw = ss.GetProperty("w").GetInt32();
            fh = ss.GetProperty("h").GetInt32();
        }

        var anchor = new PixelPoint(fw / 2, fh);
        if (meta.TryGetProperty("slices", out var slices))
            foreach (var s in slices.EnumerateArray())
            {
                if (s.GetProperty("name").GetString() is not ("anchor" or "ground")) continue;
                var key = s.GetProperty("keys")[0];
                var b = key.GetProperty("bounds");
                int px = b.GetProperty("w").GetInt32() / 2, py = b.GetProperty("h").GetInt32();
                if (key.TryGetProperty("pivot", out var pv))
                {
                    px = pv.GetProperty("x").GetInt32();
                    py = pv.GetProperty("y").GetInt32();
                }
                anchor = new PixelPoint(b.GetProperty("x").GetInt32() + px, b.GetProperty("y").GetInt32() + py);
                break;
            }

        var clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        if (meta.TryGetProperty("frameTags", out var tags))
            foreach (var t in tags.EnumerateArray())
            {
                string name = t.GetProperty("name").GetString() ?? "";
                int from = Math.Clamp(t.GetProperty("from").GetInt32(), 0, frames.Count - 1);
                int to = Math.Clamp(t.GetProperty("to").GetInt32(), from, frames.Count - 1);
                string dir = t.TryGetProperty("direction", out var d) ? d.GetString() ?? "forward" : "forward";
                clips[name] = AnimationClip.FromRange(name, frames, from, to, dir);
            }
        if (clips.Count == 0)
            clips["default"] = AnimationClip.FromRange("default", frames, 0, frames.Count - 1, "forward");

        string image = meta.GetProperty("image").GetString() ?? throw new InvalidDataException("meta.image missing");
        string imagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(jsonPath)!, image));
        return new SpriteAtlas(jsonPath, imagePath, fw, fh, anchor, frames, clips);
    }

    static AtlasFrame ReadFrame(string name, JsonElement f, int index)
    {
        var src = ReadRect(f.GetProperty("frame"));
        var placement = f.TryGetProperty("spriteSourceSize", out var sss) ? ReadRect(sss) : new PixelRect(0, 0, src.W, src.H);
        int duration = f.TryGetProperty("duration", out var d) ? d.GetInt32() : 100;
        return new AtlasFrame(name, src, placement, duration > 0 ? duration : 100);
    }

    static PixelRect ReadRect(JsonElement e) => new(
        e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32(),
        e.GetProperty("w").GetInt32(), e.GetProperty("h").GetInt32());
}
