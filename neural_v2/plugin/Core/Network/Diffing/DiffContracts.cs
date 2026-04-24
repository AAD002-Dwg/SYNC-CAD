using System;
using Autodesk.AutoCAD.DatabaseServices;

namespace HSync.Core.Network.Diffing
{
    public enum EntityType
    {
        LINE,
        CIRCLE,
        POLYLINE,
        BLOCKREF,
        TEXT,
        UNKNOWN
    }

    /// <summary>
    /// Objeto base inmutable capturado antes del comando.
    /// </summary>
    public abstract class EntitySnapshot
    {
        public string Layer { get; set; }
        public int Color { get; set; }
        public string Linetype { get; set; }
        public bool Visible { get; set; }
    }

    /// <summary>
    /// Par Clave-Valor que representa una única propiedad mutada.
    /// </summary>
    public class PropDelta
    {
        public string Key { get; }
        public object Value { get; }

        public PropDelta(string key, object value)
        {
            Key = key;
            Value = value;
        }
    }

    /// <summary>
    /// Contrato estricto para los motores de Diff Hardcodeados.
    /// </summary>
    public interface IEntityDiffer
    {
        EntityType Type { get; }
        EntitySnapshot CaptureSnapshot(Entity entity, Transaction tr);
        System.Collections.Generic.Dictionary<string, object> Diff(EntitySnapshot before, EntitySnapshot after);
    }
}
