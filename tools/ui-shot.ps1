# 启动 MCPackStudio，截取窗口截图，并导出控件布局 dump
# 用法: powershell -File tools\ui-shot.ps1 -Out shot.png [-Layout layout.txt] [-Args "--layout","x"]
param(
    [string]$Exe = "D:\dsh\mcpack-converter\release\MCPackStudio.exe",
    [string]$Out = "D:\dsh\mcpack-converter\tools\out\shot.png",
    [string]$Layout = "",
    [int]$WaitMs = 2600,
    [int]$Width = 0,
    [int]$Height = 0,
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -Namespace Shot -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@

[Shot.Native]::SetProcessDPIAware() | Out-Null

# 结束可能残留的实例
Get-Process -Name MCPackStudio -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$argList = New-Object System.Collections.ArrayList
if ($Layout) {
    [void]$argList.Add("--layout")
    [void]$argList.Add($Layout)
}
foreach ($a in $ExtraArgs) { [void]$argList.Add($a) }

$proc = if ($argList.Count -gt 0) {
    Start-Process -FilePath $Exe -ArgumentList $argList.ToArray() -PassThru
} else {
    Start-Process -FilePath $Exe -PassThru
}
Start-Sleep -Milliseconds $WaitMs

$proc.Refresh()
if ($proc.HasExited) {
    Write-Output "PROCESS_EXITED code=$($proc.ExitCode)"
    exit 1
}

$h = $proc.MainWindowHandle
if ($h -eq [IntPtr]::Zero) {
    for ($i = 0; $i -lt 20 -and $h -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
        $h = $proc.MainWindowHandle
    }
}
if ($h -eq [IntPtr]::Zero) { Write-Output "NO_WINDOW"; $proc | Stop-Process -Force; exit 1 }

if ($Width -gt 0 -and $Height -gt 0) {
    [Shot.Native]::MoveWindow($h, 40, 40, $Width, $Height, $true) | Out-Null
    Start-Sleep -Milliseconds 2500
}

[Shot.Native]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600

$r = New-Object Shot.Native+RECT
[Shot.Native]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
Write-Output ("WINDOW_RECT: " + $r.Left + "," + $r.Top + "  " + $w + "x" + $ht)

$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $ht))
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("SHOT: " + $Out)

if ($Layout -and (Test-Path $Layout)) {
    Write-Output "---- LAYOUT DUMP ----"
    Get-Content $Layout -Encoding UTF8 | ForEach-Object { Write-Output $_ }
}

$proc | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Output "DONE"
