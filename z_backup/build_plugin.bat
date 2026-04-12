@echo off
setlocal enabledelayedexpansion

echo ==========================================
echo    INSTALADOR/COMPILADOR CAD SYNC
echo ==========================================

echo Selecciona tu version de AutoCAD:
echo [1] AutoCAD 2022 (.NET Framework 4.8)
echo [2] AutoCAD 2025 (.NET 8.0)
echo [3] AutoCAD 2027 (.NET 10.0)
set /p "CHOICE=Opcion (1-3): "

if "%CHOICE%"=="1" (
    set "TARGET=net48"
    set "VERSION=2022"
) else if "%CHOICE%"=="2" (
    set "TARGET=net8.0-windows"
    set "VERSION=2025"
) else if "%CHOICE%"=="3" (
    set "TARGET=net10.0-windows"
    set "VERSION=2027"
) else (
    echo Opcion invalida.
    pause
    exit /b
)

echo.
echo Compilando para AutoCAD %VERSION% (%TARGET%)...
dotnet build plugin/CadSyncPlugin.csproj -c Release -f %TARGET% -o ./bin/%TARGET%

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ==========================================
    echo EXITO: DLL generada en ./bin/%TARGET%/CadSyncPlugin.dll
    echo Pasos:
    echo 1. En AutoCAD %VERSION%, usa el comando NETLOAD.
    echo 2. Selecciona la DLL en ./bin/%TARGET%/CadSyncPlugin.dll
    echo 3. Usa CADSYNC_SETUP para configurar la URL de la nube.
    echo ==========================================
) else (
    echo.
    echo ERROR: No se pudo compilar. 
    echo Asegurate de tener instalado el SDK de .NET correspondiente.
)

pause
