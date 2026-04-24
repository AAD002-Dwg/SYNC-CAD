using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using HSync.Core.Network.Diffing;

namespace HSync.Core.Network
{
    /// <summary>
    /// Escucha los comandos nativos de AutoCAD para ejecutar el DiffEngine
    /// exactamente en el momento adecuado, superando el problema de la falta de 'before-state'.
    /// </summary>
    public static class EventMonitor
    {
        private static DiffEngine _diffEngine;
        
        // El Snapshot temporal que guardará el estado ANTES del comando
        private static readonly Dictionary<ObjectId, EntitySnapshot> _preCommandSnapshots = new Dictionary<ObjectId, EntitySnapshot>();
        
        // Entidades generadas por el comando en curso (ej: COPY)
        private static readonly HashSet<ObjectId> _newlyCreatedObjects = new HashSet<ObjectId>();
        
        private static bool _isCommandRunning = false;

        public static void Initialize()
        {
            _diffEngine = new DiffEngine();
            Application.DocumentManager.MdiActiveDocument.CommandWillStart += OnCommandWillStart;
            Application.DocumentManager.MdiActiveDocument.CommandEnded += OnCommandEnded;
            Application.DocumentManager.MdiActiveDocument.Database.ObjectAppended += OnObjectAppended;
        }

        public static void Terminate()
        {
            if (Application.DocumentManager.MdiActiveDocument != null)
            {
                Application.DocumentManager.MdiActiveDocument.CommandWillStart -= OnCommandWillStart;
                Application.DocumentManager.MdiActiveDocument.CommandEnded -= OnCommandEnded;
                Application.DocumentManager.MdiActiveDocument.Database.ObjectAppended -= OnObjectAppended;
            }
        }

        private static void OnObjectAppended(object sender, ObjectEventArgs e)
        {
            if (e.DBObject is Entity ent)
            {
                // AC-601: Registrar propiedad nativa (Canónico vs Proyectado)
                string uuid = ent.Handle.ToString().ToLowerInvariant();
                OwnershipRegistry.RegisterLocalEntity(uuid, ent.Id);

                if (_isCommandRunning)
                {
                    _newlyCreatedObjects.Add(ent.Id);
                }
                else
                {
                    // Si no hay comando (ej: dibujo directo de primitiva)
                    // Emitimos el CREATE inmediatamente después de que el objeto sea persistido
                    // Nota: En AutoCAD, a veces es mejor esperar al CommandEnded incluso para CIRCULO
                }
            }
        }

        private static bool IsEditCommand(string commandName)
        {
            var upperCmd = commandName.ToUpperInvariant();
            return upperCmd.Contains("MOVE") || upperCmd.Contains("COPY") || 
                   upperCmd.Contains("COLOR") || upperCmd.Contains("ERASE") ||
                   upperCmd.Contains("GRIP") || upperCmd.Contains("PROPERTIES") ||
                   upperCmd.Contains("CIRCLE") || upperCmd.Contains("LINE");
        }

        private static void OnCommandWillStart(object sender, CommandEventArgs e)
        {
            if (!IsEditCommand(e.GlobalCommandName)) return;

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var editor = doc.Editor;
            
            _isCommandRunning = true;
            _preCommandSnapshots.Clear();
            _newlyCreatedObjects.Clear();

            // Obtenemos los objetos actualmente seleccionados antes de la mutación
            var selection = editor.SelectImplied();
            if (selection.Status != PromptStatus.OK) return;

            using (var tr = doc.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in selection.Value.GetObjectIds())
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        var snapshot = _diffEngine.CaptureSnapshot(ent, tr);
                        if (snapshot != null)
                        {
                            _preCommandSnapshots[id] = snapshot;
                        }
                    }
                }
                tr.Commit();
            }
        }

        private static async void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (!_isCommandRunning) return;
            _isCommandRunning = false;

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var client = HSyncPlugin.SocketClient;
            if (client == null || !client.IsConnected) return;

            using (var tr = doc.TransactionManager.StartTransaction())
            {
                // 1. Detección Crítica de CREATE: Entidades nuevas (ej: COPY o dibujo directo)
                foreach (ObjectId newId in _newlyCreatedObjects)
                {
                    if (newId.IsErased) continue;
                    var newEnt = tr.GetObject(newId, OpenMode.ForRead) as Entity;
                    if (newEnt != null)
                    {
                        string uuid = newId.Handle.ToString().ToLowerInvariant();
                        string json = PayloadBuilder.BuildCreate(uuid, newEnt, client.UserId);
                        await client.SendDeltaAsync(json);
                    }
                }

                // 2. Diffing Clásico: Emitir UPDATE para entidades mutadas (ej: MOVE)
                foreach (var kvp in _preCommandSnapshots)
                {
                    ObjectId id = kvp.Key;
                    EntitySnapshot before = kvp.Value;

                    if (id.IsErased) continue; 

                    var after = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (after == null) continue;

                    var changes = _diffEngine.ComputeDelta(before, after, tr);
                    if (changes.Count > 0)
                    {
                        string uuid = id.Handle.ToString().ToLowerInvariant();
                        string json = PayloadBuilder.BuildUpdate(uuid, changes, client.UserId);
                        await client.SendDeltaAsync(json);
                    }
                }
                tr.Commit();
            }

            _preCommandSnapshots.Clear();
            _newlyCreatedObjects.Clear();
        }
    }
}
