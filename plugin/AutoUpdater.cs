using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Autodesk.AutoCAD.ApplicationServices;

namespace CadSyncPlugin
{
    /// <summary>
    /// Verificador silencioso de actualizaciones Over-The-Air (OTA).
    /// Lee version.json desde GitHub y, si hay nueva versión,
    /// descarga el ZIP del Release y prepara la actualización.
    ///
    /// ARQUITECTURA:
    ///   1. Se invoca desde PluginMain.Initialize() en un hilo separado.
    ///   2. Descarga version.json de la URL cruda de GitHub.
    ///   3. Compara con la versión local (version.local.json en el bundle).
    ///   4. Si hay actualización, descarga el ZIP del Release.
    ///   5. Descomprime en %TEMP% y lanza InstalarCadSync.exe --update.
    ///   6. El EXE espera que acad.exe cierre y reemplaza los archivos.
    ///
    /// REUTILIZACIÓN:
    ///   Para nuevas versiones, solo actualizar version.json en GitHub
    ///   y crear un tag (ej: git tag v1.1.0 && git push --tags).
    ///   GitHub Actions genera el Release automáticamente.
    /// </summary>
    public static class AutoUpdater
    {
        // ── Configuración ────────────────────────────────────────
        // URL del archivo de versión en GitHub (branch main, raw content)
        private const string VERSION_URL =
            "https://raw.githubusercontent.com/AAD002-Dwg/SYNC-CAD/main/version.json";

        // Versión compilada en este binario (se actualiza en cada release)
        public const string CURRENT_VERSION = "1.3.1";

        // Nombre del JSON local dentro del bundle instalado
        private const string LOCAL_VERSION_FILE = "version.local.json";

        // ── Punto de entrada ─────────────────────────────────────
        /// <summary>
        /// Verifica silenciosamente si hay una nueva versión disponible.
        /// Diseñado para ejecutarse con Task.Run() — nunca bloquea AutoCAD.
        /// Si falla cualquier paso, sale sin errores visibles.
        /// </summary>
        public static async Task CheckAsync()
        {
            try
            {
                // Esperar 10 segundos para que AutoCAD termine de cargar
                await Task.Delay(10_000);

                // 1. Obtener versión remota
                var remoteInfo = await FetchRemoteVersionAsync();
                if (remoteInfo == null) return;

                string remoteVersionStr  = remoteInfo["version"]?.ToString() ?? "";
                string downloadUrl       = remoteInfo["downloadUrl"]?.ToString() ?? "";
                string releaseNotes      = remoteInfo["releaseNotes"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(remoteVersionStr) || string.IsNullOrEmpty(downloadUrl))
                    return;

                // 2. Obtener versión local
                string localVersionStr = GetLocalVersion();

                // 3. Comparar
                if (!Version.TryParse(remoteVersionStr, out var remoteVersion)) return;
                if (!Version.TryParse(localVersionStr,  out var localVersion))  return;

                if (remoteVersion <= localVersion)
                    return; // Ya tenemos la última versión

                // 4. Notificar al usuario
                NotifyUser(remoteVersionStr, releaseNotes);

                // 5. Descargar el ZIP
                string tempDir = Path.Combine(Path.GetTempPath(), "CadSyncUpdate");
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string zipPath = Path.Combine(tempDir, "CadSyncInstaller.zip");
                bool downloaded = await DownloadFileAsync(downloadUrl, zipPath);
                if (!downloaded) return;

                // 6. Descomprimir (compatible con net48 y net8.0)
                string extractDir = Path.Combine(tempDir, "extracted");
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                // 7. Buscar y lanzar el instalador en modo --update
                string installerPath = Path.Combine(extractDir, "InstalarCadSync.exe");
                if (!File.Exists(installerPath))
                {
                    // Buscar en subdirectorios (algunos ZIPs anidan una carpeta)
                    foreach (var dir in Directory.GetDirectories(extractDir))
                    {
                        string candidate = Path.Combine(dir, "InstalarCadSync.exe");
                        if (File.Exists(candidate))
                        {
                            installerPath = candidate;
                            break;
                        }
                    }
                }

                if (!File.Exists(installerPath)) return;

                Process.Start(new ProcessStartInfo
                {
                    FileName        = installerPath,
                    Arguments       = "--update",
                    CreateNoWindow  = true,
                    UseShellExecute = false,
                    WindowStyle     = ProcessWindowStyle.Hidden
                });

                // 8. Mensaje final
                WriteToEditor(
                    $"\n[CADSYNC] Actualización v{remoteVersionStr} descargada." +
                    "\n[CADSYNC] Se aplicará automáticamente al cerrar AutoCAD.\n");
            }
            catch
            {
                // Silencioso: la auto-actualización nunca debe romper el plugin
            }
        }

        // ── Obtener versión remota ───────────────────────────────
        private static async Task<JObject?> FetchRemoteVersionAsync()
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            // Agregar header para evitar caché de GitHub CDN
            client.DefaultRequestHeaders.CacheControl =
                new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            string json = await client.GetStringAsync(VERSION_URL);
            return JObject.Parse(json);
        }

        // ── Obtener versión local ────────────────────────────────
        private static string GetLocalVersion()
        {
            try
            {
                // Buscar version.local.json en el bundle instalado
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localFile = Path.Combine(appData, "Autodesk", "ApplicationPlugins",
                    "CadSync.bundle", LOCAL_VERSION_FILE);

                if (File.Exists(localFile))
                {
                    var json = JObject.Parse(File.ReadAllText(localFile));
                    return json["version"]?.ToString() ?? CURRENT_VERSION;
                }
            }
            catch { }

            // Fallback: usar la versión compilada
            return CURRENT_VERSION;
        }

        // ── Descargar archivo ────────────────────────────────────
        private static async Task<bool> DownloadFileAsync(string url, string destPath)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(5);

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) return false;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fs);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ── Notificaciones ───────────────────────────────────────
        private static void NotifyUser(string version, string notes)
        {
            WriteToEditor(
                $"\n[CADSYNC] ¡Nueva versión v{version} disponible!" +
                $"\n[CADSYNC] Novedades: {notes}" +
                "\n[CADSYNC] Descargando actualización en segundo plano...\n");
        }

        private static void WriteToEditor(string message)
        {
            try
            {
                var doc = Application.DocumentManager?.MdiActiveDocument;
                doc?.Editor?.WriteMessage(message);
            }
            catch { }
        }
    }
}
