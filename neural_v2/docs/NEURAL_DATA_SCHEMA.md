# Esquema de Datos Atómico: PROTOCOLO NEURAL v1

Este documento define la estructura de datos para la serialización de entidades de AutoCAD en el motor de sincronización en tiempo real.

## 1. El Objeto "EntityDelta"
Para maximizar la eficiencia, no enviamos el objeto completo, solo los cambios.

```json
{
  "op": "CREATE | UPDATE | DELETE | UNDO",
  "id": "UUID-GLOBAL-ENTIDAD",
  "projectId": "ID-PROYECTO",
  "client_seq": 105, 
  "server_seq": null, 
  "timestamp": 1234567890,
  "user": "ID-USER",
  "state": "LIVE | DRAFT", 
  "type": "LINE | POLYLINE | BLOCKREF | TEXT | ASSET",
  "props": {
    "layer": "MUROS",
    "color": 256,
    "geom": { ... data específica del tipo ... }
  }
}
```


## 2. Definiciones de Geometría (Deltas)

### LINE
```json
"geom": {
  "start": [x, y, z],
  "end": [x, y, z]
}
```

### POLYLINE (Optimizada)
Para polilíneas grandes, el delta puede especificar solo el nodo que cambió:
```json
"geom": {
  "nodes": [[x1, y1], [x2, y2]],
  "isClosed": true,
  "updateNode": { "idx": 5, "pos": [x, y] } // Solo si es un UPDATE de un vértice
}
```
### ASSET (Imágenes, Fuentes, PDFs, XREFs)
Las referencias externas no transmiten la geometría, sino el hash del archivo original.
```json
"geom": {
  "type": "IMAGE",
  "assetHash": "sha256-blob-1234",
  "pos": [x, y, z],
  "scale": [1, 1, 1]
}
```
El cliente leerá el `assetHash` y si no lo tiene en su caché local (S3), pausará el render de esta entidad e iniciará un flujo de descarga asíncrono.

### BLOCKREF (Clave para eficiencia)
```json
"geom": {
  "blockName": "PUERTA_80",
  "pos": [x, y, z],
  "rot": 1.57,
  "scale": [1, 1, 1],
  "dynProps": {
    "Visibility1": "Abierto",
    "Flip State": 0
  }
}
```

## 3. Resolución de Conflictos (Clasificación de Propiedades)
Para evitar que el motor de Automerge construya geometrías Frankenstein (ej: un usuario mueve el punto de inicio de una línea, otro el punto final, resultando en una geometría rota), todas las propiedades del EntityDelta se dividen en dos categorías estrictas:

### 3.1. Scalar-Mergeables (Independientes)
Mutaciones que pueden fusionarse de forma segura, incluso si ocurren en paralelo por diferentes usuarios.
*   `color`, `layer`, `linetype`, `lineweight`, `visibility`, `transparency`

### 3.2. Atomic-Groups (Bloques Indivisibles)
Propiedades co-dependientes que definen físicamente a la entidad. El grupo entero se trata como una unidad. Si dos usuarios mutan el mismo Grupo Atómico simultáneamente, se aplica un conflicto estricto *Last Write Wins (LWW)* sobre el bloque entero, y el perdedor es notificado visualmente (Glow Rojo).
*   **Geometría Lineal:** `startPoint`, `endPoint` (Mover un vértice sobrescribe a todos los vértices del oponente).
*   **Geometría Circular:** `center`, `radius`
*   **Textos:** `content`, `textHeight`, `location`, `rotation`

*Regla del Juez Automerge:* 
- Mutación de Usuario A (Scalar) + Mutación de Usuario B (Atomic) = **Merge Exitoso.**
- Mutación de Usuario A (Atomic Geometría) + Mutación de Usuario B (Atomic Geometría) = **Conflicto LWW. Gana el `server_seq` mayor.**

## 4. Estructura en Redis (Servidor)
El servidor mantiene el estado actual (Snapshot) en una estructura de Hash Set:
*   `PROJECT:[ID]:ENTITIES` -> Mapa de `UUID -> EntityData`.
*   `PROJECT:[ID]:HISTORY` -> Lista ordenada de los últimos 1000 deltas para reconciliación rápida.

## 8. Orden Operacional (FIFO)
Para evitar estados inconsistentes (ej: mover un objeto antes de crearlo):
*   **Sequential ID:** Cada cliente adjunta un `client_seq` incremental a sus mensajes.
*   **Atomic Processing:** El servidor procesa los deltas de un mismo `user + projectId` de forma secuencial y estricta en el orden recibido.
*   **Idempotencia:** Si el servidor recibe un delta con un `client_seq` que ya procesó (debido a un reintento de red), lo ignora silenciosamente.

## 9. Compresión de Snapshots Masivos
Para proyectos de >20,000 entidades, el Snapshot JSON se servirá:
1.  **Binario:** Usando MessagePack o Protocol Buffers para reducir el tamaño base.
2.  **HTTP Gzip:** El servidor forzará compresión Gzip en la respuesta `/api/snapshot`.
3.  **Priorización Visual (Capas Críticas):** El Snapshot envía primero la arquitectura gruesa. El servidor evalúa el nombre de la capa usando Regex: si la capa contiene la subcadena `MURO`, `ESTRUCTURA`, `COLUMN` o `WALL`, se empuja al inicio del Array de respuesta. El resto se encola detrás, permitiendo que el cliente renderice el esqueleto del edificio instantáneamente.
