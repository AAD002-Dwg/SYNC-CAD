using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace HSync.Core.Network.Diffing
{
    /// <summary>
    /// Despachador Central O(1) sin Reflection para máxima performance.
    /// Calcula exactamente qué propiedades han mutado entre dos estados (AC-401).
    /// </summary>
    public class DiffEngine
    {
        private readonly Dictionary<Type, IEntityDiffer> _differs;

        public DiffEngine()
        {
            _differs = new Dictionary<Type, IEntityDiffer>
            {
                [typeof(Line)] = new LineDiffer(),
                // [typeof(Circle)] = new CircleDiffer(), // TODO: Fase 2
                // [typeof(BlockReference)] = new BlockRefDiffer(),
                // [typeof(DBText)] = new TextDiffer(),
            };
        }

        public EntitySnapshot CaptureSnapshot(Entity entity, Transaction tr)
        {
            if (_differs.TryGetValue(entity.GetType(), out var differ))
            {
                return differ.CaptureSnapshot(entity, tr);
            }
            return null;
        }

        public Dictionary<string, object> ComputeDelta(EntitySnapshot snapBefore, Entity after, Transaction tr)
        {
            if (!_differs.TryGetValue(after.GetType(), out var differ))
                return new Dictionary<string, object>(); // Tipo no soportado, ignorar
                
            var snapAfter  = differ.CaptureSnapshot(after, tr);
            
            return differ.Diff(snapBefore, snapAfter);
        }
    }
}
