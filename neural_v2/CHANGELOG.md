# Changelog (H-SYNC)

Registro formal de todos los cambios, mejoras continuas, y adiciones arquitectónicas realizadas en el repositorio **neural_v2**. 

El formato se basa en "Keep a Changelog" y respeta SemVer (versionado semántico). Emplearemos un enfoque sistemático y estricto donde **cada cambio estructural o lógico que toque la fase de diseño es documentado aquí preventivamente.**

### Added
- **Shadow Triplet Completo (Sprint 9):** Implementación end-to-end del motor de Shadowing Colaborativo. Primera prueba exitosa de co-edición en tiempo real sobre AutoCAD nativo sin polucionar el UNDO stack.
  - `ShadowDrawOverrule`: Intercepta `WorldDraw`, devuelve `true` sin geometría. Purga la entidad del índice espacial haciéndola invisible e inseleccionable por clic/ventana.
  - `ShadowOsnapOverrule`: Intercepta `GetObjectSnapPoints`, evita que el cursor se ancle a entidades sombreadas.
  - `ShadowRegistry`: Diccionario estático con `SetIdFilter` para control O(1) de entidades sombreadas. Permanencia de sesión hasta Bake.
  - `OwnershipRegistry`: Puente Canónico-Proyectado. Registra entidades nativas creadas en sesión local para identificar colisiones remotas.
  - Ghost fresco (no Clone): Creación de entidades Transient desde cero (`new Circle()`) en lugar de `Entity.Clone()` que retiene estado de BD incompatible con `TransientManager`.
  - `ApplyMergedState`: Parseo de geometría canónica del servidor y reposicionamiento del Holograma a coordenadas ganadoras del LWW.
- **Correcciones críticas de red (Sprint 9):**
  - Fix `CancellationToken` en `ReceiveLoopAsync`: Elimina ReceiveLoops zombi al reconectar (`HSYNC_CONNECT` múltiple).
  - Fix `TryGetProperty("type")`: Los deltas crudos del hub no tienen campo `type`, `GetProperty` explotaba con `KeyNotFoundException`.
  - Fix `JsonElement` use-after-dispose: El `winnerState` era un puntero a memoria del `JsonDocument` destruido por el `using` block. Se serializa a `string` antes de salir.
  - Fix `DocumentLock`: Requerido para transacciones desde contextos no-comando (`AppIdle`). Sin él, AutoCAD lanza `eLockViolation`.
  - Fix `seqCache` envenenado en hub: `client_seq` del tester usaba `Math.random()` que generaba valores menores a los cacheados por idempotencia. Cambiado a `Date.now()`.
  - Timeout del hub subido a 600s (10 min) para desarrollo.
- **Test de Fuego (Sprint 8):** Ejecución de `tester.js` con colisiones LWW de 5ms. El test reveló una falla arquitectónica crucial: las mutaciones remotas sobre entidades nativas locales fallaban porque no existía un puente entre el estado "Canónico" (Servidor) y la entidad nativa "Proyectada".
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
