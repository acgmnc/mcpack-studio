# 验证自绘控件在真实交互下是否正常：点开下拉框、点选条目、截图
# 用法: powershell -File tools\ui-interact.ps1
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace IN -Name Nat -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@
[IN.Nat]::SetProcessDPIAware() | Out-Null

$out = "D:\dsh\mcpack-converter\tools\out\interact"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Get-Process MCPackStudio -EA SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$p = Start-Process -FilePath "D:\dsh\mcpack-converter\release\MCPackStudio.exe" -PassThru
Start-Sleep -Seconds 3
$p.Refresh()
$h = $p.MainWindowHandle
[IN.Nat]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 400

$r = New-Object IN.Nat+RECT
[IN.Nat]::GetWindowRect($h, [ref]$r) | Out-Null
# 客户区原点：边框 8，标题栏 31
$ox = $r.L + 8; $oy = $r.T + 31
Write-Output ("window=" + $r.L + "," + $r.T + "  client origin=" + $ox + "," + $oy)

function Shot([string]$name) {
  $w = $r.R - $r.L; $ht = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap -ArgumentList $w, $ht
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $ht))
  $g.Dispose()
  $bmp.Save((Join-Path $out ($name + ".png")), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Output ("  shot " + $name)
}

function ClickClient([int]$cx, [int]$cy, [string]$what) {
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ($ox + $cx), ($oy + $cy)
  Start-Sleep -Milliseconds 320
  [IN.Nat]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 90
  [IN.Nat]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 800
  Write-Output ("  click " + $what + " @client(" + $cx + "," + $cy + ")")
}

# 首页 -> 资源包 -> 版本转换（含 FlatSelect 版本下拉框）
ClickClient 155 106 "nav 资源包"
Shot "01-pack-landing"
ClickClient 105 150 "sub 版本转换"
Shot "02-convert"

# 点开版本下拉框（FlatSelect 的核心交互）
ClickClient 200 440 "FlatSelect 版本下拉"
Shot "03-dropdown-open"

# 选第二项
ClickClient 200 470 "下拉第二项"
Shot "04-after-select"
Start-Sleep -Milliseconds 500

# 切到「转换报告」页签（PageTabs 交互）
ClickClient 175 690 "PageTabs 转换报告"
Shot "05-report-tab"
ClickClient 105 690 "PageTabs 改动明细"
Shot "06-preview-tab"

# 去「新建资源包」看命名空间输入框
ClickClient 720 150 "sub 新建资源包"
Shot "07-new-pack"

Get-Process MCPackStudio -EA SilentlyContinue | Stop-Process -Force
Write-Output "DONE"
