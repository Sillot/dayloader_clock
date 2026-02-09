# ============================================================
#  DayloaderClock - Build & Publish Script
#  Usage:  .\build.ps1
# ============================================================

$ErrorActionPreference = "Stop"

Write-Host "
DayloaderClock - Build
" -ForegroundColor Cyan

# Kill running instance
$proc = Get-Process -Name "DayloaderClock" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "  [STOP] Arret de l'instance en cours..." -ForegroundColor Yellow
    Stop-Process -Name "DayloaderClock" -Force
    Start-Sleep -Milliseconds 500
}

# Clean
Write-Host "  [CLEAN] Nettoyage..." -ForegroundColor Gray
dotnet clean -c Release --nologo -v q 2>$null

# Publish
Write-Host "  [BUILD] Publication (self-contained, single-file)..." -ForegroundColor Gray
dotnet publish -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o .\publish `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "
[FAIL] Echec de la publication.
" -ForegroundColor Red
    exit 1
}

# Result
$exe = Get-Item ".\publish\DayloaderClock.exe"
$sizeMB = [math]::Round($exe.Length / 1MB, 1)
$ver = (Get-Item ".\publish\DayloaderClock.exe").VersionInfo.FileVersion

Write-Host "
[OK] Build reussi !" -ForegroundColor Green
Write-Host "   Version: $ver" -ForegroundColor Cyan
Write-Host "   Fichier: $($exe.FullName)" -ForegroundColor White
Write-Host "   Taille:  $sizeMB Mo" -ForegroundColor White
Write-Host "
   Pour lancer: .\publish\DayloaderClock.exe
"
