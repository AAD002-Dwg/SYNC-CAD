# Guía de Compilación y Distribución — CadSync

Esta guía explica cómo generar el instalador final para distribuir el complemento a otros usuarios.

## Requisitos en la Máquina de Desarrollo
- **.NET SDK 8.0** (Para compilar el instalador y la versión 2025).
- **.NET Framework 4.8 Developer Pack** (Para compilar la versión 2022).
- **.NET SDK 10.0** (Opcional, para la versión 2027).

## Cómo Generar el Instalador (`.exe`)
1. Abre una terminal (CMD o PowerShell) en la carpeta `g:\SYNC-CAD\plugin`.
2. Ejecuta el archivo de automatización:
   ```bash
   .\build_release.bat
   ```
3. El script compilará todas las versiones disponibles y organizará los archivos.

## Resultado: Carpeta `dist`
Al finalizar, encontrarás una carpeta llamada `dist` con el siguiente contenido:
- `InstalarCadSync.exe` — El archivo que debes enviar al usuario.
- `CadSync.bundle/` — Carpeta que contiene las DLLs y la configuración (debe ir siempre JUNTO al `.exe`).

## Instrucciones para el Usuario Final
Para instalar el complemento en otra computadora:
1. Recibir la carpeta `dist` (comprimida en un `.zip` por ejemplo).
2. Descomprimirla en cualquier lugar (Escritorio, Descargas, etc.).
3. Ejecutar **`InstalarCadSync.exe`**.
4. Abrir AutoCAD 2022 o 2025. El complemento se cargará automáticamente.

> [!IMPORTANT]
> **Seguridad de Windows:** Como este es un ejecutable generado por ti, Windows podría mostrar una advertencia de "SmartScreen". El usuario debe hacer clic en "Más información" -> "Ejecutar de todas formas".

## Solución de Problemas
- **Si falla la compilación de 2022:** Verifica que tengas instalado el ".NET Framework 4.8 Developer Pack".
- **Si falla la compilación de 2025:** Verifica que el la ruta en el `.csproj` sea correcta para tu instalación local.
- **Si AutoCAD no reconoce el comando `CADSYNC`:** Verifica que el archivo `PackageContents.xml` esté correctamente copiado dentro de la carpeta `%AppData%\Autodesk\ApplicationPlugins\CadSync.bundle`.
