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
        private CancellationTokenSource _cts; // Controla el ciclo de vida del ReceiveLoop
        private readonly string _url;
        private readonly string _userId;
        private System.Timers.Timer _heartbeatTimer;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public string UserId => _userId;

        public SyncSocketClient(string host, string userId)
        {
            _url = host;
            _userId = userId;

            // AC-305: Inicializar el latido (30s)
            _heartbeatTimer = new System.Timers.Timer(30000);
            _heartbeatTimer.Elapsed += async (s, e) => await SendHeartbeat();
        }

        private async Task SendHeartbeat()
        {
            if (IsConnected)
            {
                try { await SendDeltaAsync("{\"type\":\"ALIVE_HEARTBEAT\"}"); }
                catch { /* Fallo silencioso */ }
            }
        }

        public async Task ConnectAsync()
        {
            // BUGFIX: Cancelar el ReceiveLoop anterior para evitar loops zombi
            _cts?.Cancel();
            _heartbeatTimer.Stop();
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconectando", CancellationToken.None); }
                catch { /* Ignorar si ya estaba muerto */ }
            }

            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            
            // WAN OPTIMIZATION (AC-201):
            _ws.Options.KeepAliveInterval = TimeSpan.Zero; 

            try
            {
                await _ws.ConnectAsync(new Uri(_url), CancellationToken.None);
                
                // AC-305: Iniciar latido
                _heartbeatTimer.Start();

                // Iniciamos la oreja asíncrona con el token de cancelación
                _ = ReceiveLoopAsync(_ws, _cts.Token);
            }
            catch (Exception ex)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[H-SYNC] Error de red: {ex.Message}");
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket localWs, CancellationToken ct)
        {
            var buffer = new byte[8192];
            
            while (localWs.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var result = await localWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                        .MdiActiveDocument?.Editor.WriteMessage($"\n[H-SYNC NET] Recibido: {msg.Substring(0, Math.Min(msg.Length, 120))}");
                    
                    using (var doc = System.Text.Json.JsonDocument.Parse(msg))
                    {
                        string type = null;
                        if (doc.RootElement.TryGetProperty("type", out var typeProp))
                            type = typeProp.GetString();

                        if (type == "RECONCILE_FIX")
                        {
                            string entityId = doc.RootElement.GetProperty("id").GetString();
                            // BUGFIX: Serializar ANTES de salir del using (JsonElement use-after-dispose)
                            string winnerStateRaw = doc.RootElement.GetProperty("state").GetRawText();

                            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                                .MdiActiveDocument?.Editor.WriteMessage(
                                    $"\n[H-SYNC] RECONCILE_FIX para '{entityId}'");

                            AppIdleManager.EnqueueAction(() => 
                            {
                                try
                                {
                                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                                        .MdiActiveDocument?.Editor.WriteMessage(
                                            $"\n[H-SYNC] Ejecutando SetGlowRed('{entityId}')...");
                                    HSync.Render.GhostManager.SetGlowRed(entityId);
                                }
                                catch (Exception glowEx)
                                {
                                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                                        .MdiActiveDocument?.Editor.WriteMessage(
                                            $"\n[H-SYNC ERROR] SetGlowRed crash: {glowEx.Message}");
                                }
                            });

                            Task.Delay(2000).ContinueWith(_ => 
                            {
                                AppIdleManager.EnqueueAction(() => 
                                {
                                    try
                                    {
                                        using (var stateDoc = System.Text.Json.JsonDocument.Parse(winnerStateRaw))
                                        {
                                            HSync.Render.GhostManager.ApplyMergedState(entityId, stateDoc.RootElement);
                                        }
                                    }
                                    catch (Exception mergeEx)
                                    {
                                        Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                                            .MdiActiveDocument?.Editor.WriteMessage(
                                                $"\n[H-SYNC ERROR] ApplyMergedState crash: {mergeEx.Message}");
                                    }
                                });
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // Cancelación limpia por reconexión
                }
                catch (Exception ex)
                {
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                        .MdiActiveDocument?.Editor.WriteMessage($"\n[H-SYNC NET] Loop Crash: {ex.Message}");
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
