using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Core.Network.Diffing
{
    public class LineSnapshot : EntitySnapshot
    {
        public Point3d Start { get; set; }
        public Point3d End { get; set; }
    }

    public class LineGeom
    {
        public double[] start { get; set; }
        public double[] end { get; set; }

        public LineGeom(Point3d s, Point3d e)
        {
            start = new double[] { s.X, s.Y, s.Z };
            end = new double[] { e.X, e.Y, e.Z };
        }
    }

    public class LineDiffer : IEntityDiffer
    {
        public EntityType Type => EntityType.LINE;

        public EntitySnapshot CaptureSnapshot(Entity entity, Transaction tr)
        {
            var line = (Line)entity;
            return new LineSnapshot
            {
                Layer = line.Layer,
                Color = line.ColorIndex,
                Linetype = line.Linetype,
                Visible = line.Visible,
                Start = line.StartPoint, // Struct copy, no boxing
                End = line.EndPoint
            };
        }

        public PropDelta[] Diff(EntitySnapshot before, EntitySnapshot after)
        {
            var b = (LineSnapshot)before;
            var a = (LineSnapshot)after;

            var deltas = new List<PropDelta>(4);

            // Scalar-Mergeables
            if (b.Layer != a.Layer)
                deltas.Add(new PropDelta("layer", a.Layer));
            if (b.Color != a.Color)
                deltas.Add(new PropDelta("color", a.Color));
            if (b.Linetype != a.Linetype)
                deltas.Add(new PropDelta("linetype", a.Linetype));
            if (b.Visible != a.Visible)
                deltas.Add(new PropDelta("visibility", a.Visible));

            // Atomic-Group (Geometría completa, todo o nada)
            bool geomChanged = !a.Start.IsEqualTo(b.Start, Tolerance.Global) ||
                               !a.End.IsEqualTo(b.End, Tolerance.Global);

            if (geomChanged)
                deltas.Add(new PropDelta("geom", new LineGeom(a.Start, a.End)));

            return deltas.ToArray();
        }
    }
}
