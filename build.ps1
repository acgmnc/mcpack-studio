# MCPack Studio 构建脚本
# 用 Windows 自带的 .NET Framework 编译器，不需要 Visual Studio / MSBuild

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$ref  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"

if (-not (Test-Path $csc)) {
    Write-Error "找不到 C# 编译器：$csc`n请确认已安装 .NET Framework 4.x"
}

$outDir = Join-Path $root "release"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sources = Get-ChildItem (Join-Path $root "src\*.cs") | ForEach-Object { $_.FullName }
$exe = Join-Path $outDir "MCPackStudio.exe"

Write-Host "编译 $($sources.Count) 个源文件..." -ForegroundColor Cyan

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /langversion:5 `
    "/out:$exe" `
    "/reference:$ref\System.dll" `
    "/reference:$ref\System.Core.dll" `
    "/reference:$ref\System.Drawing.dll" `
    "/reference:$ref\System.Windows.Forms.dll" `
    "/reference:$ref\System.IO.Compression.dll" `
    "/reference:$ref\System.IO.Compression.FileSystem.dll" `
    $sources

if ($LASTEXITCODE -ne 0) {
    Write-Error "编译失败（exit $LASTEXITCODE）"
}

# 使用说明
$readme = Join-Path $root "README.md"
if (Test-Path $readme) {
    Copy-Item $readme (Join-Path $outDir "使用说明.md") -Force
}

$size = [math]::Round((Get-Item $exe).Length / 1KB, 1)
Write-Host ""
Write-Host "构建完成" -ForegroundColor Green
Write-Host "  输出: $exe"
Write-Host "  体积: $size KB"
Write-Host ""
Write-Host "运行自测: `"$exe`" --selftest selftest.txt" -ForegroundColor DarkGray
Write-Host "命令行转换: `"$exe`" --convert <源> --to 1.20.1 --out <输出>" -ForegroundColor DarkGray
