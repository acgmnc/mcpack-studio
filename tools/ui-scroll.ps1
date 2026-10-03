Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms @"
using System;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;
public class Sv {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  public struct RECT { public int L,T,Rt,B; }
  public static string Grab(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    int w = r.Rt - r.L, ht = r.B - r.T;
    using (var bmp = new Bitmap(w, ht)) {
      using (var g = Graphics.FromImage(bmp)) {
        IntPtr hdc = g.GetHdc();
        PrintWindow(h, hdc, 2);
        g.ReleaseHdc(hdc);
      }
      bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    return "ok";
  }
  public static void Click(IntPtr win, int cx, int cy) {
    RECT r; GetWindowRect(win, out r);
    SetCursorPos(r.L + 8 + cx, r.T + 31 + cy);
    System.Threading.Thread.Sleep(300);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(95);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(1500);
  }
  public static void Wheel(IntPtr win, int cx, int cy, int clicks) {
    RECT r; GetWindowRect(win, out r);
    SetCursorPos(r.L + 8 + cx, r.T + 31 + cy);
    System.Threading.Thread.Sleep(250);
    for (int i = 0; i < Math.Abs(clicks); i++) {
      mouse_event(0x0800, 0, 0, clicks > 0 ? unchecked((uint)-120) : 120, IntPtr.Zero);
      System.Threading.Thread.Sleep(70);
    }
    System.Threading.Thread.Sleep(700);
  }
}
"@

$out = "D:\dsh\mcpack-converter\shots-verify"
if (!(Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }

$p = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
$h = $p.MainWindowHandle

# 窗口挪到 (40,40)，这样客户区原点可预测（边框 8, 标题栏 31）
[Sv]::MoveWindow($h, 40, 40, 1256, 859, $true) | Out-Null
[Sv]::ShowWindow($h, 5) | Out-Null
[Sv]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 900

# 顶部导航「资源包」pill：client 约 (155,31)
[Sv]::Click($h, 155, 31)
# 子模块条「合并资源包」：client 约 (326,143)
[Sv]::Click($h, 326, 143)
[Sv]::Grab($h, (Join-Path $out "merge-top.png")) | Out-Null
Write-Output "  saved merge-top.png"

# 在页面中部滚轮向下，验证滚动
[Sv]::Wheel($h, 600, 500, 5)
[Sv]::Grab($h, (Join-Path $out "merge-scrolled.png")) | Out-Null
Write-Output "  saved merge-scrolled.png"

# 对比两张是否不同（不同 = 滚动生效）
Add-Type -AssemblyName System.Drawing
$a = [System.Drawing.Bitmap]::FromFile((Join-Path $out "merge-top.png"))
$b = [System.Drawing.Bitmap]::FromFile((Join-Path $out "merge-scrolled.png"))
$diff = 0
for ($y = 160; $y -lt 780; $y += 6) {
  for ($x = 20; $x -lt 1200; $x += 6) {
    if ($a.GetPixel($x,$y).ToArgb() -ne $b.GetPixel($x,$y).ToArgb()) { $diff++ }
  }
}
$a.Dispose(); $b.Dispose()
Write-Output ("  两张截图采样点差异数: " + $diff + "  (>0 说明滚动生效)")
