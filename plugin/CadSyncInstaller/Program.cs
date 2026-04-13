using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;

namespace CadSyncInstaller
{
    class Program
    {
        // ── Constantes ───────────────────────────────────────────
        const string APP_NAME       = "SYNC-CAD Plugin";
        const string APP_VERSION    = "1.0.5";
        const string BUNDLE_NAME    = "CadSync.bundle";
        const string REGISTRY_KEY   = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CadSync";
        const string PUBLISHER      = "AAD002 - SYNC-CAD";

        static readonly string PluginsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "ApplicationPlugins");

        static readonly string DestBundle = Path.Combine(PluginsFolder, BUNDLE_NAME);

        // ── Entry Point ──────────────────────────────────────────
        static void Main(string[] args)
        {
            Console.Title = $"Gestor {APP_NAME} v{APP_VERSION}";

            // Modo silencioso por argumento
            if (args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "--uninstall":
                        SilentUninstall();
                        return;

                    case "--update":
                        WaitAndUpdate();
                        return;
                }
            }

            // Modo interactivo (doble clic)
            ShowMenu();
        }

        // ── Menú Interactivo ─────────────────────────────────────
        static void ShowMenu()
        {
            bool isInstalled = Directory.Exists(DestBundle);

            Console.WriteLine("====================================================");
            Console.WriteLine($"       GESTOR DE COMPLEMENTO {APP_NAME}");
            Console.WriteLine($"       Versión {APP_VERSION}");
            Console.WriteLine("====================================================");
            Console.WriteLine();

            if (isInstalled)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  Estado: INSTALADO");
                Console.ResetColor();
                Console.WriteLine($"  Ubicación: {DestBundle}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  Estado: NO INSTALADO");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("  [1] Instalar / Reparar");
            Console.WriteLine("  [2] Desinstalar");
            Console.WriteLine("  [3] Salir");
            Console.WriteLine();
            Console.Write("  Seleccione una opción: ");

            var key = Console.ReadKey();
            Console.WriteLine();
            Console.WriteLine();

            switch (key.KeyChar)
            {
                case '1':
                    Install();
                    break;
                case '2':
                    Uninstall();
                    break;
                default:
                    return;
            }

            Console.WriteLine("\nPresiona cualquier tecla para salir...");
            Console.ReadKey();
        }

        // ── Instalación ──────────────────────────────────────────
        static void Install()
        {
            try
            {
                string sourceBundle = FindSourceBundle();
                if (sourceBundle == null)
                {
                    ShowError("No se encontró la carpeta 'CadSync.bundle' junto al instalador.");
                    return;
                }

                // Crear carpeta de plugins si no existe
                if (!Directory.Exists(PluginsFolder))
                {
                    Console.WriteLine("Creando carpeta de plugins de Autodesk...");
                    Directory.CreateDirectory(PluginsFolder);
                }

                // Limpiar instalación anterior
                if (Directory.Exists(DestBundle))
                {
                    Console.WriteLine("Removiendo versión anterior...");
                    try { Directory.Delete(DestBundle, true); }
                    catch { Console.WriteLine("  Aviso: Sobreescribiendo archivos existentes..."); }
                }

                // Copiar archivos
                Console.WriteLine("Copiando archivos del complemento...");
                CopyFolder(sourceBundle, DestBundle);

                // Escribir version.local.json dentro del bundle instalado
                WriteLocalVersion();

                // Registrar en Panel de Control
                RegisterInWindows();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("====================================================");
                Console.WriteLine("  ¡INSTALACIÓN COMPLETADA CON ÉXITO!");
                Console.WriteLine("====================================================");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("  El complemento se cargará automáticamente");
                Console.WriteLine("  la próxima vez que inicies AutoCAD.");
            }
            catch (Exception ex)
            {
                ShowError($"Error durante la instalación: {ex.Message}");
            }
        }

        // ── Desinstalación (Interactiva) ─────────────────────────
        static void Uninstall()
        {
            if (!Directory.Exists(DestBundle))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("El complemento no está instalado.");
                Console.ResetColor();
                return;
            }

            Console.Write("¿Está seguro que desea desinstalar? (S/N): ");
            var confirm = Console.ReadKey();
            Console.WriteLine();

            if (confirm.KeyChar != 'S' && confirm.KeyChar != 's')
            {
                Console.WriteLine("Desinstalación cancelada.");
                return;
            }

            PerformUninstall();
        }

        // ── Desinstalación Silenciosa (desde Panel de Control) ───
        static void SilentUninstall()
        {
            PerformUninstall();
        }

        // ── Lógica común de desinstalación ───────────────────────
        static void PerformUninstall()
        {
            try
            {
                Console.WriteLine("Desinstalando complemento...");

                if (Directory.Exists(DestBundle))
                {
                    Directory.Delete(DestBundle, true);
                    Console.WriteLine("  Archivos del complemento eliminados.");
                }

                // Eliminar entrada del registro
                UnregisterFromWindows();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n  Complemento desinstalado correctamente.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                ShowError($"Error durante la desinstalación: {ex.Message}");
            }
        }

        // ── Modo Update: esperar que AutoCAD cierre ──────────────
        static void WaitAndUpdate()
        {
            // Este modo es invocado por el AutoUpdater del plugin.
            // El plugin descargó el nuevo ZIP, lo descomprimió en %TEMP%
            // y ejecutó este EXE con --update.
            // Debemos esperar que acad.exe cierre antes de reemplazar los archivos.

            Console.WriteLine("[Auto-Update] Esperando que AutoCAD cierre...");

            // Esperar hasta 10 minutos máximo
            int maxWaitMs = 10 * 60 * 1000;
            int elapsed = 0;
            int pollInterval = 2000; // verificar cada 2 segundos

            while (elapsed < maxWaitMs)
            {
                var acadProcesses = Process.GetProcessesByName("acad");
                if (acadProcesses.Length == 0)
                    break;

                Thread.Sleep(pollInterval);
                elapsed += pollInterval;
            }

            // Verificar si AutoCAD sigue abierto después del timeout
            if (Process.GetProcessesByName("acad").Length > 0)
            {
                Console.WriteLine("[Auto-Update] Timeout: AutoCAD sigue abierto. Abortando.");
                return;
            }

            Console.WriteLine("[Auto-Update] AutoCAD cerrado. Aplicando actualización...");

            // Pequeña espera adicional para que Windows libere los archivos
            Thread.Sleep(2000);

            // Ejecutar la instalación normal
            Install();

            Console.WriteLine("[Auto-Update] Actualización completada.");
        }

        // ── Registro de Windows (Agregar/Quitar Programas) ───────
        static void RegisterInWindows()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InstalarCadSync.exe");

                using var key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY);
                key.SetValue("DisplayName",     APP_NAME);
                key.SetValue("DisplayVersion",  APP_VERSION);
                key.SetValue("Publisher",        PUBLISHER);
                key.SetValue("InstallLocation", DestBundle);
                key.SetValue("UninstallString",  $"\"{exePath}\" --uninstall");
                key.SetValue("NoModify",   1, RegistryValueKind.DWord);
                key.SetValue("NoRepair",   1, RegistryValueKind.DWord);

                Console.WriteLine("  Registrado en Panel de Control (Aplicaciones instaladas).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Aviso: No se pudo registrar en Windows: {ex.Message}");
            }
        }

        static void UnregisterFromWindows()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKey(REGISTRY_KEY, false);
                Console.WriteLine("  Entrada del registro eliminada.");
            }
            catch { /* Si no existe, ignorar */ }
        }

        // ── Archivo de versión local ─────────────────────────────
        static void WriteLocalVersion()
        {
            try
            {
                string versionFile = Path.Combine(DestBundle, "version.local.json");
                string content = $"{{ \"version\": \"{APP_VERSION}\", \"installedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\" }}";
                File.WriteAllText(versionFile, content);
                Console.WriteLine("  Versión local registrada.");
            }
            catch { /* No crítico */ }
        }

        // ── Utilidades ───────────────────────────────────────────
        static string? FindSourceBundle()
        {
            // Buscar junto al EXE
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BUNDLE_NAME);
            if (Directory.Exists(path)) return path;

            // Buscar un nivel arriba (caso debug)
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", BUNDLE_NAME);
            if (Directory.Exists(path)) return Path.GetFullPath(path);

            return null;
        }

        static void CopyFolder(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }
            foreach (var dir in Directory.GetDirectories(source))
            {
                CopyFolder(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        static void ShowError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  ERROR: {message}");
            Console.ResetColor();
        }
    }
}
