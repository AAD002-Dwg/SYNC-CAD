# Definition of Done: Fase 1 — Motor Holográfico + Sincronización Transaccional

Este documento define de forma implacable e inequívoca las condiciones de victoria para el cierre de la **Fase 1**. Ninguna tarea se considerará completa a menos que pase estos Criterios de Aceptación (AC) en un entorno de pruebas real.

---

## Categoría 1: Renderizado Transient (El Motor)
*   **AC-101** — Dado un proyecto con 5,000 entidades activas en el servidor, cuando un cliente nuevo completa el handshake, entonces todos los hologramas deben estar visibles en viewport en menos de 3 segundos, con FPS sostenido por encima de 30 durante el proceso de proyección.
*   **AC-102** — Dado un holograma proyectado por TransientManager, cuando el usuario local ejecuta `Ctrl+Z` cualquier cantidad de veces, entonces ningún holograma de otros usuarios debe desaparecer ni corromperse.
*   **AC-103** — Dado un holograma de tipo LINE o POLYLINE, cuando el usuario activa un OSNAP (endpoint, midpoint, intersection), entonces el snap debe resolverse correctamente sobre la geometría del holograma con un tiempo de respuesta menor a 50ms.
*   **AC-104** — Dado un proyecto activo, cuando el usuario hace Pan y Zoom libremente con 5,000 hologramas proyectados, entonces el FPS no debe caer por debajo de 24 en ningún momento de la interacción.

## Categoría 2: Pipeline de Streaming (La Red)
*   **AC-201** — Dado un delta generado en el cliente A (por ejemplo, mover una línea), cuando el servidor lo recibe y redistribuye, entonces el cliente B debe ver el holograma actualizado en menos de 150ms bajo condiciones de red local (LAN). Bajo red WAN con latencia simulada de 80ms, el límite sube a 300ms.
*   **AC-202** — Dado un usuario usando Grips para arrastrar una entidad, cuando el arrastre está en progreso, entonces el servidor no debe recibir más de 30 mensajes por segundo de ese cliente. Al soltar el Grip, debe llegar exactamente un delta final con la posición definitiva.
*   **AC-203** — Dado que el servidor recibe el mismo `client_seq` dos veces del mismo usuario (retry por red), entonces el segundo delta debe ignorarse silenciosamente sin producir duplicados en el estado ni en los hologramas de otros clientes.
*   **AC-204** — Dado un usuario en modo DRAFT, cuando dibuja entidades, entonces ningún delta de esas entidades debe llegar al servidor ni aparecer en el viewport de otros usuarios hasta que el usuario ejecute "Commit".

## Categoría 3: Handshake y Ciclo de Vida de Sesión
*   **AC-301** — Dado un cliente con `checkpointSeq` dentro de los últimos 5,000 deltas del servidor, cuando ejecuta `CONNECT_REQ`, entonces el servidor debe responder `SESSION_INIT` con `syncMode: PATCH` y el cliente debe alcanzar estado `LIVE` en menos de 5 segundos.
*   **AC-302** — Dado un cliente con historial expirado (más de 5,000 deltas de diferencia), cuando ejecuta `CONNECT_REQ`, entonces el servidor debe forzar `syncMode: SNAPSHOT`, el cliente debe reconstruir el estado completo y alcanzar `LIVE` en menos de 15 segundos para proyectos de hasta 10,000 entidades.
*   **AC-303** — Dado un cliente con trabajo offline pendiente, cuando ejecuta `OFFLINE_PUSH`, entonces los deltas deben enviarse en chunks de máximo 500, el servidor debe confirmar cada chunk antes de que el cliente envíe el siguiente, y el cliente no debe declarar `LIVE_READY` hasta que el servidor confirme el último chunk procesado.
*   **AC-304** — Dado un usuario que cierra AutoCAD normalmente, cuando el servidor recibe `DISCONNECT_REQ`, entonces las Auras de Edición de ese usuario deben desaparecer del viewport de todos los demás clientes en menos de 2 segundos.
*   **AC-305** — Dado un usuario que pierde conexión sin enviar `DISCONNECT_REQ`, cuando el servidor no recibe `ALIVE_HEARTBEAT` por dos ciclos consecutivos (60-120 segundos), entonces el servidor debe marcar la sesión como terminada y notificar a los demás con `cursor_remove`.

## Categoría 4: Consistencia de Estado
*   **AC-401** — Dado que el Usuario A y el Usuario B modifican simultáneamente propiedades distintas de la misma entidad (A cambia color, B cambia posición), cuando el servidor aplica ambos deltas, entonces el estado final debe contener ambos cambios fusionados. Este escenario debe reproducirse y verificarse 10 veces consecutivas sin una sola inconsistencia.
*   **AC-402** — Dado que dos usuarios mueven la misma entidad a posiciones distintas con timestamps de cliente casi simultáneos, cuando el servidor resuelve el conflicto por LWW, entonces el usuario "perdedor" debe recibir una notificación visual (Glow Rojo) dentro de los 2 segundos siguientes a la resolución, y su holograma debe actualizarse a la posición ganadora automáticamente.
*   **AC-403** — Dado un proyecto en estado activo, cuando se toma el estado de Redis (`PROJECT:[ID]:ENTITIES`) y se compara contra el viewport de tres clientes distintos conectados simultáneamente, entonces los tres viewports deben ser idénticos al estado del servidor. Esta verificación debe pasar al menos 5 veces en sesiones distintas.

## Categoría 5: Onboarding y Valor Single-Player
*   **AC-501** — Dado un usuario que instala el plugin por primera vez en un proyecto sin otros usuarios conectados, cuando abre el plugin, entonces debe poder activar la Máquina del Tiempo Visual, navegar al menos 20 pasos atrás en el historial visual, y restaurar cualquier estado histórico — todo sin conexión a internet.
*   **AC-502** — Dado el flujo de primera conexión, cuando el usuario completa el handshake por primera vez en un proyecto nuevo, entonces debe ver una guía de onboarding de no más de 3 pasos que demuestre el valor del sistema (hologramas, snapping sobre hologramas, Máquina del Tiempo) antes de declarar `LIVE_READY`.

---

## El Criterio de Integración Final (El "Boss" de Fase)
> [!CAUTION]
> **AC-601** — Dado un escenario con 3 clientes simultáneos, un proyecto con 3,000 entidades preexistentes, y uno de los clientes reconectándose tras 10 minutos offline con 50 deltas locales pendientes, cuando el sistema completa la reconciliación, entonces los tres viewports deben converger al mismo estado en menos de 30 segundos, sin crashes, sin entidades duplicadas, y sin intervención manual.

*(Este último criterio no se ejecuta en unit tests — se ejecuta en una sesión real con tres personas. Si AC-601 pasa, la Fase 1 tiene la medalla de oro y está lista para producción).*

---

## Sugerencia de Mapeo de Sprints
*   **Sprint 1-2**: AC-101 a AC-104 *(Estabilidad del motor visual antes de conectar la red)*
*   **Sprint 3-4**: AC-201 a AC-204 *(Pipeline de streaming)*
*   **Sprint 5**: AC-301 a AC-305 *(Handshake y casos edge)*
*   **Sprint 6**: AC-401 a AC-403 *(Consistencia LWW y CRDTs bajo concurrencia)*
*   **Sprint 7**: AC-501 a AC-502 *(Onboarding y Fallback Single-player)*
*   **Sprint 8**: AC-601 *(Prueba de fuego final de integración masiva)*
