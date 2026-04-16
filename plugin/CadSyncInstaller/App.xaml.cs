using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace CadSyncInstaller
{
    public partial class App : Application
    {
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string mutexName = "Global\\CadSyncInstallerMutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                // Otra instancia ya está ejecutándose
                if (!e.Args.Contains("--update") && !e.Args.Contains("--uninstall"))
                {
                    MessageBox.Show("El instalador/actualizador ya se está ejecutando.", "SYNC-CAD", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                Environment.Exit(0);
                return;
            }

            var args = Environment.GetCommandLineArgs();
            bool silent = false;

            if (args.Length > 1)
            {
                var action = args[1].ToLower();
                if (action == "--uninstall")
                {
                    silent = true;
                    new SetupService().SilentUninstall();
                    Environment.Exit(0);
                }
                else if (action == "--update")
                {
                    silent = true;
                    new SetupService().SilentWaitAndUpdate();
                    Environment.Exit(0);
                }
            }

            if (!silent)
            {
                base.OnStartup(e);
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
            else
            {
                Environment.Exit(0);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
