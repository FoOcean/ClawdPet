namespace ClawdPet.SpriteGen;

static class Palette
{
    public const uint Body = 0xFFD77757;      // Claude Code's "claude" color, rgb(215,119,87)
    public const uint BodySide = 0xFFBE684B;  // the darker right-hand face when Clawd turns 3/4
    public const uint ArmEdge = 0xFFC0694D;   // outline that separates the reaching arm from the body
    public const uint LegBack = 0xFF9E583F;   // far legs in the 3/4 view
    public const uint Eye = 0xFF141413;
    public const uint Shadow = 0x50000000;
    public const uint Blush = 0xFFF0A08A;
    public const uint Heart = 0xFFE5484D;
    public const uint Spark = 0xFFD77757;     // the ✻ spinner uses the same orange as in the terminal
    public const uint Zzz = 0xFF86A3CF;
    public const uint Alert = 0xFFE5484D;
    public const uint Dust = 0xC0D9D0C3;
    public const uint Poof = 0xFFFBEFE3;
    public const uint LidLight = 0xFF858583;
    public const uint LidDark = 0xFF5C5C5A;
    public const uint BaseLight = 0xFF7E7E7C;
    public const uint BaseDark = 0xFF585856;
}

enum Eye { Open, Half, Closed, Happy, Squeeze, Wide }

/// <summary>
/// Laptop on Clawd's left, seen exactly side-on like in the Claude app: a thin base on the ground
/// and a thin lid line. Closed, lid swinging up (45°), upright, or open and leaning back.
/// </summary>
enum Laptop { None, Closed, Half, Upright, Open }

/// <summary>Four values, one per leg (left-outer, left-inner, right-inner, right-outer).</summary>
readonly record struct L4(int A, int B, int C, int D)
{
    public static readonly L4 Zero = new(0, 0, 0, 0);
    public static L4 All(int v) => new(v, v, v, v);
    public int this[int i] => i switch { 0 => A, 1 => B, 2 => C, _ => D };
}

/// <summary>
/// Parameters for one full drawing of Clawd. The sprite sheet is authored from these, but every
/// frame is flattened into a single picture — the runtime never composites parts.
///
/// Proportions are the Claude Code welcome-screen Clawd (quadrant-block art) at 2× in square pixels:
/// body 24×16, eyes 2×4, arm nubs 4×4, legs 2×4.
/// </summary>
sealed record Pose
{
    public int Dx { get; init; }
    /// <summary>Vertical offset of the body's bottom edge; negative is up.</summary>
    public int Dy { get; init; }
    public int W { get; init; } = 24;
    public int H { get; init; } = 16;
    public Eye Eyes { get; init; } = Eye.Open;
    public int EyeDx { get; init; }
    public int EyeDy { get; init; }
    /// <summary>Arm top relative to body top; null = vertically centred (canonical).</summary>
    public int? ArmL { get; init; }
    public int? ArmR { get; init; }
    public L4 Legs { get; init; } = L4.All(4);
    public L4 LegDx { get; init; } = L4.Zero;
    public bool Blush { get; init; }
    /// <summary>-1 = automatic ground shadow, 0 = none.</summary>
    public int Shadow { get; init; } = -1;
    /// <summary>Turned 3/4 to face left (front face + darker side face), as in the Claude app's typing Clawd.</summary>
    public bool Turned { get; init; }
    /// <summary>When turned: the left arm reaches down onto the keyboard, lifted this many pixels; null = plain nub.</summary>
    public int? Reach { get; init; }
    public Laptop Laptop { get; init; }
}

static class ClawdPainter
{
    public const int FrameW = 64, FrameH = 48;
    public const int CenterX = 32;
    /// <summary>Effect glyph coordinates are authored on a 48-wide stage centred on Clawd; this shifts them into the frame.</summary>
    public const int StageX = CenterX - 24;
    /// <summary>Exclusive bottom row of the body when standing; legs fill rows 42..45.</summary>
    public const int BaseBottom = 42;
    /// <summary>First row below the feet — the ground line / anchor.</summary>
    public const int GroundY = 46;

    public static void Draw(Img img, Pose p)
    {
        int bottom = BaseBottom + p.Dy;
        int top = bottom - p.H;
        int x0 = CenterX - p.W / 2 + p.Dx;

        if (p.Shadow != 0)
        {
            int maxLeg = Math.Max(Math.Max(p.Legs.A, p.Legs.B), Math.Max(p.Legs.C, p.Legs.D));
            int lift = (GroundY - 1) - (bottom + maxLeg - 1);
            if (lift >= 0)
            {
                int sw = p.Shadow > 0 ? p.Shadow : Math.Max(8, p.W - 2 - lift / 2 * 2);
                img.Rect(CenterX - sw / 2 + p.Dx, GroundY, sw, 1, Palette.Shadow);
            }
        }

        // Behind Clawd, so the reaching arm rests on top of the keyboard.
        if (p.Laptop != Laptop.None) DrawLaptop(img, p.Laptop);

        if (p.Turned) DrawTurned(img, p, x0, top, bottom);
        else DrawFront(img, p, x0, top, bottom);
    }

    static void DrawFront(Img img, Pose p, int x0, int top, int bottom)
    {
        int[] legX = [2, 6, p.W - 8, p.W - 4];
        for (int i = 0; i < 4; i++)
            img.Rect(x0 + legX[i] + p.LegDx[i], bottom, 2, p.Legs[i], Palette.Body);

        img.Rect(x0, top, p.W, p.H, Palette.Body);

        img.Rect(x0 - 4, top + (p.ArmL ?? p.H / 2), 4, 4, Palette.Body);
        img.Rect(x0 + p.W, top + (p.ArmR ?? p.H / 2), 4, 4, Palette.Body);

        int ey = top + (p.H >= 15 ? 4 : 3) + p.EyeDy;
        int lx = x0 + 4 + p.EyeDx, rx = x0 + p.W - 6 + p.EyeDx;
        DrawEye(img, lx, ey, p.Eyes, left: true, p.H);
        DrawEye(img, rx, ey, p.Eyes, left: false, p.H);

        if (p.Blush)
        {
            img.Rect(lx - 2, ey + 5, 3, 1, Palette.Blush);
            img.Rect(rx + 1, ey + 5, 3, 1, Palette.Blush);
        }
    }

    /// <summary>
    /// 3/4 view facing left: an 18-px front face with both eyes pushed to the left (the near eye sits
    /// right on the edge), a 6-px darker side face, near legs orange and far legs darker, and the
    /// left arm reaching down to the keyboard.
    /// </summary>
    static void DrawTurned(Img img, Pose p, int x0, int top, int bottom)
    {
        const int side = 6;
        int front = p.W - side;

        int[] legX = [2, 7, 13, 19];
        uint[] legC = [Palette.Body, Palette.LegBack, Palette.Body, Palette.LegBack];
        for (int i = 0; i < 4; i++)
            img.Rect(x0 + legX[i] + p.LegDx[i], bottom, 2, p.Legs[i], legC[i]);

        img.Rect(x0, top, front, p.H, Palette.Body);
        img.Rect(x0 + front, top, side, p.H, Palette.BodySide);

        int armTop = top + (p.ArmL ?? p.H / 2);
        if (p.Reach is { } lift)
        {
            // Long arm from the shoulder down to the keys, overlapping the body by 2 px, with a
            // shaded top and inner edge like the original.
            int ax = x0 - 5, aw = 7, ab = 43 - lift;
            img.Rect(ax, armTop, aw, ab - armTop + 1, Palette.Body);
            img.Rect(ax, armTop, aw, 1, Palette.ArmEdge);
            img.Rect(ax + aw - 1, armTop, 1, Math.Max(1, ab - armTop - 2), Palette.ArmEdge);
        }
        else
        {
            img.Rect(x0 - 4, armTop, 4, 4, Palette.Body);
        }

        int ey = top + (p.H >= 15 ? 4 : 3) + p.EyeDy;
        int lx = x0 + p.EyeDx + (p.Eyes == Eye.Happy ? 1 : 0), rx = x0 + 11 + p.EyeDx;
        DrawEye(img, lx, ey, p.Eyes, left: true, p.H);
        DrawEye(img, rx, ey, p.Eyes, left: false, p.H);

        if (p.Blush) img.Rect(rx - 1, ey + 5, 3, 1, Palette.Blush);
    }

    // Laptop geometry: hinge at x 10, base running right toward Clawd under its arm.
    const int HingeX = 10, BaseW = 11;

    static void DrawLaptop(Img img, Laptop state)
    {
        img.Rect(HingeX - 1, GroundY, BaseW + 1, 1, Palette.Shadow);
        img.Rect(HingeX, 44, BaseW, 1, Palette.BaseLight);
        img.Rect(HingeX, 45, BaseW, 1, Palette.BaseDark);

        switch (state)
        {
            case Laptop.Closed:
                img.Rect(HingeX, 43, BaseW, 1, Palette.LidLight);
                break;

            case Laptop.Half:
                // Free end swinging up over the hinge, about 45°.
                for (int i = 0; i <= 8; i++)
                {
                    img.Set(HingeX + i, 42 - i, Palette.LidLight);
                    img.Set(HingeX + i, 43 - i, Palette.LidDark);
                }
                break;

            case Laptop.Upright:
                img.Rect(HingeX, 31, 1, 13, Palette.LidLight);
                img.Rect(HingeX + 1, 31, 1, 13, Palette.LidDark);
                break;

            case Laptop.Open:
                // Leaning back away from Clawd; the screen side (dark) faces it.
                for (int y = 31; y <= 43; y++)
                {
                    int x = HingeX - (43 - y) * 2 / 3;
                    img.Set(x, y, Palette.LidLight);
                    img.Set(x + 1, y, Palette.LidDark);
                }
                break;
        }
    }

    static void DrawEye(Img img, int x, int y, Eye e, bool left, int bodyH)
    {
        switch (e)
        {
            case Eye.Open:
                img.Rect(x, y, 2, bodyH <= 13 ? 3 : 4, Palette.Eye);
                break;
            case Eye.Half:
                img.Rect(x, y + 2, 2, 2, Palette.Eye);
                break;
            case Eye.Closed:
                img.Rect(x - 1, y + 3, 4, 1, Palette.Eye);
                break;
            case Eye.Happy:
                img.Mask([".##.", "#..#"], x - 1, y + 1, Palette.Eye);
                break;
            case Eye.Squeeze:
                img.Mask(left ? ["#..", ".#.", "..#", ".#.", "#.."] : ["..#", ".#.", "#..", ".#.", "..#"], left ? x - 1 : x, y, Palette.Eye);
                break;
            case Eye.Wide:
                img.Rect(x, y - 1, 2, 5, Palette.Eye);
                break;
        }
    }
}

static class Glyphs
{
    public static readonly string[] ZSmall = ["####", "..#.", ".#..", "####"];
    public static readonly string[] ZMid = ["#####", "...#.", "..#..", ".#...", "#####"];
    public static readonly string[] ZBig = ["######", "....#.", "...#..", "..#...", ".#....", "######"];

    public static readonly string[] HeartSmall = ["##.##", "#####", ".###.", "..#.."];
    public static readonly string[] Heart = [".##.##.", "#######", "#######", ".#####.", "..###..", "...#..."];

    public static readonly string[] Bang = ["##", "##", "##", "##", "..", "##"];

    public static readonly string[] Sparkle = [".#.", "###", ".#."];
    public static readonly string[] Dot = ["#"];

    // Claude Code's spinner: · ✢ ✳ ✶ ✻ ✽ (then back), drawn on a 7×7 grid.
    public static readonly string[][] Spinner =
    [
        [".......", ".......", ".......", "...#...", ".......", ".......", "......."],
        [".......", "...#...", "...#...", ".##.##.", "...#...", "...#...", "......."],
        [".......", ".#.#.#.", "..###..", ".##.##.", "..###..", ".#.#.#.", "......."],
        ["...#...", "...#...", "#.###.#", ".##.##.", "#.###.#", "...#...", "...#..."],
        ["#..#..#", ".#.#.#.", "..###..", "###.###", "..###..", ".#.#.#.", "#..#..#"],
        ["##.#.##", "##.#.##", "..###..", "###.###", "..###..", "##.#.##", "##.#.##"],
    ];
}

readonly record struct Fx(string[] Glyph, int X, int Y, uint Color);

/// <summary>A frame = one complete pose + overlay effects + how long it stays on screen.</summary>
sealed record FrameDef(Pose Pose, int Ms, params Fx[] Effects)
{
    public Img Render()
    {
        var img = new Img(ClawdPainter.FrameW, ClawdPainter.FrameH);
        ClawdPainter.Draw(img, Pose);
        foreach (var fx in Effects) img.Mask(fx.Glyph, fx.X + ClawdPainter.StageX, fx.Y, fx.Color);
        return img;
    }
}

sealed record AnimDef(string Name, IReadOnlyList<FrameDef> Frames, bool Mirror = false);
