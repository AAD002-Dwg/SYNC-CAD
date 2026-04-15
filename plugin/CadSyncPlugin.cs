using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Windows;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json;
using SocketIOClient;
using SocketIOClient.Newtonsoft.Json;
using System.Text.Json.Serialization;

[assembly: ExtensionApplication(typeof(CadSyncPlugin.PluginMain))]

namespace CadSyncPlugin
{
    // ── Configuration ─────────────────────────────────────────
    public class Config
    {
        public string ServerUrl        { get; set; } = "http://localhost:3001";
        public string LastUser         { get; set; } = Environment.UserName;
        public string LastLayer        { get; set; } = "";
        public string StudioKey        { get; set; } = "";
        public bool   AutoPush         { get; set; } = false;
        public bool   AutoPull         { get; set; } = false;
        public bool   ShowGhostCursors { get; set; } = true;
    }

    // ── Plugin Entry Point ────────────────────────────────────
    public class PluginMain : IExtensionApplication
    {
        private static PaletteSet?  _paletteSet;
        public  static CadSyncControl? MyControl;
        private static SocketIOClient.SocketIO? _socket;

        // Plan 2 — Dirty layer tracker
        private static DirtyLayerTracker _dirtyTracker = new DirtyLayerTracker();

        // Plan 3 — Ghost cursors
        private static GhostCursorManager _ghostManager = new GhostCursorManager();
        private static readonly Stopwatch  _cursorThrottle = new Stopwatch();

        // Mejoras — Heartbeat + Offline Queue
        private static readonly OfflineQueue   _offlineQueue    = new OfflineQueue();
        private static System.Timers.Timer?    _heartbeatTimer;
        private static System.Timers.Timer?    _retryTimer;

        public void Initialize()
        {
            try
            {
                // Habilitar compatibilidad TLS 1.2 necesaria para AutoCAD 2022 / .NET 4.8
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                Application.Idle += (s, e) => ShowPalette();
                Application.DocumentManager.DocumentActivated += (s, e) => AttachDocEvents(e.Document);
                if (Application.DocumentManager.MdiActiveDocument != null)
                    AttachDocEvents(Application.DocumentManager.MdiActiveDocument);

                _ = ConnectSocket();

                // Heartbeat: refresca lockedAt cada 5 min para evitar expiración TTL
                _heartbeatTimer = new System.Timers.Timer(5 * 60 * 1000) { AutoReset = true };
                _heartbeatTimer.Elapsed += async (s, e) => await Commands.SendHeartbeatAsync();
                _heartbeatTimer.Start();

                // Retry: procesa cola offline cada 30 segundos si hay elementos
                _retryTimer = new System.Timers.Timer(30 * 1000) { AutoReset = true };
                _retryTimer.Elapsed += async (s, e) => await Commands.ProcessOfflineQueueAsync(_offlineQueue);
                _retryTimer.Start();

                // Auto-Update: verificar silenciosamente si hay nueva versión en GitHub
                _ = Task.Run(() => AutoUpdater.CheckAsync());
            }
            catch { /* silencioso: AutoCAD puede no estar listo aún */ }
        }

        public static OfflineQueue GetOfflineQueue() => _offlineQueue;
        public static GhostCursorManager GetGhostManager() => _ghostManager;

        // ── Document Events ───────────────────────────────────
        private void AttachDocEvents(Document? doc)
        {
            if (doc == null) return;

            // Auto-push on command end
            doc.CommandEnded -= Doc_CommandEnded;
            doc.CommandEnded += Doc_CommandEnded;

            // Plan 2: attach dirty tracker to the document's database
            _dirtyTracker.Detach();
            _dirtyTracker.Attach(doc.Database);
            _dirtyTracker.LayersDirty -= OnLayersDirty;
            _dirtyTracker.LayersDirty += OnLayersDirty;

            // Plan 3: attach PointMonitor for cursor broadcasting
            doc.Editor.PointMonitor -= OnPointMonitor;
            doc.Editor.PointMonitor += OnPointMonitor;

            CheckAndBindProjectContext(doc);
        }

        private static async void CheckAndBindProjectContext(Document doc)
        {
            string currentId = ProjectContextManager.GetBoundProjectId(doc);
            if (!string.IsNullOrEmpty(currentId)) return;

            string filename = Path.GetFileName(doc.Name);
            if (string.IsNullOrEmpty(filename) || filename.StartsWith("Drawing")) return;

            try
            {
                var response = await Commands.GetAsync($"{Commands.GetServerUrl()}/api/files/meta");
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var metaDict = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, dynamic>>(json);

                    if (metaDict.TryGetValue(filename, out dynamic metaItem))
                    {
                        string pid = metaItem?.projectId;
                        if (!string.IsNullOrEmpty(pid))
                        {
                            ProjectContextManager.BindProject(doc, pid);
                        }
                    }
                }
            }
            catch { /* Silencioso en fondo */ }
        }

        private async void Doc_CommandEnded(object sender, CommandEventArgs e)
        {
            if (!Commands.GetAutoPush()) return;

            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "CADSYNC", "SAVE", "QSAVE", "SAVEAS", "GRIP_STRETCH", "U", "UNDO" };
            if (ignored.Contains(e.GlobalCommandName)) return;

            Document? doc = sender as Document;
            if (doc == null) return;
            await Task.Delay(2000); // debounce
            _ = Commands.ExecutePushSmart(doc, isAuto: true);
        }

        // Plan 2 — When debounce fires, request locks for dirty layers
        private async void OnLayersDirty(IReadOnlyCollection<string> layers)
        {
            if (layers.Count == 0) return;
            try
            {
                var result = await Commands.CheckLocksAsync(layers);
                var conflicts = new List<string>();
                foreach (var kvp in result)
                {
                    if (kvp.Value.Locked && kvp.Value.LockedBy != Commands.GetLastUser())
                        conflicts.Add($"{kvp.Key} ({kvp.Value.LockedBy})");
                }

                if (conflicts.Count > 0)
                {
                    Application.DocumentManager.MdiActiveDocument?.Editor
                        .WriteMessage($"\n[CADSYNC] ⚠ Conflicto en capas: {string.Join(", ", conflicts)}");
                    MyControl?.ShowConflicts(conflicts);
                }
                else
                {
                    // Auto-reserve all dirty layers
                    foreach (var layer in layers)
                        await Commands.LockLayerAsync(layer);
                }

                MyControl?.RefreshActiveLayers(_dirtyTracker.PeekDirtyLayers());
            }
            catch { /* ignore network errors silently */ }
        }

        // Plan 3 — Broadcast cursor position (throttled to 100ms)
        private static int _cursorEmitCount;
        private void OnPointMonitor(object sender, PointMonitorEventArgs e)
        {
            if (!Commands.GetShowGhostCursors()) return;
            if (_cursorThrottle.IsRunning && _cursorThrottle.ElapsedMilliseconds < 100) return;
            _cursorThrottle.Restart();

            // PointMonitor already provides WCS coordinates
            var pt = e.Context.ComputedPoint;
            _ = EmitCursorMove(pt.X, pt.Y, pt.Z);
        }

        private static async Task EmitCursorMove(double x, double y, double z)
        {
            try
            {
                if (_socket?.Connected == true)
                {
                    await _socket.EmitAsync("cursor_move", new { x, y, z });
                    _cursorEmitCount++;
                }
                else
                {
                    _cursorEmitCount++;
                    if (_cursorEmitCount <= 3)
                        MyControl?.AddLog($"[DEBUG-CURSOR] Socket NO conectado — cursor no enviado");
                }
            }
            catch (System.Exception ex)
            {
                MyControl?.AddLog($"[DEBUG-CURSOR] Error al emitir: {ex.Message}");
            }
        }

        // ── Socket Connection ─────────────────────────────────
        private static async Task ConnectSocket()
        {
            var url = Commands.GetServerUrl();
            _socket = new SocketIOClient.SocketIO(url, new SocketIOOptions
            {
                Auth = new Dictionary<string, string>
                {
                    ["studioKey"] = Commands.GetStudioKey(),
                    ["user"]      = Commands.GetLastUser()
                }
            });

            _socket.OnConnected += (sender, e) => 
            {
                if (MyControl != null) MyControl.SetConnectionStatus(true);
            };

            _socket.OnDisconnected += (sender, e) => 
            {
                if (MyControl != null) MyControl.SetConnectionStatus(false);
            };

            _socket.On("lock_update", response =>
            {
                try
                {
                    var locks = response.GetValue<Dictionary<string, LockInfo>>();
                    
                    MyControl?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MyControl?.UpdateLocks(locks);
                    }));

                    EventHandler? idleHandler = null;
                    idleHandler = (s, e) =>
                    {
                        Application.Idle -= idleHandler;
                        var doc = Application.DocumentManager.MdiActiveDocument;
                        if (doc != null)
                        {
                            try
                            {
                                using (doc.LockDocument())
                                {
                                    Commands.ApplyLayerLocks(doc, locks);
                                }
                            }
                            catch { }
                        }
                    };
                    Application.Idle += idleHandler;

                    Application.DocumentManager.MdiActiveDocument?.Editor
                        .WriteMessage("\n[CADSYNC] Bloqueos actualizados.");
                }
                catch { }
            });

            _socket.On("sync_update", response =>
            {
                // Refrescar lista de archivos en el UI (thread-safe)
                MyControl?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = MyControl?.RefreshFiles();
                }));

                if (!Commands.GetAutoPull()) return;
                try
                {
                    var data = response.GetValue<SyncEntry>();
                    if (string.IsNullOrEmpty(data?.Layer)) return;
                    if (string.Equals(data.Layer, Commands.GetLastLayer(), StringComparison.OrdinalIgnoreCase)) return;

                    // IMPORTANTE: ExecuteMergeDelta requiere el hilo principal de AutoCAD.
                    // Socket.io corre en un hilo de background donde doc.LockDocument() falla.
                    // Usamos Application.Idle para ejecutar en el hilo correcto.
                    string layerToMerge = data.Layer;
                    EventHandler? idleHandler = null;
                    idleHandler = (s, e) =>
                    {
                        Application.Idle -= idleHandler;
                        var doc = Application.DocumentManager.MdiActiveDocument;
                        if (doc == null) return;
                        doc.Editor.WriteMessage($"\n[CADSYNC] Auto-descarga: capa {layerToMerge}");
                        _ = Commands.ExecuteMergeDelta(doc, layerToMerge);
                    };
                    Application.Idle += idleHandler;
                }
                catch { }
            });

            // Plan 3 — Receive ghost cursor events
            int cursorReceiveCount = 0;
            _socket.On("cursor_move", response =>
            {
                try
                {
                    cursorReceiveCount++;
                    var data = response.GetValue<CursorPayload>();
                    if (data?.User == null)
                    {
                        return;
                    }

                    var pt = new Point3d(data.X, data.Y, data.Z);
                    MyControl?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            _ghostManager.UpdateCursor(data.User, pt);
                            MyControl?.UpdateConnectedUsers(_ghostManager.GetUserColors());
                        }
                        catch (System.Exception ex)
                        {
                            MyControl?.AddLog($"[DEBUG-CURSOR] Error en UpdateCursor: {ex.Message}");
                        }
                    }));
                }
                catch (System.Exception ex)
                {
                    MyControl?.Dispatcher.BeginInvoke(new Action(() =>
                        MyControl?.AddLog($"[DEBUG-CURSOR] Error al procesar cursor_move: {ex.Message}")));
                }
            });

            _socket.On("cursor_remove", response =>
            {
                try
                {
                    var data = response.GetValue<CursorRemovePayload>();
                    if (data?.User == null) return;

                    MyControl?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MyControl?.AddLog($"[DEBUG-CURSOR] Usuario desconectado: {data.User}");
                        _ghostManager.RemoveCursor(data.User);
                        MyControl?.UpdateConnectedUsers(_ghostManager.GetUserColors());
                    }));
                }
                catch { }
            });

            try 
            { 
                await _socket.ConnectAsync(); 
            }
            catch (System.Exception ex)
            { 
                MyControl?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    MyControl?.SetConnectionStatus(false);
                    MyControl?.AddLog($"Error Socket: {ex.Message}");
                }));
            }
        }

        public void Terminate()
        {
            _heartbeatTimer?.Stop();
            _heartbeatTimer?.Dispose();
            _retryTimer?.Stop();
            _retryTimer?.Dispose();
            _dirtyTracker.Dispose();
            _ghostManager.Dispose();
            if (_socket != null) _ = _socket.DisconnectAsync();
        }

        public static void ShowPalette()
        {
            if (_paletteSet == null)
            {
                _paletteSet = new PaletteSet("CAD Sync",
                    new Guid("F30B4A22-3B1E-4E9A-8765-CAD123456789"));
                _paletteSet.Style =
                    PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton;
                _paletteSet.MinimumSize = new System.Drawing.Size(300, 600);

                MyControl = new CadSyncControl();
                _paletteSet.AddVisual("Proyecto Activo", MyControl);
            }
            _paletteSet.Visible = true;
        }

        public static DirtyLayerTracker GetDirtyTracker() => _dirtyTracker;
    }

    // ── Commands ──────────────────────────────────────────────
    public class Commands
    {
        private static Config _config = new Config();
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CadSyncConfig.json");

        public static readonly HttpClient client = new HttpClient();

        static Commands() { LoadConfig(); }

        // Config accessors
        public static string GetServerUrl()         => _config.ServerUrl;
        public static string GetLastUser()          => _config.LastUser;
        public static string GetLastLayer()         => _config.LastLayer;
        public static string GetStudioKey()         => _config.StudioKey;
        public static bool   GetAutoPush()          => _config.AutoPush;
        public static bool   GetAutoPull()          => _config.AutoPull;
        public static bool   GetShowGhostCursors()  => _config.ShowGhostCursors;

        public static void SetAutoPush(bool val)         { _config.AutoPush = val; SaveConfig(); }
        public static void SetAutoPull(bool val)         { _config.AutoPull = val; SaveConfig(); }
        public static void SetStudioKey(string val)      { _config.StudioKey = val; SaveConfig(); }
        public static void SetShowGhostCursors(bool val) { _config.ShowGhostCursors = val; SaveConfig(); }

        private static void LoadConfig()
        {
            if (File.Exists(ConfigPath))
                _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigPath)) ?? new Config();
        }

        private static void SaveConfig()
        {
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
        }

        // ── HTTP helpers with Studio Key header ───────────────
        private static HttpRequestMessage MakeRequest(HttpMethod method, string url)
        {
            var req = new HttpRequestMessage(method, url);
            if (!string.IsNullOrEmpty(_config.StudioKey))
                req.Headers.Add("x-studio-key", _config.StudioKey);
            return req;
        }

        public static async Task<HttpResponseMessage> GetAsync(string url)
            => await client.SendAsync(MakeRequest(HttpMethod.Get, url));

        public static async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
        {
            var req = MakeRequest(HttpMethod.Post, url);
            req.Content = content;
            return await client.SendAsync(req);
        }

        // ── Plan 2: Batch lock check ──────────────────────────
        public static async Task<Dictionary<string, LockCheckResult>> CheckLocksAsync(
            IEnumerable<string> layers)
        {
            var body = new StringContent(
                JsonConvert.SerializeObject(new { layers }),
                Encoding.UTF8, "application/json");
            var response = await PostAsync($"{_config.ServerUrl}/api/locks/check", body);
            if (!response.IsSuccessStatusCode)
                return new Dictionary<string, LockCheckResult>();
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<Dictionary<string, LockCheckResult>>(json)
                   ?? new Dictionary<string, LockCheckResult>();
        }

        public static async Task LockLayerAsync(string layer)
        {
            var body = new StringContent(
                JsonConvert.SerializeObject(new { layer, user = _config.LastUser }),
                Encoding.UTF8, "application/json");
            await PostAsync($"{_config.ServerUrl}/api/lock", body);
        }

        // ── AutoCAD Commands ──────────────────────────────────
        [CommandMethod("CADSYNC")]
        public void CadSyncShow() => PluginMain.ShowPalette();

        [CommandMethod("CADSYNC_SETUP")]
        public void CadSyncSetup()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var opts = new PromptStringOptions($"\nURL actual [{_config.ServerUrl}]. Nueva URL: ");
            var res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK)
            {
                _config.ServerUrl = res.StringResult;
                SaveConfig();
                Application.ShowAlertDialog("Reinicia AutoCAD para reconectar al servidor.");
            }
        }

        [CommandMethod("CADSYNC_STUDIO")]
        public void CadSyncStudio()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var opts = new PromptStringOptions(
                $"\nEstudio actual [{_config.StudioKey}]. Nueva Studio Key: ");
            var res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK)
            {
                _config.StudioKey = res.StringResult.Trim().ToUpper();
                SaveConfig();
                Application.ShowAlertDialog("Studio Key actualizado. Reinicia AutoCAD para reconectar.");
            }
        }

        [CommandMethod("CADSYNC_RESERVE_UI", CommandFlags.Session)]
        public void CadSyncReserveUI()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var opts = new PromptStringOptions("") { AllowSpaces = true };
            var res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK)
            {
                _config.LastLayer = res.StringResult.ToUpper();
                _ = ExecuteReserve(doc, _config.LastLayer);
            }
        }

        private static async Task ExecuteReserve(Document doc, string layer)
        {
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layer))
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord { Name = layer };
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 4);
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                db.Clayer = lt[layer];
                tr.Commit();
            }

            try
            {
                await LockLayerAsync(layer);
                PluginMain.MyControl?.AddLog($"Reservada y activada: {layer}");
            }
            catch { }
        }

        public static void ApplyLayerLocks(Document doc, Dictionary<string, LockInfo> serverLocks)
        {
            var db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            string currentUser = GetLastUser();

            foreach (ObjectId id in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                bool shouldLock = false;

                if (serverLocks != null && serverLocks.TryGetValue(ltr.Name, out var lockInfo))
                {
                    if (lockInfo != null && lockInfo.User != currentUser)
                    {
                        shouldLock = true;
                    }
                }

                ltr.IsLocked = shouldLock;
            }
            tr.Commit();
        }

        // ── Plan 2: Smart Push — all dirty layers ─────────────
        [CommandMethod("CADSYNC_PUSH_DELTA", CommandFlags.Session)]
        public async void CadSyncPushDelta()
        {
            await ExecutePushSmart(Application.DocumentManager.MdiActiveDocument, isAuto: false);
        }

        public static async Task ExecutePushSmart(Document doc, bool isAuto)
        {
            var dirtyLayers = PluginMain.GetDirtyTracker().FlushDirtyLayers();

            // Fallback to last manually reserved layer if nothing is dirty
            if (dirtyLayers.Count == 0 && !string.IsNullOrEmpty(_config.LastLayer))
                dirtyLayers.Add(_config.LastLayer);

            if (dirtyLayers.Count == 0) return;

            int uploaded = 0;
            var errors = new List<string>();

            foreach (var layer in dirtyLayers)
            {
                try
                {
                    bool ok = await PushLayerDelta(doc, layer);
                    if (ok) uploaded++;
                }
                catch (System.Exception ex)
                {
                    errors.Add($"{layer}: {ex.Message}");
                }
            }

            string summary = isAuto
                ? $"Auto-subida: {uploaded} capa(s) sincronizada(s)."
                : $"Push completado: {uploaded}/{dirtyLayers.Count} capa(s).";

            if (errors.Count > 0)
                summary += $" Errores: {string.Join("; ", errors)}";

            PluginMain.MyControl?.AddLog(summary);
            PluginMain.MyControl?.RefreshActiveLayers(new List<string>());
        }

        private static async Task<bool> PushLayerDelta(Document doc, string layer,
            bool enqueueOnFailure = true)
        {
            ObjectIdCollection ids = new ObjectIdCollection();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (string.Equals(ent.Layer, layer, StringComparison.OrdinalIgnoreCase))
                        ids.Add(id);
                }
                tr.Commit();
            }

            if (ids.Count == 0) return false;

            string tempPath = Path.Combine(Path.GetTempPath(), $"{layer}.dwg");
            using (doc.LockDocument())
            {
                using (var sideDb = doc.Database.Wblock(ids, Point3d.Origin))
                    sideDb.SaveAs(tempPath, DwgVersion.Current);
            }

            try
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(Commands.GetLastUser()), "user");
                form.Add(new StringContent(layer), "layer");

                string projectId = ProjectContextManager.GetBoundProjectId(doc);
                if (!string.IsNullOrEmpty(projectId))
                    form.Add(new StringContent(projectId), "projectId");

                using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                form.Add(new StreamContent(stream), "file", Path.GetFileName(tempPath));

                var response = await Commands.PostAsync($"{Commands.GetServerUrl()}/api/sync", form);
                if (response.IsSuccessStatusCode)
                {
                    try { File.Delete(tempPath); } catch { }
                    return true;
                }
            }
            catch
            {
                // Network failure — enqueue for later retry
                if (enqueueOnFailure)
                {
                    PluginMain.GetOfflineQueue().Enqueue(layer, tempPath);
                    PluginMain.MyControl?.AddLog($"Sin conexión — capa '{layer}' guardada en cola offline.");
                }
            }
            return false;
        }

        // ── Push / Pull ───────────────────────────────────────
        [CommandMethod("CADSYNC_PULL_UI", CommandFlags.Session)]
        public void CadSyncPullUI()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var opts = new PromptStringOptions("") { AllowSpaces = true };
            var res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK) _ = ExecutePull(doc, res.StringResult);
        }

        private static async Task ExecutePull(Document doc, string filename)
        {
            try
            {
                string encoded = Uri.EscapeDataString(filename);
                var response = await GetAsync($"{_config.ServerUrl}/api/download/{encoded}");
                if (response.IsSuccessStatusCode)
                {
                    string localPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);
                    using var fs = new FileStream(localPath, FileMode.Create);
                    await response.Content.CopyToAsync(fs);
                    Application.ShowAlertDialog($"Descargado en Escritorio:\n{filename}");
                }
                else
                {
                    doc.Editor.WriteMessage($"\n[CADSYNC] Error al descargar: {response.StatusCode}");
                }
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n[CADSYNC] Error crítico: {ex.Message}");
            }
        }

        [CommandMethod("CADSYNC_PULL_DELTA", CommandFlags.Session)]
        public void CadSyncPullDelta()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var opts = new PromptStringOptions("") { AllowSpaces = true };
            var res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK)
                _ = ExecuteMergeDelta(doc, res.StringResult.Trim().Replace("\"", ""));
        }

        public static async Task ExecuteMergeDelta(Document doc, string layerName)
        {
            try
            {
                string encoded = Uri.EscapeDataString($"{layerName}.dwg");
                var response = await GetAsync($"{_config.ServerUrl}/api/download/{encoded}");
                if (!response.IsSuccessStatusCode)
                {
                    doc.Editor.WriteMessage(
                        $"\n[CADSYNC] Error: No se pudo descargar {layerName}.dwg (HTTP {response.StatusCode})");
                    return;
                }

                string tempPath = Path.Combine(Path.GetTempPath(),
                    $"remote_{Guid.NewGuid().ToString().Substring(0, 8)}.dwg");
                using (var fs = new FileStream(tempPath, FileMode.Create))
                    await response.Content.CopyToAsync(fs);

                var db = doc.Database;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // Ensure layer exists
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (!lt.Has(layerName))
                    {
                        lt.UpgradeOpen();
                        var ltr = new LayerTableRecord { Name = layerName };
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    // Clear local layer content
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    foreach (ObjectId id in ms)
                    {
                        var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (string.Equals(ent.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                        {
                            tr.GetObject(id, OpenMode.ForWrite);
                            ent.Erase();
                        }
                    }

                    // Clone from remote DWG
                    using var sideDb = new Database(false, true);
                    sideDb.ReadDwgFile(tempPath, FileShare.Read, true, "");
                    var idsToClone = new ObjectIdCollection();
                    using (var trSide = sideDb.TransactionManager.StartTransaction())
                    {
                        var btSide = (BlockTable)trSide.GetObject(
                            sideDb.BlockTableId, OpenMode.ForRead);
                        var msSide = (BlockTableRecord)trSide.GetObject(
                            btSide[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        foreach (ObjectId id in msSide) idsToClone.Add(id);
                        trSide.Commit();
                    }

                    var idMap = new IdMapping();
                    db.WblockCloneObjects(idsToClone, ms.ObjectId, idMap,
                        DuplicateRecordCloning.Replace, false);

                    tr.Commit();
                    doc.Editor.Regen();
                }

                PluginMain.MyControl?.AddLog($"Capa {layerName} actualizada desde nube.");
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog($"Error al fusionar capa {layerName}:\n{ex.Message}");
            }
        }

        // ── Heartbeat ─────────────────────────────────────────
        public static async Task SendHeartbeatAsync()
        {
            var lockedLayers = PluginMain.GetDirtyTracker().PeekDirtyLayers();
            if (lockedLayers.Count == 0) return;
            try
            {
                var body = new StringContent(
                    JsonConvert.SerializeObject(new { layers = lockedLayers, user = _config.LastUser }),
                    Encoding.UTF8, "application/json");
                await PostAsync($"{_config.ServerUrl}/api/lock/heartbeat", body);
            }
            catch { /* non-critical */ }
        }

        // ── Offline Queue: process pending pushes ─────────────
        public static async Task ProcessOfflineQueueAsync(OfflineQueue queue)
        {
            if (queue.Count() == 0) return;
            var items = queue.DequeueAll();
            int ok = 0;
            var failed = new List<QueueItem>();

            foreach (var item in items)
            {
                try
                {
                    if (!File.Exists(item.DwgPath)) continue;
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent(_config.LastUser), "user");
                    form.Add(new StringContent(item.Layer), "layer");
                    using var stream = new FileStream(item.DwgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    form.Add(new StreamContent(stream), "file", Path.GetFileName(item.DwgPath));
                    var res = await PostAsync($"{_config.ServerUrl}/api/sync", form);
                    if (res.IsSuccessStatusCode)
                    {
                        ok++;
                        try { File.Delete(item.DwgPath); } catch { }
                    }
                    else
                    {
                        failed.Add(item);
                    }
                }
                catch { failed.Add(item); }
            }

            // Re-queue failures for next retry
            foreach (var f in failed) queue.Enqueue(f.Layer, f.DwgPath);

            if (ok > 0)
                PluginMain.MyControl?.AddLog($"Cola offline: {ok} capa(s) sincronizada(s). Pendientes: {failed.Count}");
        }

        // ── CADSYNC_STATUS command ─────────────────────────────
        [CommandMethod("CADSYNC_STATUS")]
        public async void CadSyncStatus()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            try
            {
                var res = await GetAsync($"{_config.ServerUrl}/api/status");
                if (!res.IsSuccessStatusCode)
                {
                    ed.WriteMessage($"\n[CADSYNC] Error al obtener estado: {res.StatusCode}");
                    return;
                }
                var json   = await res.Content.ReadAsStringAsync();
                var status = JsonConvert.DeserializeObject<StatusResponse>(json);

                ed.WriteMessage("\n╔══════════════════════════════════════╗");
                ed.WriteMessage("\n║       CAD SYNC — ESTADO ACTUAL        ║");
                ed.WriteMessage("\n╚══════════════════════════════════════╝");
                ed.WriteMessage($"\n  Estudio : {status?.Studio?.Name ?? "(sin clave)"}");
                ed.WriteMessage($"\n  Servidor: {_config.ServerUrl}");

                var locks = status?.Locks;
                if (locks == null || locks.Count == 0)
                {
                    ed.WriteMessage("\n  Bloqueos: ninguno activo");
                }
                else
                {
                    ed.WriteMessage($"\n  Bloqueos activos ({locks.Count}):");
                    foreach (var kvp in locks)
                        ed.WriteMessage($"\n    • {kvp.Key,-20} → {kvp.Value.User}");
                }

                int pending = PluginMain.GetOfflineQueue().Count();
                if (pending > 0)
                    ed.WriteMessage($"\n  ⚠ Cola offline: {pending} capa(s) pendientes de subida");

                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[CADSYNC] No se pudo conectar al servidor: {ex.Message}");
            }
        }

        [CommandMethod("CADSYNC_PUSH", CommandFlags.Session)]
        public async void CadSyncPush()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            try
            {
                string filePath = doc.Name;
                if (!File.Exists(filePath)) return;
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(_config.LastUser), "user");
                using var stream = new FileStream(filePath, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite);
                form.Add(new StreamContent(stream), "file", Path.GetFileName(filePath));
                await PostAsync($"{_config.ServerUrl}/api/sync", form);
            }
            catch { }
        }
    }

    // ── Data Transfer Objects ─────────────────────────────────
    public class LockInfo
    {
        [JsonProperty("user")] [JsonPropertyName("user")]
        public string? User { get; set; }
        [JsonProperty("lockedAt")] [JsonPropertyName("lockedAt")]
        public string? LockedAt { get; set; }
    }

    public class LockCheckResult
    {
        [JsonProperty("locked")] [JsonPropertyName("locked")]
        public bool Locked { get; set; }
        [JsonProperty("by")] [JsonPropertyName("by")]
        public string? LockedBy { get; set; }
        [JsonProperty("since")] [JsonPropertyName("since")]
        public string? Since { get; set; }
    }

    public class SyncEntry
    {
        [JsonProperty("user")] [JsonPropertyName("user")]       public string? User     { get; set; }
        [JsonProperty("layer")] [JsonPropertyName("layer")]      public string? Layer    { get; set; }
        [JsonProperty("filename")] [JsonPropertyName("filename")]public string? Filename { get; set; }
    }

    public class CursorPayload
    {
        [JsonProperty("user")] [JsonPropertyName("user")] public string? User { get; set; }
        [JsonProperty("x")] [JsonPropertyName("x")]       public double X    { get; set; }
        [JsonProperty("y")] [JsonPropertyName("y")]       public double Y    { get; set; }
        [JsonProperty("z")] [JsonPropertyName("z")]       public double Z    { get; set; }
    }

    public class CursorRemovePayload
    {
        [JsonProperty("user")] [JsonPropertyName("user")] public string? User { get; set; }
    }

    public class StatusResponse
    {
        [JsonProperty("studio")] [JsonPropertyName("studio")]   public StudioInfo?                        Studio  { get; set; }
        [JsonProperty("locks")] [JsonPropertyName("locks")]    public Dictionary<string, LockInfo>?      Locks   { get; set; }
        [JsonProperty("history")] [JsonPropertyName("history")]  public List<SyncEntry>?                   History { get; set; }
    }

    public class StudioInfo
    {
        [JsonProperty("name")] [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
