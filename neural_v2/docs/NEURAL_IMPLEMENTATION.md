# Plan de Implementación: SYNC-CAD NEURAL (Fork H-SYNC)

Este documento detalla la hoja de ruta técnica para evolucionar SYNC-CAD desde un sistema de sincronización de archivos basado en capas hacia un ecosistema de **Co-Edición Atómica y Proyección Holográfica**.

---

## 1. Visión General
**SYNC-CAD NEURAL** elimina la fricción de los bloqueos de archivos y la saturación de Google Drive. En este nuevo paradigma, los usuarios trabajan sobre una base de datos de geometría en tiempo real, donde el trabajo de los colegas se "proyecta" en el AutoCAD local sin ensuciar el dibujo físico.

## 2. Fases de Desarrollo

### Fase 1: El Motor Holográfico (Renderizado Transient)
*   **Proyección de Geometría:** Sustituir la inserción de bloques por el uso de la API `TransientManager`. Los datos recibidos de otros usuarios se dibujan en el espacio de memoria de video (GPU), siendo visualmente idénticos pero no persistentes en el `.dwg`.
*   **Snapping Inteligente:** Implementar un `PointMonitor` que permita al usuario usar referencias (OSNAP) sobre los hologramas de otros colegas.
*   **Handles Universales:** Asignación de UUIDs a cada entidad para rastreo unificado en todo el estudio.

### Fase 2: Streaming Atómico (Protocolo de Red)
*   **De Archivos a Eventos:** Reemplazar las subidas por capas por eventos de "Delta de Propiedad" (ej: cambio de color, cambio de coordenadas, cambio de visibilidad de bloque dinámico).
*   **Stream Binario:** Implementación de WebSockets optimizados (Socket.IO) con compresión binaria para una latencia menor a 100ms.
*   **State Store:** Migración del servidor a una base de datos activa (Redis/MongoDB) que mantiene el "Grafo de Dibujo" permanentemente en memoria.

### Fase 3: Neural Locking (Concurrencia Fluida)
*   **Auras de Edición:** Visualización de un resplandor (glow) alrededor de objetos que están siendo manipulados por otros.
*   **Bloqueo por Proximidad:** Sustitución del bloqueo de capas por el bloqueo de entidades individuales. Dos personas pueden dibujar en la misma capa simultáneamente.
*   **Interpolación de Movimiento:** Algoritmos de suavizado para ver el movimiento de los cursores y objetos ajenos a 60fps, eliminando el efecto de "salto".

### Fase 4: Consolidación y Backup Híbrido
*   **Sync con Google Drive:** Google Drive se convierte en el almacén de "Checkpoints" históricos y backups automáticos de seguridad en formato `.dwg`.
*   **El "Bake Engine":** Herramienta para convertir los hologramas en geometría real persistente dentro del archivo local cuando se requiere una entrega final.
*   **Slider de Tiempo:** Interfaz UI para retroceder y avanzar en el historial de cambios del proyecto de forma visual.

---

## 3. Estrategia de Resiliencia y Mitigación

### El Desafío Offline (CRDT & Reconciliación)
El Grafo de Dibujo reside en el servidor, pero el usuario no puede quedar bloqueado por la red.
*   **Conectividad Intermitente:** El plugin mantiene un "Buffer de Operaciones" local. Si la conexión cae, las entidades nuevas se crean con IDs temporales y se marcan visualmente (ej: desaturadas).
*   **Reconciliación:** Al reconectar, se utiliza una lógica basada en **CRDT (Conflict-free Replicated Data Types)**. El servidor realiza un *merge* de las operaciones. Si hay un conflicto insalvable (ej: dos personas borraron y movieron el mismo objeto), el sistema prioriza la versión del servidor pero notifica al usuario local mediante un "Glow Rojo" para revisión manual.

### Estabilidad de AutoCAD (El Spike Técnico - Fase 0)
Se reconoce que `TransientManager` no fue diseñado para concurrencia masiva. 
*   **Fase 0 (Spike):** Antes de comprometer el producto final, se ejecutará un Spike Técnico de 2 semanas.
*   **Especificación de Bifurcación (Aislamiento):** La Fase 0 será **totalmente independiente** de la versión v1.0. Se creará en una rama Git separada (`feature/neural-spike`) o en una carpeta aislada (`spike_neural`). Consistirá únicamente en un cliente mínimo de AutoCAD y un servidor Node.js crudo, enfocados exclusivamente en probar la latencia y los límites de FPS del motor de renderizado al recibir cientos de operaciones de dibujo.

### Mitigaciones de Core Constraints en AutoCAD
*   **El Comando UNDO:** Para evitar colapsos al presionar `Ctrl+Z`, el plugin interceptará el `CommandWillStart` de "U" o "UNDO". En lugar de enviar un borrado ciego, emitirá un evento especial de reversión al servidor.
*   **Tormenta de Eventos (Grips):** Al usar pinzamientos (Grips), AutoCAD dispara cientos de eventos por segundo. El sistema implementará un **Debouncer**: enviará "paquetes fantasma" efímeros durante el arrastre y solo el delta definitivo en el `CommandEnded`.

## 4. Definición de Usuario y Producto

### Modo Borrador (Draft Mode) e Intimidad
El trabajo de diseño requiere experimentación privada.
*   **El Switch "Draft":** El usuario puede activar un modo local. Sus trazos no se envían al servidor hasta que apague este modo (haciendo un "Commit" de sus garabatos ya refinados). Así evitamos "ensuciar" las vistas de los demás con pruebas fallidas.

### Onboarding Solitario (Single-Player Value)
El sistema debe tener valor antes de que se conecte el segundo usuario, de lo contrario la adopción fracasa. 
*   **Máquina del Tiempo:** Cuando un usuario está solo, H-SYNC le proporciona un "Historial de Versionado Trazabilidad Visual". Al abrir la paleta lateral, puede arrastrar el deslizador de tiempo y ver cómo su propio dibujo se retrocede trazo por trazo, permitiéndole "deshacer" visualmente horas de trabajo sin perder variables de entorno o tener que usar archivos de autoguardado (BAK). Esto resuelve el eterno problema de "borré algo hace 2 horas y no sé qué fue".

### Perfil del Usuario "Target"
*   **El Coordinador BIM / Jefe de Proyecto:** Su mayor frustración es la pérdida de tiempo en "limpiar archivos" y resolver pisoteos de capas. Para él, el beneficio es el control total del historial y la visión global.
*   **El Proyectista en Red:** Aquel que necesita ver el trabajo de estructura para poder trazar instalaciones sin esperar a que el estructuralista "suba el archivo".

### El Momento del "Bake" (Decisión Editorial)
La consolidación no es solo técnica, es de gestión.
*   **Flujo de Aprobación:** El sistema permitirá previsualizar los cambios antes del bake. El Coordinador puede seleccionar qué "Hologramas" se aceptan como oficiales y se inyectan en el DWG maestro, manteniendo la calidad y estructura de capas exigida por el cliente final.

## 5. Análisis de Competencia y Negocio

### El Gigante Autodesk (Docs / AEC Collection)
Autodesk Docs está moviendo los DWGs a la nube, pero su tecnología sigue siendo de "bloqueo de archivo completo" o XREFs lentos.
*   **Nuestra Ventaja:** Velocidad extrema y co-edición atómica. Mientras Autodesk tarda segundos o minutos en sincronizar un archivo tras un "Save", SYNC-CAD NEURAL lo hace en milisegundos por cada trazo.

### Barrera de Adopción (Efecto de Red)
Reconocemos que para que tenga valor, todo el equipo debe estar en el plugin.
*   **Estrategia de Adopción Gradual:** El sistema permitirá un modo "Híbrido" donde los usuarios sin el plugin NEURAL pueden seguir viendo el archivo base que se autosalva periódicamente en Drive, mientras que el "Core Team" disfruta de las ventajas de tiempo real.

---

## 6. Comparativa de Arquitectura (Actualizada)

| Característica | Modelo v1.0 (Actual) | Modelo NEURAL (H-Sync) |
| :--- | :--- | :--- |
| **Unidad de Datos** | Archivo `.dwg` por Capa | Entidad Geométrica Individual |
| **Latencia** | Segundos (Subida/Descarga) | Milisegundos (Streaming de Eventos) |
| **Peso del Archivo** | Pesado (Contiene todo el proyecto) | Ligero (Solo contiene tu trabajo activo) |
| **Referencia** | Bloques / Clones Físicos | Hologramas Transient / Overlays |
| **Bloqueos** | Bloqueo manual de Capa entera | Bloqueo automático de Entidad única |

---

## 4. Flujo de Trabajo del Usuario
1.  **Conexión:** El usuario abre un dibujo y se "suscribe" al canal del proyecto.
2.  **Visualización:** El entorno se puebla de hologramas que representan el trabajo del resto del equipo.
3.  **Diseño:** El usuario dibuja normalmente. Sus cambios fluyen instantáneamente a los demás.
4.  **Interacción:** Puede usar Snaps en los hologramas ajenos para coordinar medidas.
5.  **Entrega:** Usa la función "Consolidar" para generar un archivo `.dwg` tradicional para el cliente.

---

## 5. Justificación de Innovación
Este sistema posiciona a SYNC-CAD al nivel de herramientas como Figma o Miro, pero dentro del motor profesional de AutoCAD. Resuelve de raíz los problemas de:
*   Conflictos de archivos sobreescritos.
*   Lentitud de sincronización por archivos grandes.
*   Desorden de capas y archivos en servidores de nube.
