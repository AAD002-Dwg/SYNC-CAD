using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;

namespace CadSyncPlugin
{
    public partial class CadSyncControl : UserControl
    {
        // ── Observable Collections ────────────────────────────
        public ObservableCollection<LogEntry>         Activities     { get; } = new();
        public ObservableCollection<ActiveLayerEntry> ActiveLayers   { get; } = new();
        public ObservableCollection<ConnectedUser>    ConnectedUsers { get; } = new();

        public CadSyncControl()
        {
            InitializeComponent();

            // Apply saved theme before anything is rendered
            ThemeManager.LoadSaved(this);
            SyncThemeIcon();

            LogList.ItemsSource            = Activities;
            ActiveLayersList.ItemsSource   = ActiveLayers;
            ConnectedUsersList.ItemsSource = ConnectedUsers;

            LoadSettings();
            SetConnectionStatus(false);
            _ = RefreshFiles();
            AddLog("Plugin iniciado — Conectando...");
        }

        // ── Settings ──────────────────────────────────────────
        private void LoadSettings()
        {
            TxtStudioKey.Text         = Commands.GetStudioKey();
            ChkAutoPush.IsChecked     = Commands.GetAutoPush();
            ChkAutoPull.IsChecked     = Commands.GetAutoPull();
            ChkGhostCursors.IsChecked = Commands.GetShowGhostCursors();
        }

        private void BtnSaveKey_Click(object sender, RoutedEventArgs e)
        {
            var key = TxtStudioKey.Text.Trim().ToUpper();
            Commands.SetStudioKey(key);
            AddLog($"Studio Key guardado: {(key.Length > 0 ? key : "(vacío)")}. Reinicia AutoCAD para reconectar.");
        }

        private void ChkAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkAutoPush == null || ChkAutoPull == null) return;
            Commands.SetAutoPush(ChkAutoPush.IsChecked == true);
            Commands.SetAutoPull(ChkAutoPull.IsChecked == true);
        }

        private void ChkGhostCursors_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkGhostCursors == null) return;
            bool show = ChkGhostCursors.IsChecked == true;
            Commands.SetShowGhostCursors(show);
            if (!show)
            {
                ConnectedUsers.Clear();
                TxtNoUsers.Visibility = Visibility.Visible;
            }
        }

        // ── Theme Toggle ──────────────────────────────────────
        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Toggle(this);
            SyncThemeIcon();
        }

        private void SyncThemeIcon()
        {
            // ☀ = switch to light available  |  ☾ = switch to dark available
            ThemeIcon.Text = ThemeManager.IsDark ? "☀" : "☾";
            BtnTheme.ToolTip = ThemeManager.IsDark ? "Cambiar a tema claro" : "Cambiar a tema oscuro";
        }

        // ── Connection Status ─────────────────────────────────
        public void SetConnectionStatus(bool connected)
        {
            Dispatcher.Invoke(() =>
            {
                var color = connected
                    ? Color.FromRgb(0x4C, 0xAF, 0x50)  // #4CAF50 success
                    : Color.FromRgb(0xF4, 0x43, 0x36);  // #F44336 error

                StatusDotFill.Color  = color;
                StatusGlow.Color     = color;
                StatusText.Text      = connected ? "Online" : "Offline";
                StatusText.Foreground = connected
                    ? (Brush)Resources["SuccessColor"]
                    : (Brush)Resources["ErrorColor"];
            });
        }

        private string _lastLocksHash = "";

        // ── Lock Updates ──────────────────────────────────────
        public void UpdateLocks(Dictionary<string, LockInfo>? locks)
        {
            if (locks == null || locks.Count == 0) return;

            // Generate simple hash to prevent spamming
            string hash = string.Join(",", locks.Select(x => $"{x.Key}:{x.Value.User}"));
            if (hash == _lastLocksHash) return;
            _lastLocksHash = hash;

            var others = locks.Where(x => x.Value.User != Commands.GetLastUser()).ToList();
            if (others.Count > 0)
            {
                string info = string.Join(", ", others.Select(x => $"{x.Key} ({x.Value.User})"));
                AddLog($"🔒 Capas reservadas por otros: {info}");
            }
        }

        // ── Plan 2: Active Layers ─────────────────────────────
        public void RefreshActiveLayers(IEnumerable<string> layers)
        {
            Dispatcher.Invoke(() =>
            {
                ActiveLayers.Clear();
                bool any = false;
                foreach (var l in layers)
                {
                    ActiveLayers.Add(new ActiveLayerEntry { Name = l });
                    any = true;
                }
                TxtNoLayers.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        public void ShowConflicts(IEnumerable<string> conflictLayers)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var layerInfo in conflictLayers)
                {
                    // layerInfo format: "LAYERNAME (username)"
                    var layerName = layerInfo.Split(' ')[0];
                    AddLog($"⚠ Conflicto detectado: {layerInfo}");

                    foreach (var entry in ActiveLayers)
                    {
                        if (string.Equals(entry.Name, layerName, StringComparison.OrdinalIgnoreCase))
                            entry.IsConflict = true;
                    }
                }
            });
        }

        // ── Plan 3: Connected Users ───────────────────────────
        public void UpdateConnectedUsers(IReadOnlyDictionary<string, short> userColors)
        {
            Dispatcher.Invoke(() =>
            {
                ConnectedUsers.Clear();
                foreach (var kvp in userColors)
                {
                    ConnectedUsers.Add(new ConnectedUser
                    {
                        Name     = kvp.Key,
                        ColorHex = AciToWpfHex(kvp.Value)
                    });
                }
                TxtNoUsers.Visibility =
                    ConnectedUsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        /// <summary>Maps AutoCAD ACI color index to a hex string usable in WPF.</summary>
        private static string AciToWpfHex(short aci) => aci switch
        {
            1   => "#FF4444",
            2   => "#FFD700",
            3   => "#4CAF50",
            4   => "#00BCD4",
            5   => "#55AAFF",
            6   => "#E040FB",
            30  => "#FF8C00",
            50  => "#CDDC39",
            140 => "#7986CB",
            200 => "#80CBC4",
            _   => "#8A91A1"
        };

        // ── File List ─────────────────────────────────────────
        public async Task RefreshFiles()
        {
            try
            {
                var response = await Commands.GetAsync($"{Commands.GetServerUrl()}/api/files");
                if (response.IsSuccessStatusCode)
                {
                    var json  = await response.Content.ReadAsStringAsync();
                    var files = JsonConvert.DeserializeObject<List<string>>(json);
                    Dispatcher.Invoke(() =>
                    {
                        FileCombo.Items.Clear();
                        if (files != null)
                            foreach (var f in files) FileCombo.Items.Add(f);
                        if (FileCombo.Items.Count > 0) FileCombo.SelectedIndex = 0;
                    });
                }
                else
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        AddLog("Error 403: Studio Key rechazada por el servidor.");
                    else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        AddLog("Error 401: Falta Studio Key requerida.");
                    else
                        AddLog($"Error HTTP {response.StatusCode} al cargar archivos.");
                }
            }
            catch (Exception ex)
            { 
                AddLog("Error de red: No se pudo contactar al servidor.");
            }
        }

        // ── Log ───────────────────────────────────────────────
        public void AddLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                Activities.Insert(0, new LogEntry
                {
                    Message = message,
                    Time    = DateTime.Now.ToString("HH:mm:ss")
                });
                if (Activities.Count > 25) Activities.RemoveAt(25);
            });
        }

        // ── Button Handlers ───────────────────────────────────
        private void BtnPushDelta_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Subiendo capas activas...");
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                .MdiActiveDocument.SendStringToExecute("CADSYNC_PUSH_DELTA ", true, false, false);
        }

        private void BtnJumpToUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string user)
            {
                var pos = PluginMain.GetGhostManager().GetLastPosition(user);
                if (pos.HasValue)
                {
                    double x = pos.Value.X;
                    double y = pos.Value.Y;
                    string cmd = $"'_ZOOM _C {x},{y} 100\n";
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                        .MdiActiveDocument.SendStringToExecute(cmd, true, false, false);
                }
                else
                {
                    AddLog($"Coord. no disponibles para {user}.");
                }
            }
        }

        private void BtnPull_Click(object sender, RoutedEventArgs e)
        {
            if (FileCombo.SelectedItem == null) return;
            string fileName = FileCombo.SelectedItem.ToString()!;
            AddLog($"Descargando {fileName}...");

            if (fileName.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
            {
                string layer = System.IO.Path.GetFileNameWithoutExtension(fileName);
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                    .MdiActiveDocument.SendStringToExecute(
                        $"CADSYNC_PULL_DELTA\n{layer}\n", true, false, false);
            }
            else
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                    .MdiActiveDocument.SendStringToExecute(
                        $"CADSYNC_PULL_UI\n{fileName}\n", true, false, false);
            }
        }

        private void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Sincronizando proyecto completo...");
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                .MdiActiveDocument.SendStringToExecute("CADSYNC_PUSH ", true, false, false);
        }

        private void BtnRefreshFiles_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Actualizando lista de archivos...");
            _ = RefreshFiles();
        }
    }

    // ── View Models ───────────────────────────────────────────
    public class LogEntry
    {
        public string Message { get; set; } = "";
        public string Time    { get; set; } = "";
    }

    public class ActiveLayerEntry : INotifyPropertyChanged
    {
        private bool _isConflict;
        public string Name { get; set; } = "";

        public bool IsConflict
        {
            get => _isConflict;
            set { _isConflict = value; OnPropertyChanged(); OnPropertyChanged(null); }
        }

        // Dot color (raw Color for Binding to Ellipse.Fill > SolidColorBrush.Color)
        public string StatusDotColor => IsConflict ? "#F44336" : "#4CAF50";

        // Badge appearance
        public Brush BadgeBg     => IsConflict ? Br("#200808") : Br("#0d200d");
        public Brush BadgeBorder => IsConflict ? Br("#4d1010") : Br("#1f4d1f");
        public Brush BadgeFg     => IsConflict ? Br("#F44336") : Br("#4CAF50");
        public string StatusText  => IsConflict ? "CONFLICTO"  : "RESERVADA";

        private static SolidColorBrush Br(string hex) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ConnectedUser
    {
        public string Name     { get; set; } = "";
        /// <summary>Hex string ("#RRGGBB") for WPF Color binding.</summary>
        public string ColorHex { get; set; } = "#8A91A1";
    }
}
