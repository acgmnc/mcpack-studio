Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,System.IntPtr e);' -Name MB -Namespace WB -PassThru | Out-Null
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L,T,Rt,B; }' -Name MBb -Namespace WB -PassThru | Out-Null

# expected UI strings, built from code points to keep this file pure ASCII
$SEL_MODULE = [string][char]0x9009 + [char]0x62E9 + [char]0x6A21 + [char]0x5757          # "select module"
$SEL_WHAT   = [string][char]0x9009 + [char]0x62E9 + [char]0x8981 + [char]0x505A + [char]0x4EC0 + [char]0x4E48  # "select what to do"
$HOME_PFX   = [string][char]0x4E3B + [char]0x9875 + " "                                  # "home "
$SWITCH_PFX = [string][char]0x5DF2 + [char]0x5207 + [char]0x6362 + [char]0x5230            # "switched to"

$proc = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
$wr = New-Object WB.MBb+RECT
[WB.MBb]::GetWindowRect($proc.MainWindowHandle, [ref]$wr) | Out-Null
$ox = $wr.L + 8
$oy = $wr.T + 31

function ClickClient([int]$cx, [int]$cy, [string]$what) {
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ($ox + $cx), ($oy + $cy)
  Start-Sleep -Milliseconds 240
  [WB.MB]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 70
  [WB.MB]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 1400
  Write-Output ("  click " + $what)
}

function Trace() {
  $p2 = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
  $win = $AE::FromHandle($p2.MainWindowHandle)
  $all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $st = ""; $page = "?"
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

Trace

Write-Output "== nav: tools =="
ClickClient 90 462 "nav tools"
Trace

Write-Output "== nav: home =="
ClickClient 90 78 "nav home"
Trace

Write-Output "== home card: tools (middle) =="
ClickClient 750 340 "home tools card"
Trace

Write-Output "== nav: home =="
ClickClient 90 78 "nav home"
Trace

Write-Output "== home card: settings (right) =="
ClickClient 1100 340 "home settings card"
Trace
