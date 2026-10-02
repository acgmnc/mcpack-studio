Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$ELL = [string][char]0x2026

$proc = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "PROCESS_NOT_FOUND"; exit 1 }
$win = $AE::FromHandle($proc.MainWindowHandle)

$all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$names = New-Object System.Collections.ArrayList
foreach ($e in $all) {
  $n = $e.Current.Name
  if ($n -and $n.Trim().Length -gt 0) { [void]$names.Add($n.Trim()) }
}

Write-Output ("TOTAL ELEMENTS: " + $all.Count + "   NAMED: " + $names.Count)
Write-Output ""
Write-Output "=== VISIBLE TEXT IN ORDER ==="
$i = 0
foreach ($n in $names) {
  $i++
  $disp = $n
  if ($disp.Length -gt 88) { $disp = $disp.Substring(0, 88) + $ELL }
  Write-Output ("  [" + $i + "] " + $disp)
}
Write-Output ""
Write-Output "DONE"
