using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Windows;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json;
using SocketIOClient;
using SocketIOClient.Newtonsoft.Json;

[assembly: ExtensionApplication(typeof(CadSyncPlugin.PluginMain))]

namespace CadSyncPlugin
{
    public class Config
    {
        public string ServerUrl { get; set; } = "http://localhost:3001";
        public string LastUser { get; set; } = Environment.UserName;
        public string LastLayer { get; set; } = "";
        public bool AutoPush { get; set; } = false;
        public bool AutoPull { get; set; } = false;
    }

    public class PluginMain : IExtensionApplication
    {
        private static PaletteSet _paletteSet;
        public static CadSyncControl MyControl;
        private static SocketIOClient.SocketIO _socket;

        public void Initialize()
        {
            try {
                Application.Idle += (s, e) => ShowPalette();
                Application.DocumentManager.DocumentActivated += (s, e) => RegisterDocEvents(e.Document);
                if (Application.DocumentManager.MdiActiveDocument != null) 
                    RegisterDocEvents(Application.DocumentManager.MdiActiveDocument);

                _ = ConnectSocket();
            } catch { }
        }

        private void RegisterDocEvents(Document doc)
        {
            if (doc == null) return;
            doc.CommandEnded -= Doc_CommandEnded;
            doc.CommandEnded += Doc_CommandEnded;
        }

        private async void Doc_CommandEnded(object sender, CommandEventArgs e)
        {
            if (!Commands.GetAutoPush()) return;
            
            // Lista de comandos que NO disparan subida (para evitar spam)
            var ignored = new List<string> { "CADSYNC", "SAVE", "QSAVE", "SAVEAS", "GRIP_STRETCH" };
            if (ignored.Contains(e.GlobalCommandName.ToUpper())) return;

            Document doc = sender as Document;
            await Task.Delay(2000); // Debounce de 2 segundos
            _ = Commands.ExecutePushDelta(doc, true);
        }

        private async Task ConnectSocket()
        {
            var url = Commands.GetServerUrl();
            _socket = new SocketIOClient.SocketIO(url);
            
            _socket.On("lock_update", response => {
                var locks = response.GetValue<Dictionary<string, dynamic>>();
                if (MyControl != null) MyControl.UpdateLocks(locks);
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[CADSYNC] Actualización de bloqueos recibida.");
            });

            _socket.On("sync_update", async response => {
                if (MyControl != null) _ = MyControl.RefreshFiles();

                if (Commands.GetAutoPull()) {
                    try {
                        var data = response.GetValue<dynamic>();
                        string layer = data?.layer?.ToString();
                        if (!string.IsNullOrEmpty(layer)) {
                            // No bajar nuestra propia capa si acabamos de subirla
                            if (layer.ToUpper() == Commands.GetLastLayer().ToUpper()) return;
                            
                            Document doc = Application.DocumentManager.MdiActiveDocument;
                            doc.Editor.WriteMessage($"\n[CADSYNC] Auto-Descarga detectada para capa: {layer}");
                            await Commands.ExecuteMergeDelta(doc, layer);
                        }
                    } catch { 
                        // Si falla el parseo, al menos refrescamos lista
                    }
                }
            });

            try { await _socket.ConnectAsync(); } catch { }
        }

        public void Terminate() { if (_socket != null) _socket.DisconnectAsync(); }

        public static void ShowPalette()
        {
            if (_paletteSet == null)
            {
                _paletteSet = new PaletteSet("CAD Sync", new Guid("F30B4A22-3B1E-4E9A-8765-CAD123456789"));
                _paletteSet.Style = PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton;
                _paletteSet.MinimumSize = new System.Drawing.Size(300, 500);

                MyControl = new CadSyncControl();
                _paletteSet.AddVisual("Proyecto Activo", MyControl);
            }
            _paletteSet.Visible = true;
        }
    }

    public class Commands
    {
        private static Config _config = new Config();
        private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CadSyncConfig.json");
        public static readonly HttpClient client = new HttpClient();

        static Commands() { LoadConfig(); }
        public static string GetServerUrl() => _config.ServerUrl;
        public static string GetLastLayer() => _config.LastLayer;
        public static bool GetAutoPush() => _config.AutoPush;
        public static void SetAutoPush(bool val) { _config.AutoPush = val; SaveConfig(); }
        public static bool GetAutoPull() => _config.AutoPull;
        public static void SetAutoPull(bool val) { _config.AutoPull = val; SaveConfig(); }

        private static void LoadConfig()
        {
            if (File.Exists(ConfigPath))
                _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigPath));
        }

        private static void SaveConfig()
        {
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config));
        }

        [CommandMethod("CADSYNC")]
        public void CadSyncShow() => PluginMain.ShowPalette();

        [CommandMethod("CADSYNC_SETUP")]
        public void CadSyncSetup()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            PromptStringOptions opts = new PromptStringOptions($"\nURL actual [{_config.ServerUrl}]. Nueva URL: ");
            PromptResult res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK) {
                _config.ServerUrl = res.StringResult;
                SaveConfig();
                Application.ShowAlertDialog("Reinicia AutoCAD para conectar al nuevo servidor.");
            }
        }

        [CommandMethod("CADSYNC_RESERVE_UI", CommandFlags.Session)]
        public void CadSyncReserveUI()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            PromptStringOptions opts = new PromptStringOptions("");
            opts.AllowSpaces = true;
            PromptResult res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK) {
                _config.LastLayer = res.StringResult.ToUpper();
                _ = ExecuteReserve(doc, _config.LastLayer);
            }
        }

        private static async Task ExecuteReserve(Document doc, string layer)
        {
            // 0. Asegurar que la capa existe localmente
            Database db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction()) {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layer)) {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord();
                    ltr.Name = layer;
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 4); // Cyan por defecto
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                db.Clayer = lt[layer]; // Activar la capa
                tr.Commit();
            }

            var data = new { layer, user = _config.LastUser };
            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
            try {
                var response = await client.PostAsync($"{_config.ServerUrl}/api/lock", content);
                if (response.IsSuccessStatusCode) {
                    ApplyLayerLocks(doc, new List<string> { layer });
                    if (PluginMain.MyControl != null) PluginMain.MyControl.AddLog($"Reservada y activada: {layer}");
                }
            } catch { }
        }

        public static void ApplyLayerLocks(Document doc, List<string> allowedLayers)
        {
            Database db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction()) {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt) {
                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    bool isAllowed = false;
                    foreach (var al in allowedLayers) if (ltr.Name.ToUpper() == al.ToUpper()) isAllowed = true;
                    ltr.IsLocked = !isAllowed;
                }
                tr.Commit();
            }
        }

        [CommandMethod("CADSYNC_PUSH_DELTA", CommandFlags.Session)]
        public async void CadSyncPushDelta()
        {
            await ExecutePushDelta(Application.DocumentManager.MdiActiveDocument, false);
        }

        public static async Task ExecutePushDelta(Document doc, bool isAuto)
        {
            if (string.IsNullOrEmpty(_config.LastLayer)) return;

            try {
                using (doc.LockDocument()) {
                    Database db = doc.Database;
                    ObjectIdCollection ids = new ObjectIdCollection();
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        foreach (ObjectId id in ms) {
                            Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                            if (ent.Layer.ToUpper() == _config.LastLayer) ids.Add(id);
                        }
                        tr.Commit();
                    }

                    if (ids.Count == 0) return;

                    string tempPath = Path.Combine(Path.GetTempPath(), $"{_config.LastLayer}.dwg");
                    using (Database sideDb = db.Wblock(ids, Point3d.Origin)) {
                        sideDb.SaveAs(tempPath, DwgVersion.Current);
                    }

                    using (var form = new MultipartFormDataContent()) {
                        form.Add(new StringContent(_config.LastUser), "user");
                        form.Add(new StringContent(_config.LastLayer), "layer");
                        using (var stream = new FileStream(tempPath, FileMode.Open)) {
                            form.Add(new StreamContent(stream), "file", Path.GetFileName(tempPath));
                            await client.PostAsync($"{_config.ServerUrl}/api/sync", form);
                        }
                    }
                }
                string msg = isAuto ? "Auto-Subida exitosa." : $"Delta {_config.LastLayer} subido.";
                if (PluginMain.MyControl != null) PluginMain.MyControl.AddLog(msg);
            } catch (System.Exception ex) {
                doc.Editor.WriteMessage($"\nError en Push Delta: {ex.Message}");
            }
        }

        [CommandMethod("CADSYNC_PULL_UI", CommandFlags.Session)]
        public void CadSyncPullUI()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            PromptStringOptions opts = new PromptStringOptions("");
            opts.AllowSpaces = true;
            PromptResult res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK) _ = ExecutePull(doc, res.StringResult);
        }

        private static async Task ExecutePull(Document doc, string filename)
        {
            try {
                string encodedFile = Uri.EscapeDataString(filename);
                var response = await client.GetAsync($"{_config.ServerUrl}/api/download/{encodedFile}");
                if (response.IsSuccessStatusCode) {
                    string localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);
                    using (var fs = new FileStream(localPath, FileMode.Create)) await response.Content.CopyToAsync(fs);
                    Application.ShowAlertDialog($"Descargado en Escritorio:\n{filename}");
                } else {
                    doc.Editor.WriteMessage($"\n[CADSYNC] Error al descargar: {response.StatusCode}");
                }
            } catch (System.Exception ex) {
                doc.Editor.WriteMessage($"\n[CADSYNC] Error crítico: {ex.Message}");
            }
        }

        [CommandMethod("CADSYNC_PULL_DELTA", CommandFlags.Session)]
        public void CadSyncPullDelta()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            PromptStringOptions opts = new PromptStringOptions("");
            opts.AllowSpaces = true;
            PromptResult res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK) {
                string cleaned = res.StringResult.Trim().Replace("\"", "");
                _ = ExecuteMergeDelta(doc, cleaned);
            }
        }

        public static async Task ExecuteMergeDelta(Document doc, string layerName)
        {
            try {
                string filename = $"{layerName}.dwg";
                string encodedFile = Uri.EscapeDataString(filename);
                var response = await client.GetAsync($"{_config.ServerUrl}/api/download/{encodedFile}");
                
                if (!response.IsSuccessStatusCode) {
                    doc.Editor.WriteMessage($"\n[CADSYNC] Error: No se pudo descargar {filename} (HTTP {response.StatusCode})");
                    return;
                }

                string tempPath = Path.Combine(Path.GetTempPath(), $"remote_{Guid.NewGuid().ToString().Substring(0,8)}.dwg");
                using (var fs = new FileStream(tempPath, FileMode.Create)) await response.Content.CopyToAsync(fs);

                Database db = doc.Database;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction()) {
                    // 0. Asegurar que la capa existe
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (!lt.Has(layerName)) {
                        lt.UpgradeOpen();
                        LayerTableRecord ltr = new LayerTableRecord();
                        ltr.Name = layerName;
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    // 1. Borrar local
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    foreach (ObjectId id in ms) {
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (ent.Layer.ToUpper() == layerName) {
                            tr.GetObject(id, OpenMode.ForWrite);
                            ent.Erase();
                        }
                    }

                    // 2. Traer remoto
                    using (Database sideDb = new Database(false, true)) {
                        sideDb.ReadDwgFile(tempPath, FileShare.Read, true, "");
                        ObjectIdCollection idsToClone = new ObjectIdCollection();
                        using (var trSide = sideDb.TransactionManager.StartTransaction()) {
                            BlockTable btSide = (BlockTable)trSide.GetObject(sideDb.BlockTableId, OpenMode.ForRead);
                            BlockTableRecord msSide = (BlockTableRecord)trSide.GetObject(btSide[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                            foreach (ObjectId id in msSide) idsToClone.Add(id);
                            trSide.Commit();
                        }
                        
                        IdMapping idMap = new IdMapping();
                        db.WblockCloneObjects(idsToClone, ms.ObjectId, idMap, DuplicateRecordCloning.Replace, false);
                    }

                    tr.Commit();
                    doc.Editor.Regen();
                }
                if (PluginMain.MyControl != null) PluginMain.MyControl.AddLog($"Capa {layerName} actualizada.");
            } catch (System.Exception ex) {
                Application.ShowAlertDialog($"Error al fusionar: {ex.Message}");
            }
        }

        [CommandMethod("CADSYNC_PUSH", CommandFlags.Session)]
        public async void CadSyncPush()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            try {
                string filePath = doc.Name;
                if (!File.Exists(filePath)) return;
                using (var form = new MultipartFormDataContent()) {
                    form.Add(new StringContent(_config.LastUser), "user");
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                        form.Add(new StreamContent(stream), "file", Path.GetFileName(filePath));
                        await client.PostAsync($"{_config.ServerUrl}/api/sync", form);
                    }
                }
            } catch { }
        }
    }
}
