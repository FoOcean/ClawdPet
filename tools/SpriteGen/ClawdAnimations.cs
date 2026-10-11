namespace ClawdPet.SpriteGen;

/// <summary>
/// Art direction for every animation in the Clawd sheet. One row of the sheet per animation;
/// the row order here is the frame order in the exported atlas.
/// </summary>
static class ClawdAnimations
{
    static readonly Pose N = new();
    static readonly Pose Lying = new() { Dy = 2, H = 15, Legs = L4.All(2), Eyes = Eye.Closed, ArmL = 9, ArmR = 9 };

    static FrameDef F(Pose p, int ms, params Fx[] fx) => new(p, ms, fx);
    static Fx G(string[] glyph, int x, int y, uint c) => new(glyph, x, y, c);

    static readonly string[] DustBig = [".##.", "####"];
    static readonly string[] DustMid = ["##", "##"];
    static readonly string[] DustDot = Glyphs.Dot;

    public static IReadOnlyList<AnimDef> All()
    {
        var walk = Walk();
        return
        [
            new("idle", Idle()),
            new("blink", Blink()),
            new("look", Look()),
            new("walk_right", walk),
            new("walk_left", walk, Mirror: true),
            new("happy", Happy()),
            new("wave", Wave()),
            new("think", Think()),
            new("laptop_out", LaptopOut()),
            new("typing", Typing()),
            new("laptop_away", LaptopAway()),
            new("surprised", Surprised()),
            new("drag", Drag()),
            new("fall", Fall()),
            new("land", Land()),
            new("sleep_start", SleepStart()),
            new("sleep", Sleep()),
            new("wake", Wake()),
        ];
    }

    // Breathing with the arm nubs lagging one beat behind the body.
    static FrameDef[] Idle() =>
    [
        F(N, 550),
        F(N with { H = 15, ArmL = 7, ArmR = 7 }, 180),
        F(N with { H = 15, ArmL = 8, ArmR = 8 }, 550),
        F(N with { ArmL = 9, ArmR = 9 }, 180),
    ];

    static FrameDef[] Blink() =>
    [
        F(N with { Eyes = Eye.Half }, 50),
        F(N with { Eyes = Eye.Closed }, 80),
        F(N with { Eyes = Eye.Half }, 50),
    ];

    static FrameDef[] Look() =>
    [
        F(N with { EyeDx = -1 }, 90),
        F(N with { EyeDx = -2 }, 750),
        F(N with { EyeDx = -1 }, 90),
        F(N, 250),
        F(N with { EyeDx = 1 }, 90),
        F(N with { EyeDx = 2 }, 750),
        F(N with { EyeDx = 1 }, 90),
        F(N, 200),
    ];

    // Diagonal leg pairs step alternately; the body dips on the contact frames.
    static FrameDef[] Walk()
    {
        var w = N with { EyeDx = 2 };
        return
        [
            F(w with { Legs = new(3, 4, 3, 4), ArmL = 9, ArmR = 7 }, 120),
            F(w with { Dy = 1, Legs = L4.All(3) }, 120),
            F(w with { Legs = new(4, 3, 4, 3), ArmL = 7, ArmR = 9 }, 120),
            F(w with { Dy = 1, Legs = L4.All(3) }, 120),
        ];
    }

    static FrameDef[] Happy()
    {
        uint c = Palette.Heart;
        return
        [
            F(N with { Dy = 1, H = 14, W = 26, Legs = L4.All(3) }, 90),
            F(N with { Dy = -3, H = 17, W = 22, Eyes = Eye.Happy, ArmL = 3, ArmR = 3 }, 60),
            F(N with { Dy = -8, Eyes = Eye.Happy, ArmL = 3, ArmR = 3 }, 80, G(Glyphs.HeartSmall, 38, 15, c)),
            F(N with { Dy = -11, Eyes = Eye.Happy, ArmL = 2, ArmR = 2 }, 130, G(Glyphs.Heart, 37, 10, c)),
            F(N with { Dy = -11, Eyes = Eye.Happy, ArmL = 3, ArmR = 3 }, 90, G(Glyphs.Heart, 38, 7, c)),
            F(N with { Dy = -7, Eyes = Eye.Happy, ArmL = 5, ArmR = 5 }, 70, G(Glyphs.Heart, 38, 4, c)),
            F(N with { Dy = -2, H = 17, W = 22, Eyes = Eye.Happy, ArmL = 6, ArmR = 6 }, 60, G(Glyphs.HeartSmall, 40, 2, c)),
            F(N with { Dy = 1, H = 14, W = 26, Legs = L4.All(3), Eyes = Eye.Happy, Blush = true }, 110),
            F(N with { H = 15, Eyes = Eye.Happy, Blush = true }, 100),
            F(N with { Eyes = Eye.Happy, Blush = true }, 380),
        ];
    }

    static FrameDef[] Wave() =>
    [
        F(N with { Eyes = Eye.Happy, ArmR = 2 }, 140),
        F(N with { Eyes = Eye.Happy, ArmR = -2 }, 140),
        F(N with { Eyes = Eye.Happy, ArmR = 2 }, 140),
        F(N with { Eyes = Eye.Happy, ArmR = -2 }, 140),
        F(N with { Eyes = Eye.Happy, ArmR = 2 }, 160),
        F(N, 220),
    ];

    // Gazing up at the Claude Code spinner (· ✢ ✳ ✶ ✻ ✽ and back) while breathing slowly.
    static FrameDef[] Think()
    {
        var t = N with { EyeDx = 1, EyeDy = -1, ArmR = 5 };
        int[] seq = [0, 1, 2, 3, 4, 5, 4, 3, 2, 1];
        return seq.Select((g, i) =>
        {
            var pose = i < 5 ? t : t with { H = 15, ArmL = 7, ArmR = 4 };
            return F(pose, 130, G(Glyphs.Spinner[g], 36, 15, Palette.Spark));
        }).ToArray();
    }

    // Modelled on the typing Clawd in the Claude app: the laptop sits on Clawd's left seen side-on
    // (a thin base and a thin lid line), Clawd turns 3/4 toward it and reaches down to the keys —
    // nothing covers the body. Effect coordinates are 48-wide stage space (frame x - 8).
    static readonly Pose Typist = N with { Turned = true, Reach = 0, Laptop = Laptop.Open };

    static Fx Speck(int frameX, int y) => G(Glyphs.Dot, frameX - ClawdPainter.StageX, y, Palette.Poof);

    static FrameDef[] LaptopOut()
    {
        uint s = Palette.Poof;
        return
        [
            F(N with { EyeDx = -2, ArmL = 4 }, 160),
            F(N with { EyeDx = -2, ArmL = 2 }, 110, G(Glyphs.Sparkle, 0, 37, s), G(Glyphs.Sparkle, 7, 40, s)),
            F(N with { EyeDx = -2, Eyes = Eye.Happy, Laptop = Laptop.Closed }, 130, G(Glyphs.Sparkle, -4, 35, s), G(Glyphs.Sparkle, 9, 37, s)),
            F(Typist with { Reach = null, Laptop = Laptop.Closed }, 120),
            F(Typist with { Reach = 3, Laptop = Laptop.Half }, 100),
            F(Typist with { Reach = 3, Laptop = Laptop.Upright }, 90),
            F(Typist with { Reach = 2 }, 180),
            F(Typist with { Eyes = Eye.Happy }, 220),
        ];
    }

    // The reaching arm taps the keys (keystroke specks fly), with a little bob, a blink and a pause.
    static FrameDef[] Typing() =>
    [
        F(Typist, 100, Speck(12, 41)),
        F(Typist with { Reach = 2 }, 100),
        F(Typist, 100, Speck(15, 40)),
        F(Typist with { Reach = 1 }, 100),
        F(Typist, 100, Speck(11, 40)),
        F(Typist with { Reach = 2, H = 15 }, 110),
        F(Typist with { Reach = 2, H = 15, Eyes = Eye.Half }, 80),
        F(Typist with { Reach = 2, H = 15 }, 280),
        F(Typist, 100, Speck(14, 41)),
        F(Typist with { Reach = 2 }, 100),
        F(Typist, 100, Speck(12, 40)),
        F(Typist with { Reach = 2 }, 180),
    ];

    static FrameDef[] LaptopAway()
    {
        uint s = Palette.Poof;
        return
        [
            F(Typist with { Reach = 2 }, 140),
            F(Typist with { Reach = 3, Laptop = Laptop.Upright }, 90),
            F(Typist with { Reach = 3, Laptop = Laptop.Half }, 80),
            F(Typist with { Reach = null, Laptop = Laptop.Closed }, 140),
            F(N with { EyeDx = -2, Laptop = Laptop.Closed }, 130),
            F(N with { EyeDx = -2, ArmL = 4, Laptop = Laptop.Closed }, 130),
            F(N with { Eyes = Eye.Happy }, 120, G(Glyphs.Sparkle, -2, 38, s), G(Glyphs.Sparkle, 4, 35, s), G(Glyphs.Sparkle, 9, 40, s)),
            F(N with { Eyes = Eye.Happy, Blush = true }, 220),
        ];
    }

    static FrameDef[] Surprised() =>
    [
        F(N with { Dy = -2, H = 17, W = 22, Eyes = Eye.Wide, ArmL = 4, ArmR = 4 }, 90, G(Glyphs.Bang, 23, 14, Palette.Alert)),
        F(N with { Eyes = Eye.Wide }, 450, G(Glyphs.Bang, 23, 17, Palette.Alert)),
        F(N, 250, G(Glyphs.Bang, 23, 17, Palette.Alert)),
        F(N, 150),
    ];

    // Held by the cursor: stretched, arms up, legs dangling and swinging.
    static FrameDef[] Drag()
    {
        var d = N with { Dy = -1, H = 17, W = 22, Eyes = Eye.Squeeze, ArmL = 2, ArmR = 2, Shadow = 0 };
        return
        [
            F(d with { Dx = -1, ArmL = 1, ArmR = 3, Legs = new(5, 4, 5, 4), LegDx = new(-1, 0, 0, 0) }, 140),
            F(d with { Legs = L4.All(5) }, 120),
            F(d with { Dx = 1, ArmL = 3, ArmR = 1, Legs = new(4, 5, 4, 5), LegDx = new(0, 0, 0, 1) }, 140),
            F(d with { Legs = L4.All(5) }, 120),
        ];
    }

    static FrameDef[] Fall()
    {
        var f = N with { Dy = -1, Eyes = Eye.Wide, Shadow = 0 };
        return
        [
            F(f with { ArmL = -2, ArmR = 2, Legs = new(5, 4, 4, 5), LegDx = new(-1, 0, 0, 1) }, 90),
            F(f with { ArmL = 2, ArmR = -2, Legs = new(4, 5, 5, 4) }, 90),
        ];
    }

    static FrameDef[] Land()
    {
        uint d = Palette.Dust;
        return
        [
            F(N with { Dy = 2, H = 13, W = 28, Legs = L4.All(2), Eyes = Eye.Squeeze }, 80,
                G(DustBig, 3, 44, d), G(DustBig, 41, 44, d)),
            F(N with { Dy = 1, H = 14, W = 26, Legs = L4.All(3), Eyes = Eye.Squeeze }, 90,
                G(DustMid, 1, 43, d), G(DustMid, 45, 43, d)),
            F(N with { Dy = -1, H = 17, W = 22, Legs = L4.All(5) }, 80,
                G(DustDot, 0, 42, d), G(DustDot, 47, 42, d)),
            F(N, 140),
        ];
    }

    static FrameDef[] SleepStart() =>
    [
        F(N with { Eyes = Eye.Half }, 350),
        F(N, 160),
        F(N with { Eyes = Eye.Half }, 420),
        F(N with { Eyes = Eye.Closed }, 250),
        F(N with { Dy = 1, Legs = L4.All(3), Eyes = Eye.Closed, ArmL = 9, ArmR = 9 }, 200),
        F(Lying, 450),
    ];

    // Two z's rising half a cycle apart, growing as they float away.
    static FrameDef[] Sleep()
    {
        (string[] g, int x, int y)[] path =
        [
            (Glyphs.ZSmall, 37, 24), (Glyphs.ZSmall, 39, 18), (Glyphs.ZMid, 40, 11), (Glyphs.ZBig, 41, 3),
        ];
        var frames = new List<FrameDef>();
        for (int i = 0; i < 6; i++)
        {
            var fx = new List<Fx>();
            foreach (int phase in new[] { i, (i + 3) % 6 })
                if (phase < path.Length)
                    fx.Add(G(path[phase].g, path[phase].x, path[phase].y, Palette.Zzz));
            frames.Add(F(i < 3 ? Lying : Lying with { H = 14 }, 420, fx.ToArray()));
        }
        return frames.ToArray();
    }

    static FrameDef[] Wake() =>
    [
        F(Lying with { Eyes = Eye.Half }, 250),
        F(Lying with { Eyes = Eye.Open }, 200),
        F(N with { Dy = 1, Legs = L4.All(3) }, 100),
        F(N with { Dy = -1, H = 17, W = 22, Legs = L4.All(5), Eyes = Eye.Happy, ArmL = 0, ArmR = 0 }, 600),
        F(N with { Eyes = Eye.Half }, 90),
        F(N, 250),
    ];

    /// <summary>The canonical standing pose, used for the app icon.</summary>
    public static Pose Canonical => N with { Shadow = 0 };
}
