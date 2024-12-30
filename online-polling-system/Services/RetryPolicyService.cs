using Polly;
using Polly.Retry;
using Polly.Timeout;
using Polly.Wrap;
using PollSystem.Exceptions;

namespace PollSystem.Services
{
    public class RetryPolicyService
    {
        private readonly ILogger<RetryPolicyService> _logger;
        private readonly IConnectivity _connectivity;

        public RetryPolicyService(ILogger<RetryPolicyService> logger, IConnectivity connectivity)
        {
            _logger = logger;
            _connectivity = connectivity;
        }

        public AsyncPolicyWrap<T> CreateRetryPolicy<T>()
        {
            return Policy<T>
                .WrapAsync(
                    CreateTimeoutPolicy<T>(),
                    CreateRetryAndCircuitBreakerPolicy<T>());
        }

        private AsyncTimeoutPolicy<T> CreateTimeoutPolicy<T>()
        {
            return Policy.TimeoutAsync<T>(
                seconds: 10,
                onTimeoutAsync: async (context, timeSpan, task) =>
                {
                    _logger.LogWarning("Operation timed out after {Timeout}s", timeSpan.TotalSeconds);
                });
        }

        private AsyncPolicyWrap<T> CreateRetryAndCircuitBreakerPolicy<T>()
        {
            var retryPolicy = Policy<T>
                .Handle<NetworkException>()
                .Or<HttpRequestException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => 
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetryAsync: async (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "Retry {RetryCount} after {Delay}ms. Error: {Error}",
                            retryCount,
                            timeSpan.TotalMilliseconds,
                            exception.Message);

                        // Wait for network connectivity if needed
                        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
                        {
                            _logger.LogInformation("Waiting for network connectivity...");
                            await WaitForConnectivityAsync();
                        }
                    });

            var circuitBreakerPolicy = Policy<T>
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
                    });

            return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
        }

        private async Task WaitForConnectivityAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            IDisposable subscription = null;

            subscription = _connectivity.ConnectivityChanged.Subscribe(info =>
            {
                if (info.NetworkAccess == NetworkAccess.Internet)
                {
                    tcs.TrySetResult(true);
                    subscription?.Dispose();
                }
            });

            try
            {
                await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(TimeSpan.FromMinutes(1)));
            }
            finally
            {
                subscription?.Dispose();
            }
        }
    }
} 