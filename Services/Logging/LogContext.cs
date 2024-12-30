namespace PollSystem.Services.Logging
{
    public class LogContext
    {
        public string UserId { get; set; }
        public string DeviceId { get; set; }
        public string AppVersion { get; set; }
        public string NetworkStatus { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; }

        public LogContext()
        {
            AdditionalData = new Dictionary<string, object>();
        }

        public static LogContext Current => LogContextAccessor.Current;
    }

    public static class LogContextAccessor
    {
        private static readonly AsyncLocal<LogContext> _logContext = new();
        public static LogContext Current
        {
            get => _logContext.Value ??= CreateDefaultContext();
            set => _logContext.Value = value;
        }

        private static LogContext CreateDefaultContext()
        {
            return new LogContext
            {
                DeviceId = DeviceInfo.Current?.Name,
                AppVersion = AppInfo.Current.VersionString,
                NetworkStatus = Connectivity.Current.NetworkAccess.ToString()
            };
        }
    }
} 