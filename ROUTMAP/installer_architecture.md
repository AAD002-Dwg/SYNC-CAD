# 🏗️ Arquitectura: Premium CadSync Installer

Este documento detalla la estructura lógica y la ubicación de los archivos del sistema de instalación modernizado para el plugin de AutoCAD.

## 📂 Directorio Raíz: `plugin\CadSyncInstaller\`

El proyecto se basa en **.NET 8 Windows** y utiliza el patrón **MVVM** para separar la interfaz de la lógica de archivos.

---

### 1. Configuración y Arranque (Core)

| Archivo | Función |
| :--- | :--- |
| **[CadSyncInstaller.csproj](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/CadSyncInstaller.csproj)** | Archivo de proyecto. Define que es una aplicación `WinExe` con `UseWPF` habilitado y empaquetado `SelfContained` (un solo EXE). |
| **[app.manifest](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/app.manifest)** | **Crítico:** Instruye a Windows a solicitar permisos de **Administrador (UAC)** al iniciar. Sin esto, el instalador no podría borrar carpetas en AppData o matar procesos de AutoCAD. |
| **[App.xaml.cs](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/App.xaml.cs)** | Punto de entrada técnico. Implementa el **Mutex** (previene múltiples instancias) y el **Parser de CLI** (detecta `--update` o `--uninstall` para ejecución silenciosa). |

### 2. Capa de Diseño (View & Styles)

| Archivo | Función |
| :--- | :--- |
| **[App.xaml](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/App.xaml)** | El "Diccionario de Recursos". Contiene todos los colores cian/oscuros, gradientes, y el estilo personalizado de los botones y la `ProgressBar` con efecto glow. |
| **[MainWindow.xaml](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/MainWindow.xaml)** | Define la estructura visual: Header con gradiente, Card central de estado, y Footer de acciones. Utiliza **Data Binding** para conectarse al ViewModel. |
| **[MainWindow.xaml.cs](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/MainWindow.xaml.cs)** | "Code-behind" mínimo. Su única tarea es instanciar el `MainViewModel` y asignarlo como `DataContext`. |

### 3. Capa de Lógica (ViewModel & Service)

| Archivo | Función |
| :--- | :--- |
| **[MainViewModel.cs](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/MainViewModel.cs)** | El **cerebro de la UI**. Maneja las propiedades reactivas (`StatusMessage`, `ProgressValue`, `IsBusy`). Traduce los clics de los botones en llamadas asíncronas al servicio. |
| **[SetupService.cs](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/SetupService.cs)** | La **Capa de Datos/I/O**. Contiene el código puro de: copiar archivos, borrar carpetas anteriores, manipular el Registro de Windows y cerrar instancias de `acad.exe`. |

### 4. Soporte y Modelos

| Archivo | Función |
| :--- | :--- |
| **[InstallStatus.cs](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/InstallStatus.cs)** | Objeto simple utilizado para "reportar el progreso" desde el Servicio hacia la UI (Mensaje + Porcentaje). |
| **[RelayCommand.cs](file:///g:/SYNC-CAD/plugin/CadSyncInstaller/RelayCommand.cs)** | Utilidad estándar de MVVM que permite que los botones del XAML disparen métodos en el C#. |

---

## 🛠️ Flujo de Operación

1.  **Arranque:** `App.xaml.cs` verifica si AutoCAD está abierto y si se pasaron argumentos de consola.
2.  **Interacción:** El usuario hace clic en "Instalar".
3.  **Acción Asíncrona:** El `MainViewModel` inicia una `Task` de fondo. La UI sigue fluida (no se congela).
4.  **Feedback:** El `SetupService` realiza el trabajo pesado y envía reportes de progreso cada vez que termina un paso (ej. "Copiando archivos... 60%").
5.  **Finalización:** Se actualiza el `IsComplete` en la UI, mostrando la caja de éxito verde.

> [!NOTE]
> Para generar el ejecutable de producción final, se utiliza el comando:
> `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`
