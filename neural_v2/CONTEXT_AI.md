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
* `/plugin/Render/GhostManager.cs`: Inyección de hologramas RAM in-memory.
* `/plugin/Core/HologramOsnapOverrule.cs`: Lógica de forzado de snaps para hologramas abstractos.
* `/plugin/Core/UndoInterceptor.cs`: Protección del Historial Holográfico ante el temido `Ctrl+Z`.

## 5. Próximo Paso Inmediato (Punto de Retorno)
* **Completado:** Spike de estrés (10,000 hologramas validados) y Setup Inicial del Motor Visual local (Spints 1/2 de la Categoría 1). Compilador NuGet Multi-Target establecido.
* **Pendiente / Actual:** Sprint 3/4. Conectar la base del Visual Engine local con los pipelines de red. Comenzar el envío asíncrono puro de deltas desde el `PointMonitor` de AutoCAD hacia el servidor Node.js.

## 6. Hallazgos Arquitectónicos (Caso Estudio: Speckle & CRDTs)
* **Threading (AutoCAD UI vs WebSockets):** Los mensajes entrantes de WebSockets NO deben enviarse directamente vía `Dispatcher`. Se deben encolar en un diccionario concurrente (`ConcurrentDictionary`) y procesar únicamente cuando AutoCAD dispara el evento `Application.Idle`. Una vez procesados, el evento `Idle` se desuscribe. (Ref: `speckle-sharp-connectors/AppIdleManager`).
* **Serialización de Entidades:** Transformaremos las primitivas de `DatabaseServices` en POCOs (Plain Old C# Objects) puros extrayendo las coordenadas `PointToSpeckle` y los vectores nativos, aplicando un Bounding Box (`GeometricExtents`) a cada entidad para indexación espacial.
* **CRDT & Sync:** Adoptamos la arquitectura **Automerge** como estándar mental (y posible implementación a futuro) sobre Yjs. El modelo basado en JSON nativo de Automerge encaja perfectamente con nuestra estructura jerárquica de `EntityDelta`, permitiendo un control offline/online sin conflictos muy superior al de LWW custom.
