# =============================================================
#  publish.ps1  —  Publica InventarioWeb y genera un ZIP listo
#  Ejecutar en la maquina de desarrollo:
#     powershell -ExecutionPolicy Bypass -File .\publish.ps1
# =============================================================

$version      = "2.0"
$projectDir   = $PSScriptRoot                          # carpeta donde esta este script
$publishDir   = Join-Path $projectDir "PublishOutput"
$zipName      = "InventarioWeb_v$version.zip"
$zipPath      = Join-Path $projectDir $zipName

# ── 1. Limpiar publicacion anterior ─────────────────────────
if (Test-Path $publishDir) {
    Write-Host "Limpiando PublishOutput anterior..." -ForegroundColor Yellow
    Remove-Item $publishDir -Recurse -Force
}

# ── 2. Publicar en modo Release ──────────────────────────────
Write-Host ""
Write-Host "Publicando InventarioWeb v$version (Release)..." -ForegroundColor Cyan

dotnet publish "$projectDir\InventarioWeb.csproj" `
    --configuration Release `
    --output "$publishDir" `
    --no-self-contained `
    --runtime win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: dotnet publish fallo con codigo $LASTEXITCODE" -ForegroundColor Red
    exit 1
}

# ── 3. Comprimir en ZIP ──────────────────────────────────────
Write-Host ""
Write-Host "Creando ZIP: $zipName ..." -ForegroundColor Cyan

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath)

$sizeMB = [Math]::Round((Get-Item $zipPath).Length / 1MB, 2)

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Publicacion completada - v$version" -ForegroundColor Green
Write-Host "  Archivo: $zipName ($sizeMB MB)" -ForegroundColor Green
Write-Host "  Ruta:    $zipPath" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Proximos pasos:" -ForegroundColor White
Write-Host "  1. Copia '$zipName' al servidor" -ForegroundColor Gray
Write-Host "  2. En el servidor, ejecuta deploy-iis.ps1" -ForegroundColor Gray
