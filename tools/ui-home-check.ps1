Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,System.IntPtr e);' -Name M5 -Namespace W5 -PassThru | Out-Null
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L,T,Rt,B; }' -Name M5b -Namespace W5 -PassThru | Out-Null

$proc = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
$wr = New-Object W5.M5b+RECT
[W5.M5b]::GetWindowRect($proc.MainWindowHandle, [ref]$wr) | Out-Null

$originX = $wr.L + 8
$originY = $wr.T + 31
Write-Output ("window: " + $wr.L + "," + $wr.T + "   client origin: " + $originX + "," + $originY)

function ClickClient([int]$cx, [int]$cy, [string]$what) {
  $x = $originX + $cx
  $y = $originY + $cy
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point $x, $y
  Start-Sleep -Milliseconds 250
  [W5.M5]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 80
  [W5.M5]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 1500
  Write-Output ("  clicked " + $what + " client(" + $cx + "," + $cy + ")")
}

function Status() {
  $p2 = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $p2) { Write-Output "  STATUS: (no process)"; return }
  $win = $AE::FromHandle($p2.MainWindowHandle)
  $all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($e in $all) {
    $n = $e.Current.Name
    if ($n -and ($n.StartsWith("主页 ") -or $n.StartsWith("已切换到"))) { Write-Output ("  STATUS: " + $n); return }
  }
  Write-Output "  STATUS: (not found)"
}

Write-Output ""
Write-Output "== initial =="; Status

Write-Output "== nav: home =="
ClickClient 90 78 "home nav"; Status

Write-Output "== card: merge =="
ClickClient 405 700 "merge card"; Status

Write-Output "== nav: home =="
ClickClient 90 78 "home nav"; Status

Write-Output "== card: browse =="
ClickClient 1093 700 "browse card"; Status

Write-Output "== nav: home =="
ClickClient 90 78 "home nav"; Status

Write-Output "== card: settings =="
ClickClient 1093 336 "settings card"; Status

Write-Output "== nav: home =="
ClickClient 90 78 "home nav"; Status

Write-Output "== card: convert =="
ClickClient 1093 506 "convert card"; Status
