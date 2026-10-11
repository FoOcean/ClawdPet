using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClawdPet.Core.Sprites;

namespace ClawdPet.Rendering;

/// <summary>
/// Cuts frames out of the single sprite-sheet image and enlarges them by a whole number of
/// physical pixels (nearest neighbour), so every sprite pixel stays a crisp square.
/// Frames are built lazily and cached per scale.
/// </summary>
sealed class SpriteFrames
{
    readonly SpriteAtlas _atlas;
    readonly byte[] _sheet;   // BGRA, straight alpha
    readonly int _sheetW, _sheetH;
    readonly Dictionary<int, BitmapSource> _cache = [];
    int _scale = 1;
    double _dpi = 1;

    public SpriteFrames(SpriteAtlas atlas)
    {
        _atlas = atlas;
        using var stream = File.OpenRead(atlas.ImagePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        _sheetW = bgra.PixelWidth;
        _sheetH = bgra.PixelHeight;
        _sheet = new byte[_sheetW * _sheetH * 4];
        bgra.CopyPixels(_sheet, _sheetW * 4, 0);
    }

    public int Scale => _scale;

    /// <param name="scale">Physical pixels per sprite pixel.</param>
    /// <param name="dpiScale">Monitor DPI / 96, so the bitmap maps 1:1 onto physical pixels.</param>
    public void Configure(int scale, double dpiScale)
    {
        if (scale == _scale && Math.Abs(dpiScale - _dpi) < 0.001) return;
        _scale = Math.Max(1, scale);
        _dpi = dpiScale;
        _cache.Clear();
    }

    public BitmapSource Get(int index)
    {
        if (_cache.TryGetValue(index, out var hit)) return hit;

        var f = _atlas.Frames[Math.Clamp(index, 0, _atlas.Frames.Count - 1)];
        int s = _scale, w = _atlas.FrameWidth * s, h = _atlas.FrameHeight * s, stride = w * 4;
        var px = new byte[stride * h];
        for (int y = 0; y < f.Source.H; y++)
        {
            int sy = f.Source.Y + y;
            if ((uint)sy >= (uint)_sheetH) continue;
            for (int x = 0; x < f.Source.W; x++)
            {
                int sx = f.Source.X + x;
                if ((uint)sx >= (uint)_sheetW) continue;
                int si = (sy * _sheetW + sx) * 4;
                if (_sheet[si + 3] == 0) continue;
                int dx0 = (f.Placement.X + x) * s, dy0 = (f.Placement.Y + y) * s;
                for (int dy = 0; dy < s; dy++)
                {
                    int row = (dy0 + dy) * stride;
                    if (dy0 + dy >= h) break;
                    for (int dx = 0; dx < s && dx0 + dx < w; dx++)
                        Buffer.BlockCopy(_sheet, si, px, row + (dx0 + dx) * 4, 4);
                }
            }
        }
        var bmp = BitmapSource.Create(w, h, 96 * _dpi, 96 * _dpi, PixelFormats.Bgra32, null, px, stride);
        bmp.Freeze();
        _cache[index] = bmp;
        return bmp;
    }

    /// <summary>Widest opaque extent left or right of the anchor, in sprite pixels.</summary>
    public int OpaqueHalfWidth(int index)
    {
        var f = _atlas.Frames[index];
        int min = int.MaxValue, max = int.MinValue;
        for (int y = 0; y < f.Source.H; y++)
            for (int x = 0; x < f.Source.W; x++)
            {
                int si = ((f.Source.Y + y) * _sheetW + f.Source.X + x) * 4;
                if (si + 3 >= _sheet.Length || _sheet[si + 3] < 128) continue;
                min = Math.Min(min, f.Placement.X + x);
                max = Math.Max(max, f.Placement.X + x);
            }
        if (min > max) return _atlas.FrameWidth / 2;
        return Math.Max(_atlas.Anchor.X - min, max + 1 - _atlas.Anchor.X);
    }
}
