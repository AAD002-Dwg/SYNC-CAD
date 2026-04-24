using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.DatabaseServices;
using HSync.Core;
using HSync.Core.Network;
using HSync.Render;

[assembly: ExtensionApplication(typeof(HSync.HSyncPlugin))]

namespace HSync
{
    /// <summary>
    /// Punto de Entrada principal del ecosistema SYNC-CAD NEURAL.
    /// Registra los módulos de seguridad visual y OSNAP (Fase 1: Motor Local).
    /// </summary>
    public class HSyncPlugin : IExtensionApplication
    {
        public static SyncSocketClient SocketClient { get; private set; }

        public void Initialize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            
            // Inicializar Subsistemas del Sprint 1
            if (doc != null)
            {
                UndoInterceptor.Initialize(doc);
            }

            // Inicializar Overrule matemático de geometrías efímeras
            HologramOsnapOverrule.Initialize();
            
            // AC-601: Overrule de Ocultamiento de Nativos (Canónico vs Proyectado)
            var entityClass = RXObject.GetClass(typeof(Entity));
            Overrule.AddOverrule(entityClass, ShadowDrawOverrule.Instance, false);
            Overrule.AddOverrule(entityClass, ShadowOsnapOverrule.Instance, false);
            // Optimizacion masiva: empezamos con filtros vacíos
            ShadowDrawOverrule.Instance.SetIdFilter(new ObjectId[0]);
            ShadowOsnapOverrule.Instance.SetIdFilter(new ObjectId[0]);
            Overrule.Overruling = true;

            // AC-401 (Diffing pre-comando)
            EventMonitor.Initialize(); 

            var editor = Application.DocumentManager.MdiActiveDocument?.Editor;
            editor?.WriteMessage("\n[H-SYNC] Motor Holográfico Neural Inicializado (v2.0) - ¡Listo para AC-10x y Red!");

            // AC-304: Vigilar la muerte súbita del documento para avisarle al Servidor
            Application.DocumentManager.DocumentDestroyed += OnDocumentDestroyed;
        }

        private void OnDocumentDestroyed(object sender, DocumentDestroyedEventArgs e)
        {
            // TODO: Enviar DISCONNECT_REQ de rescate al WebSocket antes de que el AppDomain se cierre.
            // WebSocketClient.SendDeltaAsync("{ 'type': 'DISCONNECT_REQ' }").Wait(500);
            GhostManager.ClearAllGhosts();
        }

        public void Terminate()
        {
            Application.DocumentManager.DocumentDestroyed -= OnDocumentDestroyed;
            
            // Limpieza segura requerida por el motor nativo de AutoCAD
            UndoInterceptor.Terminate();
            HologramOsnapOverrule.Terminate();
            EventMonitor.Terminate();
            GhostManager.ClearAllGhosts();
        }

        [CommandMethod("HSYNC_TEST_GHOST")]
        public void TestGhostInjection()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // Creamos una entidad efímera mock para validar AC-101 y AC-102
            using (var line = new Autodesk.AutoCAD.DatabaseServices.Line(
                new Autodesk.AutoCAD.Geometry.Point3d(0, 0, 0),
                new Autodesk.AutoCAD.Geometry.Point3d(100, 100, 0)))
            {
                line.ColorIndex = 3; // Verde

                // Inyección
                GhostManager.AddOrUpdateGhost("mock_uuid_123", (Autodesk.AutoCAD.DatabaseServices.Entity)line.Clone());
            }

            doc.Editor.WriteMessage("\n[H-SYNC] Holograma de prueba inyectado. Intenta hacer Ctrl+Z para validar la persistencia (AC-102).");
        }
        
        [CommandMethod("HSYNC_CLEAR")]
        public void TestGhostClear()
        {
            GhostManager.ClearAllGhosts();
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[H-SYNC] Hologramas purgados.");
        }

        [CommandMethod("HSYNC_CONNECT")]
        public async void ConnectToHub()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            doc.Editor.WriteMessage("\n[H-SYNC] Conectando al Hub Transaccional (ws://localhost:3000)...");
            
            try 
            {
                if (SocketClient == null) 
                {
                    SocketClient = new SyncSocketClient("ws://localhost:3000", "ALAN-ACAD");
                }

                // Iniciamos la conexión WebSocket y el ciclo de vida (Handshake)
                await SocketClient.ConnectAsync();
                await HandshakeManager.InitiateConnectAsync(SocketClient, 0);
                doc.Editor.WriteMessage("\n[H-SYNC] Conexion WebSocket Establecida. Modo Multi-Usuario Activado.");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n[H-SYNC] Error de conexion: {ex.Message}");
            }
        }

        [CommandMethod("HSYNC_HEAVY_TEST")]
        public void TestHeavyGhostInjection()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            
            doc.Editor.WriteMessage("\n[H-SYNC] Generando 5,000 geometrías pesadas (Círculos y MText)... esto probará el verdadero límite.");
            GhostManager.ClearAllGhosts();
            
            Random rnd = new Random();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 5000; i++)
            {
                double x = (rnd.NextDouble() * 20000) - 10000;
                double y = (rnd.NextDouble() * 20000) - 10000;
                
                // Entidad 1: Círculo (Matemáticamente más complejo que una línea)
                var circle = new Autodesk.AutoCAD.DatabaseServices.Circle();
                circle.Center = new Autodesk.AutoCAD.Geometry.Point3d(x, y, 0);
                circle.Radius = rnd.NextDouble() * 100 + 10;
                circle.ColorIndex = (short)rnd.Next(1, 255);
                GhostManager.AddOrUpdateGhost($"circle_{i}", circle);
                
                // Entidad 2: Texto Multilínea MText (Pone a prueba el motor de rasterización de fuentes TTS de AutoCAD)
                var mtext = new Autodesk.AutoCAD.DatabaseServices.MText();
                mtext.Location = new Autodesk.AutoCAD.Geometry.Point3d(x, y, 0);
                mtext.Contents = "H-SYNC\\PNEURAL\\PCARGAPESADA";
                mtext.TextHeight = 50;
                mtext.ColorIndex = 2; // Amarillo
                GhostManager.AddOrUpdateGhost($"text_{i}", mtext);
            }
            
            sw.Stop();
            doc.Editor.WriteMessage($"\n[H-SYNC] 10,000 entidades pesadas combinadas inyectadas en {sw.ElapsedMilliseconds}ms. ¡Haz Pan/Zoom ahora!");
        }
        [CommandMethod("HSYNC_DEBUG")]
        public void HSyncDebug()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            
            ed.WriteMessage("\n--- H-SYNC DEBUG ---");
            ed.WriteMessage($"\nSocket Connected: {SocketClient?.IsConnected}");
            ed.WriteMessage($"\nUUIDs en OwnershipRegistry: {OwnershipRegistry.DumpAll()}");
            
            // Probar el Overrule directamente
            var res = ed.GetEntity("\nSelecciona entidad para forzar Shadowing: ");
            if (res.Status == PromptStatus.OK)
            {
                var id = res.ObjectId;
                HSync.Render.ShadowRegistry.Shadow(id);
                ed.WriteMessage($"\nSombreado aplicado a: {id.Handle.ToString().ToLowerInvariant()}");
                
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    ent.RecordGraphicsModified(true); // Forzar regeneración
                    tr.Commit();
                }
            }
        }
    }
}
