using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace CadSyncPlugin
{
    /// <summary>
    /// Gestiona los cursores flotantes ("Ghost Cursors") de los compañeros
    /// conectados usando la API de TransientGraphics de AutoCAD.
    /// Todos los objetos son puramente visuales y no se guardan en el dibujo.
    /// </summary>
    public class GhostCursorManager : IDisposable
    {
        private readonly Dictionary<string, GhostInstance> _ghosts =
            new Dictionary<string, GhostInstance>(StringComparer.OrdinalIgnoreCase);

        // Paleta de colores ACI (AutoCAD Color Index) para diferenciar usuarios
        private static readonly short[] ColorPalette = { 1, 2, 3, 4, 140, 200, 30, 50 };
        private int _nextColorIndex;
        private bool _disposed;

        /// <summary>
        /// Actualiza o crea el ghost cursor de un usuario en la posición WCS dada.
        /// Debe llamarse desde el hilo de UI de AutoCAD.
        /// </summary>
        public void UpdateCursor(string user, Point3d positionWcs)
        {
            if (string.IsNullOrEmpty(user)) return;

            if (!_ghosts.TryGetValue(user, out var ghost))
            {
                short color = ColorPalette[_nextColorIndex % ColorPalette.Length];
                _nextColorIndex++;
                ghost = new GhostInstance(user, color);
                _ghosts[user] = ghost;
            }

            ghost.UpdatePosition(positionWcs, GetViewportCrossSize());
        }

        /// <summary>
        /// Calcula el tamaño del cruce en función de la altura del viewport actual
        /// para que sea siempre proporcional al zoom (~1.5% de la altura visible).
        /// </summary>
        private static double GetViewportCrossSize()
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return 0.5;
                var view = doc.Editor.GetCurrentView();
                return Math.Max(0.01, view.Height * 0.015);
            }
            catch { return 0.5; }
        }

        /// <summary>
        /// Elimina el ghost cursor de un usuario (ej: cuando se desconecta).
        /// </summary>
        public void RemoveCursor(string user)
        {
            if (_ghosts.TryGetValue(user, out var ghost))
            {
                ghost.Dispose();
                _ghosts.Remove(user);
            }
        }

        /// <summary>
        /// Elimina todos los ghost cursors activos.
        /// </summary>
        public void RemoveAll()
        {
            foreach (var ghost in _ghosts.Values) ghost.Dispose();
            _ghosts.Clear();
        }

        /// <summary>
        /// Devuelve los usuarios conectados con su color ACI asignado.
        /// Útil para sincronizar la lista de la UI.
        /// </summary>
        public IReadOnlyDictionary<string, short> GetUserColors()
        {
            var result = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _ghosts) result[kvp.Key] = kvp.Value.ColorIndex;
            return result;
        }

        /// <summary>
        /// Devuelve la última posición conocida de un usuario, si existe.
        /// </summary>
        public Point3d? GetLastPosition(string user)
        {
            if (_ghosts.TryGetValue(user, out var ghost))
                return ghost.LastPosition;
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RemoveAll();
        }
    }

    /// <summary>
    /// Representa el cursor de un único compañero: una cruz + etiqueta de nombre
    /// renderizados como TransientGraphics (no persistentes en el dibujo).
    /// </summary>
    internal sealed class GhostInstance : IDisposable
    {
        private readonly Line _hLine;
        private readonly Line _vLine;
        private readonly DBText _label;
        private readonly IntegerCollection _viewportNums = new IntegerCollection();
        private readonly TransientDrawingMode _mode = TransientDrawingMode.DirectShortTerm;
        private bool _disposed;

        public short ColorIndex { get; }
        public string User { get; }
        public Point3d LastPosition { get; private set; }

        public GhostInstance(string user, short colorIndex)
        {
            User = user;
            ColorIndex = colorIndex;

            _hLine = new Line(Point3d.Origin, Point3d.Origin) { ColorIndex = colorIndex };
            _vLine = new Line(Point3d.Origin, Point3d.Origin) { ColorIndex = colorIndex };
            _label = new DBText
            {
                TextString = user,
                Height = 0.4,   // updated each frame by UpdatePosition
                ColorIndex = colorIndex
            };

            var tm = TransientManager.CurrentTransientManager;
            tm.AddTransient(_hLine, _mode, 128, _viewportNums);
            tm.AddTransient(_vLine, _mode, 128, _viewportNums);
            tm.AddTransient(_label, _mode, 128, _viewportNums);
        }

        public void UpdatePosition(Point3d pos, double crossSize)
        {
            LastPosition = pos;
            double s = crossSize;
            _hLine.StartPoint = new Point3d(pos.X - s, pos.Y, pos.Z);
            _hLine.EndPoint   = new Point3d(pos.X + s, pos.Y, pos.Z);
            _vLine.StartPoint = new Point3d(pos.X, pos.Y - s, pos.Z);
            _vLine.EndPoint   = new Point3d(pos.X, pos.Y + s, pos.Z);
            _label.Height     = s * 0.7;
            _label.Position   = new Point3d(pos.X + s * 0.4, pos.Y + s * 0.4, pos.Z);

            var tm = TransientManager.CurrentTransientManager;
            tm.UpdateTransient(_hLine, _viewportNums);
            tm.UpdateTransient(_vLine, _viewportNums);
            tm.UpdateTransient(_label, _viewportNums);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                var tm = TransientManager.CurrentTransientManager;
                tm.EraseTransient(_hLine, _viewportNums);
                tm.EraseTransient(_vLine, _viewportNums);
                tm.EraseTransient(_label, _viewportNums);
            }
            catch { /* AutoCAD puede no estar disponible al cerrar */ }
            finally
            {
                _hLine.Dispose();
                _vLine.Dispose();
                _label.Dispose();
            }
        }
    }
}
