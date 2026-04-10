using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Threading.Tasks;

namespace CadSyncPlugin
{
    public partial class CadSyncControl : UserControl
    {
        public ObservableCollection<LogEntry> Activities { get; set; } = new ObservableCollection<LogEntry>();

        public CadSyncControl()
        {
            InitializeComponent();
            LogList.ItemsSource = Activities;
            
            // Simular carga de URL
            UrlDisplay.Text = Commands.GetServerUrl();
            AddLog("Interfaz iniciada. Listo para sincronizar.");
        }

        public void AddLog(string message)
        {
            Dispatcher.Invoke(() => {
                Activities.Insert(0, new LogEntry { Message = message, Time = DateTime.Now.ToShortTimeString() });
                if (Activities.Count > 10) Activities.RemoveAt(10);
            });
        }

        public void SetStatus(bool online)
        {
            Dispatcher.Invoke(() => {
                StatusDot.Fill = new SolidColorBrush(online ? (Color)ColorConverter.ConvertFromString("#22c55e") : (Color)ColorConverter.ConvertFromString("#ef4444"));
                StatusText.Text = online ? "Sincronizado" : "Desconectado";
            });
        }

        private void BtnReserve_Click(object sender, RoutedEventArgs e)
        {
            if (LayerCombo.SelectedIndex <= 0) {
                MessageBox.Show("Por favor selecciona una capa válida.");
                return;
            }

            string layerName = (LayerCombo.SelectedItem as ComboBoxItem).Content.ToString();
            AddLog($"Reservando capa: {layerName}...");
            
            // Llamar al comando de AutoCAD
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.SendStringToExecute($"CADSYNC_RESERVE_UI {layerName} ", true, false, false);
        }

        private void BtnPull_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Descargando cambios del servidor...");
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.SendStringToExecute("CADSYNC_PULL_UI ", true, false, false);
        }

        private void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Sincronizando dibujo...");
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.SendStringToExecute("CADSYNC_PUSH ", true, false, false);
        }
    }

    public class LogEntry
    {
        public string Message { get; set; }
        public string Time { get; set; }
    }
}
