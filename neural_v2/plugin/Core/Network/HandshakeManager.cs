using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSync.Core.Network
{
    public enum SessionState
    {
        OFFLINE,
        CONNECTING,
        SYNCING,
        LIVE
    }

    /// <summary>
    /// Administrador del "Apretón de Manos" y estados de sesión (Sprint 5).
    /// Ejecuta la maquinaria requerida por AC-301, AC-302 y AC-303.
    /// </summary>
    public static class HandshakeManager
    {
        public static SessionState CurrentState { get; private set; } = SessionState.OFFLINE;
        
        // Simulación del log local de deltas pendientes de subir.
        private static List<string> _offlineDeltasQueue = new List<string>();

        public static async Task InitiateConnectAsync(SyncSocketClient client, long lastServerSeq)
        {
            CurrentState = SessionState.CONNECTING;

            var req = new
            {
                type = "CONNECT_REQ",
                user = "ARQ_USER_01",
                checkpointSeq = lastServerSeq
            };

            await client.SendDeltaAsync(JsonSerializer.Serialize(req));
        }

        /// <summary>
        /// Procesa la respuesta SESSION_INIT del servidor.
        /// </summary>
        public static async Task HandleSessionInitAsync(JsonElement payload, SyncSocketClient client)
        {
            string syncMode = payload.GetProperty("syncMode").GetString();
            var dataArray = payload.GetProperty("data");

            CurrentState = SessionState.SYNCING;

            // AC-302: Parseo asíncrono profundo en Thread de Fondo para no congelar AutoCAD
            await Task.Run(() => 
            {
                if (syncMode == "SNAPSHOT")
                {
                    // Lógica para purgar estado local antiguo y reconstruir desde cero.
                    // HSync.Render.GhostManager.ClearAllGhosts();
                }

                // Parsear los miles de deltas de forma paralela si es necesario
                foreach (JsonElement delta in dataArray.EnumerateArray())
                {
                    // var entity = PayloadBuilder.ParseEntity(delta);
                    // AppIdleManager.EnqueueAction(() => GhostManager.AddOrUpdateGhost(entity));
                }
            });

            // Si sobrevivimos a la ingesta masiva (Snapshot) o ligera (Patch), comprobamos trabajo offline
            if (_offlineDeltasQueue.Count > 0)
            {
                await ExecuteOfflinePushAsync(client);
            }
            else
            {
                DeclareLive();
            }
        }

        /// <summary>
        /// Sube el historial offline local en Chunks (AC-303).
        /// </summary>
        private static async Task ExecuteOfflinePushAsync(SyncSocketClient client)
        {
            // Dividimos en bloques de 500
            int chunkSize = 500;
            for (int i = 0; i < _offlineDeltasQueue.Count; i += chunkSize)
            {
                var chunk = _offlineDeltasQueue.GetRange(i, Math.Min(chunkSize, _offlineDeltasQueue.Count - i));
                
                var pushMsg = new
                {
                    type = "OFFLINE_PUSH",
                    chunkId = (i / chunkSize) + 1,
                    deltas = chunk
                };

                await client.SendDeltaAsync(JsonSerializer.Serialize(pushMsg));
                // NOTA: En la lógica de red real, debemos esperar el CHUNK_ACK aquí antes de seguir iterando.
            }
        }

        public static void DeclareLive()
        {
            CurrentState = SessionState.LIVE;
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[H-SYNC] Sesión en vivo. Modo Co-Edición Atómica Activo.");
        }
    }
}
