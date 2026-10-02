Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class UX {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  public struct RECT { public int L,T,Rt,B; }
  public static IntPtr Find(uint pid) {
    IntPtr main = IntPtr.Zero;
    EnumWindows((h,l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      var c = new StringBuilder(256); GetClassName(h,c,256);
      if (c.ToString().StartsWith("WindowsForms10.Window")) { main = h; return false; }
      return true;
    }, IntPtr.Zero);
    return main;
  }
}
"@

function Shot($path,$h) {
  $r = New-Object UX+RECT
  [UX]::GetWindowRect($h, [ref]$r) | Out-Null
  $w=$r.Rt-$r.L; $ht=$r.B-$r.T
  $bmp = New-Object System.Drawing.Bitmap $w,$ht
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size $w,$ht))
  $g.Dispose(); $bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Write-Output ("  shot: " + (Split-Path $path -Leaf))
}

function Click([int]$x,[int]$y) {
  [UX]::SetCursorPos($x,$y) | Out-Null
  Start-Sleep -Milliseconds 220
  [UX]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 80
  [UX]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 1100
}

$p = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
$h = [UX]::Find([uint32]$p.Id)
[UX]::BringWindowToTop($h) | Out-Null
[UX]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1300
$r = New-Object UX+RECT
[UX]::GetWindowRect($h, [ref]$r) | Out-Null
$X = $r.L; $Y = $r.T
Write-Output ("window origin: $X,$Y")

# 导航面板客户区：x 偏移约 8，主模块行从 y≈76 开始，行高 38，组间距 4；子模块行高 31
# 通过点击主模块标题来展开/折叠并选中
$navX = $X + 8 + 90
$base = $Y + 31

# 版本转换组：主 76..114, 子1(资源包转换) 114..145, 子2(批量转换) 145..176, 间距 4
# 资源包合并：主 180..218, 子(合并向导) 218..249, 间距 4
# 资源包编辑：主 253..291, 子(内容浏览)291..322, 子(pack.mcmeta)322..353, 子(新建)353..384, 间距4
# 工具箱：主 388..426, 子(批量重命名)426..457, 子(版本对照表)457..488, 子(图标)488..519, 间距4
# 设置与帮助：主 523..561, 子(转换规则)561..592, 子(关于与自测)592..623

Write-Output "--- 批量转换 ---"
Click $navX ($base + 160)
Shot "D:\dsh\mcpack-converter\p1-batch.png" $h

Write-Output "--- 合并向导（规划中）---"
Click $navX ($base + 233)
Shot "D:\dsh\mcpack-converter\p2-merge.png" $h

Write-Output "--- 工具箱 · 版本对照表 ---"
Click $navX ($base + 472)
Shot "D:\dsh\mcpack-converter\p3-versiontable.png" $h

Write-Output "--- 设置 · 转换规则说明 ---"
Click $navX ($base + 576)
Shot "D:\dsh\mcpack-converter\p4-rules.png" $h

Write-Output "--- 设置 · 关于与自测 ---"
Click $navX ($base + 607)
Shot "D:\dsh\mcpack-converter\p5-about.png" $h

Write-Output "done"
