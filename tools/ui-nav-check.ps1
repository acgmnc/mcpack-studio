Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$ELL = [string][char]0x2026

$proc = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
$win = $AE::FromHandle($proc.MainWindowHandle)
$rect = $win.Current.BoundingRectangle

function ClickClient([int]$cx, [int]$cy) {
  $x = [int]$rect.Left + 8 + $cx
  $y = [int]$rect.Top + 31 + $cy
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point $x, $y
  Start-Sleep -Milliseconds 200
  Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,System.IntPtr e);' -Name M -Namespace Win32 -PassThru | Out-Null
  [Win32.M]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 70
  [Win32.M]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 1200
}

function Dump([string]$tag) {
  $all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $out = New-Object System.Collections.ArrayList
  foreach ($e in $all) {
    $n = $e.Current.Name
    if ($n -and $n.Trim().Length -gt 0) { [void]$out.Add($n.Trim()) }
  }
  Write-Output ("--- " + $tag + " (" + $out.Count + " named) ---")
  foreach ($n in $out) {
    $d = $n
    if ($d.Length -gt 86) { $d = $d.Substring(0,86) + $ELL }
    Write-Output ("  " + $d)
  }
  Write-Output ""
}

Write-Output ("WINDOW " + [int]$rect.Width + "x" + [int]$rect.Height)
Dump "BEFORE"

# nav rows measured from screenshots
ClickClient 88 515      # settings > rules
Dump "AFTER CLICK rules"

ClickClient 88 436      # tools > version table
Dump "AFTER CLICK version table"
