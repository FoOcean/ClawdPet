namespace ClawdPet.Core.World;

/// <summary>
/// Where the pet is. All coordinates are physical screen pixels; (X, Y) is the anchor point under
/// the feet, so a standing pet has Y == the floor of the surface it is on.
/// </summary>
public sealed class PetBody
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
    /// <summary>Screen pixels per sprite pixel (integer, so pixels stay crisp).</summary>
    public int Scale { get; set; } = 3;
    /// <summary>Sprite pixels from the anchor to the outermost opaque column (set by the host).</summary>
    public int HalfWidthArt { get; set; } = 16;
    public double HalfWidth => HalfWidthArt * Scale;
}

/// <summary>A walkable region: usually one monitor's work area, whose bottom edge is the floor.</summary>
public readonly record struct Surface(double Left, double Top, double Right, double Floor)
{
    public double ClampX(double x, double halfWidth)
    {
        double min = Left + halfWidth, max = Math.Max(min, Right - halfWidth);
        return Math.Clamp(x, min, max);
    }
}

public interface IPetWorld
{
    /// <summary>The surface under (or nearest to) a point.</summary>
    Surface SurfaceAt(double x, double y);
}

/// <summary>Mouse state fed in by the host every tick (physical pixels).</summary>
public sealed class PetPointer
{
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsDown { get; set; }
}
