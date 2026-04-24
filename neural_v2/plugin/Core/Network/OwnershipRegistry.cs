using System.Collections.Concurrent;
using Autodesk.AutoCAD.DatabaseServices;

namespace HSync.Core.Network
{
    /// <summary>
    /// AC-601 Fix: Puente Canónico-Proyectado.
    /// Registra qué entidades nativas en el archivo DWG fueron creadas por la sesión local.
    /// Esto permite saber si un delta remoto intenta modificar una entidad "nuestra" 
    /// para aplicar el Shadowing en lugar de intentar buscarla en el diccionario de Hologramas.
    /// </summary>
    public static class OwnershipRegistry
    {
        private static readonly ConcurrentDictionary<string, ObjectId> _localOwnedEntities = new ConcurrentDictionary<string, ObjectId>();

        /// <summary>
        /// Registra una entidad nativa recién creada en AutoCAD como de propiedad local.
        /// </summary>
        public static void RegisterLocalEntity(string globalUuid, ObjectId nativeHandle)
        {
            _localOwnedEntities[globalUuid] = nativeHandle;
        }

        /// <summary>
        /// Comprueba si una entidad es de propiedad local (nativa) en lugar de un Holograma remoto.
        /// </summary>
        public static bool IsOwnedLocally(string globalUuid)
        {
            return _localOwnedEntities.ContainsKey(globalUuid);
        }

        /// <summary>
        /// Recupera el puntero real (ObjectId) a la base de datos de AutoCAD.
        /// </summary>
        public static ObjectId GetNativeHandle(string globalUuid)
        {
            if (_localOwnedEntities.TryGetValue(globalUuid, out ObjectId handle))
            {
                return handle;
            }
            return ObjectId.Null;
        }

        /// <summary>
        /// Purga el registro (llamado al cerrar la sesión o cambiar de documento).
        /// </summary>
        public static void Clear()
        {
            _localOwnedEntities.Clear();
        }

        public static string DumpAll()
        {
            return string.Join(", ", _localOwnedEntities.Keys);
        }
    }
}
