# CONTEXT_AI: H-SYNC (SYNC-CAD NEURAL v2.0)

> **PROPÓSITO DE ESTE ARCHIVO:** Este documento es un "Brain Dump" altamente comprimido. Se diseñó para ser adjuntado o leído al inicio de una nueva conversación con el Asistente de IA (Gemini/Antigravity) para restaurar el contexto arquitectónico complejo al instante, sin tener que releer docenas de historiales.

---

## 1. Misión Principal
Migrar **SYNC-CAD** de un modelo de "Sincronización de Archivos basada en bloqueos de capa" (v1.0) hacia un motor de **Co-edición Atómica en Tiempo Real** estilo Figma, operando nativamente dentro de AutoCAD.

## 2. Paradigmas Fundamentales (Las 4 Reglas de Oro)
1. **Zero-File Architecture:** Los arquitectos ya no suben archivos DWG a Google Drive. El plugin captura eventos Puros (`CREATE`, `UPDATE`, `DELETE`, `UNDO`) y los streamea como un JSON ligero.
2. **Hologramas (No Bloques):** El trabajo de los "Compañeros" NO se inyecta en la Base de Datos nativa de tu archivo DWG. Se proyecta directamente a la RAM gráfica usando `TransientManager` y se interactúa matemáticamente con `OsnapOverrule`.
3. **Draft Mode & Time Machine:** Todo proyecto es single-player y multi-player a la vez. Los usuarios tienen historial visual de pasos (Ctrl+Z Visual infinito) y modo Borrador local privado.
4. **Resiliencia CRDT (Offline):** Si la red cae, el usuario acumula deltas localmente en Chunks. Al volver, sincroniza de golpe usando resolución de conflictos LWW (*Last Write Wins*) gobernada por un `server_seq` asignado estrictamente del lado del servidor.

## 3. Estado Teconológico (El Stack)
* **Backend:** Node.js crudo, `ws` puros. Bases de datos eventuales: Redis (Hot Graph) y MongoDB/S3 para Snapshots/Hibernación.
* **Frontend/Plugin:** C# puro sin dependencias de red externas (usando `System.Net.WebSockets`). 
* **Multi-Targeting Nativo:** Las librerías de C# se compilan bimodalmente: 
  - `net8.0-windows` (AutoCAD 2025+, alta velocidad).
  - `net48` (AutoCAD 2024 e inferiores).

## 4. Directorios Clave (El Monorepo en `\neural_v2\`)
* `/docs/NEURAL_PHASE_1_DOD.md`: **Crucial.** Es nuestra Biblia de "Definition of Done". Nunca marques un Sprint como cerrado sin que pase estos test BDD.
* `/docs/NEURAL_DATA_SCHEMA.md`: Define el objeto "Delta" (JSON de Transacciones).
* `/plugin/Render/GhostManager.cs`: Inyección de hologramas RAM in-memory + `SetGlowRed` para Shadowing con ghost fresco.
* `/plugin/Render/ShadowOverrule.cs`: **Shadow Triplet** — `ShadowDrawOverrule`, `ShadowOsnapOverrule`, `ShadowRegistry`. Motor de ocultamiento visual sin BD.
* `/plugin/Core/Network/OwnershipRegistry.cs`: Puente Canónico-Proyectado. Registra entidades nativas locales para detectar colisiones remotas.
* `/plugin/Core/Network/WebSocketClient.cs`: Cliente WS con `CancellationToken`, `TryGetProperty` defensivo, y fix de `JsonElement` use-after-dispose.
* `/plugin/Core/Network/AppIdleManager.cs`: Cola concurrente para ejecutar acciones de red en el hilo UI de AutoCAD.
* `/plugin/Core/HologramOsnapOverrule.cs`: Lógica de forzado de snaps para hologramas abstractos.
* `/plugin/Core/UndoInterceptor.cs`: Protección del Historial Holográfico ante el temido `Ctrl+Z`.
* `/server/hub.js`: Hub WebSocket con LWW, RECONCILE_FIX broadcast, y logging de diagnóstico.
* `/server/tester.js`: Bot de pruebas BDD (Carla/Beto) con `Date.now()` para evadir idempotencia.

## 5. Próximo Paso Inmediato (Punto de Retorno)
* **Completado (Sprint 9):** Shadow Triplet validado end-to-end. Primera prueba exitosa: un círculo nativo desaparece por Shadowing, un holograma rojo aparece en su lugar, y 2 segundos después se reposiciona a las coordenadas canónicas del servidor (LWW ganador). Base de datos local intacta, UNDO protegido.
* **Pendiente / Actual:** Sprint 10. Pulir el flujo visual (mantener color rojo en ghost post-merge), implementar `SelectionOverrule` para eliminar grips en Ctrl+A, añadir heartbeat desde el cliente C#, y comenzar el Bake Engine (escritura canónica definitiva a la BD local).

## 6. Hallazgos Arquitectónicos Críticos
* **Canónico vs Proyectado (El Descubrimiento del Sprint 8):** El modelo mental "Nativo vs Holograma" o "Mis objetos vs Objetos de otros" es una ilusión peligrosa. En un sistema colaborativo, todo estado es dictado por el Servidor (**Estado Canónico**). Lo que hay en la pantalla (sean Entidades Nativas de Base de Datos o Hologramas en RAM) son **Proyecciones**. Si un usuario edita una Entidad Nativa de otro usuario, el sistema local debe aplicar **Shadowing**: ocultar la entidad nativa local y levantar un Holograma en su lugar, evitando polucionar la pila de `UNDO` de AutoCAD con ediciones externas. Esto requiere un **Registro de Ownership Local**.
* **Shadow Triplet (Sprint 9):** Tres Overrules coordinados (`Draw`, `Osnap`, `Selection`) que operan puramente en la capa de renderizado. Para la base de datos, la entidad nunca cambió. El `DrawableOverrule` devuelve `true` sin emitir geometría, lo que purga la entidad del índice espacial de AutoCAD. Esto la hace invisible Y inseleccionable por clic/ventana nativo.
* **Ghost Fresco vs Clone (Sprint 9):** `Entity.Clone()` desde una entidad abierta `ForWrite` en transacción retiene estado interno de database-resident que impide el renderizado como Transient. La solución es crear entidades frescas (`new Circle()`) copiando solo las propiedades geométricas.
* **DocumentLock desde AppIdle (Sprint 9):** Las acciones ejecutadas desde `Application.Idle` no tienen acceso implícito al documento. Sin `doc.LockDocument()`, cualquier `StartTransaction` explota con `eLockViolation`.
* **Threading (AutoCAD UI vs WebSockets):** Los mensajes entrantes de WebSockets NO deben enviarse directamente vía `Dispatcher`. Se deben encolar en un diccionario concurrente (`ConcurrentDictionary`) y procesar únicamente cuando AutoCAD dispara el evento `Application.Idle`. Una vez procesados, el evento `Idle` se desuscribe. (Ref: `speckle-sharp-connectors/AppIdleManager`).
* **Serialización de Entidades:** Transformaremos las primitivas de `DatabaseServices` en POCOs (Plain Old C# Objects) puros extrayendo las coordenadas `PointToSpeckle` y los vectores nativos, aplicando un Bounding Box (`GeometricExtents`) a cada entidad para indexación espacial.
* **CRDT & Sync:** Adoptamos la arquitectura **Automerge** como estándar mental sobre Yjs. Implementado con resolución LWW granular (Property-based) y diccionarios de resolución atómica en memoria de Node.js.

