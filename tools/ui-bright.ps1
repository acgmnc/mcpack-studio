# 扫描截图里「过亮的像素」，用来自动定位跟深色主题冲突的控件
# 用法: powershell -File tools\ui-bright.ps1 [-Dir <目录>] [-Threshold 140]
param(
    [string]$Dir = "D:\dsh\mcpack-converter\tools\out\theme1",
    [int]$Threshold = 140,
    [int]$MinRegion = 30
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Get-ChildItem $Dir -Filter *.png | Sort-Object Name | ForEach-Object {
    $src = [System.Drawing.Bitmap]::FromFile($_.FullName)
    # 统一转成 32bppArgb，才能直接读像素缓冲区
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $src.Width, $src.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.DrawImage($src, 0, 0, $src.Width, $src.Height)
    $g.Dispose()
    $src.Dispose()

    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $buf = New-Object 'byte[]' ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    $bmp.UnlockBits($data)

    $rows = New-Object 'int[]' $h
    $total = 0
    for ($y = 0; $y -lt $h; $y++) {
        $n = 0
        $base = $y * $stride
        for ($x = 0; $x -lt $w; $x += 2) {
            $i = $base + $x * 4
            $lum = 0.114 * $buf[$i] + 0.587 * $buf[$i + 1] + 0.299 * $buf[$i + 2]
            if ($lum -gt $Threshold) { $n++ }
        }
        $rows[$y] = $n * 2
        $total += $rows[$y]
    }

    $bands = New-Object System.Collections.ArrayList
    $start = -1
    for ($y = 0; $y -lt $h; $y++) {
        if ($rows[$y] -ge $MinRegion) {
            if ($start -lt 0) { $start = $y }
        } else {
            if ($start -ge 0 -and ($y - $start) -ge 3) {
                $sum = 0; for ($k = $start; $k -lt $y; $k++) { $sum += $rows[$k] }
                [void]$bands.Add([pscustomobject]@{ Y = $start; H = $y - $start; Px = $sum })
            }
            $start = -1
        }
    }

    Write-Output ("=== " + $_.Name + "  亮像素=" + $total)
    foreach ($b in $bands) {
        Write-Output ("    y=" + $b.Y + ".." + ($b.Y + $b.H - 1) + "  高=" + $b.H + "  亮像素=" + $b.Px)
    }
    $bmp.Dispose()
}
Write-Output "DONE"
