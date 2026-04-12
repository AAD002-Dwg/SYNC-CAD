# Plan de Implementación: Ghost Cursors (Colaboración Live)

Este plan describe cómo añadir presencia en tiempo real mediante cursores flotantes de otros usuarios utilizando Transient Graphics y Socket.io.

## User Review Required

> [!CAUTION]
> **Coordenadas**: Este sistema requiere que todos los usuarios en el mismo estudio estén trabajando en un sistema de coordenadas compatible. Si un usuario tiene un SCP (Sistema de Coordenadas Personales) rotado, su cursor podría aparecer desplazado para otros. Implementaremos una normalización a SCU (Mundo) para mitigar esto.

## Proposed Changes

### 1. Servidor (Node.js)

#### [MODIFY] `index.js`(file:///g:/SYNC-CAD/server/index.js)
- **Evento `cursor_move`**: Añadir un socket listener que reciba `{ x, y, user }` y haga un `broadcast.to(studioKey)` a los demás miembros de la sala.
- **Optimización**: No guardar estos datos en ninguna base de datos; son puramente efímeros.

---

### 2. Plugin AutoCAD (C#)

#### [NEW] `GhostCursorManager.cs`
- Clase encargada de gestionar los `TransientManager` de AutoCAD.
- Mantendrá un diccionario de `Dictionary<string, GhostInstance>` para rastrear a cada compañero conectado.

#### [MODIFY] `CadSyncPlugin.cs`(file:///g:/SYNC-CAD/plugin/CadSyncPlugin.cs)
- **Sensor de Movimiento**: Suscribirse a `Editor.PointMonitor`.
- **Throttling**: Usar un `System.Diagnostics.Stopwatch` para asegurar que solo enviamos la posición cada 100ms.
- **Recepción**: Al recibir un evento `cursor_move` desde el socket:
  1. Si el usuario no existe en el diccionario, crear un nuevo gráfico transitorio (una cruz de color).
  2. Si existe, actualizar las coordenadas del objeto transitorio y llamar a `UpdateTransient()`.

---

### 3. Interfaz de Usuario (WPF)

#### [MODIFY] `CadSyncControl.xaml`(file:///g:/SYNC-CAD/plugin/CadSyncControl.xaml)
- Añadir un ToggleSwitch: **"Ver Compañeros Live"**.
- Mostrar una lista de "Usuarios Conectados" con un pequeño círculo de color (el mismo color que su Ghost Cursor).

## Plan de Verificación

### Pruebas Técnicas
1. **Latencia**: Verificar que el cursor se mueva con fluidez entre dos instancias de AutoCAD.
2. **Limpieza**: Cerrar una instancia de AutoCAD y verificar que su "Ghost Cursor" desaparezca de la pantalla de los demás usuarios inmediatamente (evento `disconnect`).
3. **Escalabilidad**: Probar con 3-4 cursores simultáneos para asegurar que el zoom y paneo de AutoCAD no se vean afectados.
