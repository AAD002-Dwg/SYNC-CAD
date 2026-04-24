# Changelog (H-SYNC)

Registro formal de todos los cambios, mejoras continuas, y adiciones arquitectónicas realizadas en el repositorio **neural_v2**. 

El formato se basa en "Keep a Changelog" y respeta SemVer (versionado semántico). Emplearemos un enfoque sistemático y estricto donde **cada cambio estructural o lógico que toque la fase de diseño es documentado aquí preventivamente.**

### Added
- Juez Transaccional y Automerge (Sprint 6): Motor de diffing `_preCommandSnapshot` sin reflexión, mapeo de Mutaciones Parciales, mitigación de colisiones cruzadas usando `AppIdleManager`, y detección automática del comando COPY vía `Database.ObjectAppended`.
- Ruta HTTP GET `/api/snapshot` para tests BDD (AC-403).
- Ciclo de Vida y Handshake (Sprint 5): Enrutador PATCH/SNAPSHOT en Node, `HandshakeManager` asíncrono en C# (Task.Run), y limpieza de Auras por `DocumentDestroyed`.
- Pipeline de Streaming (Sprint 3-4): Implementación de `PayloadBuilder` con `System.Text.Json` puro, `DraftState` para privacidad local, y `WebSocketClient` con optimización WAN (`NoDelay = true`).
- Servidor Hub (Node.js) inicializado con lógica de Idempotencia y LWW básico para AC-203.
- Comando `HSYNC_HEAVY_TEST` implementado (Spike 1.5), inyectando exitosamente 10,000 entidades pesadas (MText/Circles) en ~1s sin impacto residual en FPS.
- Estructuras en C# consolidadas para Sprint 1-2: `GhostManager`, `UndoInterceptor` y `HologramOsnapOverrule`.
- Documentos de Definición de Producto (Esquema, Protocolo y DoD) para la Fase 1.
- Inicialización de estructura de directorios pura para `plugin` (C#) y `server` (Node).
- Aislamiento de código basado en pruebas empíricas exitosas del Spike de `TransientManager`.

### Changed
- Migración dictaminada de API C#: Se abandona .NET Framework 4.8. El proyecto a partir de este punto se configura pura y exclusivamente bajo .NET 8 (AutoCAD 2025).

### Deprecated
- SYNC-CAD v1.0 (Flujo de base de datos DWG completa y bloqueos) queda oficialmente deprecado a favor de H-SYNC Neural.
