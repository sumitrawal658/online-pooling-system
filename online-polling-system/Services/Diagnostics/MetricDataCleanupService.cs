using System.Collections.Concurrent;

namespace PollSystem.Services.Diagnostics
{
    public class MetricDataCleanupService : IHostedService, IDisposable
    {
        private readonly ILogger<MetricDataCleanupService> _logger;
        private readonly MetricDataManager _dataManager;
        private readonly IOptions<CleanupConfiguration> _config;
        private readonly Timer _cleanupTimer;
        private readonly ConcurrentDictionary<string, DateTime> _lastCleanupTime;
        private readonly SemaphoreSlim _cleanupLock;
        private bool _isRunning;

        public MetricDataCleanupService(
            ILogger<MetricDataCleanupService> logger,
            MetricDataManager dataManager,
            IOptions<CleanupConfiguration> config)
        {
            _logger = logger;
            _dataManager = dataManager;
            _config = config;
            _lastCleanupTime = new ConcurrentDictionary<string, DateTime>();
            _cleanupLock = new SemaphoreSlim(1, 1);
            _cleanupTimer = new Timer(ExecuteCleanup, null, Timeout.Infinite, Timeout.Infinite);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting metric data cleanup service");
            _isRunning = true;
            ScheduleNextCleanup();
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping metric data cleanup service");
            _isRunning = false;

            if (_cleanupTimer != null)
            {
                await _cleanupTimer.DisposeAsync();
            }

            // Perform one final cleanup
            await ExecuteCleanupAsync(cancellationToken);
        }

        private void ScheduleNextCleanup()
        {
            if (!_isRunning) return;

            var now = DateTime.UtcNow;
            var nextRun = CalculateNextCleanupTime(now);
            var delay = nextRun - now;

            _cleanupTimer.Change(delay, TimeSpan.FromMilliseconds(-1));
        }

        private DateTime CalculateNextCleanupTime(DateTime now)
        {
            // Schedule cleanup during off-peak hours (default: 2 AM local time)
            var preferredTime = _config.Value.PreferredCleanupTime;
            var nextRun = now.Date.Add(preferredTime);
            
            if (nextRun <= now)
            {
                nextRun = nextRun.AddDays(1);
            }

            return nextRun;
        }

        private async void ExecuteCleanup(object state)
        {
            try
            {
                await ExecuteCleanupAsync(CancellationToken.None);
            }
            finally
            {
                ScheduleNextCleanup();
            }
        }

        private async Task ExecuteCleanupAsync(CancellationToken cancellationToken)
        {
            if (!await _cleanupLock.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                _logger.LogWarning("Cleanup already in progress, skipping this run");
                return;
            }

            try
            {
                _logger.LogInformation("Starting metric data cleanup");
                var startTime = DateTime.UtcNow;
                var cleanupTasks = new List<Task>();
                var metrics = await _dataManager.GetAllMetricNamesAsync();

                foreach (var metric in metrics)
                {
                    if (ShouldCleanupMetric(metric))
                    {
                        cleanupTasks.Add(CleanupMetricDataAsync(metric, cancellationToken));
                    }
                }

                await Task.WhenAll(cleanupTasks);
                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Completed metric data cleanup in {Duration}",
                    duration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during metric data cleanup");
            }
            finally
            {
                _cleanupLock.Release();
            }
        }

        private bool ShouldCleanupMetric(string metricName)
        {
            var lastCleanup = _lastCleanupTime.GetOrAdd(metricName, DateTime.MinValue);
            var timeSinceLastCleanup = DateTime.UtcNow - lastCleanup;
            return timeSinceLastCleanup >= _config.Value.MinimumCleanupInterval;
        }

        private async Task CleanupMetricDataAsync(
            string metricName,
            CancellationToken cancellationToken)
        {
            try
            {
                var config = _config.Value;
                var retentionPeriod = GetRetentionPeriod(metricName);
                var cutoffTime = DateTime.UtcNow - retentionPeriod;

                var cleanupResult = await _dataManager.CleanupMetricDataAsync(
                    metricName,
                    cutoffTime,
                    config.BatchSize,
                    cancellationToken);

                _lastCleanupTime.AddOrUpdate(
                    metricName,
                    DateTime.UtcNow,
                    (_, _) => DateTime.UtcNow);

                if (cleanupResult.RemovedDataPoints > 0)
                {
                    _logger.LogInformation(
                        "Cleaned up {Count} data points for metric {MetricName}",
                        cleanupResult.RemovedDataPoints,
                        metricName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error cleaning up data for metric {MetricName}",
                    metricName);
            }
        }

        private TimeSpan GetRetentionPeriod(string metricName)
        {
            // Get metric-specific retention period if configured, otherwise use default
            return _config.Value.MetricRetentionPeriods.GetValueOrDefault(
                metricName,
                _config.Value.DefaultRetentionPeriod);
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _cleanupLock?.Dispose();
        }
    }

    public class CleanupConfiguration
    {
        public TimeSpan PreferredCleanupTime { get; set; } = TimeSpan.FromHours(2); // 2 AM
        public TimeSpan MinimumCleanupInterval { get; set; } = TimeSpan.FromHours(12);
        public TimeSpan DefaultRetentionPeriod { get; set; } = TimeSpan.FromDays(30);
        public Dictionary<string, TimeSpan> MetricRetentionPeriods { get; set; } = new();
        public int BatchSize { get; set; } = 1000;
        public bool EnableAggressiveCleanup { get; set; } = false;
        public double MemoryThresholdPercentage { get; set; } = 85;
    }

    public class CleanupResult
    {
        public int RemovedDataPoints { get; init; }
        public long FreedMemoryBytes { get; init; }
        public TimeSpan Duration { get; init; }
    }
} 