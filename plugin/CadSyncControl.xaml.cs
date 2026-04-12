using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CadSyncPlugin
{
    public partial class CadSyncControl : UserControl
    {
        public ObservableCollection<LogEntry> Activities { get; set; } = new ObservableCollection<LogEntry>();
        private System.Collections.Generic.List<string> _availableFiles = new System.Collections.Generic.List<string>();

        public CadSyncControl()
        {
            InitializeComponent();
            LogList.ItemsSource = Activities;
            _ = RefreshFiles();
            LoadAutoSettings();
            AddLog("Interfaz iniciada. Modo Sincronización Real activo.");
        }

        private void LoadAutoSettings()
        {
            ChkAutoPush.IsChecked = Commands.GetAutoPush();
            ChkAutoPull.IsChecked = Commands.GetAutoPull();
        }

        private void ChkAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkAutoPush == null || ChkAutoPull == null) return;
            Commands.SetAutoPush(ChkAutoPush.IsChecked == true);
            Commands.SetAutoPull(ChkAutoPull.IsChecked == true);
        }

        public async Task RefreshFiles()
        {
            try {
                var response = await Commands.client.GetAsync($"{Commands.GetServerUrl()}/api/files");
                if (response.IsSuccessStatusCode) {
                    var json = await response.Content.ReadAsStringAsync();
                    var files = JsonConvert.DeserializeObject<System.Collections.Generic.List<string>>(json);
                    Dispatcher.Invoke(() => {
                        FileCombo.Items.Clear();
                        foreach (var f in files) FileCombo.Items.Add(f);
                        if (FileCombo.Items.Count > 0) FileCombo.SelectedIndex = 0;
                    });
                }
            } catch { }
        }

        public void UpdateLocks(System.Collections.Generic.Dictionary<string, dynamic> locks)
        {
            foreach (var kvp in locks) {
                AddLog($"Aviso: Capa {kvp.Key} ocupada por {kvp.Value.user}");
            }
        }

        public void AddLog(string message)
        {
            Dispatcher.Invoke(() => {
                Activities.Insert(0, new LogEntry { Message = message, Time = DateTime.Now.ToShortTimeString() });
                if (Activities.Count > 15) Activities.RemoveAt(15);
            });
        }

        private void BtnReserve_Click(object sender, RoutedEventArgs e)
        {
            if (LayerCombo.SelectedIndex <= 0) return;
            string layerName = (LayerCombo.SelectedItem as ComboBoxItem).Content.ToString();
            AddLog($"Reservando capa: {layerName}...");
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.SendStringToExecute($"CADSYNC_RESERVE_UI\n{layerName}\n", true, false, false);
            
            // Habilitar botón de Push Delta
            BtnPushDelta.IsEnabled = true;
            BtnPushDelta.Content = $"SUBIR CAMBIOS: {layerName}";
        }

        private void BtnPushDelta_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Subiendo delta de mi capa...");
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.SendStringToExecute("CADSYNC_PUSH_DELTA ", true, false, false);
        }

        private void BtnPull_Click(object sender, RoutedEventArgs e)
        {
            if (FileCombo.SelectedItem == null) return;
            string fileName = FileCombo.SelectedItem.ToString();
            
            if (fileName.EndsWith(".dwg") && fileName.Contains("_")) {
                // Es un delta (ej: Proyecto_ELECTRICO.dwg) -> NO, en mi lógica es Capa.dwg
            }

            AddLog($"Descargando {fileName}...");
            
            // Si el nombre coincide con una capa conocida, tratamos de hacer MERGE
            // Para simplificar, si el archivo es un DWG pequeño, intentamos PULL_DELTA
            if (fileName.Contains(".dwg")) {
                string layerPart = fileName.Replace(".dwg", "");
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.SendStringToExecute($"CADSYNC_PULL_DELTA\n{layerPart}\n", true, false, false);
            } else {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.SendStringToExecute($"CADSYNC_PULL_UI\n{fileName}\n", true, false, false);
            }
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
