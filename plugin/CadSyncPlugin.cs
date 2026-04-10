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
using Newtonsoft.Json;

namespace CadSyncPlugin
{
    public class Config
    {
        public string ServerUrl { get; set; } = "http://localhost:3001";
        public string LastUser { get; set; } = Environment.UserName;
        public string ReservedLayer { get; set; } = "";
    }

    public class Commands
    {
        private static Config _config = new Config();
        private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CadSyncConfig.json");
        private static readonly HttpClient client = new HttpClient();

        static Commands() { LoadConfig(); }

        private static void LoadConfig()
        {
            if (File.Exists(ConfigPath))
                _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigPath));
        }

        private static void SaveConfig()
        {
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config));
        }

        [CommandMethod("CADSYNC_SETUP")]
        public void CadSyncSetup()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            PromptStringOptions opts = new PromptStringOptions($"\nURL actual [{_config.ServerUrl}]. Nueva URL: ");
            opts.AllowSpaces = false;
            PromptResult res = ed.GetString(opts);

            if (res.Status == PromptStatus.OK)
            {
                _config.ServerUrl = res.StringResult;
                SaveConfig();
                ed.WriteMessage("\n[CADSYNC] Servidor configurado correctamente.");
            }
        }

        [CommandMethod("CADSYNC_RESERVE")]
        public async void CadSyncReserve()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            PromptStringOptions opts = new PromptStringOptions("\nCapa a reservar (ej: ELECTRICIDAD): ");
            PromptResult res = ed.GetString(opts);

            if (res.Status == PromptStatus.OK)
            {
                string layer = res.StringResult.ToUpper();
                _config.ReservedLayer = layer;
                
                // Intentar bloquear en el servidor
                var data = new { layer, user = _config.LastUser };
                var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                
                try {
                    var response = await client.PostAsync($"{_config.ServerUrl}/api/lock", content);
                    if (response.IsSuccessStatusCode) {
                        ed.WriteMessage($"\n[CADSYNC] Capa {layer} reservada con éxito.");
                        ApplyLayerLocks(doc, layer);
                        SaveConfig();
                    } else {
                        ed.WriteMessage("\n[CADSYNC] Error: Capa ocupada por otro usuario.");
                    }
                } catch (System.Exception ex) { ed.WriteMessage("\nError: " + ex.Message); }
            }
        }

        private void ApplyLayerLocks(Document doc, string allowedLayer)
        {
            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    if (ltr.Name.ToUpper() == allowedLayer)
                        ltr.IsLocked = false;
                    else
                        ltr.IsLocked = true;
                }
                tr.Commit();
            }
            doc.Editor.WriteMessage($"\n[CADSYNC] Solo la capa {allowedLayer} esta habilitada para edicion.");
        }

        [CommandMethod("CADSYNC_PUSH")]
        public async void CadSyncPush()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try {
                string filePath = doc.Name;
                if (!File.Exists(filePath)) { ed.WriteMessage("\nError: Guarda el archivo."); return; }

                using (var form = new MultipartFormDataContent()) {
                    form.Add(new StringContent(_config.LastUser), "user");
                    form.Add(new StringContent("Cloud-Project"), "project");

                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                        form.Add(new StreamContent(stream), "file", Path.GetFileName(filePath));
                        var response = await client.PostAsync($"{_config.ServerUrl}/api/sync", form);
                        if (response.IsSuccessStatusCode) ed.WriteMessage("\n[CADSYNC] Sincronizacion completa.");
                    }
                }
            }
            catch (System.Exception ex) { ed.WriteMessage("\nError: " + ex.Message); }
        }
    }
}
