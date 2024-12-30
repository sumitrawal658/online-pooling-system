using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace PollSystem.Services.Logging
{
    public class DetailedLoggingService
    {
        private readonly ILogger _logger;
        private readonly IDeviceInfoService _deviceInfo;
        private readonly INetworkMonitor _networkMonitor;
        private readonly Dictionary<string, object> _baseContext;

        public DetailedLoggingService(
            ILogger logger,
            IDeviceInfoService deviceInfo,
            INetworkMonitor networkMonitor)
        {
            _logger = logger;
            _deviceInfo = deviceInfo;
            _networkMonitor = networkMonitor;
            _baseContext = CreateBaseContext();
        }

        public IDisposable BeginOperation(
            string operationName,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var operationId = Guid.NewGuid().ToString();
            var timestamp = DateTime.UtcNow;

            var context = new Dictionary<string, object>(_baseContext)
            {
                ["OperationId"] = operationId,
                ["OperationName"] = operationName,
                ["StartTime"] = timestamp,
                ["CallerName"] = callerName,
                ["SourceFile"] = Path.GetFileName(sourceFile),
                ["LineNumber"] = lineNumber
            };

            _logger.LogInformation(
                "Starting operation {OperationName} ({OperationId})",
                operationName,
                operationId);

            return new OperationScope(this, operationId, operationName, timestamp);
        }

        public void LogException(
            Exception exception,
            string context,
            Dictionary<string, object> additionalData = null,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var errorContext = GatherErrorContext(exception, context, additionalData, callerName, sourceFile, lineNumber);
            var exceptionDetails = GetExceptionDetails(exception);

            using (_logger.BeginScope(errorContext))
            {
                _logger.LogError(
                    exception,
                    "Error in {Context}: {Message}. Details: {@ExceptionDetails}",
                    context,
                    exception.Message,
                    exceptionDetails);
            }
        }

        public void LogMetric(
            string metricName,
            double value,
            Dictionary<string, object> dimensions = null)
        {
            var context = new Dictionary<string, object>(_baseContext)
            {
                ["MetricName"] = metricName,
                ["Value"] = value,
                ["Timestamp"] = DateTime.UtcNow
            };

            if (dimensions != null)
            {
                foreach (var dimension in dimensions)
                {
                    context[$"Dimension_{dimension.Key}"] = dimension.Value;
                }
            }

            using (_logger.BeginScope(context))
            {
                _logger.LogInformation(
                    "Metric {MetricName}: {Value}",
                    metricName,
                    value);
            }
        }

        private Dictionary<string, object> CreateBaseContext()
        {
            var deviceDetails = _deviceInfo.GetDeviceDetails();
            return new Dictionary<string, object>
            {
                ["AppVersion"] = deviceDetails.AppInfo.Version,
                ["DeviceId"] = deviceDetails.Name,
                ["Platform"] = deviceDetails.Platform.ToString(),
                ["OSVersion"] = deviceDetails.VersionString,
                ["NetworkStatus"] = _networkMonitor.CurrentStatus.ToString(),
                ["ProcessId"] = Process.GetCurrentProcess().Id
            };
        }

        private Dictionary<string, object> GatherErrorContext(
            Exception exception,
            string context,
            Dictionary<string, object> additionalData,
            string callerName,
            string sourceFile,
            int lineNumber)
        {
            var errorContext = new Dictionary<string, object>(_baseContext)
            {
                ["ErrorId"] = Guid.NewGuid().ToString(),
                ["Timestamp"] = DateTime.UtcNow,
                ["Context"] = context,
                ["ExceptionType"] = exception.GetType().FullName,
                ["CallerName"] = callerName,
                ["SourceFile"] = Path.GetFileName(sourceFile),
                ["LineNumber"] = lineNumber,
                ["StackTrace"] = exception.StackTrace,
                ["Memory"] = GC.GetTotalMemory(false),
                ["ThreadId"] = Environment.CurrentManagedThreadId
            };

            if (additionalData != null)
            {
                foreach (var item in additionalData)
                {
                    errorContext[$"Additional_{item.Key}"] = item.Value;
                }
            }

            return errorContext;
        }

        private Dictionary<string, object> GetExceptionDetails(Exception exception)
        {
            var details = new Dictionary<string, object>();
            var currentEx = exception;
            var level = 0;

            while (currentEx != null)
            {
                details[$"Level{level}_Type"] = currentEx.GetType().FullName;
                details[$"Level{level}_Message"] = currentEx.Message;
                details[$"Level{level}_StackTrace"] = currentEx.StackTrace;

                if (currentEx is PollSystemException pollEx)
                {
                    details[$"Level{level}_ErrorCode"] = pollEx.Metadata.ErrorCode;
                    details[$"Level{level}_Category"] = pollEx.Category.ToString();
                    details[$"Level{level}_Severity"] = pollEx.Severity.ToString();
                    details[$"Level{level}_IsRetryable"] = pollEx.IsRetryable;

                    foreach (var item in pollEx.Metadata.Context)
                    {
                        details[$"Level{level}_Context_{item.Key}"] = item.Value;
                    }
                }

                currentEx = currentEx.InnerException;
                level++;
            }

            return details;
        }

        private class OperationScope : IDisposable
        {
            private readonly DetailedLoggingService _loggingService;
            private readonly string _operationId;
            private readonly string _operationName;
            private readonly DateTime _startTime;
            private readonly Stopwatch _stopwatch;

            public OperationScope(
                DetailedLoggingService loggingService,
                string operationId,
                string operationName,
                DateTime startTime)
            {
                _loggingService = loggingService;
                _operationId = operationId;
                _operationName = operationName;
                _startTime = startTime;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _stopwatch.Stop();
                var context = new Dictionary<string, object>(_loggingService._baseContext)
                {
                    ["OperationId"] = _operationId,
                    ["OperationName"] = _operationName,
                    ["StartTime"] = _startTime,
                    ["EndTime"] = DateTime.UtcNow,
                    ["Duration"] = _stopwatch.ElapsedMilliseconds
                };

                using (_loggingService._logger.BeginScope(context))
                {
                    _loggingService._logger.LogInformation(
                        "Completed operation {OperationName} ({OperationId}) in {Duration}ms",
                        _operationName,
                        _operationId,
                        _stopwatch.ElapsedMilliseconds);
                }
            }
        }
    }
} 