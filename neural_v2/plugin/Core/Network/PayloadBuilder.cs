using System;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;

namespace HSync.Core.Network
{
    // Estructura oficial del Schema NEURAL_DATA_SCHEMA.md
    public class EntityDelta
    {
        public string id { get; set; }
        public string type { get; set; } // LINE, CIRCLE, BLOCKREF
        public string user { get; set; }
        public long client_seq { get; set; }
        public OpType op { get; set; }
        public double[] coords { get; set; } // XYZ compactado
        
        // Spatial Indexing (Bounding Box: MinX, MinY, MinZ, MaxX, MaxY, MaxZ)
        public double[] extents { get; set; } 
    }

    public enum OpType
    {
        CREATE = 1,
        UPDATE = 2,
        DELETE = 3,
        UNDO = 4
    }

    /// <summary>
    /// Motor de Serialización de Alto Rendimiento. 
    /// Usa System.Text.Json nativo de .NET 8 para evitar el lag histórico de Newtonsoft.Json.
    /// </summary>
    public static class PayloadBuilder
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static long _localClientSequence = 0;

        public static string BuildDelta(string globalId, Entity ent, OpType operation, string userId)
        {
            var delta = new EntityDelta
            {
                id = globalId,
                user = userId,
                client_seq = ++_localClientSequence,
                op = operation,
                type = ent.GetType().Name.ToUpper() // 'LINE', 'CIRCLE', etc.
            };

            // Extracción plana de Coordenadas
            if (ent is Line line)
            {
                delta.coords = new double[] 
                { 
                    line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z,
                    line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z 
                };
            }
            else if (ent is Circle circle)
            {
                delta.coords = new double[]
                {
                    circle.Center.X, circle.Center.Y, circle.Center.Z,
                    circle.Radius
                };
            }
            // Agregaremos MText / BlockRef progresivamente en base al Schema.

            // Calculo Seguro de GeometricExtents (Evita eNullExtents en Transients)
            delta.extents = CalculateSafeExtents(ent);

            return JsonSerializer.Serialize(delta, _options);
        }

        private static double[] CalculateSafeExtents(Entity ent)
        {
            try
            {
                var ext = ent.GeometricExtents;
                return new double[] { ext.MinPoint.X, ext.MinPoint.Y, ext.MinPoint.Z, ext.MaxPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z };
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.NullExtents)
            {
                // Fallback matemático para entidades virtualizadas problemáticas
                if (ent is Line line)
                {
                    double minX = Math.Min(line.StartPoint.X, line.EndPoint.X);
                    double minY = Math.Min(line.StartPoint.Y, line.EndPoint.Y);
                    double minZ = Math.Min(line.StartPoint.Z, line.EndPoint.Z);
                    double maxX = Math.Max(line.StartPoint.X, line.EndPoint.X);
                    double maxY = Math.Max(line.StartPoint.Y, line.EndPoint.Y);
                    double maxZ = Math.Max(line.StartPoint.Z, line.EndPoint.Z);
                    return new double[] { minX, minY, minZ, maxX, maxY, maxZ };
                }
                if (ent is Circle circ)
                {
                    return new double[] { 
                        circ.Center.X - circ.Radius, circ.Center.Y - circ.Radius, circ.Center.Z,
                        circ.Center.X + circ.Radius, circ.Center.Y + circ.Radius, circ.Center.Z 
                    };
                }
                // Si falla, retornamos el origen
                return new double[] { 0, 0, 0, 0, 0, 0 };
            }
        }
    }
}
