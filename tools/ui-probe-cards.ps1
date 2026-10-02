Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$AE = [System.Windows.Automation.AutomationElement]
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,System.IntPtr e);' -Name M6 -Namespace W6 -PassThru | Out-Null
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L,T,Rt,B; }' -Name M6b -Namespace W6 -PassThru | Out-Null

$proc = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
$wr = New-Object W6.M6b+RECT
[W6.M6b]::GetWindowRect($proc.MainWindowHandle, [ref]$wr) | Out-Null
$ox = $wr.L + 8
$oy = $wr.T + 31

function ClickClient([int]$cx, [int]$cy) {
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ($ox + $cx), ($oy + $cy)
  Start-Sleep -Milliseconds 220
  [W6.M6]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 70
  [W6.M6]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 1200
}

function Status() {
  $p2 = Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Select-Object -First 1
  $win = $AE::FromHandle($p2.MainWindowHandle)
  $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($e in $all) {
    $n = $e.Current.Name
    if ($n -and ($n.StartsWith([char]0x4E3B + [char]0x9875 + " ") -or $n.StartsWith([char]0x5DF2 + [char]0x5207 + [char]0x6362 + [char]0x5230))) {
      Write-Output ("    -> " + $n); return
    }
  }
  Write-Output "    -> (unknown)"
}

Write-Output "=== 探针：沿 x=405 扫 y，找出「资源包合并」卡片 ==="
foreach ($y in 620, 660, 700, 740) {
  ClickClient 90 78          # 先回主页
  ClickClient 405 $y
  Write-Output ("  x=405 y=" + $y)
  Status
}

Write-Output ""
Write-Output "=== 探针：沿 x=1093 扫 y ==="
foreach ($y in 600, 640, 680, 720) {
  ClickClient 90 78
  ClickClient 1093 $y
  Write-Output ("  x=1093 y=" + $y)
  Status
}

Write-Output ""
Write-Output "=== 探针：设置小卡片 ==="
foreach ($y in 250, 290, 330) {
  ClickClient 90 78
  ClickClient 1093 $y
  Write-Output ("  x=1093 y=" + $y)
  Status
}
