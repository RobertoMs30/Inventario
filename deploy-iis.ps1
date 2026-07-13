# =============================================================
#  deploy-iis.ps1  -  Despliega InventarioWeb en IIS
#  Ejecutar en el SERVIDOR con permisos de Administrador:
#     powershell -ExecutionPolicy Bypass -File .\deploy-iis.ps1
# =============================================================

# ╔══════════════════════════════════════════════════════════╗
# ║  CONFIGURACION  -  Ajusta estos valores a tu servidor   ║
# ╚══════════════════════════════════════════════════════════╝

$appPoolName  = "DefaultAppPool"                       # Nombre del Application Pool en IIS  ← confirmar en Grupos de aplicaciones
$siteName     = "Default Web Site"                     # Nombre del sitio en IIS
$sitePath     = "E:\SISTEMAS\Sistema de Inventario"    # Ruta fisica del sitio en el servidor
$zipPath      = "$PSScriptRoot\InventarioWeb_v2.0.zip" # ZIP copiado al servidor
$backupDir    = "E:\SISTEMAS\Backups\InventarioWeb_v1.0_$(Get-Date -Format 'yyyyMMdd_HHmm')"

# ── Verificar que el ZIP existe ──────────────────────────────
if (-not (Test-Path $zipPath)) {
    Write-Host "ERROR: No se encontro el archivo ZIP en: $zipPath" -ForegroundColor Red
    Write-Host "Copia primero InventarioWeb_v2.0.zip a la misma carpeta que este script." -ForegroundColor Yellow
    exit 1
}

# ── Verificar que se ejecuta como administrador ──────────────
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: Ejecuta este script como Administrador." -ForegroundColor Red
    exit 1
}

Import-Module WebAdministration -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Desplegando InventarioWeb v2.0 en IIS" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# ── 1. Detener el Application Pool ──────────────────────────
Write-Host ""
Write-Host "[1/5] Deteniendo Application Pool '$appPoolName'..." -ForegroundColor Yellow

try {
    $pool = Get-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' `
            -filter "system.applicationHost/applicationPools/add[@name='$appPoolName']" `
            -name "state" -ErrorAction Stop

    if ((Get-WebConfigurationProperty `
            -pspath 'MACHINE/WEBROOT/APPHOST' `
            -filter "system.applicationHost/applicationPools/add[@name='$appPoolName']" `
            -name "state").Value -ne "Stopped") {
        Stop-WebAppPool -Name $appPoolName
        Start-Sleep -Seconds 3
    }
    Write-Host "   Application Pool detenido." -ForegroundColor Green
}
catch {
    # Alternativa si WebAdministration no esta disponible
    Write-Host "   Intentando con appcmd..." -ForegroundColor Gray
    & "$env:windir\system32\inetsrv\appcmd.exe" stop apppool /apppool.name:"$appPoolName"
    Start-Sleep -Seconds 3
}

# ── 2. Hacer backup de v1.0 ──────────────────────────────────
Write-Host ""
Write-Host "[2/5] Haciendo backup de v1.0 en: $backupDir" -ForegroundColor Yellow

if (Test-Path $sitePath) {
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    Copy-Item -Path "$sitePath\*" -Destination $backupDir -Recurse -Force
    Write-Host "   Backup completado." -ForegroundColor Green
} else {
    Write-Host "   Carpeta del sitio no encontrada - se creara nueva." -ForegroundColor Gray
    New-Item -ItemType Directory -Path $sitePath -Force | Out-Null
}

# ── 3. Limpiar archivos anteriores (excepto logs y appsettings) ──
Write-Host ""
Write-Host "[3/5] Reemplazando archivos de la aplicacion..." -ForegroundColor Yellow

# Eliminar todo EXCEPTO appsettings.json y carpeta logs
Get-ChildItem -Path $sitePath -Exclude "appsettings.json","appsettings.Production.json","logs" |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# ── 4. Extraer nueva version ─────────────────────────────────
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $sitePath)

# Restaurar appsettings.json del backup si fue eliminado
$settingsBackup = Join-Path $backupDir "appsettings.json"
$settingsDest   = Join-Path $sitePath  "appsettings.json"
if ((Test-Path $settingsBackup) -and -not (Test-Path $settingsDest)) {
    Copy-Item $settingsBackup $settingsDest -Force
    Write-Host "   appsettings.json restaurado del backup." -ForegroundColor Gray
}

Write-Host "   Archivos nuevos copiados correctamente." -ForegroundColor Green

# ── 5. Reiniciar el Application Pool ────────────────────────
Write-Host ""
Write-Host "[4/5] Iniciando Application Pool '$appPoolName'..." -ForegroundColor Yellow

try {
    Start-WebAppPool -Name $appPoolName
    Write-Host "   Application Pool iniciado." -ForegroundColor Green
}
catch {
    & "$env:windir\system32\inetsrv\appcmd.exe" start apppool /apppool.name:"$appPoolName"
}

# ── 5. Verificar estado ──────────────────────────────────────
Write-Host ""
Write-Host "[5/5] Verificando estado..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

try {
    $state = (Get-WebConfigurationProperty `
        -pspath 'MACHINE/WEBROOT/APPHOST' `
        -filter "system.applicationHost/applicationPools/add[@name='$appPoolName']" `
        -name "state").Value
    Write-Host "   App Pool estado: $state" -ForegroundColor Cyan
}
catch {
    Write-Host "   (verificacion manual: abre IIS Manager)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Despliegue v2.0 completado exitosamente" -ForegroundColor Green
Write-Host "  Backup v1.0 guardado en:" -ForegroundColor Green
Write-Host "  $backupDir" -ForegroundColor White
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
