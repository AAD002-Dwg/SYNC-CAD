using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CadSyncPlugin
{
    /// <summary>
    /// Cola persistente de operaciones de push que fallaron por ausencia de red.
    /// Se guarda en disco como JSON; al reconectar se procesan en orden FIFO.
    /// </summary>
    public class OfflineQueue
    {
        private readonly string _filePath;
        private readonly object _lock = new();

        public OfflineQueue()
        {
            _filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CadSyncOfflineQueue.json");
        }

        // ── Public API ────────────────────────────────────────
        public void Enqueue(string layer, string dwgTempPath)
        {
            var items = Load();
            items.RemoveAll(i => string.Equals(i.Layer, layer, StringComparison.OrdinalIgnoreCase));
            items.Add(new QueueItem
            {
                Layer      = layer,
                DwgPath    = dwgTempPath,
                QueuedAt   = DateTime.UtcNow.ToString("o")
            });
            Save(items);
        }

        public List<QueueItem> DequeueAll()
        {
            lock (_lock)
            {
                var items = Load();
                Save(new List<QueueItem>());
                return items;
            }
        }

        public int Count()
        {
            var items = Load();
            return items.Count;
        }

        // ── Persistence ───────────────────────────────────────
        private List<QueueItem> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_filePath))
                        return JsonConvert.DeserializeObject<List<QueueItem>>(
                            File.ReadAllText(_filePath)) ?? new();
                }
                catch { }
                return new List<QueueItem>();
            }
        }

        private void Save(List<QueueItem> items)
        {
            lock (_lock)
            {
                try { File.WriteAllText(_filePath, JsonConvert.SerializeObject(items, Formatting.Indented)); }
                catch { }
            }
        }
    }

    public class QueueItem
    {
        public string Layer    { get; set; } = "";
        public string DwgPath  { get; set; } = "";
        public string QueuedAt { get; set; } = "";
    }
}
