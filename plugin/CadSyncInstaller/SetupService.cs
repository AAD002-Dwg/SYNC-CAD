using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace CadSyncInstaller
{
    public class SetupService
    {
        const string APP_NAME = "SYNC-CAD Plugin";
        const string APP_VERSION = "1.3.0";
        const string BUNDLE_NAME = "CadSync.bundle";
        const string REGISTRY_KEY = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CadSync";
        const string PUBLISHER = "AAD002 - SYNC-CAD";

        static readonly string PluginsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "ApplicationPlugins");

        static readonly string DestBundle = Path.Combine(PluginsFolder, BUNDLE_NAME);

        public bool IsInstalled()
        {
            return Directory.Exists(DestBundle);
        }

        public async Task InstallAsync(IProgress<InstallStatus> progress)
        {
            await Task.Run(() =>
            {
                try
                {
                    progress.Report(new InstallStatus { Message = "Iniciando instalación...", ProgressPercentage = 10 });

                    string? sourceBundle = FindSourceBundle();
                    if (sourceBundle == null)
                    {
                        throw new Exception("No se encontró la carpeta 'CadSync.bundle' junto al instalador.");
                    }

                    progress.Report(new InstallStatus { Message = "Verificando estado de AutoCAD...", ProgressPercentage = 20 });
                    if (!EnsureFilesAvailable())
                    {
                        throw new Exception("Operación cancelada por el usuario. AutoCAD sigue en ejecución.");
                    }

                    if (!Directory.Exists(PluginsFolder))
                    {
                        Directory.CreateDirectory(PluginsFolder);
                    }

                    if (Directory.Exists(DestBundle))
                    {
                        progress.Report(new InstallStatus { Message = "Removiendo versión anterior...", ProgressPercentage = 40 });
                        try { Directory.Delete(DestBundle, true); }
                        catch { /* Seguir adelante, se sobreescribirá lo que se pueda */ }
                    }

                    progress.Report(new InstallStatus { Message = "Copiando nuevos archivos...", ProgressPercentage = 60 });
                    CopyFolder(sourceBundle, DestBundle);

                    WriteLocalVersion();

                    progress.Report(new InstallStatus { Message = "Registrando instalación en Windows...", ProgressPercentage = 80 });
                    RegisterInWindows();
                    RegisterAutoloadInRegistry();

                    progress.Report(new InstallStatus { Message = "¡Instalación completada con éxito!", ProgressPercentage = 100, IsComplete = true });
                }
                catch (Exception ex)
                {
                    progress.Report(new InstallStatus { Message = $"Error: {ex.Message}", ProgressPercentage = 100, IsError = true });
                }
            });
        }

        public async Task UninstallAsync(IProgress<InstallStatus> progress)
        {
            await Task.Run(() =>
            {
                try
                {
                    progress.Report(new InstallStatus { Message = "Iniciando desinstalación...", ProgressPercentage = 10 });

                    if (!IsInstalled())
                    {
                        throw new Exception("El complemento no está instalado.");
                    }

                    if (!EnsureFilesAvailable())
                    {
                        throw new Exception("Operación cancelada por el usuario. AutoCAD sigue en ejecución.");
                    }

                    progress.Report(new InstallStatus { Message = "Eliminando archivos del complemento...", ProgressPercentage = 60 });
                    if (Directory.Exists(DestBundle))
                    {
                        Directory.Delete(DestBundle, true);
                    }

                    progress.Report(new InstallStatus { Message = "Removiendo registro de Windows...", ProgressPercentage = 80 });
                    UnregisterFromWindows();
                    UnregisterAutoloadFromRegistry();

                    progress.Report(new InstallStatus { Message = "Complemento desinstalado correcamente.", ProgressPercentage = 100, IsComplete = true });
                }
                catch (Exception ex)
                {
                    progress.Report(new InstallStatus { Message = $"Error: {ex.Message}", ProgressPercentage = 100, IsError = true });
                }
            });
        }

        private bool EnsureFilesAvailable()
        {
            var blockingProcesses = new[] { "acad", "accoreconsole" };
            bool issuesFound = false;
            foreach (var procName in blockingProcesses)
            {
                if (Process.GetProcessesByName(procName).Length > 0)
                {
                    issuesFound = true;
                    break;
                }
            }

            if (issuesFound)
            {
                bool kill = false;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var result = MessageBox.Show(
                        "Se detectaron procesos de AutoCAD en ejecución.\nEsto impedirá actualizar los archivos.\n\n¿Desea que el instalador cierre estos procesos automáticamente? (Perderá el trabajo no guardado)",
                        "AutoCAD en Ejecución",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    kill = result == MessageBoxResult.Yes;
                });

                if (kill)
                {
                    foreach (var procName in blockingProcesses)
                    {
                        foreach (var process in Process.GetProcessesByName(procName))
                        {
                            try { process.Kill(); process.WaitForExit(3000); } catch { }
                        }
                    }
                    Thread.Sleep(1000);
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        static string? FindSourceBundle()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BUNDLE_NAME);
            if (Directory.Exists(path)) return path;

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

        static void RegisterInWindows()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InstalarCadSync.exe");

                using var key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY);
                key.SetValue("DisplayName", APP_NAME);
                key.SetValue("DisplayVersion", APP_VERSION);
                key.SetValue("Publisher", PUBLISHER);
                key.SetValue("InstallLocation", DestBundle);
                key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            catch { /* Ignorar error de registro */ }
        }

        static void UnregisterFromWindows()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKey(REGISTRY_KEY, false);
            }
            catch { /* Ignorar */ }
        }

        static void RegisterAutoloadInRegistry()
        {
            try
            {
                using var acadKey = Registry.CurrentUser.OpenSubKey(@"Software\Autodesk\AutoCAD");
                if (acadKey == null) return;

                foreach (var version in acadKey.GetSubKeyNames())
                {
                    using var versionKey = acadKey.OpenSubKey(version);
                    if (versionKey == null) continue;

                    string dllSubPath;
                    if (version.StartsWith("R24")) dllSubPath = @"Contents\2022\CadSyncPlugin.dll"; // 2021-2024
                    else if (version.StartsWith("R25")) dllSubPath = @"Contents\2025\CadSyncPlugin.dll"; // 2025-2026
                    else dllSubPath = @"Contents\2027\CadSyncPlugin.dll"; // 2027+

                    string fullDllPath = Path.Combine(DestBundle, dllSubPath);

                    foreach (var lang in versionKey.GetSubKeyNames())
                    {
                        var appKeyPath = $@"{version}\{lang}\Applications\CadSync";
                        using var appKey = acadKey.CreateSubKey(appKeyPath);
                        if (appKey != null)
                        {
                            appKey.SetValue("DESCRIPTION", "CadSync Bidirectional Synchronization");
                            appKey.SetValue("LOADCTRLS", 2, RegistryValueKind.DWord);
                            appKey.SetValue("LOADER", fullDllPath);
                            appKey.SetValue("MANAGED", 1, RegistryValueKind.DWord);
                        }
                    }
                }
            }
            catch { /* Ignorar errores de permisos */ }
        }

        static void UnregisterAutoloadFromRegistry()
        {
            try
            {
                using var acadKey = Registry.CurrentUser.OpenSubKey(@"Software\Autodesk\AutoCAD", true);
                if (acadKey == null) return;

                foreach (var version in acadKey.GetSubKeyNames())
                {
                    using var versionKey = acadKey.OpenSubKey(version, true);
                    if (versionKey == null) continue;

                    foreach (var lang in versionKey.GetSubKeyNames())
                    {
                        try
                        {
                            acadKey.DeleteSubKeyTree($@"{version}\{lang}\Applications\CadSync", false);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        static void WriteLocalVersion()
        {
            try
            {
                string versionFile = Path.Combine(DestBundle, "version.local.json");
                string content = $"{{ \"version\": \"{APP_VERSION}\", \"installedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\" }}";
                File.WriteAllText(versionFile, content);
            }
            catch { /* Ignorar */ }
        }

        public void SilentWaitAndUpdate()
        {
            // Este método se invoca crudo desde Console mode --update
            int maxWaitMs = 10 * 60 * 1000;
            int elapsed = 0;
            int pollInterval = 2000;

            while (elapsed < maxWaitMs)
            {
                if (Process.GetProcessesByName("acad").Length == 0) break;
                Thread.Sleep(pollInterval);
                elapsed += pollInterval;
            }

            if (Process.GetProcessesByName("acad").Length > 0)
            {
                return; // Timeout
            }

            Thread.Sleep(2000);
            
            // Ejecutar la lógia pura sin UI
            var dummyProgress = new Progress<InstallStatus>();
            InstallAsync(dummyProgress).Wait();
        }

        public void SilentUninstall()
        {
            var dummyProgress = new Progress<InstallStatus>();
            UninstallAsync(dummyProgress).Wait();
        }
    }
}
