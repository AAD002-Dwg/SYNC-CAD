using System.Collections.Generic;

namespace HSync.Core.Network
{
    /// <summary>
    /// Repositorio local de trazos privados (AC-204).
    /// Asegura que el trabajo marcado bajo el "Draft Mode" no escape a la red hasta que el usuario decida hacer Commit.
    /// </summary>
    public static class DraftState
    {
        public static bool IsDraftModeActive { get; private set; } = false;

        // IDs globales retenidos que pertenecen puramente a la sesión privada (Aún no enviados)
        private static readonly HashSet<string> _privateDeltas = new HashSet<string>();

        public static void SetMode(bool isActive)
        {
            IsDraftModeActive = isActive;
        }

        public static void RegisterPrivateDelta(string globalId)
        {
            if (IsDraftModeActive)
            {
                _privateDeltas.Add(globalId);
            }
        }

        public static bool IsDeltaPrivate(string globalId)
        {
            return _privateDeltas.Contains(globalId);
        }

        /// <summary>
        /// Libera todos los deltas privados a la red asíncronamente (Flush).
        /// </summary>
        public static void CommitDraft()
        {
            IsDraftModeActive = false;
            // TODO: Integración con PayloadBuilder para enviar todo lo contenido en _privateDeltas
            _privateDeltas.Clear();
        }
    }
}
