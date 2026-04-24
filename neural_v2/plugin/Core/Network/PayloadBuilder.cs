using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.AutoCAD.DatabaseServices;

namespace HSync.Core.Network
{
    // Estructura oficial compatible con NEURAL_DATA_SCHEMA.md
    public class EntityDelta
    {
        public string id { get; set; }
        public string projectId { get; set; } = "PROJ-TEST-AC601";
        public string user { get; set; }
        public long client_seq { get; set; }
        public string op { get; set; } // "CREATE", "UPDATE", "DELETE"
        public string type { get; set; } // "LINE", "CIRCLE"
        public EntityProps props { get; set; }
    }

    public class EntityProps
    {
        public Dictionary<string, object> geom { get; set; }
        public string layer { get; set; }
        public int? color { get; set; }
    }

    /// <summary>
    /// Motor de Serialización de Alto Rendimiento compatible con el Hub.
    /// </summary>
    public static class PayloadBuilder
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static long _localClientSequence = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;

        public static string BuildCreate(string globalId, Entity ent, string userId)
        {
            var delta = new EntityDelta
            {
                id = globalId,
                user = userId,
                client_seq = ++_localClientSequence,
                op = "CREATE",
                type = ent.GetType().Name.ToUpper(),
                props = new EntityProps
                {
                    layer = ent.Layer,
                    color = ent.ColorIndex,
                    geom = ExtractGeometry(ent)
                }
            };

            return JsonSerializer.Serialize(delta, _options);
        }

        public static string BuildUpdate(string globalId, Dictionary<string, object> changedProps, string userId)
        {
            var delta = new EntityDelta
            {
                id = globalId,
                user = userId,
                client_seq = ++_localClientSequence,
                op = "UPDATE",
                props = new EntityProps
                {
                    geom = changedProps
                }
            };

            return JsonSerializer.Serialize(delta, _options);
        }

        private static Dictionary<string, object> ExtractGeometry(Entity ent)
        {
            var geom = new Dictionary<string, object>();
            if (ent is Line line)
            {
                geom["start"] = new double[] { line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z };
                geom["end"] = new double[] { line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z };
            }
            else if (ent is Circle circle)
            {
                geom["center"] = new double[] { circle.Center.X, circle.Center.Y, circle.Center.Z };
                geom["radius"] = circle.Radius;
            }
            return geom;
        }
    }
}
