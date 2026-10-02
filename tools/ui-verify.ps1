# 多窗口尺寸 + 多页面状态批量截图，用来核对排版有没有错位
# 用法: powershell -File tools\ui-verify.ps1

$ErrorActionPreference = 'Stop'
$exe   = "D:\dsh\mcpack-converter\release\MCPackStudio.exe"
$outDir = "D:\dsh\mcpack-converter\tools\out"
$shot  = "D:\dsh\mcpack-converter\tools\ui-shot.ps1"

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sizes = @(
    @{ n = "default"; w = 1264; h = 861 },
    @{ n = "small";   w = 1016; h = 701 },
    @{ n = "wide";    w = 1600; h = 900 }
)

foreach ($s in $sizes) {
    Write-Output ("=== size: " + $s.n + " (" + $s.w + "x" + $s.h + ") ===")
    $layout = Join-Path $outDir ("v-" + $s.n + "-home.txt")
    & powershell -NoProfile -ExecutionPolicy Bypass -File $shot `
        -Out (Join-Path $outDir ("v-" + $s.n + "-home.png")) `
        -Layout $layout -Width $s.w -Height $s.h 2>&1 | Select-String -Pattern "ModuleCard|SHOT|WINDOW_RECT|Panel  bounds=0,1|Panel  bounds=0,7|bounds=0,0 1" | ForEach-Object { Write-Output ("  " + $_) }
}

Write-Output ""
Write-Output "DONE"
