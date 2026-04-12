# Plan de Implementación: Sync Inteligente

Este plan describe la transición de SYNC-CAD desde reservas manuales de capas a un sistema de detección automática de cambios y bloqueos dinámicos.

## User Review Required

> [!WARNING]
> **Cambio de Paradigma**: El usuario ya no elegirá una capa para trabajar. El plugin observará qué capas se modifican y gestionará las reservas en segundo plano. Esto requiere que el plugin sea más "ruidoso" en términos de notificaciones para evitar que el usuario trabaje en algo que ya está bloqueado por otro.

## Proposed Changes

### 1. Servidor (Node.js)

#### [MODIFY] `index.js`(file:///g:/SYNC-CAD/server/index.js)
- **Multi-Lock per User**: Permitir que un mismo `user` tenga múltiples entradas en `layerLocks`.
- **Auto-Expire**: Implementar un tiempo de vida (TTL) para los bloqueos (ej: 30 minutos sin actividad) para evitar bloqueos perpetuos si AutoCAD falla.
- **API Endpoint**: Crear un endpoint optimizado `/api/locks/check` que reciba un array de capas y devuelva cuáles están disponibles.

---

### 2. Plugin AutoCAD (C#)

#### [MODIFY] `CadSyncPlugin.cs`(file:///g:/SYNC-CAD/plugin/CadSyncPlugin.cs)
- **DirtyLayerTracker**: Implementar una clase que se suscriba a los eventos de la base de datos de AutoCAD.
  - `Database.ObjectAppended`: Detecta nuevas líneas/bloques.
  - `Database.ObjectModified`: Detecta ediciones en objetos existentes.
- **Logical Buffer**: Un `HashSet<string>` que guarde los nombres de las capas tocadas durante la sesión activa.

#### [MODIFY] `Commands.cs` (dentro de CadSyncPlugin.cs)
- **ExecutePushSmart**: Nuevo método que en lugar de usar `_config.LastLayer`, itere sobre el `HashSet` de capas sucias, haga un `WBLOCK` de todas ellas y las suba como un único paquete o múltiples deltas en paralelo.

---

### 3. Interfaz de Usuario (WPF)

#### [MODIFY] `CadSyncControl.xaml`(file:///g:/SYNC-CAD/plugin/CadSyncControl.xaml)
- **Estado de Capas**: Sustituir el combo de reserva por una lista dinámica de "Mis Capas Activas" con indicadores de estado (Verde: Reservada, Rojo: Conflicto).

## Plan de Verificación

### Pruebas Manuales
1. **Dibujo Libre**: Dibujar en la capa "MUROS". Verificar que en el Log del plugin aparezca "Reserva dinámica solicitada para MUROS".
2. **Conflicto Proactivo**: Un Usuario A dibuja en "SOLADOS". Un Usuario B intenta dibujar en "SOLADOS" segundos después. Verificar que el Usuario B reciba un aviso de conflicto.
3. **Push Consolidado**: Editar tres capas distintas y clickear "Sincronizar". Verificar en Google Drive que se actualizaron los tres archivos `.dwg` correspondientes.
