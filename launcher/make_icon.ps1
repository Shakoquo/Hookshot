# Makes launcher.ico (and hookshot_gold.png) from the builder's hookshot picture: every pixel's brightness mapped onto a gold ramp,
# alpha kept, centred on a 256x256 square.
param([string]$src = "C:\hookshot\builder-main\tools\builder\HookshotBuilder\Images\hookshot.png")
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class Gold {
  static int L(double a, double b, double t) { return (int)Math.Round(a + (b - a) * t); }
  public static Bitmap Make(Bitmap s) {
    var b = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(b)) g.DrawImage(s, 0, 0, s.Width, s.Height);
    var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var px = new byte[d.Stride * b.Height]; Marshal.Copy(d.Scan0, px, 0, px.Length);
    // (dark bronze -> gold -> pale highlight)
    double[][] ramp = { new double[]{40,22,4}, new double[]{140,88,20}, new double[]{232,184,76}, new double[]{255,236,170}, new double[]{255,252,235} };
    for (int i = 0; i < px.Length; i += 4) {
      if (px[i + 3] == 0) continue;
      double l = (0.299 * px[i + 2] + 0.587 * px[i + 1] + 0.114 * px[i]) / 255.0;
      l = Math.Pow(l, 0.8) * (ramp.Length - 1);
      int k = Math.Min((int)l, ramp.Length - 2); double t = l - k;
      px[i + 2] = (byte)L(ramp[k][0], ramp[k + 1][0], t); px[i + 1] = (byte)L(ramp[k][1], ramp[k + 1][1], t); px[i] = (byte)L(ramp[k][2], ramp[k + 1][2], t);
    }
    Marshal.Copy(px, 0, d.Scan0, px.Length); b.UnlockBits(d);
    return b;
  }
}
"@
$s = [Drawing.Bitmap]::FromFile($src)
$gold = [Gold]::Make($s); $s.Dispose()
$gold.Save((Join-Path $here "hookshot_gold.png"))
$sq = New-Object Drawing.Bitmap 256, 256
$g = [Drawing.Graphics]::FromImage($sq); $g.InterpolationMode = "HighQualityBicubic"
$scale = [Math]::Min(248.0 / $gold.Width, 248.0 / $gold.Height)
$w = [int]($gold.Width * $scale); $h = [int]($gold.Height * $scale)
$g.DrawImage($gold, [int]((256 - $w) / 2), [int]((256 - $h) / 2), $w, $h); $g.Dispose()
$ms = New-Object IO.MemoryStream; $sq.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $png = $ms.ToArray()
$out = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
$bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0)
$bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22); $bw.Write($png); $bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $here "launcher.ico"), $out.ToArray())
"icon ok"
