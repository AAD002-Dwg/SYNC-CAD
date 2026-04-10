# CAD Sync Platform (Cloud MVP)

Plataforma de sincronización y colaboración para AutoCAD. Permite que múltiples usuarios trabajen sobre el mismo dibujo mediante un sistema de reserva de capas (Modelo A), evitando conflictos y centralizando las versiones en la nube.

## Componentes

- **`/server`**: Backend en Node.js + Express + Socket.io. Gestiona la lógica de red y las notificaciones en tiempo real.
- **`/client`**: Frontend en React + Vite. Dashboard para monitoreo y gestión de capas.
- **`/plugin`**: Plugin de AutoCAD desarrollado en C# .NET. Proporciona comandos nativos para sincronizar el dibujo.

## Características de la Fase 3
- **Conectividad Cloud**: Desplegable en Render con un solo clic.
- **Modelo de Capas**: Reserva de capas desde el dashboard y bloqueo automático en AutoCAD.
- **Configuración Dinámica**: No requiere recompilación para cambiar la IP o URL del servidor.

## Instalación y Despliegue

### 1. Despliegue en Render (Backend + Web)
1. Conecta este repositorio a un nuevo **Web Service** en Render.
2. Comando de Build: `npm run render-build`
3. Comando de Start: `npm start`

### 2. Plugin de AutoCAD
1. Abre la solución en Visual Studio.
2. Compila el proyecto `plugin/CadSyncPlugin.csproj`.
3. Carga el `.dll` en AutoCAD usando `NETLOAD`.
4. Configura la URL con `CADSYNC_SETUP`.

---
*Desarrollado como parte del proyecto SYNC-CAD — Abril 2026*
