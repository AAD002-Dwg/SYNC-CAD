# SYNC-CAD Platform

Plataforma de sincronización y colaboración para AutoCAD. Permite que múltiples usuarios trabajen sobre el mismo dibujo mediante un sistema de reserva de capas, evitando conflictos y centralizando las versiones en Google Drive.

## Componentes

- **`/server`** — Backend en Node.js + Express + Socket.io. Gestiona la sincronización y notificaciones en tiempo real.
- **`/client`** — Frontend en React + Vite. Dashboard para monitoreo y gestión de capas.
- **`/plugin`** — Plugin de AutoCAD en C# .NET. Soporta AutoCAD 2022, 2025 y 2027.

## Instalación del Plugin de AutoCAD

### Opción 1: Descarga directa (Recomendado)
1. Descargá **[CadSyncInstaller.zip](https://github.com/AAD002-Dwg/SYNC-CAD/releases/latest/download/CadSyncInstaller.zip)** desde GitHub Releases.
2. Descomprimí el ZIP en cualquier carpeta.
3. Ejecutá `InstalarCadSync.exe` y elegí la opción **[1] Instalar**.
4. Abrí AutoCAD — el complemento se carga automáticamente.

### Desinstalación
- Ejecutá `InstalarCadSync.exe` → opción **[2] Desinstalar**, o
- Desde *Configuración de Windows > Aplicaciones instaladas* → buscar "SYNC-CAD Plugin".

### Actualizaciones Automáticas
El plugin verifica automáticamente si hay nuevas versiones cada vez que abrís AutoCAD.
Si detecta una actualización, la descarga en segundo plano y se aplica al cerrar el programa.

## Versiones de AutoCAD Soportadas

| AutoCAD | .NET | Compilado por |
|---------|------|---------------|
| 2022–2024 | .NET Framework 4.8 | GitHub Actions (MSBuild) |
| 2025 | .NET 8.0 | GitHub Actions + Local |
| 2027 | .NET 10.0 | GitHub Actions |

## Despliegue del Servidor

### En Render (Cloud)
1. Conectá este repositorio a un **Web Service** en Render.
2. Build command: `npm run render-build`
3. Start command: `npm start`

### Local (Desarrollo)
```bash
npm install
npm start
```

## Publicar una Nueva Versión

```bash
# 1. Actualizar version.json con la nueva versión
# 2. Commit y tag
git commit -am "release: v1.1.0"
git tag v1.1.0
git push && git push --tags
```

GitHub Actions compila las 3 versiones, empaqueta el ZIP, y crea un Release automáticamente.
Los usuarios con el plugin instalado recibirán la actualización la próxima vez que abran AutoCAD.

---
*Desarrollado como parte del proyecto SYNC-CAD — Abril 2026*
