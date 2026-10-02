Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$ErrorActionPreference = 'Continue'
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

# 用主窗口句柄直接拿 AutomationElement（避免标题里有中文导致匹配问题）
$proc = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "PROCESS_NOT_FOUND"; exit 1 }
$win = $AE::FromHandle($proc.MainWindowHandle)
if (-not $win) { Write-Output "ELEMENT_NOT_FOUND"; exit 1 }

$rect = $win.Current.BoundingRectangle
Write-Output ("WINDOW_SIZE: " + [int]$rect.Width + "x" + [int]$rect.Height)
Write-Output ""

function Dump($ct, $label) {
  Write-Output ("=== " + $label + " ===")
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ct)
  $els = $win.FindAll($TS::Descendants, $cond)
  Write-Output ("  count=" + $els.Count)
  foreach ($e in $els) {
    $n = $e.Current.Name
    $extra = ""
    if ($ct -eq $CT::CheckBox -or $ct -eq $CT::RadioButton) {
      try { $extra = " state=" + $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState } catch { }
    }
    if ($ct -eq $CT::Edit -or $ct -eq $CT::ComboBox) {
      try { $extra = " value=[" + $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value + "]" } catch { }
    }
    if ($ct -eq $CT::Button) { $extra = " enabled=" + $e.Current.IsEnabled }
    Write-Output ("  [" + $n + "]" + $extra)
  }
}

Dump $CT::Button "BUTTONS"
Dump $CT::CheckBox "CHECKBOXES"
Dump $CT::RadioButton "RADIOBUTTONS"
Dump $CT::ComboBox "COMBOBOXES"
Dump $CT::Edit "EDITS"

Write-Output "=== TABS ==="
$tc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::TabItem)
foreach ($e in $win.FindAll($TS::Descendants, $tc)) { Write-Output ("  [" + $e.Current.Name + "]") }

Write-Output "=== LISTS / TABLES ==="
foreach ($ct in @($CT::List, $CT::DataGrid, $CT::Table)) {
  $cc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ct)
  $els = $win.FindAll($TS::Descendants, $cc)
  Write-Output ("  " + ($ct.ProgrammaticName -replace 'ControlType\.','') + " count=" + $els.Count)
}

Write-Output "=== TEXT (<=60 chars) ==="
$ttc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text)
$texts = $win.FindAll($TS::Descendants, $ttc)
Write-Output ("  total text elements=" + $texts.Count)
$shown = 0
foreach ($t in $texts) {
  $n = $t.Current.Name
  if ($n -and $n.Length -gt 1) {
    if ($shown -lt 12) { Write-Output ("  " + $n); $shown++ }
  }
}

Write-Output "DONE"
