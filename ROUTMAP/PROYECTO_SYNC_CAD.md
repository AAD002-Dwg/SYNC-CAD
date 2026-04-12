# Roadmap Técnico: SYNC-CAD

Este documento detalla la visión, el estado actual y los próximos pasos del proyecto **SYNC-CAD**, una plataforma de sincronización bidireccional y colaborativa para AutoCAD.

---

## 1. Visión del Proyecto
Eliminar el caos de versiones en equipos de arquitectura e ingeniería, permitiendo que múltiples usuarios trabajen sobre el mismo plano de forma coordinada, utilizando la nube (Google Drive) como motor de almacenamiento y una interfaz web para supervisión.

---

## 2. Estado Actual (Fase 1: Infraestructura Completa)

### Hitos Alcanzados:
- **Multi-Versión:** Soporte para AutoCAD 2022 (.NET 4.8), 2025 (.NET 8) y 2027 (.NET 10).
- **Compilación en la Nube (CI/CD):** GitHub Actions compila las 3 versiones automáticamente.
- **Instalador Inteligente:** `InstalarCadSync.exe` con menú interactivo, desinstalación, y registro en Panel de Control de Windows.
- **Auto-Update OTA:** El plugin verifica actualizaciones desde GitHub Releases y se actualiza solo al cerrar AutoCAD.
- **GitHub Releases:** Publicación automática de paquetes descargables con un simple `git tag`.
- **Estructura Bundle:** Estándar *Autoloader* de Autodesk para carga automática del plugin.

### Arquitectura de Distribución:

```text
Desarrollador                          Usuario Final
─────────────                          ─────────────
git tag v1.1.0                         Abre AutoCAD
     │                                      │
     ▼                                      ▼
GitHub Actions                         AutoUpdater.cs
  ├── Compila 2022 (MSBuild)             ├── Lee version.json de GitHub
  ├── Compila 2025 (dotnet)              ├── Compara versión local vs remota
  ├── Compila 2027 (dotnet)              ├── Descarga ZIP si hay nueva versión
  ├── Empaqueta ZIP                      ├── Lanza InstalarCadSync.exe --update
  └── Crea GitHub Release               └── Al cerrar AutoCAD → se actualiza
```

### Archivos Clave:

| Archivo | Propósito |
|---------|-----------|
| `version.json` | Fuente de verdad para versiones (leído desde GitHub raw) |
| `plugin/AutoUpdater.cs` | Verificador silencioso de actualizaciones OTA |
| `plugin/CadSyncInstaller/Program.cs` | Gestor: instalar / desinstalar / actualizar |
| `plugin/CadSyncPlugin.2022.csproj` | Proyecto aislado para la compilación legacy (net48) |
| `plugin/CadSyncPlugin.csproj` | Proyecto moderno (net8.0 + net10.0) |
| `.github/workflows/build-plugin.yml` | Pipeline CI/CD con Release automático |
| `plugin/build_release.bat` | Script de compilación local (2022 + 2025) |

---

## 3. Próximos Pasos (Fase 2: Sincronización Avanzada)

### 3.1 Sincronización por Deltas
- Subir solo las entidades modificadas (vía DXF) en lugar del archivo completo.

### 3.2 Bloqueo de Capas (Layer Locking)
- Sistema de "Dueños de Capas" con bloqueo automático vía servidor.

### 3.3 Visualizador Web en Tiempo Real
- Cambios reflejados en navegador mediante `dxf-viewer`.
- Posibilidad de comentarios y marcas desde la web.

---

## 4. Desafíos Técnicos Resueltos

| Desafío | Solución |
|---------|----------|
| SDK .NET 10 rompe compilación net48 (bug MC1000) | Proyecto aislado `CadSyncPlugin.2022.csproj` + MSBuild clásico en CI |
| Sintaxis C# 8 incompatible con net48 | `[..8]` → `.Substring(0, 8)` + `<LangVersion>latest</LangVersion>` |
| Archivos XAML generados causan duplicados | Exclusiones explícitas `<Compile Remove="obj\**"/>` en .csproj |
| Auto-update bloqueado por AutoCAD (File Lock) | Patrón "Baton Relay": EXE externo espera cierre de `acad.exe` |
| Instalación sin permisos de admin | Registro en `HKCU` (usuario actual) en vez de `HKLM` |

---

## 5. Cómo Publicar una Nueva Versión

```bash
# 1. Editar version.json con la nueva versión y notas
# 2. Commit y tag
git commit -am "release: v1.1.0"
git tag v1.1.0
git push && git push --tags
```

GitHub Actions automáticamente: compila → empaqueta → publica Release.
Los usuarios reciben la actualización al abrir AutoCAD.

---
*Documento actualizado: 12 de Abril, 2026*
