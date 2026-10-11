namespace ClawdPet.SpriteGen;

/// <summary>Self-contained HTML page that plays every tag of the atlas (no server needed).</summary>
static class PreviewHtml
{
    public static string Build(string pngBase64, string atlasJson) => Template
        .Replace("__PNG__", pngBase64)
        .Replace("__ATLAS__", atlasJson);

    const string Template = """
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Clawd Sprite Preview</title>
<style>
  :root { --bg:#1f1e1d; --card:#2b2a28; --fg:#ece9e1; --muted:#9a968c; --line:#3a3936; }
  body.light { --bg:#f4f1ea; --card:#ffffff; --fg:#1f1e1d; --muted:#6b675f; --line:#e2ddd2; }
  body { margin:0; background:var(--bg); color:var(--fg); font:14px/1.5 system-ui, "Segoe UI", "Microsoft YaHei", sans-serif; }
  header { display:flex; gap:16px; align-items:center; flex-wrap:wrap; padding:16px 20px; border-bottom:1px solid var(--line); }
  header h1 { font-size:16px; margin:0 12px 0 0; }
  label { color:var(--muted); display:flex; gap:6px; align-items:center; }
  button { background:var(--card); color:var(--fg); border:1px solid var(--line); border-radius:6px; padding:4px 10px; cursor:pointer; }
  main { display:grid; grid-template-columns:repeat(auto-fill, minmax(220px, 1fr)); gap:14px; padding:20px; }
  .card { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:12px; display:flex; flex-direction:column; align-items:center; gap:8px; }
  canvas { image-rendering:pixelated; background:repeating-conic-gradient(#0000 0 25%, #8881 0 50%) 0 0/16px 16px; border-radius:6px; }
  .name { font-weight:600; }
  .meta { color:var(--muted); font-size:12px; }
</style>
</head>
<body>
<header>
  <h1>Clawd 精灵图预览</h1>
  <label>缩放 <input id="scale" type="range" min="1" max="8" value="4"></label>
  <label><input id="ground" type="checkbox" checked> 显示锚点</label>
  <button id="theme">切换背景</button>
</header>
<main id="grid"></main>
<script>
const atlas = __ATLAS__;
const sheet = new Image();
sheet.src = "data:image/png;base64,__PNG__";
const grid = document.getElementById("grid");
const scaleInput = document.getElementById("scale");
const groundInput = document.getElementById("ground");
document.getElementById("theme").onclick = () => document.body.classList.toggle("light");

const slice = (atlas.meta.slices || []).find(s => s.name === "anchor");
const anchor = slice ? { x: slice.keys[0].bounds.x + slice.keys[0].pivot.x, y: slice.keys[0].bounds.y + slice.keys[0].pivot.y } : null;
const players = atlas.meta.frameTags.map(tag => {
  const frames = atlas.frames.slice(tag.from, tag.to + 1);
  const total = frames.reduce((s, f) => s + f.duration, 0);
  const card = document.createElement("div");
  card.className = "card";
  const canvas = document.createElement("canvas");
  card.append(canvas);
  card.insertAdjacentHTML("beforeend", `<div class="name">${tag.name}</div><div class="meta">${frames.length} 帧 · ${total} ms</div>`);
  grid.append(card);
  return { frames, total, canvas, ctx: canvas.getContext("2d") };
});

function draw(now) {
  const s = +scaleInput.value;
  for (const p of players) {
    const f0 = p.frames[0].sourceSize;
    if (p.canvas.width !== f0.w * s) { p.canvas.width = f0.w * s; p.canvas.height = f0.h * s; }
    let t = now % p.total, f = p.frames[0];
    for (const fr of p.frames) { if (t < fr.duration) { f = fr; break; } t -= fr.duration; }
    p.ctx.imageSmoothingEnabled = false;
    p.ctx.clearRect(0, 0, p.canvas.width, p.canvas.height);
    p.ctx.drawImage(sheet, f.frame.x, f.frame.y, f.frame.w, f.frame.h,
      f.spriteSourceSize.x * s, f.spriteSourceSize.y * s, f.frame.w * s, f.frame.h * s);
    if (anchor && groundInput.checked) {
      p.ctx.fillStyle = "#4aa3ff";
      p.ctx.fillRect(0, anchor.y * s, p.canvas.width, 1);
      p.ctx.fillRect(anchor.x * s, anchor.y * s - 3, 1, 7);
    }
  }
  requestAnimationFrame(draw);
}
sheet.onload = () => requestAnimationFrame(draw);
</script>
</body>
</html>
""";
}
