Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,System.IntPtr e);' -Name MC -Namespace WC -PassThru | Out-Null
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L,T,Rt,B; }' -Name MCb -Namespace WC -PassThru | Out-Null

$HOME_PFX   = [string][char]0x4E3B + [char]0x9875 + " "
$SWITCH_PFX = [string][char]0x5DF2 + [char]0x5207 + [char]0x6362 + [char]0x5230
$SEL_MODULE = [string][char]0x9009 + [char]0x62E9 + [char]0x6A21 + [char]0x5757
$SEL_WHAT   = [string][char]0x9009 + [char]0x62E9 + [char]0x8981 + [char]0x505A + [char]0x4EC0 + [char]0x4E48

function Get-OriginX() {
  $pr = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
  $wr = New-Object WC.MCb+RECT
  [WC.MCb]::GetWindowRect($pr.MainWindowHandle, [ref]$wr) | Out-Null
  return [int]$wr.L + 8
}

function Get-OriginY() {
  $pr = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
  $wr = New-Object WC.MCb+RECT
  [WC.MCb]::GetWindowRect($pr.MainWindowHandle, [ref]$wr) | Out-Null
  return [int]$wr.T + 31
}

function ClickClient([int]$cx, [int]$cy, [string]$what) {
  $x = (Get-OriginX) + $cx
  $y = (Get-OriginY) + $cy
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point $x, $y
  Start-Sleep -Milliseconds 240
  [WC.MC]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 70
  [WC.MC]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 1400
  Write-Output ("  click " + $what + " -> screen(" + $x + "," + $y + ")")
}

function Trace() {
  $p2 = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
  $win = $AE::FromHandle($p2.MainWindowHandle)
  $all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $st = ""; $page = "SUB-PAGE"
  foreach ($e in $all) {
    $n = $e.Current.Name
    if (-not $n) { continue }
    if ($n.StartsWith($HOME_PFX) -or $n.StartsWith($SWITCH_PFX)) { $st = $n }
    if ($n -eq $SEL_MODULE) { $page = "HOME" }
    if ($n -eq $SEL_WHAT) { $page = "PACK-LANDING" }
  }
  Write-Output ("    page=" + $page)
  Write-Output ("    status=" + $st)
}

Write-Output "== initial =="; Trace

# 顶部导航：资源包 pill（约 x=155,y=65 相对窗口，去掉标题栏偏移后 client y≈65-31=34）
Write-Output "== top nav: 资源包 =="
ClickClient 155 34 "nav pack"; Trace

Write-Output "== top nav: 工具箱 =="
ClickClient 290 34 "nav tools"; Trace

Write-Output "== top nav: 主页 =="
ClickClient 60 34 "nav home"; Trace

# 主页卡片：第一排中间（工具箱）
Write-Output "== home card: 工具箱 =="
ClickClient 630 255 "card tools"; Trace

Write-Output "== top nav: 主页 =="
ClickClient 60 34 "nav home"; Trace

# 主页卡片：第一排左边（资源包）
Write-Output "== home card: 资源包 =="
ClickClient 200 255 "card pack"; Trace
