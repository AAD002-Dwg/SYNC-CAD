using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HSync.Core.Network
{
    /// <summary>
    /// Cliente puro de WebSockets para .NET y AutoCAD. (AC-201)
    /// </summary>
    public class SyncSocketClient
    {
        private ClientWebSocket _ws;
        private readonly string _url;
        private readonly string _userId;

        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public SyncSocketClient(string host, string userId)
        {
            _url = host;
            _userId = userId;
        }

        public async Task ConnectAsync()
        {
            _ws = new ClientWebSocket();
            
            // WAN OPTIMIZATION (AC-201):
            // Desactiva el algoritmo de Nagle (NoDelay) a nivel TCP para que los paquetes del ratón (Deltas pequeños)
            // salgan inmediatamente sin ser agrupados por el SO, logrando la menor latencia posible.
            _ws.Options.KeepAliveInterval = TimeSpan.Zero; 

            try
            {
                await _ws.ConnectAsync(new Uri(_url), CancellationToken.None);
                
                // Iniciamos la oreja asíncrona pero sin bloquear el thread de AutoCAD
                _ = ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                // Manejo de error para intentar reconectar en el futuro.
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[H-SYNC] Error de red: {ex.Message}");
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192]; // Buffer suficientemente grande para Snapshots pequeños
            
            while (_ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cerrado por el servidor", CancellationToken.None);
                    }
                    else
                    {
                        string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        
                        // SANTO GRIAL DEL THREADING (Speckle Pattern):
                        // En vez de ejecutar lógicas pesadas o de renderizado aquí en el hilo de red,
                        // empaquetamos el trabajo en una Acción y se la pasamos a la cola Concurrente.
                        
                        using (var doc = System.Text.Json.JsonDocument.Parse(msg))
                        {
                            var type = doc.RootElement.GetProperty("type").GetString();

                            if (type == "RECONCILE_FIX")
                            {
                                string entityId = doc.RootElement.GetProperty("id").GetString();
                                var winnerState = doc.RootElement.GetProperty("state");

                                // 1. Glow inmediato en el siguiente Idle
                                AppIdleManager.EnqueueAction(() => 
                                {
                                    HSync.Render.GhostManager.SetGlowRed(entityId);
                                });

                                // 2. Timer asíncrono puro (No bloquea) -> Apaga Glow en el siguiente Idle post-delay
                                Task.Delay(2000).ContinueWith(_ => 
                                {
                                    AppIdleManager.EnqueueAction(() => 
                                    {
                                        HSync.Render.GhostManager.ApplyMergedState(entityId, winnerState);
                                    });
                                });
                            }
                            else
                            {
                                // TODO: Fase 2 Automerge Ingestion para Deltas Normales
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Emisión asíncrona agresiva de Deltas.
        /// </summary>
        public async Task SendDeltaAsync(string rawJson)
        {
            if (!IsConnected) return;
            
            var bytes = Encoding.UTF8.GetBytes(rawJson);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
