using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;

namespace HSync.Core.Network
{
    /// <summary>
    /// El "Santo Grial" del Threading en AutoCAD (Basado en Speckle Architecture).
    /// Encola tareas provenientes de WebSockets asíncronos y las despacha ÚNICAMENTE
    /// cuando AutoCAD entra en estado de reposo absoluto (Idle), evitando ExecutionEngineException.
    /// </summary>
    public static class AppIdleManager
    {
        // Diccionario Concurrente para encolar Tareas de forma segura entre threads
        private static readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();
        
        // Bandera de seguridad para evitar dobles suscripciones
        private static bool _isSubscribedToIdle = false;

        /// <summary>
        /// Agrega una acción a la cola segura y activa el listener de reposo.
        /// (Debe llamarse desde el WebSocketClient al recibir un mensaje).
        /// </summary>
        public static void EnqueueAction(Action action)
        {
            _actionQueue.Enqueue(action);

            // Si AutoCAD no nos está avisando de sus descansos, nos suscribimos.
            if (!_isSubscribedToIdle)
            {
                _isSubscribedToIdle = true;
                Application.Idle += AutocadAppOnIdle;
            }
        }

        /// <summary>
        /// Disparador nativo que se ejecuta en el Hilo Principal Seguro (Main Thread)
        /// solo cuando el mouse no se mueve y ningún comando está corriendo.
        /// </summary>
        private static void AutocadAppOnIdle(object sender, EventArgs e)
        {
            // 1. Nos desuscribimos inmediatamente para no quemar ciclos de CPU en el Main Thread
            Application.Idle -= AutocadAppOnIdle;
            _isSubscribedToIdle = false;

            // 2. Vaciamos la cola de forma segura (Lock-free dequeueing)
            while (_actionQueue.TryDequeue(out Action action))
            {
                try
                {
                    // Al ejecutarse aquí, estamos 100% seguros dentro del Thread bloqueante de AutoCAD
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[H-SYNC] Error en ejecución Idle: {ex.Message}");
                }
            }
        }
    }
}
