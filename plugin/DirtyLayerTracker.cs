using System;
using System.Collections.Generic;
using System.Timers;
using Autodesk.AutoCAD.DatabaseServices;

namespace CadSyncPlugin
{
    /// <summary>
    /// Observa eventos de la base de datos de AutoCAD y acumula las capas
    /// que han sido modificadas, disparando un evento con debounce para
    /// evitar saturar el servidor con solicitudes de reserva.
    /// </summary>
    public class DirtyLayerTracker : IDisposable
    {
        private readonly HashSet<string> _dirtyLayers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();
        private Timer? _debounceTimer;
        private Database? _attachedDb;
        private bool _disposed;

        // Tiempo de espera tras el último cambio antes de notificar (ms)
        private const int DebounceMs = 3000;

        /// <summary>
        /// Se dispara (en hilo de timer) con el conjunto de capas sucias
        /// acumuladas desde el último evento.
        /// </summary>
        public event Action<IReadOnlyCollection<string>>? LayersDirty;

        public void Attach(Database db)
        {
            if (_attachedDb != null) Detach();
            _attachedDb = db;
            db.ObjectModified += OnObjectChanged;
            db.ObjectAppended += OnObjectChanged;
        }

        public void Detach()
        {
            if (_attachedDb == null) return;
            _attachedDb.ObjectModified -= OnObjectChanged;
            _attachedDb.ObjectAppended -= OnObjectChanged;
            _attachedDb = null;
        }

        private void OnObjectChanged(object sender, ObjectEventArgs e)
        {
            // Solo nos interesan entidades (geometría), no registros de tabla
            if (e.DBObject is not Entity ent) return;

            string layerName;
            try { layerName = ent.Layer; }
            catch { return; }

            if (string.IsNullOrWhiteSpace(layerName)) return;

            lock (_lock)
            {
                _dirtyLayers.Add(layerName);
            }

            ResetDebounce();
        }

        private void ResetDebounce()
        {
            lock (_lock)
            {
                _debounceTimer?.Stop();

                if (_debounceTimer == null)
                {
                    _debounceTimer = new Timer(DebounceMs) { AutoReset = false };
                    _debounceTimer.Elapsed += OnDebounceElapsed;
                }

                _debounceTimer.Start();
            }
        }

        private void OnDebounceElapsed(object? sender, ElapsedEventArgs e)
        {
            IReadOnlyCollection<string> snapshot;
            lock (_lock)
            {
                if (_dirtyLayers.Count == 0) return;
                snapshot = new List<string>(_dirtyLayers).AsReadOnly();
            }
            LayersDirty?.Invoke(snapshot);
        }

        /// <summary>
        /// Devuelve las capas sucias acumuladas y limpia el buffer.
        /// Llamar antes de un Push para obtener la lista completa.
        /// </summary>
        public HashSet<string> FlushDirtyLayers()
        {
            lock (_lock)
            {
                var copy = new HashSet<string>(_dirtyLayers, StringComparer.OrdinalIgnoreCase);
                _dirtyLayers.Clear();
                return copy;
            }
        }

        /// <summary>
        /// Consulta sin limpiar qué capas están marcadas como sucias.
        /// </summary>
        public IReadOnlyCollection<string> PeekDirtyLayers()
        {
            lock (_lock)
            {
                return new List<string>(_dirtyLayers).AsReadOnly();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
            lock (_lock)
            {
                _debounceTimer?.Stop();
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
        }
    }
}
