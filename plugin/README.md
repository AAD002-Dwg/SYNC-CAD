# Guía de Compilación: CadSyncPlugin

Este plugin permite a AutoCAD comunicarse con tu servidor web.

## Requisitos
1. **Visual Studio (2019 o superior)**.
2. **AutoCAD instalado** (para obtener las librerías de referencia).

## Pasos para compilar
1. Crea un proyecto nuevo en Visual Studio de tipo **Biblioteca de clases (.NET Framework 4.8 o .NET 6/7/8)** según tu versión de AutoCAD.
2. Agrega las siguientes referencias desde tu carpeta de instalación de AutoCAD (normalmente en `C:\Program Files\Autodesk\AutoCAD 20XX`):
   - `acdbmgd.dll`
   - `acmgd.dll`
   - `accoremgd.dll`
3. Copia el contenido de `CadSyncPlugin.cs` en tu archivo principal.
4. Compila el proyecto (**Build Solution**). Esto generará un archivo `CadSyncPlugin.dll`.

## Cómo usar en AutoCAD
1. Abre AutoCAD.
2. Escribe el comando `NETLOAD`.
3. Busca y selecciona el archivo `CadSyncPlugin.dll` generado.
4. Usa los comandos:
   - `CADSYNC_PUSH`: Sube tu plano actual al servidor.
   - `CADSYNC_PULL`: Descarga un plano del servidor.

## Notas del MVP
- Asegúrate de que el servidor Node.js esté corriendo en `http://localhost:3001` antes de usar los comandos.
- Si el servidor está en otra dirección, actualiza la variable `ServerUrl` en el código C# antes de compilar.
