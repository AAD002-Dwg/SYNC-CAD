# Protocolo de Handshake: Los Primeros 5 Segundos

Este documento define el intercambio de mensajes crítico entre el Plugin de AutoCAD y el Servidor SYNC-CAD NEURAL para establecer una sesión de co-edición consistente.

## 0. El Punto de Partida: El Archivo Base
El usuario puede abrir el proyecto desde:
1.  **Un Template Vacío:** El plugin descargará todo desde cero.
2.  **Una Copia Local previa:** El plugin solo pedirá lo que cambió desde su última conexión.

---

## 1. El Flujo de Handshake (Timeline)

### T = 0ms: Mensaje `CONNECT_REQ` (Cliente -> Servidor)
El cliente envía sus credenciales y el índice de su última sesión conocida.
```json
{
  "event": "CONNECT_REQ",
  "auth": { "studioKey": "...", "user": "..." },
  "projectId": "PROJ-ABC",
  "checkpointSeq": 10502 // Sustituye al antiguo hash de archivo
}
```

### T = 500ms: Mensaje `SESSION_INIT` (Servidor -> Cliente)
El servidor valida la identidad y decide si el cliente necesita un **FullSync** o un **DeltaPatch**.
```json
{
  "event": "SESSION_INIT",
  "sessionId": "UUID-SESION",
  "currentServerSeq": 11000,
  "syncMode": "PATCH | SNAPSHOT",
  "serverTime": 1234567890
}
```

### T = 1000ms: Transferencia de Estado (`DATA_SYNC`)
*   **Si es PATCH:** El servidor envía todos los deltas desde `lastServerSeq` hasta `currentServerSeq`.
*   **Si es SNAPSHOT:** El servidor envía un JSON comprimido con el estado actual de todas las entidades activas del proyecto.

### T = 3000ms: Inicialización del Viewport (AutoCAD Plugin)
1.  **Limpieza:** El plugin borra cualquier entidad que ya no exista en el servidor.
2.  **Proyección:** Se crean los `TransientObjects` para todas las entidades recibidas.
3.  **Carga de Bloques/XREFs:** Si el proyecto usa bloques que el cliente no tiene en su tabla de bloques local, el plugin inicia descargas asíncronas de los `.dwg` de definición.

### T = 5000ms: Mensaje `LIVE_READY` (Cliente -> Servidor)
El cliente confirma que su viewport está sincronizado y AutoCAD está listo para capturar nuevos eventos.
A partir de este momento, el canal queda abierto para mensajes bidireccionales `ENTITY_DELTA`.

---

## 2. Resolución de Diferencias en el Handshake

### Caso A: El usuario tiene trabajo offline no sincronizado
Si el cliente tiene deltas locales que el servidor no conoce:
1.  El cliente evalúa el tamaño de su Log. Si excede los 500 deltas, divide el envío en ráfagas (Chunks).
2.  El cliente envía mensajes `OFFLINE_PUSH` seriados (con `chunk: 1/N`).
3.  El servidor almacena en buffer hasta recibir el último chunk y procesa atómicamente para prevenir estados rotos si la red vuelve a caer.
4.  Si hay un conflicto (ej: un objeto local se movió pero también se movió en el servidor), el servidor responde con un `RECONCILE_FIX` que el cliente aplica para quedar alineado.

### Caso B: Historial expirado en el servidor
Si el `lastServerSeq` del cliente es demasiado viejo (> 5000 deltas de diferencia):
*   El servidor fuerza un `syncMode: SNAPSHOT`.
*   El cliente reconstruye su AutoCAD local desde cero para asegurar integridad.

---

## 4. Estado de Carga Progresivo (Lifecycle)
En lugar de un tiempo fijo, el usuario atraviesa estados visibles para los demás:

1.  **SYNCHRONIZING:** El cliente está descargando el Snapshot o Patches. Los demás ven su cursor con un icono de "Reloj".
2.  **RECONCILING:** El cliente está inyectando deltas locales offline.
3.  **LOADING_ASSETS:** El cliente ya tiene la geometría pero está descargando definiciones de bloques/Xrefs pesadas.
4.  **LIVE:** Sesión activa y bidireccional.

## 5. Salida del Sistema
*   **DISCONNECT_REQ:** Mensaje explícito al cerrar AutoCAD para liberar Locks y Auras instantáneamente.
*   **ALIVE_HEARTBEAT:** Cada 30-60 segundos. Si el servidor pierde 2 latidos, desconecta al usuario forzosamente y notifica al resto del equipo (`cursor_remove`).
