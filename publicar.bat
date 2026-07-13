@echo off
echo ========================================
echo   PUBLICANDO INVENTARIO WEB...
echo ========================================

rd /s /q "C:\PublishListo" 2>nul
dotnet publish "C:\InventarioWeb_Rescatado\InventarioWeb\InventarioWeb.csproj" -c Release -o "C:\PublishListo"

echo.
echo ========================================
echo   LISTO. Ahora ejecuta deploy.bat
echo   en el SERVIDOR.
echo ========================================
pause
