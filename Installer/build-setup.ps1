# LiwaPlayer kurulum paketi üretir: Installer\Output\LiwaPlayer-Setup-<sürüm>.exe
# Kullanım:  .\build-setup.ps1            (sürüm: 1.0.0)
#            .\build-setup.ps1 -Version 1.1.0

param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot "LiwaPlayer\LiwaPlayer.csproj"
$publishDir = Join-Path $PSScriptRoot "publish"
$script = Join-Path $PSScriptRoot "LiwaPlayer.iss"

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 bulunamadı. https://jrsoftware.org/isdl.php adresinden kurun."
}

Write-Host "1/2  Uygulama yayınlanıyor (self-contained, win-x64, v$Version)..." -ForegroundColor Cyan

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

# Self-contained: hedef makinede .NET kurulu olması gerekmez
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none -o $publishDir -v q -nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish başarısız oldu."
}

Write-Host "2/2  Kurulum paketi derleniyor..." -ForegroundColor Cyan

& $iscc "/DAppVersion=$Version" "/Qp" $script

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup derlemesi başarısız oldu."
}

$output = Join-Path $PSScriptRoot "Output\LiwaPlayer-Setup-$Version.exe"
$sizeMb = [math]::Round((Get-Item $output).Length / 1MB, 1)

Write-Host "Hazır: $output  ($sizeMb MB)" -ForegroundColor Green
