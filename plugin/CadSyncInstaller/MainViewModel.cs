using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CadSyncInstaller
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly SetupService _setupService;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "Listo para instalar / desinstalar SYNC-CAD.";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        private bool _isComplete;
        public bool IsComplete
        {
            get => _isComplete;
            set { 
                _isComplete = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsNotComplete)); 
            }
        }

        public bool IsNotComplete => !_isComplete;


        public bool IsInstalled => _setupService.IsInstalled();

        public ICommand InstallCommand { get; }
        public ICommand UninstallCommand { get; }
        public ICommand CloseCommand { get; }

        public MainViewModel()
        {
            _setupService = new SetupService();

            InstallCommand = new RelayCommand(async _ => await InstallAsync(), _ => !IsBusy && !IsComplete);
            UninstallCommand = new RelayCommand(async _ => await UninstallAsync(), _ => !IsBusy && IsInstalled && !IsComplete);
            CloseCommand = new RelayCommand(_ => Application.Current.Shutdown());
        }

        private async Task InstallAsync()
        {
            IsBusy = true;
            HasError = false;
            IsComplete = false;
            ProgressValue = 0;

            var progress = new Progress<InstallStatus>(status =>
            {
                StatusMessage = status.Message;
                ProgressValue = status.ProgressPercentage;
                if (status.IsError) HasError = true;
                if (status.IsComplete) 
                {
                    IsComplete = true;
                    OnPropertyChanged(nameof(IsInstalled));
                }
            });

            await _setupService.InstallAsync(progress);
            IsBusy = false;
        }

        private async Task UninstallAsync()
        {
            IsBusy = true;
            HasError = false;
            IsComplete = false;
            ProgressValue = 0;

            var progress = new Progress<InstallStatus>(status =>
            {
                StatusMessage = status.Message;
                ProgressValue = status.ProgressPercentage;
                if (status.IsError) HasError = true;
                if (status.IsComplete)
                {
                    IsComplete = true;
                    OnPropertyChanged(nameof(IsInstalled));
                }
            });

            await _setupService.UninstallAsync(progress);
            IsBusy = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
