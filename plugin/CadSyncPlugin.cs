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
            if (res.Status == PromptStatus.OK) _ = ExecuteReserve(doc, res.StringResult.ToUpper());
        }

        private static async Task ExecuteReserve(Document doc, string layer)
        {
            var data = new { layer, user = _config.LastUser };
            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
            try {
                var response = await client.PostAsync($"{_config.ServerUrl}/api/lock", content);
                if (response.IsSuccessStatusCode) {
                    ApplyLayerLocks(doc, new List<string> { layer }); // El usuario local desbloquea su capa
                    if (PluginMain.MyControl != null) PluginMain.MyControl.AddLog($"Has reservado {layer}");
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

        [CommandMethod("CADSYNC_PULL_UI", CommandFlags.Session)]
        public void CadSyncPullUI()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            PromptStringOptions opts = new PromptStringOptions("");
            PromptResult res = doc.Editor.GetString(opts);
            if (res.Status == PromptStatus.OK) _ = ExecutePull(doc, res.StringResult);
        }

        private static async Task ExecutePull(Document doc, string filename)
        {
            try {
                var response = await client.GetAsync($"{_config.ServerUrl}/api/download/{filename}");
                if (response.IsSuccessStatusCode) {
                    string localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);
                    using (var fs = new FileStream(localPath, FileMode.Create)) await response.Content.CopyToAsync(fs);
                    Application.ShowAlertDialog($"Descargado en Escritorio:\n{filename}");
                }
            } catch { }
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
