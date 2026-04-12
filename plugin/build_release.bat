@echo off
SETLOCAL EnableDelayedExpansion

set PROJECT_DIR=%~dp0
set DIST_DIR=%PROJECT_DIR%dist
set BUNDLE_DIR=%DIST_DIR%\CadSync.bundle
set CONTENTS_DIR=%BUNDLE_DIR%\Contents

echo.
echo ============================================================
echo   GENERADOR DE DISTRIBUCION - CADSYNC PLUGIN (FINAL FIX)
echo ============================================================
echo.

:: 1. Limpieza Profunda
if exist "%DIST_DIR%" rd /s /q "%DIST_DIR%"
if exist obj rd /s /q obj
if exist bin rd /s /q bin
if exist obj_2022 rd /s /q obj_2022

mkdir "%CONTENTS_DIR%\2022"
mkdir "%CONTENTS_DIR%\2025"
mkdir "%CONTENTS_DIR%\2027"

:: 2. Compilacion de Plugins
echo [1/3] Compilando versiones...

:: --- FASE 2022 ---
echo - AutoCAD 2022 (net48)...
:: Forzamos SDK 8 y aislamos el proyecto para evitar el bug del SDK 10
echo { "sdk": { "version": "8.0.419", "rollForward": "latestFeature" } } > global.json
dotnet build CadSyncPlugin.2022.csproj -c Release -o "%CONTENTS_DIR%\2022" -p:BaseIntermediateOutputPath=obj_2022/
if exist global.json del global.json

:: --- FASE MODERNA ---
echo - AutoCAD 2025 (net8.0)...
dotnet build CadSyncPlugin.csproj -c Release -f net8.0-windows -o "%CONTENTS_DIR%\2025"

echo - AutoCAD 2027 (net10.0)...
dotnet build CadSyncPlugin.csproj -c Release -f net10.0-windows -o "%CONTENTS_DIR%\2027"

:: 3. Empaquetado
echo [2/3] Copiando manifiesto...
copy "%PROJECT_DIR%PackageContents.xml" "%BUNDLE_DIR%\" > nul

echo [3/3] Compilando Instalador...
cd CadSyncInstaller
dotnet publish CadSyncInstaller.csproj -c Release -o "%DIST_DIR%" 
cd ..

echo.
echo ============================================================
echo   PROCESO COMPLETADO
echo ============================================================
pause
