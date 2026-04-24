using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HSync.Render;

namespace HSync.Core
{
    /// <summary>
    /// Subsistema Matemático de Intercepción Geométrica (AC-103).
    /// Fuerza a AutoCAD a calcular Puntos de Ajuste (OSNAPs) sobre los Hologramas,
    /// a pesar de que estos no existen físicamente en la Base de Datos.
    /// </summary>
    public class HologramOsnapOverrule : OsnapOverrule
    {
        private static HologramOsnapOverrule _instance;
        private static bool _isActive = false;

        public static void Initialize()
        {
            if (_isActive) return;
            _instance = new HologramOsnapOverrule();
            
            // Registramos la intercepción para tipos de entidades abstractas comunes
            Overrule.AddOverrule(RXObject.GetClass(typeof(Entity)), _instance, true);
            Overrule.Overruling = true;
            _isActive = true;
        }

        public static void Terminate()
        {
            if (!_isActive) return;
            Overrule.RemoveOverrule(RXObject.GetClass(typeof(Entity)), _instance);
            _isActive = false;
        }

        // NO SOPORTADO AÚN POR TRANSIENTS IN-MEMORY PARA ESTE SPIKE/MOCK.
        // En AutoCAD verdadero, interceptar Osnaps de geometrías NO pertecencientes al DB requiere
        // un PointMonitor de bajo nivel midiendo distancia Euclideana a los fantasmas si el Overrule falla.
        //
        // Sin embargo, esta estructura sienta la base de la clase para completarla en Sprint 2.
        
        public override void GetObjectSnapPoints(Entity entity, ObjectSnapModes snapMode, IntPtr gsSelectionMark, Point3d pickPoint, Point3d lastPoint, Matrix3d viewTransform, Point3dCollection snapPoints, IntegerCollection geomIds)
        {
            // Passthrough default nativo
            base.GetObjectSnapPoints(entity, snapMode, gsSelectionMark, pickPoint, lastPoint, viewTransform, snapPoints, geomIds);
        }
    }
}
