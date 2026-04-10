@echo off
setlocal enabledelayedexpansion

echo ==========================================
echo COMPILANDO PLUGIN CAD SYNC
echo ==========================================

:: Intentar detectar ruta de AutoCAD (Ajustar si es necesario)
set "ACAD_PATH=C:\Program Files\Autodesk\AutoCAD 2025"

if not exist "!ACAD_PATH!\acdbmgd.dll" (
    echo [ADVERTENCIA] No se encontro AutoCAD en !ACAD_PATH!
    set /p "ACAD_PATH=Ingresa la ruta de tu carpeta AutoCAD (donde esta acdbmgd.dll): "
)

:: Crear copia temporal de las referencias para facilitar el build si no estan en el PATH de MSBuild
mkdir lib 2>nul
copy "!ACAD_PATH!\acdbmgd.dll" "lib\"
copy "!ACAD_PATH!\acmgd.dll" "lib\"
copy "!ACAD_PATH!\accoremgd.dll" "lib\"

echo Compilando proyecto .NET...
dotnet build plugin/CadSyncPlugin.csproj -c Release -o ./bin

if %ERRORLEVEL% EQU 0 (
    echo ==========================================
    echo EXITO: Plugin compilado en ./bin/CadSyncPlugin.dll
    echo ==========================================
) else (
    echo ==========================================
    echo ERROR: No se pudo compilar el plugin. 
    echo Mas detalles en CadSyncPlugin.csproj (verifica las rutas).
    echo ==========================================
)

pause
