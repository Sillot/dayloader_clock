# ============================================================
#  DayloaderClock — Build & Publish Script
#  Usage:  .\build.ps1
# ============================================================

$ErrorActionPreference = "Stop"

Write-Host "`n🔨 DayloaderClock — Build`n" -ForegroundColor Cyan

# Kill running instance
$proc = Get-Process -Name "DayloaderClock" -ErrorAction SilentlyContinue
if ($proc) {
  Write-Host "  ⏹  Arrêt de l'instance en cours..." -ForegroundColor Yellow
  Stop-Process -Name "DayloaderClock" -Force
  Start-Sleep -Milliseconds 500
}

# Clean
Write-Host "  🧹 Nettoyage..." -ForegroundColor Gray
dotnet clean -c Release --nologo -v q 2>$null

# Publish
Write-Host "  📦 Publication (self-contained, single-file)..." -ForegroundColor Gray
dotnet publish -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o .\publish `
  --nologo

if ($LASTEXITCODE -ne 0) {
  Write-Host "`n❌ Échec de la publication.`n" -ForegroundColor Red
  exit 1
}

# Result
$exe = Get-Item ".\publish\DayloaderClock.exe"
$sizeMB = [math]::Round($exe.Length / 1MB, 1)

Write-Host "`n✅ Build réussi !" -ForegroundColor Green
Write-Host "   📁 $($exe.FullName)" -ForegroundColor White
Write-Host "   📏 $sizeMB Mo" -ForegroundColor White
Write-Host "`n   Pour lancer : .\publish\DayloaderClock.exe`n"
