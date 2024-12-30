using Polly;
using Polly.Retry;
using Polly.Timeout;
using Polly.Wrap;
using Microsoft.Extensions.Logging;

namespace PollSystem.Services.Network
{
    public class NetworkRetryPolicyService
    {
        private readonly ILogger<NetworkRetryPolicyService> _logger;
        private readonly INetworkMonitor _networkMonitor;
        private readonly RetryPolicyConfiguration _configuration;
        private readonly Random _random = new();

        public NetworkRetryPolicyService(
            ILogger<NetworkRetryPolicyService> logger,
            INetworkMonitor networkMonitor,
            RetryPolicyConfiguration configuration = null)
        {
            _logger = logger;
            _networkMonitor = networkMonitor;
            _configuration = configuration ?? new RetryPolicyConfiguration();
        }

        public AsyncPolicyWrap<T> CreatePolicy<T>()
        {
            return Policy<T>
                .WrapAsync(
                    CreateTimeoutPolicy<T>(),
                    CreateRetryPolicy<T>(),
                    CreateCircuitBreakerPolicy<T>());
        }

        private AsyncTimeoutPolicy<T> CreateTimeoutPolicy<T>()
        {
            return Policy.TimeoutAsync<T>(
                _configuration.Timeout,
                TimeoutStrategy.Optimistic,
                onTimeoutAsync: async (context, timeSpan, task) =>
                {
                    _logger.LogWarning(
                        "Operation timed out after {Timeout}s",
                        timeSpan.TotalSeconds);
                });
        }

        private AsyncRetryPolicy<T> CreateRetryPolicy<T>()
        {
            return Policy<T>
                .Handle<NetworkException>()
                .Or<HttpRequestException>()
                .Or<TimeoutRejectedException>()
                .WaitAndRetryAsync(
                    _configuration.MaxRetries,
                    CalculateDelay,
                    OnRetryAsync);
        }

        private AsyncCircuitBreakerPolicy<T> CreateCircuitBreakerPolicy<T>()
        {
            return Policy<T>
                .Handle<Exception>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (exception, duration) =>
                    {
                        _logger.LogError(
                            exception,
                            "Circuit breaker opened for {Duration}s",
                            duration.TotalSeconds);
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation("Circuit breaker reset");
                    },
                    onHalfOpen: () =>
                    {
                        _logger.LogInformation("Circuit breaker half-open");
                    });
        }

        private TimeSpan CalculateDelay(int retryAttempt)
        {
            TimeSpan delay;

            if (_configuration.UseExponentialBackoff)
            {
                delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1));
            }
            else
            {
                delay = _configuration.InitialDelay;
            }

            // Add jitter
            var jitter = delay.TotalMilliseconds * _configuration.JitterFactor;
            var jitterMilliseconds = _random.NextDouble() * jitter * 2 - jitter;
            delay = delay.Add(TimeSpan.FromMilliseconds(jitterMilliseconds));

            return TimeSpan.FromMilliseconds(
                Math.Min(delay.TotalMilliseconds, _configuration.MaxDelay.TotalMilliseconds));
        }

        private async Task OnRetryAsync(
            Exception exception,
            TimeSpan timeSpan,
            int retryCount,
            Context context)
        {
            _logger.LogWarning(
                exception,
                "Retry {RetryCount} after {Delay}ms. Error: {Error}",
                retryCount,
                timeSpan.TotalMilliseconds,
                exception.Message);

            if (_networkMonitor.CurrentStatus != NetworkAccess.Internet)
            {
                _logger.LogInformation("Waiting for network connectivity...");
                await _networkMonitor.WaitForConnectionAsync(TimeSpan.FromSeconds(30));
            }

            // Add retry telemetry
            await AddRetryTelemetryAsync(exception, retryCount, timeSpan);
        }

        private async Task AddRetryTelemetryAsync(
            Exception exception,
            int retryCount,
            TimeSpan delay)
        {
            try
            {
                var telemetry = new Dictionary<string, object>
                {
                    ["RetryCount"] = retryCount,
                    ["Delay"] = delay.TotalMilliseconds,
                    ["ExceptionType"] = exception.GetType().Name,
                    ["NetworkStatus"] = _networkMonitor.CurrentStatus.ToString(),
                    ["ConnectionType"] = _networkMonitor.ConnectionProfile?.ToString()
                };

                var metrics = await _networkMonitor.GetNetworkMetricsAsync();
                telemetry["NetworkMetrics"] = metrics;

                _logger.LogInformation(
                    "Retry telemetry: {Telemetry}",
                    telemetry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging retry telemetry");
            }
        }
    }
} 