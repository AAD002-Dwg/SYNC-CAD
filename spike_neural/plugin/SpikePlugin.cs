using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

[assembly: ExtensionApplication(typeof(SpikeNeural.SpikePlugin))]

namespace SpikeNeural
{
    public class SpikePlugin : IExtensionApplication
    {
        private static ClientWebSocket _socket;
        private static CancellationTokenSource _cts;
        private static Document _doc;
        private static Stopwatch _throttle;

        // Transient Management
        private static List<Line> _loadTestTransients = new List<Line>();
        private static GhostCursor _peerCursor = null;

        public void Initialize()
        {
            // Empty. Commands triggered manually.
        }

        public void Terminate()
        {
            DisconnectSocket();
            ClearTransients();
        }

        [CommandMethod("SPIKE_START")]
        public async void SpikeStart()
        {
            _doc = Application.DocumentManager.MdiActiveDocument;
            var ed = _doc.Editor;

            if (_socket != null && _socket.State == WebSocketState.Open)
            {
                ed.WriteMessage("\n[SPIKE] Ya conectado.");
                return;
            }

            ed.WriteMessage("\n[SPIKE] Conectando a ws://localhost:3002...");

            _socket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            _throttle = new Stopwatch();
            _throttle.Start();

            try
            {
                await _socket.ConnectAsync(new Uri("ws://localhost:3002"), _cts.Token);
                ed.WriteMessage("\n[SPIKE] ¡Conectado al servidor NEURAL!");
                
                // Attach Event
                ed.PointMonitor -= OnPointMonitor;
                ed.PointMonitor += OnPointMonitor;

                _ = ReceiveLoop();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SPIKE] Error: {ex.Message}");
            }
        }

        [CommandMethod("SPIKE_STOP")]
        public void SpikeStop()
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            if (ed != null) ed.PointMonitor -= OnPointMonitor;

            DisconnectSocket();
            ClearTransients();
            ed?.WriteMessage("\n[SPIKE] Desconectado y limpiado.");
        }

        [CommandMethod("SPIKE_LOAD_TEST")]
        public void SpikeLoadTest()
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null) return;

            ed.WriteMessage("\n[SPIKE] Generando 10,000 Hologramas...");

            ClearTransients();

            var tm = TransientManager.CurrentTransientManager;
            var ids = new IntegerCollection();
            Random rnd = new Random();

            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < 10000; i++)
            {
                // Lineas aleatorias en WCS (eje X e Y entre -10000 y 10000)
                double x1 = (rnd.NextDouble() * 20000) - 10000;
                double y1 = (rnd.NextDouble() * 20000) - 10000;
                double x2 = x1 + (rnd.NextDouble() * 1000) - 500;
                double y2 = y1 + (rnd.NextDouble() * 1000) - 500;

                Line ln = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
                ln.ColorIndex = (short)(rnd.Next(1, 255));
                
                _loadTestTransients.Add(ln);
                tm.AddTransient(ln, TransientDrawingMode.DirectShortTerm, 128, ids);
            }

            sw.Stop();
            ed.WriteMessage($"\n[SPIKE] Carga completa en {sw.ElapsedMilliseconds}ms. Intenta hacer Pan/Zoom.");
        }

        // --- Logic ---

        private async void OnPointMonitor(object sender, PointMonitorEventArgs e)
        {
            if (_socket == null || _socket.State != WebSocketState.Open) return;
            
            // Limit to roughly 30 FPS (every ~33ms)
            if (_throttle.ElapsedMilliseconds < 33) return;
            _throttle.Restart();

            var pt = e.Context.ComputedPoint;
            
            // Raw binary string: "X|Y|Z" para mínima serialización (Evitar librerías)
            string msg = $"{pt.X:F2}|{pt.Y:F2}|{pt.Z:F2}";
            var buffer = Encoding.UTF8.GetBytes(msg);

            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts.Token);
            }
            catch { /* Ignore */ }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[1024];

            try
            {
                while (_socket.State == WebSocketState.Open)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var parts = msg.Split('|');
                    
                    if (parts.Length == 3 && 
                        double.TryParse(parts[0], out double x) && 
                        double.TryParse(parts[1], out double y) && 
                        double.TryParse(parts[2], out double z))
                    {
                        var pt = new Point3d(x, y, z);
                        
                        // We must update visuals on the main UI thread safely!
                        Application.DocumentManager.MdiActiveDocument.Editor.Document.SendStringToExecute("", false, false, false);
                        // Better raw hack for UI thread invoker in Spike:
                        Application.Idle += (s, e) => {
                            UpdatePeerCursor(pt);
                        };
                    }
                }
            }
            catch { /* Socket exception / CTS */ }
        }

        private static void UpdatePeerCursor(Point3d pt)
        {
            if (_peerCursor == null) _peerCursor = new GhostCursor();
            _peerCursor.Update(pt);
        }

        private void DisconnectSocket()
        {
            if (_socket != null)
            {
                _cts?.Cancel();
                _socket.Dispose();
                _socket = null;
            }
        }

        private void ClearTransients()
        {
            var tm = TransientManager.CurrentTransientManager;
            var ids = new IntegerCollection();

            foreach (var ln in _loadTestTransients)
            {
                try { tm.EraseTransient(ln, ids); ln.Dispose(); } catch { }
            }
            _loadTestTransients.Clear();

            _peerCursor?.Dispose();
            _peerCursor = null;
        }
    }

    internal class GhostCursor : IDisposable
    {
        private Line _hLine;
        private Line _vLine;
        private IntegerCollection _vp = new IntegerCollection();

        public GhostCursor()
        {
            _hLine = new Line(Point3d.Origin, Point3d.Origin) { ColorIndex = 1 };
            _vLine = new Line(Point3d.Origin, Point3d.Origin) { ColorIndex = 1 };
            
            var tm = TransientManager.CurrentTransientManager;
            tm.AddTransient(_hLine, TransientDrawingMode.DirectShortTerm, 128, _vp);
            tm.AddTransient(_vLine, TransientDrawingMode.DirectShortTerm, 128, _vp);
        }

        public void Update(Point3d pt)
        {
            double s = 1.0; // Fixed size for spike
            _hLine.StartPoint = new Point3d(pt.X - s, pt.Y, pt.Z);
            _hLine.EndPoint   = new Point3d(pt.X + s, pt.Y, pt.Z);
            _vLine.StartPoint = new Point3d(pt.X, pt.Y - s, pt.Z);
            _vLine.EndPoint   = new Point3d(pt.X, pt.Y + s, pt.Z);

            var tm = TransientManager.CurrentTransientManager;
            tm.UpdateTransient(_hLine, _vp);
            tm.UpdateTransient(_vLine, _vp);
        }

        public void Dispose()
        {
            var tm = TransientManager.CurrentTransientManager;
            try { tm.EraseTransient(_hLine, _vp); tm.EraseTransient(_vLine, _vp); } catch { }
            _hLine?.Dispose();
            _vLine?.Dispose();
        }
    }
}
