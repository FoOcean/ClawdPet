namespace ClawdPet.SpriteGen;

/// <summary>Minimal animated GIF89a writer (global palette, LZW, looping) for README previews.</summary>
static class Gif
{
    public static void Save(IReadOnlyList<(Img Frame, int DelayMs)> frames, string path)
    {
        var palette = new List<uint>();
        var lookup = new Dictionary<uint, byte>();
        foreach (var (img, _) in frames)
            foreach (uint c in img.Px)
                if (!lookup.ContainsKey(c))
                {
                    if (palette.Count == 256) throw new InvalidOperationException("GIF frames use more than 256 colours");
                    lookup[c] = (byte)palette.Count;
                    palette.Add(c);
                }

        int bits = 1;
        while ((1 << bits) < palette.Count) bits++;
        int w = frames[0].Frame.W, h = frames[0].Frame.H;

        using var ms = new MemoryStream();
        using var o = new BinaryWriter(ms);
        o.Write("GIF89a"u8);
        o.Write((ushort)w); o.Write((ushort)h);
        o.Write((byte)(0x80 | ((bits - 1) << 4) | (bits - 1)));
        o.Write((byte)0); o.Write((byte)0);
        for (int i = 0; i < 1 << bits; i++)
        {
            uint c = i < palette.Count ? palette[i] : 0;
            o.Write((byte)(c >> 16)); o.Write((byte)(c >> 8)); o.Write((byte)c);
        }
        o.Write([0x21, 0xFF, 0x0B]); o.Write("NETSCAPE2.0"u8); o.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        int minCode = Math.Max(2, bits);
        foreach (var (img, delay) in frames)
        {
            o.Write([0x21, 0xF9, 0x04, 0x04]);
            o.Write((ushort)Math.Max(2, (int)Math.Round(delay / 10.0)));
            o.Write([0x00, 0x00]);
            o.Write((byte)0x2C); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)w); o.Write((ushort)h); o.Write((byte)0);
            o.Write((byte)minCode);
            var data = Lzw(img.Px.Select(c => lookup[c]).ToArray(), minCode);
            for (int i = 0; i < data.Length; i += 255)
            {
                int n = Math.Min(255, data.Length - i);
                o.Write((byte)n);
                o.Write(data, i, n);
            }
            o.Write((byte)0);
        }
        o.Write((byte)0x3B);
        o.Flush();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ms.ToArray());
    }

    static byte[] Lzw(byte[] pixels, int minCode)
    {
        int clear = 1 << minCode, eoi = clear + 1;
        int codeSize = minCode + 1, next = eoi + 1;
        var table = new Dictionary<(int, byte), int>();
        var output = new List<byte>();
        int buffer = 0, count = 0;

        void Emit(int code)
        {
            buffer |= code << count;
            count += codeSize;
            while (count >= 8) { output.Add((byte)buffer); buffer >>= 8; count -= 8; }
        }

        // Width grows right after writing a code once the next free code no longer fits (decoder lags by one).
        void Grow()
        {
            if (next >= 1 << codeSize && codeSize < 12) codeSize++;
        }

        Emit(clear);
        int prefix = pixels[0];
        for (int i = 1; i < pixels.Length; i++)
        {
            byte k = pixels[i];
            if (table.TryGetValue((prefix, k), out int code)) { prefix = code; continue; }
            Emit(prefix);
            if (next < 4096)
            {
                Grow();
                table[(prefix, k)] = next++;
            }
            else
            {
                Emit(clear);
                table.Clear();
                codeSize = minCode + 1;
                next = eoi + 1;
            }
            prefix = k;
        }
        Emit(prefix);
        Grow();
        Emit(eoi);
        if (count > 0) output.Add((byte)buffer);
        return output.ToArray();
    }
}
