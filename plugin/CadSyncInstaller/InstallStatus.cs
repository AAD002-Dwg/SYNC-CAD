namespace CadSyncInstaller
{
    public class InstallStatus
    {
        public string Message { get; set; } = string.Empty;
        public double ProgressPercentage { get; set; }
        public bool IsError { get; set; }
        public bool IsComplete { get; set; }
    }
}
