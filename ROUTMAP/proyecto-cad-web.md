# Proyecto — Plataforma CAD Web con Sincronización Bidireccional con AutoCAD

> Documento de alcance — Abril 2026

---

## 1. Visión General

Una aplicación web que permite a equipos de diseño trabajar colaborativamente sobre planos de AutoCAD, eliminando el caos de versiones y el intercambio manual de archivos. Cada usuario sigue trabajando en su AutoCAD habitual, mientras la plataforma sincroniza los cambios automáticamente y ofrece una vista unificada del proyecto en el navegador.

---

## 2. Problema que resuelve

El flujo actual de equipos CAD genera:

- Múltiples versiones desincronizadas del mismo archivo (`palier_v3_FINAL_ok.dwg`)
- Pérdida de datos por sobreescritura accidental
- Comunicación de cambios por email o WhatsApp, sin trazabilidad
- Imposibilidad de revisar planos sin tener AutoCAD instalado
- Sin historial claro de quién cambió qué y cuándo

---

## 3. Solución Propuesta

### 3.1 Plugin para AutoCAD (Cliente)
- Desarrollado en **C# con la AutoCAD .NET API** (API oficial de Autodesk)
- Se instala una vez en cada computadora como archivo `.dll`
- Detecta eventos nativos de AutoCAD: guardar, modificar, cerrar
- Exporta únicamente los **deltas** (cambios) en formato **DXF** — no el archivo completo
- Sube cambios al servidor automáticamente al guardar
- Recibe notificaciones en tiempo real cuando otros miembros del equipo actualizan
- El usuario decide cuándo incorporar los cambios externos a su copia local

### 3.2 Servidor (Backend)
- Desarrollado en **Node.js + TypeScript**
- Expone una **API REST** para operaciones de proyectos y usuarios
- **WebSockets** para notificaciones en tiempo real entre miembros del equipo
- Motor de sincronización que combina cambios de distintos usuarios
- Sistema de detección y resolución de conflictos por capas
- Historial versionado de todos los cambios con autor y timestamp
- Almacenamiento de archivos DWG/DXF en la nube (compatible con S3)
- Base de datos **PostgreSQL** para proyectos, usuarios y metadatos

### 3.3 Aplicación Web (Frontend)
- Desarrollada en **React**
- Visualizador de planos DXF en el navegador (usando la librería open source `dxf-viewer`)
- Sin necesidad de tener AutoCAD instalado para visualizar
- Sistema de comentarios y markups vinculados a zonas específicas del plano
- Timeline de historial de cambios
- Panel de gestión de proyectos y equipos
- Indicador de estado de sincronización por usuario

---

## 4. Modelos de Colaboración Soportados

### Modelo A — Múltiples usuarios, un archivo DWG
Varios usuarios trabajan sobre el mismo plano dividido por **capas**:

| Usuario | Capa asignada |
|---|---|
| Diseñador 1 | ESTRUCTURA |
| Diseñador 2 | REVESTIMIENTOS |
| Diseñador 3 | INSTALACIONES |
| Diseñador 4 | COTAS Y TEXTO |

Cada uno tiene autonomía total sobre su capa. El servidor combina las capas en una vista unificada. Los conflictos se vuelven prácticamente imposibles.

### Modelo B — Múltiples archivos DWG, un proyecto web (Modelo Federado)
Cada usuario vincula su propio archivo DWG (por disciplina) a un proyecto compartido:

```
estructura.dwg (Usuario A)   ──┐
electricidad.dwg (Usuario B) ──┼──► Proyecto Web (vista unificada)
fontanería.dwg (Usuario C)   ──┘
```

La app web compone una vista unificada de todos los archivos, permitiendo:
- Activar/desactivar disciplinas
- Detección de interferencias geométricas entre disciplinas (**clash detection**)
- Coordinación entre equipos sin compartir archivos

### Modelo C — Combinado
Ambos modelos activos simultáneamente: varios DWGs vinculados, cada uno con múltiples colaboradores.

---

## 5. Sistema de Permisos

| Rol | Puede editar desde AutoCAD | Puede editar desde Web | Puede comentar | Solo lectura |
|---|---|---|---|---|
| Propietario | ✅ | ✅ | ✅ | — |
| Editor | ✅ | ✅ | ✅ | — |
| Revisor | ❌ | ❌ | ✅ | ✅ |
| Observador | ❌ | ❌ | ❌ | ✅ |

---

## 6. Gestión de Conflictos

Cuando dos usuarios modifican la misma zona simultáneamente:

1. **Sistema de capas** (primera línea de defensa) — asignación por disciplina elimina la mayoría de conflictos
2. **Sistema de turno** por capa — si dos usuarios necesitan la misma capa, uno edita y el otro está en modo lectura
3. **Política de resolución** configurable por proyecto:
   - AutoCAD siempre tiene prioridad
   - Último en guardar gana
   - Resolución manual con vista de diferencias

---

## 7. Stack Tecnológico

| Capa | Tecnología | Justificación |
|---|---|---|
| Plugin AutoCAD | C# + .NET | API oficial de AutoCAD |
| Formato de intercambio | DXF | Abierto, gratuito, bien documentado |
| Backend | Node.js + TypeScript | WebSockets nativos, ecosistema DXF |
| Base de datos | PostgreSQL | Historial, usuarios, proyectos |
| Storage | S3 (o compatible) | Versionado de archivos DWG |
| Tiempo real | WebSockets | Notificaciones instantáneas |
| Frontend | React + dxf-viewer | Visualización sin plugins |

**Sin dependencia de APIs de pago de Autodesk (APS/Forge).** El formato DXF como capa de intercambio garantiza independencia total y costo cero de licenciamiento.

---

## 8. Diferenciadores frente al mercado

| Funcionalidad | AutoCAD Web | Onshape | Trimble Connect | Esta plataforma |
|---|---|---|---|---|
| Sincronización bidireccional con AutoCAD desktop | ⚠️ Parcial | ❌ | ❌ | ✅ |
| Independiente de suscripción AutoCAD | ❌ | ❌ | ⚠️ | ✅ |
| Colaboración por capas en tiempo real | ❌ | ✅ | ❌ | ✅ |
| Modelo federado multi-DWG | ❌ | ❌ | ✅ | ✅ |
| Gestión de proyectos integrada | ❌ | ⚠️ | ⚠️ | ✅ |
| Clash detection entre disciplinas | ❌ | ❌ | ✅ | ✅ |

---

## 9. Roadmap de Desarrollo

### Fase 1 — MVP (2-3 meses)
- Plugin básico: sube/baja el DWG completo al guardar
- Servidor: API REST + storage de versiones
- Web: visualizador básico del plano + lista de versiones
- **Resultado:** elimina el caos de versiones. Ya resuelve el 80% del problema.

### Fase 2 — Colaboración (2 meses)
- Sincronización por deltas (solo cambios, no archivo completo)
- Notificaciones en tiempo real vía WebSockets
- Sistema de capas y asignación de zonas
- Historial detallado con autor y timestamp

### Fase 3 — Plataforma completa (2 meses)
- Comentarios y markups vinculados al plano
- Gestión de proyectos, equipos y permisos
- Modelo federado multi-DWG
- Detección básica de interferencias entre disciplinas

---

## 10. Caso de Uso Principal Validado

**Equipo de 4 diseñadores trabajando en el plano del palier de un edificio:**

| | Flujo actual | Con la plataforma |
|---|---|---|
| Versión del archivo | Múltiples copias desincronizadas | Una sola fuente de verdad |
| Comunicación de cambios | Email / WhatsApp | Notificaciones automáticas |
| Historial | Nombres tipo `_v2_FINAL_ok` | Historial automático con autor y fecha |
| Revisión sin AutoCAD | Imposible | Cualquiera desde el navegador |
| Pérdida de datos | Frecuente | Eliminada |
| Conflictos de edición | Sin sistema | Gestionados por capas |

---

*Documento generado como síntesis de la sesión de diseño del producto — Abril 2026*
