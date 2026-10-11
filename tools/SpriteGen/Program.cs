// Generates the Clawd sprite sheet (one PNG + Aseprite-compatible JSON atlas), the app icon,
// and preview images / an HTML player for checking the animations.
//
//   dotnet run --project tools/SpriteGen            (writes into the repo)
//   dotnet run --project tools/SpriteGen -- <root>  (writes under another root)

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdPet.SpriteGen;

const int FW = ClawdPainter.FrameW, FH = ClawdPainter.FrameH;

string root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
string skinDir = Path.Combine(root, "src", "ClawdPet", "Assets", "Skins", "clawd");
string iconPath = Path.Combine(root, "src", "ClawdPet", "Assets", "clawd.ico");
string previewDir = Path.Combine(root, "artifacts", "sprite-preview");

var anims = ClawdAnimations.All();
int cols = anims.Max(a => a.Frames.Count);
var sheet = new Img(cols * FW, anims.Count * FH);

var frames = new JsonArray();
var tags = new JsonArray();
var rendered = new List<List<Img>>();
int index = 0;
for (int row = 0; row < anims.Count; row++)
{
    var anim = anims[row];
    var strip = new List<Img>();
    int from = index;
    for (int col = 0; col < anim.Frames.Count; col++, index++)
    {
        var def = anim.Frames[col];
        var img = def.Render();
        if (anim.Mirror) img = img.MirrorX();
        strip.Add(img);
        sheet.Blit(img, col * FW, row * FH);
        frames.Add(new JsonObject
        {
            ["filename"] = $"{anim.Name} {col}",
            ["frame"] = Rect(col * FW, row * FH, FW, FH),
            ["rotated"] = false,
            ["trimmed"] = false,
            ["spriteSourceSize"] = Rect(0, 0, FW, FH),
            ["sourceSize"] = new JsonObject { ["w"] = FW, ["h"] = FH },
            ["duration"] = def.Ms,
        });
    }
    tags.Add(new JsonObject
    {
        ["name"] = anim.Name,
        ["from"] = from,
        ["to"] = index - 1,
        ["direction"] = "forward",
        ["color"] = "#000000ff",
    });
    rendered.Add(strip);
}

// The "anchor" slice pivot marks the ground point under Clawd's feet (frame-local coordinates).
var anchorBounds = Rect(ClawdPainter.CenterX - 16, 22, 32, 24);
var atlas = new JsonObject
{
    ["frames"] = frames,
    ["meta"] = new JsonObject
    {
        ["app"] = "ClawdPet SpriteGen",
        ["version"] = "1.0",
        ["image"] = "clawd.png",
        ["format"] = "RGBA8888",
        ["size"] = new JsonObject { ["w"] = sheet.W, ["h"] = sheet.H },
        ["scale"] = "1",
        ["frameTags"] = tags,
        ["layers"] = new JsonArray(new JsonObject { ["name"] = "clawd", ["opacity"] = 255, ["blendMode"] = "normal" }),
        ["slices"] = new JsonArray(new JsonObject
        {
            ["name"] = "anchor",
            ["color"] = "#0000ffff",
            ["keys"] = new JsonArray(new JsonObject
            {
                ["frame"] = 0,
                ["bounds"] = anchorBounds,
                ["pivot"] = new JsonObject { ["x"] = 16, ["y"] = ClawdPainter.GroundY - 22 },
            }),
        }),
    },
};

Directory.CreateDirectory(skinDir);
Png.Save(sheet, Path.Combine(skinDir, "clawd.png"));
string json = atlas.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(skinDir, "clawd.json"), json, new UTF8Encoding(false));

// Icon: the canonical 1× Clawd (16×10) is exactly the 2× frame art downsampled.
var canonical = new FrameDef(ClawdAnimations.Canonical, 0).Render().Downsample(2).Crop(ClawdPainter.CenterX / 2 - 8, 13, 16, 10);
var icoSizes = new List<Img>();
foreach (int size in new[] { 16, 24, 32, 48, 64, 256 })
{
    int s = size / 16;
    var art = canonical.Scale(s);
    var icon = new Img(size, size);
    icon.Blit(art, 0, (size - art.H) / 2);
    icoSizes.Add(icon);
}
Ico.Save(icoSizes, iconPath);

// Previews: each animation on a dark and a light background, plus a self-contained HTML player.
Directory.CreateDirectory(previewDir);
foreach (var stale in Directory.GetFiles(previewDir, "*.png")) File.Delete(stale);
const int PS = 3;
for (int a = 0; a < anims.Count; a++)
{
    var strip = rendered[a];
    var pv = new Img(strip.Count * (FW + 2) * PS, 2 * FH * PS);
    for (int bg = 0; bg < 2; bg++)
    {
        uint color = bg == 0 ? 0xFF2B2B2Bu : 0xFFF4F1EAu;
        for (int i = 0; i < strip.Count; i++)
        {
            var cell = new Img(FW, FH, color);
            cell.Blit(strip[i], 0, 0);
            pv.Blit(cell.Scale(PS), i * (FW + 2) * PS, bg * FH * PS);
        }
    }
    Png.Save(pv, Path.Combine(previewDir, $"{a:00}-{anims[a].Name}.png"));
}
Png.Save(sheet.Scale(2), Path.Combine(previewDir, "sheet@2x.png"));
Png.Save(icoSizes[^1], Path.Combine(previewDir, "icon-256.png"));

string html = PreviewHtml.Build(Convert.ToBase64String(Png.Encode(sheet)), json);
File.WriteAllText(Path.Combine(previewDir, "preview.html"), html, new UTF8Encoding(false));

// README animations: the main clips as looping GIFs on a terminal-dark backdrop, 4× size.
string docsDir = Path.Combine(root, "docs");
string[] showcase = ["idle", "walk_right", "typing", "think", "happy", "sleep", "drag", "wave"];
foreach (var name in showcase)
{
    int a = anims.ToList().FindIndex(x => x.Name == name);
    if (a < 0) continue;
    var gifFrames = rendered[a].Select((img, i) =>
    {
        var cell = new Img(FW, FH, 0xFF1F1E1Du);
        cell.Blit(img, 0, 0);
        return (cell.Scale(4), anims[a].Frames[i].Ms);
    }).ToList();
    Gif.Save(gifFrames, Path.Combine(docsDir, $"{name}.gif"));
}

Console.WriteLine($"{anims.Count} animations, {index} frames, sheet {sheet.W}x{sheet.H}");
Console.WriteLine($"  sheet   -> {Path.Combine(skinDir, "clawd.png")}");
Console.WriteLine($"  atlas   -> {Path.Combine(skinDir, "clawd.json")}");
Console.WriteLine($"  icon    -> {iconPath}");
Console.WriteLine($"  preview -> {Path.Combine(previewDir, "preview.html")}");
Console.WriteLine($"  gifs    -> {docsDir}");

static JsonObject Rect(int x, int y, int w, int h) => new() { ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h };

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "ClawdPet.slnx"))) return dir.FullName;
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "ClawdPet.slnx"))) return dir.FullName;
    throw new InvalidOperationException("Cannot find ClawdPet.slnx; pass the repo root as the first argument.");
}
