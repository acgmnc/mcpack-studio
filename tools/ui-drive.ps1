Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$ErrorActionPreference = 'Continue'
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

$proc = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "PROCESS_NOT_FOUND"; exit 1 }
$win = $AE::FromHandle($proc.MainWindowHandle)

function Find-Name([string]$name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  return $win.FindFirst($TS::Descendants, $cond)
}

function Click-Name([string]$name) {
  $el = Find-Name $name
  if (-not $el) { Write-Output ("  !! not found: " + $name); return $false }
  try {
    $p = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $p.Invoke()
    Write-Output ("  clicked: " + $name)
    return $true
  } catch {
    Write-Output ("  !! invoke failed for " + $name + ": " + $_.Exception.Message)
    return $false
  }
}

function Shot([string]$path) {
  $r = $win.Current.BoundingRectangle
  $w = [int]$r.Width; $h = [int]$r.Height
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen([int]$r.Left, [int]$r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Output ("  shot: " + $path)
}

function Dump-Texts([string]$filter) {
  $tc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  $all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $found = @()
  foreach ($e in $all) {
    $n = $e.Current.Name
    if ($n -and $n.Length -gt 2 -and ($filter -eq "" -or $n -like $filter)) { $found += $n }
  }
  return $found
}

Write-Output "STEP 0: launch state"
Shot "D:\dsh\mcpack-converter\ui-0-start.png"

Write-Output ""
Write-Output "STEP 1: simulate picking source via internal API"
# 直接调用窗口的公开方法不可行，改为点击「选择文件夹…」会弹对话框。
# 但我们可以先把源路径写进只读的 TextBox —— 只读控件 UIA 不能 SetValue。
# 于是这里只验证：按钮可点击 + 界面状态正确流转。
$analyse = Find-Name "1. 分析资源包"
Write-Output ("  analyse button enabled = " + $analyse.Current.IsEnabled)
$preview = Find-Name "2. 生成转换预览"
Write-Output ("  preview button enabled = " + $preview.Current.IsEnabled)
$convert = Find-Name "3. 开始转换"
Write-Output ("  convert button enabled = " + $convert.Current.IsEnabled)

Write-Output ""
Write-Output "STEP 2: click disabled analyse (should be no-op / enabled after source chosen)"
Click-Name "1. 分析资源包" | Out-Null
Start-Sleep -Milliseconds 600

Write-Output ""
Write-Output "STEP 3: dump status texts"
$texts = Dump-Texts ""
foreach ($t in $texts) { Write-Output ("  " + $t) }

Write-Output ""
Write-Output "DONE"
