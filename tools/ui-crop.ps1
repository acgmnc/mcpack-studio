# 把截图的某个区域放大另存，用来细看漏白的到底是什么控件
# 用法: powershell -File tools\ui-crop.ps1 -In a.png -Out b.png -Y 660 -H 80
param(
    [Parameter(Mandatory=$true)][string]$In,
    [Parameter(Mandatory=$true)][string]$Out,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$W = 0,
    [int]$H = 80,
    [int]$Zoom = 2
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile($In)
if ($W -le 0) { $W = $src.Width }
if ($Y + $H -gt $src.Height) { $H = $src.Height - $Y }
if ($X + $W -gt $src.Width) { $W = $src.Width - $X }

$rect = New-Object System.Drawing.Rectangle $X, $Y, $W, $H
$crop = $src.Clone($rect, $src.PixelFormat)
$src.Dispose()

$bmp = New-Object System.Drawing.Bitmap -ArgumentList ($W * $Zoom), ($H * $Zoom)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($crop, 0, 0, $bmp.Width, $bmp.Height)
$g.Dispose()
$crop.Dispose()

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("CROP: " + $Out + "  " + $W + "x" + $H + " @ " + $X + "," + $Y + "  zoom=" + $Zoom)
