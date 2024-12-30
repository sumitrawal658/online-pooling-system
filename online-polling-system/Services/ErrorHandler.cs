using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using PollSystem.Extensions;
using PollSystem.Exceptions;

namespace PollSystem.Services
{
    public class ErrorHandler : IErrorHandler
    {
        private readonly ILogger<ErrorHandler> _logger;
        private readonly IConnectivity _connectivity;
        private readonly AsyncRetryPolicy _retryPolicy;

        public ErrorHandler(
            ILogger<ErrorHandler> logger,
            IConnectivity connectivity)
        {
            _logger = logger;
            _connectivity = connectivity;
            _retryPolicy = CreateRetryPolicy();
        }

        private AsyncRetryPolicy CreateRetryPolicy()
        {
            return Policy
                .Handle<NetworkException>()
                .Or<HttpRequestException>()
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "Retry {RetryCount} after {Delay}ms. Error: {Error}",
                            retryCount,
                            timeSpan.TotalMilliseconds,
                            exception.Message);
                    }
                );
        }

        public async Task HandleExceptionAsync(Exception ex, string context = null)
        {
            var pollEx = ex as PollSystemException ?? ex.ToPollSystemException();
            var properties = new Dictionary<string, object>(pollEx.Metadata.Context)
            {
                ["Context"] = context ?? "Unknown",
                ["Category"] = pollEx.Category.ToString(),
                ["Severity"] = pollEx.Severity.ToString(),
                ["ErrorCode"] = pollEx.Metadata.ErrorCode,
                ["IsRetryable"] = pollEx.IsRetryable,
                ["NetworkStatus"] = _connectivity.NetworkAccess.ToString()
            };

            var logLevel = pollEx.Severity switch
            {
                ErrorSeverity.Critical => LogLevel.Critical,
                ErrorSeverity.Error => LogLevel.Error,
                ErrorSeverity.Warning => LogLevel.Warning,
                _ => LogLevel.Information
            };

            _logger.Log(
                logLevel,
                ex,
                "{Category} error ({ErrorCode}) in {Context}: {Message}",
                pollEx.Category,
                pollEx.Metadata.ErrorCode,
                context,
                pollEx.Message);

            await LogErrorAsync(pollEx.Message, pollEx, properties);

            if (pollEx.Severity == ErrorSeverity.Critical)
            {
                // Notify monitoring system or take critical action
                await NotifyCriticalErrorAsync(pollEx);
            }
        }

        private async Task NotifyCriticalErrorAsync(PollSystemException ex)
        {
            // Implement critical error notification logic
            // For example, send to monitoring service, crash reporting, etc.
        }

        public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
        {
            try
            {
                return await _retryPolicy.ExecuteAsync(action);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex, $"Retry failed after {maxRetries} attempts");
                throw;
            }
        }

        public async Task LogErrorAsync(string message, Exception ex = null, IDictionary<string, object> properties = null)
        {
            var logProperties = new Dictionary<string, object>
            {
                { "Timestamp", DateTime.UtcNow },
                { "NetworkStatus", _connectivity.NetworkAccess.ToString() },
                { "DeviceInfo", DeviceInfo.Current.Model }
            };

            if (properties != null)
            {
                foreach (var prop in properties)
                {
                    logProperties[prop.Key] = prop.Value;
                }
            }

            if (ex != null)
            {
                logProperties["StackTrace"] = ex.StackTrace;
                logProperties["ExceptionType"] = ex.GetType().Name;
            }

            using (_logger.BeginScope(logProperties))
            {
                if (ex != null)
                {
                    _logger.LogError(ex, message);
                }
                else
                {
                    _logger.LogError(message);
                }
            }
        }
    }
} 