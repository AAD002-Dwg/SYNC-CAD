using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using HSync.Render;

namespace HSync.Core
{
    /// <summary>
    /// Escudo protector (UndoInterceptor) para el ecosistema holográfico (AC-102).
    /// Evita que el comando Ctrl+Z de AutoCAD destruya el historial visual remoto inyectado en TransientManager.
    /// </summary>
    public static class UndoInterceptor
    {
        private static Document _activeDoc;
        public static bool IsUndoActive { get; private set; } = false;

        public static void Initialize(Document doc)
        {
            if (doc == null) return;
            _activeDoc = doc;
            _activeDoc.CommandWillStart += OnCommandWillStart;
            _activeDoc.CommandEnded += OnCommandEnded;
            _activeDoc.CommandCancelled += OnCommandEnded;
        }

        public static void Terminate()
        {
            if (_activeDoc != null)
            {
                _activeDoc.CommandWillStart -= OnCommandWillStart;
                _activeDoc.CommandEnded -= OnCommandEnded;
                _activeDoc.CommandCancelled -= OnCommandEnded;
            }
        }

        private static void OnCommandWillStart(object sender, CommandEventArgs e)
        {
            // Intercepta comando puramente de Deshacer (U, UNDO)
            if (e.GlobalCommandName.Equals("U", StringComparison.OrdinalIgnoreCase) || 
                e.GlobalCommandName.Equals("UNDO", StringComparison.OrdinalIgnoreCase))
            {
                IsUndoActive = true;
                
                // NOTA ARQUITECTÓNICA: En la fase de red, aquí silenciaremos la emisión del "Delete",
                // y enviaremos un "Op: UNDO" explícito al servidor para iniciar CRDT rollback.
            }
        }

        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (IsUndoActive && 
               (e.GlobalCommandName.Equals("U", StringComparison.OrdinalIgnoreCase) || 
                e.GlobalCommandName.Equals("UNDO", StringComparison.OrdinalIgnoreCase)))
            {
                IsUndoActive = false;
                
                // Forzamos un refresco visual global. Como TransientManager maneja sus gráficos 
                // fuera del DB transaccional, los hologramas de la sesión sobrevivirán intactos (AC-102).
                if (_activeDoc != null && _activeDoc.Editor != null)
                {
                    _activeDoc.Editor.UpdateScreen();
                }
            }
        }
    }
}
