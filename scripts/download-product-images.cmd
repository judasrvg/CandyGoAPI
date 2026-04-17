@echo off
setlocal

REM One-click downloader for CandyGo product images.
REM Adjust values if needed.
set "API_BASE_URL=https://candygoapi.onrender.com"
set "ADMIN_PHONE=54831128"
set "ADMIN_PASSWORD=Admin123*"

cd /d "%~dp0\.."
powershell -ExecutionPolicy Bypass -File ".\scripts\download-product-images.ps1" ^
  -ApiBaseUrl "%API_BASE_URL%" ^
  -AdminPhone "%ADMIN_PHONE%" ^
  -AdminPassword "%ADMIN_PASSWORD%" ^
  -ClearOutput

echo.
echo Proceso finalizado. Presiona una tecla para cerrar.
pause >nul
endlocal
