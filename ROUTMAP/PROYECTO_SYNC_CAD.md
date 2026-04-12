# Roadmap Técnico: SYNC-CAD

Este documento detalla la visión, el estado actual y los próximos pasos del proyecto **SYNC-CAD**, una plataforma de sincronización bidireccional y colaborativa para AutoCAD.

---

## 1. Visión del Proyecto
Eliminar el caos de versiones en equipos de arquitectura e ingeniería, permitiendo que múltiples usuarios trabajen sobre el mismo plano de forma coordinada, utilizando la nube (Google Drive) como motor de almacenamiento y una interfaz web para supervisión.

---

## 2. Estado Actual (Fase 1: Infraestructura y Multi-Versión)
En esta fase estamos estableciendo los cimientos técnicos para asegurar que el plugin funcione en cualquier entorno profesional.

### Hitos Alcanzados:
- **Estrategia Multi-Target:** Soporte para AutoCAD 2022 (.NET 4.8), 2025 (.NET 8) y 2027 (.NET 10).
- **Compilación Híbrida:** Uso de NuGet para independencia de versiones instaladas.
- **Instalador Automático:** Creación de un `.exe` que despliega el plugin sin configuración manual.
- **Estructura Bundle:** Implementación del estándar *Autoloader* de Autodesk.

---

## 3. Próximos Pasos (Fase 2: Sincronización Avanzada)
Una vez estabilizada la instalación, el foco pasará a la funcionalidad estratégica:

### 3.1 Sincronización por Deltas
- En lugar de subir el archivo completo, el plugin detectará y subirá solo las entidades modificadas (vía DXF).
- Reducción drástica del ancho de banda y tiempos de espera.

### 3.2 Bloqueo de Capas (Layer Locking)
- Implementación de un sistema de "Dueños de Capas".
- Si un usuario está editando la capa de "INSTALACIONES", el servidor la bloquea para los demás para evitar conflictos.

### 3.3 Visualizador Web en Tiempo Real
- Los cambios realizados en AutoCAD se reflejarán en un navegador mediante un `dxf-viewer`.
- Posibilidad de hacer comentarios y marcas (markups) desde la web que lleguen al AutoCAD.

---

## 4. Desafíos Técnicos Resueltos/En Curso
- **Compatibilidad de SDKs:** Resolución de conflictos entre el nuevo SDK de .NET 10 y los requerimientos de .NET 4.8 (WPF) para versiones antiguas.
- **Integración con Google Drive:** Manejo de credenciales y permisos de múltiples estudios de arquitectura.
- **Comunicación en Tiempo Real:** Uso de WebSockets para notificar cambios entre usuarios instantáneamente.

---

## 5. Distribución
El producto final se entrega como un paquete de instalación sencillo:
- `InstalarCadSync.exe` + `CadSync.bundle`.

---
*Documento actualizado: 12 de Abril, 2026*
