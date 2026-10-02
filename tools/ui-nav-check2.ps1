Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$ELL = [string][char]0x2026

Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,System.IntPtr e);' -Name M2 -Namespace W2 -PassThru | Out-Null

$proc = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
$win = $AE::FromHandle($proc.MainWindowHandle)
$rect = $win.Current.BoundingRectangle

function ClickClient([int]$cx, [int]$cy) {
  $x = [int]$rect.Left + 8 + $cx
  $y = [int]$rect.Top + 31 + $cy
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point $x, $y
  Start-Sleep -Milliseconds 220
  [W2.M2]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 70
  [W2.M2]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 1300
}

function Dump([string]$tag, [int]$max) {
  $all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $out = New-Object System.Collections.ArrayList
  foreach ($e in $all) {
    $n = $e.Current.Name
    if ($n -and $n.Trim().Length -gt 0) { [void]$out.Add($n.Trim()) }
  }
  Write-Output ("--- " + $tag + " (" + $out.Count + " named) ---")
  $k = 0
  foreach ($n in $out) {
    if ($k -ge $max) { Write-Output ("  ... and " + ($out.Count - $max) + " more"); break }
    $d = $n
    if ($d.Length -gt 86) { $d = $d.Substring(0,86) + $ELL }
    Write-Output ("  " + $d)
    $k++
  }
  Write-Output ""
}

# nav y positions relative to client area
ClickClient 88 467      # tools > version table
Dump "VERSION TABLE" 36

ClickClient 88 576      # settings > about
Dump "ABOUT" 40
