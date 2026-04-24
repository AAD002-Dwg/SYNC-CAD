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
                // Usamos el Handle nativo en Hexadecimal como UUID en Fase 1
                string uuid = ent.Handle.ToString().ToLowerInvariant();
                OwnershipRegistry.RegisterLocalEntity(uuid, ent.Id);

                if (_isCommandRunning)
                {
                    _newlyCreatedObjects.Add(ent.Id);
                }
            }
        }

        private static bool IsEditCommand(string commandName)
        {
            var upperCmd = commandName.ToUpperInvariant();
            return upperCmd.Contains("MOVE") || upperCmd.Contains("COPY") || 
                   upperCmd.Contains("COLOR") || upperCmd.Contains("ERASE") ||
                   upperCmd.Contains("GRIP") || upperCmd.Contains("PROPERTIES");
        }

        private static void OnCommandWillStart(object sender, CommandEventArgs e)
        {
            if (!IsEditCommand(e.GlobalCommandName)) return;

            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;
            
            // Obtenemos los objetos actualmente seleccionados antes de la mutación
            var selection = editor.SelectImplied();
            if (selection.Status != PromptStatus.OK) return;

            _isCommandRunning = true;
            _preCommandSnapshots.Clear();
            _newlyCreatedObjects.Clear();

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

        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (!_isCommandRunning) return;
            _isCommandRunning = false;

            if (_preCommandSnapshots.Count == 0) return;

            var doc = Application.DocumentManager.MdiActiveDocument;

            using (var tr = doc.TransactionManager.StartTransaction())
            {
                // 1. Detección Crítica de COPY: Emitir CREATE para las entidades clonadas
                foreach (ObjectId newId in _newlyCreatedObjects)
                {
                    if (newId.IsErased) continue;
                    var newEnt = tr.GetObject(newId, OpenMode.ForRead) as Entity;
                    if (newEnt != null)
                    {
                        // TODO: PayloadBuilder.EmitCreate(newId.Handle.Value.ToString(), newEnt);
                        // Esto garantiza que el CREATE viaja antes del UPDATE
                    }
                }

                // 2. Diffing Clásico: Emitir UPDATE para entidades mutadas
                foreach (var kvp in _preCommandSnapshots)
                {
                    ObjectId id = kvp.Key;
                    EntitySnapshot before = kvp.Value;

                    if (id.IsErased) continue; // Si se borró, sería un DELETE, se maneja distinto

                    var after = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    var propDeltas = _diffEngine.ComputeDelta(before, after, tr);

                    if (propDeltas.Length > 0)
                    {
                        // TODO: PayloadBuilder.EmitUpdate(id.Handle.Value.ToString(), propDeltas);
                    }
                }
                tr.Commit();
            }

            _preCommandSnapshots.Clear();
            _newlyCreatedObjects.Clear();
        }
    }
}
