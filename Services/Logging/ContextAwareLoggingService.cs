namespace PollSystem.Services.Logging
{
    public class ContextAwareLoggingService
    {
        private readonly ILogger _logger;
        private readonly IDeviceInfoService _deviceInfo;
        private readonly INetworkMonitor _networkMonitor;

        public ContextAwareLoggingService(
            ILogger logger,
            IDeviceInfoService deviceInfo,
            INetworkMonitor networkMonitor)
        {
            _logger = logger;
            _deviceInfo = deviceInfo;
            _networkMonitor = networkMonitor;
        }

        public IDisposable BeginOperation(string operationName)
        {
            var context = OperationContext.Current;
            var scope = OperationContext.BeginScope(operationName);

            var logScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = context.CorrelationId,
                ["OperationName"] = operationName,
                ["OperationStack"] = string.Join(" -> ", context.OperationStack),
                ["StartTime"] = DateTime.UtcNow,
                ["UserId"] = context.UserId,
                ["DeviceInfo"] = _deviceInfo.GetDeviceDetails().ToDictionary(),
                ["NetworkStatus"] = _networkMonitor.CurrentStatus.ToString()
            });

            return new CompositeDisposable(scope, logScope);
        }

        public void LogWithContext(
            LogLevel level,
            string message,
            Exception exception = null,
            Dictionary<string, object> additionalData = null)
        {
            var context = OperationContext.Current;
            var logData = new Dictionary<string, object>
            {
                ["CorrelationId"] = context.CorrelationId,
                ["OperationStack"] = string.Join(" -> ", context.OperationStack),
                ["ElapsedTime"] = (DateTime.UtcNow - context.StartTime).TotalMilliseconds,
                ["ContextData"] = context.Data
            };

            if (additionalData != null)
            {
                foreach (var kvp in additionalData)
                {
                    logData[kvp.Key] = kvp.Value;
                }
            }

            using (_logger.BeginScope(logData))
            {
                if (exception != null)
                {
                    _logger.Log(level, exception, message);
                }
                else
                {
                    _logger.Log(level, message);
                }
            }
        }

        private class CompositeDisposable : IDisposable
        {
            private readonly IDisposable[] _disposables;

            public CompositeDisposable(params IDisposable[] disposables)
            {
                _disposables = disposables;
            }

            public void Dispose()
            {
                foreach (var disposable in _disposables)
                {
                    disposable?.Dispose();
                }
            }
        }
    }
} 