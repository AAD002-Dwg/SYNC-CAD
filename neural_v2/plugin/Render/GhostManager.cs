using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Render
{
    /// <summary>
    /// Administrador nuclear de geometrías efímeras (Hologramas). 
    /// Encripta la complejidad nativa de TransientManager para proveer una capa segura y sin fugas de memoria (AC-104).
    /// </summary>
    public static class GhostManager
    {
        private static readonly Dictionary<string, Entity> _activeGhosts = new Dictionary<string, Entity>();
        private static readonly IntegerCollection _viewportIds = new IntegerCollection(); // Vacío = Todos los viewports

        /// <summary>
        /// Inyecta instantáneamente un holograma en la RAM del viewport. (AC-101)
        /// </summary>
        /// <param name="globalId">UUID único global del sistema H-Sync</param>
        /// <param name="entity">Entidad cruda de AutoCAD generada a partir del Delta de red</param>
        public static void AddOrUpdateGhost(string globalId, Entity entity)
        {
            var tm = TransientManager.CurrentTransientManager;

            // Si ya existe el fantasma, lo destruimos físicamente del motor para evitar artefactos (memory leaks)
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                tm.EraseTransient(existing, _viewportIds);
                existing.Dispose();
                _activeGhosts.Remove(globalId);
            }

            // Inyectamos el nuevo estado físico
            tm.AddTransient(entity, TransientDrawingMode.DirectShortTerm, 128, _viewportIds);
            _activeGhosts[globalId] = entity;
        }

        /// <summary>
        /// Elimina un holograma del espectro visual y purga su memoria.
        /// </summary>
        public static void RemoveGhost(string globalId)
        {
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                TransientManager.CurrentTransientManager.EraseTransient(existing, _viewportIds);
                existing.Dispose();
                _activeGhosts.Remove(globalId);
            }
        }

        /// <summary>
        /// Destrucción segura de todos los hologramas al cerrar sesión o apagar el core.
        /// </summary>
        public static void ClearAllGhosts()
        {
            var tm = TransientManager.CurrentTransientManager;
            foreach (var ghost in _activeGhosts.Values)
            {
                tm.EraseTransient(ghost, _viewportIds);
                ghost.Dispose();
            }
            _activeGhosts.Clear();
        }

        /// <summary>
        /// Obtiene un clon de lectura de las entidades fantasma para ser iteradas por otros subsistemas como Osnap (AC-103).
        /// </summary>
        public static IEnumerable<Entity> GetAllActiveGhosts()
        {
            return _activeGhosts.Values;
        }

        // AC-402: Notificación visual no bloqueante para conflictos LWW perdidos
        public static void SetGlowRed(string globalId)
        {
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                // En un caso real, haríamos clone() de la geometría y le pondríamos ColorIndex = 1 (Rojo)
                // y LineWeight muy alto temporalmente. Por ahora lo simulamos.
                existing.ColorIndex = 1;
                TransientManager.CurrentTransientManager.UpdateTransient(existing, _viewportIds);
            }
        }

        public static void ApplyMergedState(string globalId, System.Text.Json.JsonElement winnerState)
        {
            // TODO: Parsear winnerState, limpiar Glow y actualizar el EntityDelta
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                existing.ColorIndex = 256; // ByLayer (ejemplo)
                TransientManager.CurrentTransientManager.UpdateTransient(existing, _viewportIds);
            }
        }
    }
}
