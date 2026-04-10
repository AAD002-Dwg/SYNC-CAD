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
                _ = ConnectSocket();
            } catch { }
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

            _socket.On("sync_update", response => {
                if (MyControl != null) _ = MyControl.RefreshFiles();
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
            PromptResult res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK) {
                _config.LastLayer = res.StringResult.ToUpper();
                _ = ExecuteReserve(doc, _config.LastLayer);
            }
        }

        private static async Task ExecuteReserve(Document doc, string layer)
        {
            var data = new { layer, user = _config.LastUser };
            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
            try {
                var response = await client.PostAsync($"{_config.ServerUrl}/api/lock", content);
                if (response.IsSuccessStatusCode) {
                    ApplyLayerLocks(doc, new List<string> { layer });
                    if (PluginMain.MyControl != null) PluginMain.MyControl.AddLog($"Reservada: {layer}");
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
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (string.IsNullOrEmpty(_config.LastLayer)) {
                Application.ShowAlertDialog("No tienes ninguna capa reservada para subir delta.");
                return;
            }

            try {
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

                if (ids.Count == 0) {
                    Application.ShowAlertDialog($"La capa {_config.LastLayer} está vacía.");
                    return;
                }

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
                if (PluginMain.MyControl != null) PluginMain.MyControl.AddLog($"Delta {_config.LastLayer} subido.");
            } catch (System.Exception ex) {
                doc.Editor.WriteMessage($"\nError en Push Delta: {ex.Message}");
            }
        }

        [CommandMethod("CADSYNC_PULL_DELTA", CommandFlags.Session)]
        public void CadSyncPullDelta()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            PromptStringOptions opts = new PromptStringOptions("");
            PromptResult res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK) _ = ExecuteMergeDelta(doc, res.StringResult.ToUpper());
        }

        private static async Task ExecuteMergeDelta(Document doc, string layerName)
        {
            try {
                string filename = $"{layerName}.dwg";
                var response = await client.GetAsync($"{_config.ServerUrl}/api/download/{filename}");
                if (!response.IsSuccessStatusCode) return;

                string tempPath = Path.Combine(Path.GetTempPath(), $"remote_{layerName}.dwg");
                using (var fs = new FileStream(tempPath, FileMode.Create)) await response.Content.CopyToAsync(fs);

                Database db = doc.Database;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction()) {
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
