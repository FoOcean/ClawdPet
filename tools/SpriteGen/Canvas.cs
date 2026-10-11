using System.IO.Compression;

namespace ClawdPet.SpriteGen;

/// <summary>A tiny RGBA pixel canvas. Colors are packed 0xAARRGGBB.</summary>
sealed class Img
{
    public readonly int W, H;
    public readonly uint[] Px;

    public Img(int w, int h, uint fill = 0)
    {
        W = w; H = h; Px = new uint[w * h];
        if (fill != 0) Array.Fill(Px, fill);
    }

    public uint Get(int x, int y) => (uint)x < W && (uint)y < H ? Px[y * W + x] : 0;

    public void Set(int x, int y, uint c)
    {
        if ((uint)x < W && (uint)y < H) Px[y * W + x] = c;
    }

    public void Rect(int x, int y, int w, int h, uint c)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                Set(xx, yy, c);
    }

    /// <summary>Stamps a glyph mask where '#' pixels take <paramref name="c"/>.</summary>
    public void Mask(string[] rows, int x, int y, uint c)
    {
        for (int r = 0; r < rows.Length; r++)
            for (int i = 0; i < rows[r].Length; i++)
                if (rows[r][i] == '#') Set(x + i, y + r, c);
    }

    /// <summary>Copies src onto this canvas; transparent source pixels are skipped, others alpha-blended.</summary>
    public void Blit(Img src, int dx, int dy)
    {
        for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
            {
                uint s = src.Px[y * src.W + x];
                if ((s >> 24) == 0) continue;
                Set(dx + x, dy + y, Over(s, Get(dx + x, dy + y)));
            }
    }

    public Img MirrorX()
    {
        var m = new Img(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                m.Px[y * W + (W - 1 - x)] = Px[y * W + x];
        return m;
    }

    public Img Crop(int x, int y, int w, int h)
    {
        var c = new Img(w, h);
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
                c.Px[yy * w + xx] = Get(x + xx, y + yy);
        return c;
    }

    /// <summary>Nearest-neighbour integer upscale.</summary>
    public Img Scale(int s)
    {
        var o = new Img(W * s, H * s);
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
                o.Px[y * o.W + x] = Px[(y / s) * W + (x / s)];
        return o;
    }

    /// <summary>Keeps the top-left pixel of each s×s block (exact inverse of <see cref="Scale"/> for grid-aligned art).</summary>
    public Img Downsample(int s)
    {
        var o = new Img(W / s, H / s);
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
                o.Px[y * o.W + x] = Px[(y * s) * W + (x * s)];
        return o;
    }

    static uint Over(uint s, uint d)
    {
        uint sa = s >> 24;
        if (sa == 255) return s;
        uint da = d >> 24;
        double a = sa / 255.0, b = da / 255.0 * (1 - a), oa = a + b;
        if (oa <= 0) return 0;
        uint Ch(int sh) => (uint)Math.Round((((s >> sh) & 255) * a + ((d >> sh) & 255) * b) / oa);
        return ((uint)Math.Round(oa * 255) << 24) | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }
}

static class Png
{
    static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(Img img)
    {
        using var ms = new MemoryStream();
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)img.W);
        WriteBE(ihdr, 4, (uint)img.H);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        Chunk(ms, "IHDR", ihdr);

        var raw = new byte[(img.W * 4 + 1) * img.H];
        int o = 0;
        for (int y = 0; y < img.H; y++)
        {
            raw[o++] = 0; // filter: none
            for (int x = 0; x < img.W; x++)
            {
                uint c = img.Px[y * img.W + x];
                raw[o++] = (byte)(c >> 16);
                raw[o++] = (byte)(c >> 8);
                raw[o++] = (byte)c;
                raw[o++] = (byte)(c >> 24);
            }
        }
        using (var z = new MemoryStream())
        {
            using (var zs = new ZLibStream(z, CompressionLevel.SmallestSize, leaveOpen: true))
                zs.Write(raw);
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    public static void Save(Img img, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encode(img));
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4];
        WriteBE(crc, 0, Crc(td));
        s.Write(crc);
    }

    static void WriteBE(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}

static class Ico
{
    /// <summary>Writes a multi-size .ico whose entries are PNG-compressed (supported since Vista).</summary>
    public static void Save(IReadOnlyList<Img> sizes, string path)
    {
        var pngs = sizes.Select(Png.Encode).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Count);
        int offset = 6 + 16 * sizes.Count;
        for (int i = 0; i < sizes.Count; i++)
        {
            w.Write((byte)(sizes[i].W >= 256 ? 0 : sizes[i].W));
            w.Write((byte)(sizes[i].H >= 256 ? 0 : sizes[i].H));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
        w.Flush();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ms.ToArray());
    }
}
