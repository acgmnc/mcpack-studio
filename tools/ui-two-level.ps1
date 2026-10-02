Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,System.IntPtr e);' -Name MA -Namespace WA -PassThru | Out-Null
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L,T,Rt,B; }' -Name MAb -Namespace WA -PassThru | Out-Null

$proc = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
$wr = New-Object WA.MAb+RECT
[WA.MAb]::GetWindowRect($proc.MainWindowHandle, [ref]$wr) | Out-Null
$ox = $wr.L + 8
$oy = $wr.T + 31
Write-Output ("window " + $wr.L + "," + $wr.T + "  client origin " + $ox + "," + $oy)

function ClickClient([int]$cx, [int]$cy, [string]$what) {
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ($ox + $cx), ($oy + $cy)
  Start-Sleep -Milliseconds 240
  [WA.MA]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 70
  [WA.MA]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 1400
  Write-Output ("  click " + $what + " @client(" + $cx + "," + $cy + ")")
}

function Dump([string]$tag, [int]$max) {
  $p2 = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
  $win = $AE::FromHandle($p2.MainWindowHandle)
  $all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $names = New-Object System.Collections.ArrayList
  foreach ($e in $all) {
    $n = $e.Current.Name
    if ($n -and $n.Trim().Length -gt 0) { [void]$names.Add($n.Trim()) }
  }
  Write-Output ("--- " + $tag + " (" + $names.Count + " named) ---")
  $k = 0
  foreach ($n in $names) {
    if ($k -ge $max) { Write-Output ("  ... +" + ($names.Count - $max) + " more"); break }
    $d = $n
    if ($d.Length -gt 80) { $d = $d.Substring(0,80) + [char]0x2026 }
    Write-Output ("  " + $d)
    $k++
  }
  Write-Output ""
}

Dump "HOME (module list)" 22

# 点「资源包」卡片本体（第一排左边）
ClickClient 400 340 "资源包 module card"
Dump "AFTER entering 资源包 (row1) + plus buttons" 40
