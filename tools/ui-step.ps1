# Step-by-step UI interaction check with diagnostics
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace IN2 -Name Nat2 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@
[IN2.Nat2]::SetProcessDPIAware() | Out-Null

$out = "D:\dsh\mcpack-converter\tools\out\step"
Remove-Item $out -Recurse -Force -EA SilentlyContinue
New-Item -ItemType Directory -Force -Path $out | Out-Null
Get-Process MCPackStudio -EA SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$proc = Start-Process -FilePath "D:\dsh\mcpack-converter\release\MCPackStudio.exe" -PassThru
Start-Sleep -Seconds 3
$proc.Refresh()
if ($proc.HasExited) { Write-Output "FAIL: process exited on start"; exit 1 }

$h = $proc.MainWindowHandle
[IN2.Nat2]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500

$r = New-Object IN2.Nat2+RECT
[IN2.Nat2]::GetWindowRect($h, [ref]$r) | Out-Null
$script:ox = $r.L + 8
$script:oy = $r.T + 31
Write-Output ("client origin = " + $script:ox + "," + $script:oy + "  size=" + ($r.R - $r.L) + "x" + ($r.B - $r.T))

function Test-Alive {
    $proc.Refresh()
    if ($proc.HasExited) { Write-Output ("  !! process exited, code=" + $proc.ExitCode); return $false }
    return $true
}

function Save-Shot([string]$name) {
    if (-not (Test-Alive)) { return }
    try {
        $rr = New-Object IN2.Nat2+RECT
        [IN2.Nat2]::GetWindowRect($h, [ref]$rr) | Out-Null
        $w = $rr.R - $rr.L
        $ht = $rr.B - $rr.T
        $bmp = New-Object System.Drawing.Bitmap -ArgumentList $w, $ht
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($rr.L, $rr.T, 0, 0, (New-Object System.Drawing.Size $w, $ht))
        $g.Dispose()
        $bmp.Save((Join-Path $out ($name + ".png")), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Output ("  shot OK   " + $name)
    } catch {
        Write-Output ("  shot FAIL " + $name + " : " + $_.Exception.Message)
    }
}

function Invoke-Click([int]$cx, [int]$cy, [string]$what) {
    if (-not (Test-Alive)) { return }
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ($script:ox + $cx), ($script:oy + $cy)
    Start-Sleep -Milliseconds 350
    [IN2.Nat2]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [IN2.Nat2]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 900
    Write-Output ("  click " + $what)
}

Write-Output "STEP 0 home"
Save-Shot "00-home"

Write-Output "STEP 1 nav pack"
Invoke-Click 155 106 "nav pack"
Save-Shot "01-pack"

Write-Output "STEP 2 sub version-convert"
Invoke-Click 105 150 "sub version-convert"
Save-Shot "02-convert"

Write-Output "STEP 3 open FlatSelect"
Invoke-Click 120 423 "open FlatSelect"
Save-Shot "03-dropdown"

Get-Process MCPackStudio -EA SilentlyContinue | Stop-Process -Force
Write-Output "DONE"
