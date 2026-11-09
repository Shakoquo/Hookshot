# Makes the Mako launcher's pictures from the shark logo: mako_logo.png (the shark with its dark background taken out, the glow kept
# as soft transparency, cropped) and mako.ico (256/64/48/32/16, the shark centred on a square).
# Background removal: pixels close to the background colour that connect to the edges, or form a big dark area, become transparent;
# small dark areas inside the shark (gills, eye) stay; everything else is "unblended" from the background (glow -> partial alpha).
param([string]$src = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "mako_source.png"))
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Collections.Generic; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class Cut {
  public static Bitmap Make(Bitmap s) {
    int W = s.Width, H = s.Height;
    var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(b)) g.DrawImage(s, 0, 0, W, H);
    var d = b.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var px = new byte[d.Stride * H]; Marshal.Copy(d.Scan0, px, 0, px.Length);
    int st = d.Stride;
    // (the background colour: the darkest corner region's average)
    double br = 0, bgc = 0, bb = 0; int n = 0;
    foreach (var c in new[] { new Point(0, 0), new Point(W - 40, 0), new Point(0, H - 40), new Point(W - 40, H - 40) })
      for (int y = c.Y; y < c.Y + 40; y++) for (int x = c.X; x < c.X + 40; x++) { int i = y * st + x * 4; bb += px[i]; bgc += px[i + 1]; br += px[i + 2]; n++; }
    br /= n; bgc /= n; bb /= n;
    Func<int, double> dist = i => Math.Max(Math.Abs(px[i + 2] - br), Math.Max(Math.Abs(px[i + 1] - bgc), Math.Abs(px[i] - bb)));
    var orig = (byte[])px.Clone();
    // (the shark's body: grey/white and clearly off the background; its core = that, 2 pixels in from its edge)
    var S = new bool[W * H];
    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) {
      int i = y * st + x * 4;
      int mx = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])), mn = Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));
      S[y * W + x] = mx - mn < 40 && dist(i) > 90;
    }
    var core = new bool[W * H];
    for (int y = 2; y < H - 2; y++) for (int x = 2; x < W - 2; x++) {
      bool all = true;
      for (int dy = -2; dy <= 2 && all; dy++) for (int dx = -2; dx <= 2 && all; dx++) all = S[(y + dy) * W + x + dx];
      core[y * W + x] = all;
    }
    // (holes in the body - gills, eye, the shading around it: small areas not on an edge, walled almost all round by the body)
    var solid = (bool[])core.Clone(); var seen = new bool[W * H]; var stack = new Stack<int>(); var comp = new List<int>();
    for (int p0 = 0; p0 < W * H; p0++) {
      if (core[p0] || seen[p0]) continue;
      comp.Clear(); bool edge = false; int wall = 0; stack.Push(p0); seen[p0] = true;
      while (stack.Count > 0) {
        int p = stack.Pop(); comp.Add(p); int x = p % W, y = p / W;
        if (x == 0 || y == 0 || x == W - 1 || y == H - 1) { edge = true; continue; }
        foreach (int q in new[] { p - 1, p + 1, p - W, p + W }) {
          if (core[q]) { wall++; continue; }
          if (!seen[q]) { seen[q] = true; stack.Push(q); }
        }
      }
      if (edge || comp.Count > W * H / 40) continue;
      // (walled by the body, not by blue glow: the pixels just outside it are all core)
      foreach (int p in comp) solid[p] = true;
    }
    // (the background is a dark gradient, lighter in the middle: estimated locally - per 8x8 cell the darkest of the cells within
    // 48 pixels (streaks and glow are thinner than that), smoothed, read back bilinearly. One colour for all of it left the lighter
    // middle as white grain.)
    const int C = 8; int cw = (W + C - 1) / C, ch = (H + C - 1) / C;
    var cell = new double[3, cw * ch];
    for (int cy = 0; cy < ch; cy++) for (int cx = 0; cx < cw; cx++) {
      double[] sum = new double[3]; int cnt = 0;
      for (int y = cy * C; y < Math.Min(H, cy * C + C); y++) for (int x = cx * C; x < Math.Min(W, cx * C + C); x++) {
        int i = y * st + x * 4; sum[0] += orig[i + 2]; sum[1] += orig[i + 1]; sum[2] += orig[i]; cnt++; }
      for (int j = 0; j < 3; j++) cell[j, cy * cw + cx] = sum[j] / cnt;
    }
    Func<double[,], int, double[,]> pass = (src, mode) => {   // mode 0: min over radius 6 cells; 1: mean over radius 4
      var o = new double[3, cw * ch]; int r = mode == 0 ? 6 : 4;
      for (int cy = 0; cy < ch; cy++) for (int cx = 0; cx < cw; cx++) for (int j = 0; j < 3; j++) {
        double v = mode == 0 ? 1e9 : 0; int cnt = 0;
        for (int yy = Math.Max(0, cy - r); yy <= Math.Min(ch - 1, cy + r); yy++) for (int xx = Math.Max(0, cx - r); xx <= Math.Min(cw - 1, cx + r); xx++) {
          double q = src[j, yy * cw + xx]; if (mode == 0) { if (q < v) v = q; } else { v += q; cnt++; } }
        o[j, cy * cw + cx] = mode == 0 ? v : v / cnt;
      }
      return o;
    };
    var bgm = pass(pass(cell, 0), 1);
    Func<int, int, int, double> bgAt = (x, y, j) => {
      double fx = Math.Max(0, Math.Min(cw - 1.001, (x + 0.5) / C - 0.5)), fy = Math.Max(0, Math.Min(ch - 1.001, (y + 0.5) / C - 0.5));
      int gx = (int)fx, gy = (int)fy; double ax = fx - gx, ay = fy - gy;
      return (bgm[j, gy * cw + gx] * (1 - ax) + bgm[j, gy * cw + gx + 1] * ax) * (1 - ay) +
             (bgm[j, (gy + 1) * cw + gx] * (1 - ax) + bgm[j, (gy + 1) * cw + gx + 1] * ax) * ay;
    };
    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) {
      int i = y * st + x * 4, p = y * W + x;
      if (solid[p]) { px[i + 3] = 255; continue; }   // (the body and its inner details: as drawn)
      double[] c = { px[i + 2], px[i + 1], px[i] }, k = { bgAt(x, y, 0) + 3, bgAt(x, y, 1) + 3, bgAt(x, y, 2) + 3 };
      double a = 0;
      for (int j = 0; j < 3; j++) if (c[j] > k[j]) a = Math.Max(a, (c[j] - k[j]) / (255 - k[j]));
      a = Math.Min(1, Math.Max(0, (a - 0.07) / 0.93) * 1.05);   // (the background's own grain: nothing)
      a = a * a * (3 - 2 * a) * 0.35 + a * 0.65;   // (a softer start, so no speckles where the glow fades out)
      if (a <= 0.004) { px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0; continue; }
      for (int j = 0; j < 3; j++) c[j] = Math.Max(0, Math.Min(255, k[j] + (c[j] - k[j]) / a));
      px[i + 2] = (byte)c[0]; px[i + 1] = (byte)c[1]; px[i] = (byte)c[2]; px[i + 3] = (byte)(a * 255);
    }
    Marshal.Copy(px, 0, d.Scan0, px.Length); b.UnlockBits(d);
    // (cropped to what is visible)
    int x0 = W, y0 = H, x1 = 0, y1 = 0;
    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (px[y * st + x * 4 + 3] > 10) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
    x0 = Math.Max(0, x0 - 8); y0 = Math.Max(0, y0 - 8); x1 = Math.Min(W - 1, x1 + 8); y1 = Math.Min(H - 1, y1 + 8);
    var crop = b.Clone(new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1), PixelFormat.Format32bppArgb);
    b.Dispose();
    return crop;
  }
  public static byte[] Square(Bitmap logo, int size, double fill) {
    var sq = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(sq)) {
      g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
      g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
      double sc = Math.Min(size * fill / logo.Width, size * fill / logo.Height);
      int w = (int)(logo.Width * sc), h = (int)(logo.Height * sc);
      g.DrawImage(logo, (size - w) / 2, (size - h) / 2, w, h);
    }
    var ms = new System.IO.MemoryStream(); sq.Save(ms, ImageFormat.Png); sq.Dispose();
    return ms.ToArray();
  }
}
"@
$s = [Drawing.Bitmap]::FromFile($src)
$logo = [Cut]::Make($s); $s.Dispose()
$logo.Save((Join-Path $here "mako_logo.png"), [Drawing.Imaging.ImageFormat]::Png)
"logo $($logo.Width)x$($logo.Height)"
# (the icon: several sizes, PNG inside)
$sizes = 256, 64, 48, 32, 16
$imgs = @(); foreach ($z in $sizes) { $imgs += ,([Cut]::Square($logo, $z, 0.98)) }
$out = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $z = $sizes[$i]; $b = if ($z -ge 256) { 0 } else { $z }
  $bw.Write([Byte]$b); $bw.Write([Byte]$b); $bw.Write([Byte]0); $bw.Write([Byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$imgs[$i].Length); $bw.Write([UInt32]$offset)
  $offset += $imgs[$i].Length
}
foreach ($im in $imgs) { $bw.Write($im) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $here "mako.ico"), $out.ToArray())
[IO.File]::WriteAllBytes((Join-Path $here "mako_icon_256.png"), $imgs[0])
"icon ok"

