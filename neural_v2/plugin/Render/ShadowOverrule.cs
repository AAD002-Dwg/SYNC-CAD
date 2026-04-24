using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace HSync.Render
{
    /// <summary>
    /// Registro global de entidades sometidas a Shadowing (Opción B: Permanente por Sesión).
    /// </summary>
    public static class ShadowRegistry
    {
        private static readonly HashSet<ObjectId> _shaded = new HashSet<ObjectId>();
        
        public static void Shadow(ObjectId id)
        {
            if (_shaded.Add(id))
            {
                var ids = _shaded.ToArray();
                ShadowDrawOverrule.Instance.SetIdFilter(ids);
                ShadowOsnapOverrule.Instance.SetIdFilter(ids);
                ShadowGripOverrule.Instance.SetIdFilter(ids);
            }
        }

        public static bool IsShaded(ObjectId id) => _shaded.Contains(id);
    }

    public class ShadowGripOverrule : GripOverrule
    {
        public static readonly ShadowGripOverrule Instance = new ShadowGripOverrule();
        public override void GetGripPoints(Entity entity, GripDataCollection grips, double curViewUnitSize, int gripSize, Vector3d curViewDir, GetGripPointsFlags bitFlags)
        {
            if (ShadowRegistry.IsShaded(entity.ObjectId)) return;
            base.GetGripPoints(entity, grips, curViewUnitSize, gripSize, curViewDir, bitFlags);
        }
    }

    /// <summary>
    /// 1. Oculta visualmente (Y bloquea la selección física al no emitir geometría).
    /// </summary>
    public class ShadowDrawOverrule : DrawableOverrule
    {
        public static readonly ShadowDrawOverrule Instance = new ShadowDrawOverrule();

        public override bool WorldDraw(Drawable drawable, WorldDraw wd)
        {
            if (drawable is Entity e && ShadowRegistry.IsShaded(e.ObjectId))
            {
                // Retornar true significa "Yo me encargué de dibujarlo todo", pero al no llamar 
                // a ningún wd.Geometry.*, la entidad queda 100% invisible y, por ende, INSELECCIONABLE 
                // por clics o cajas de selección (AutoCAD basa su selección en la caja de gráficos).
                return true; 
            }
            return base.WorldDraw(drawable, wd);
        }
    }

    /// <summary>
    /// 2. Bloquea Osnap (Para que el cursor no se ancle a vértices invisibles).
    /// </summary>
    public class ShadowOsnapOverrule : OsnapOverrule
    {
        public static readonly ShadowOsnapOverrule Instance = new ShadowOsnapOverrule();

        public override void GetObjectSnapPoints(
            Entity entity, ObjectSnapModes snapMode, 
            IntPtr gsSelectionMark, Point3d pickPoint,
            Point3d lastPoint, Matrix3d viewXform,
            Point3dCollection snapPoints, IntegerCollection geometryIds)
        {
            if (ShadowRegistry.IsShaded(entity.ObjectId))
            {
                // Salida temprana: no inyectamos ningún snap point.
                return;
            }
                
            base.GetObjectSnapPoints(entity, snapMode, gsSelectionMark, pickPoint, lastPoint, viewXform, snapPoints, geometryIds);
        }

        public override void GetObjectSnapPoints(Entity entity, ObjectSnapModes snapMode, IntPtr gsSelectionMark, Point3d pickPoint, Point3d lastPoint, Matrix3d viewXform, Point3dCollection snapPoints, IntegerCollection geometryIds, Matrix3d insertionMat)
        {
            if (ShadowRegistry.IsShaded(entity.ObjectId)) return;
            base.GetObjectSnapPoints(entity, snapMode, gsSelectionMark, pickPoint, lastPoint, viewXform, snapPoints, geometryIds, insertionMat);
        }
    }
}
