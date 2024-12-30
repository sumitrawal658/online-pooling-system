using System.Collections.Concurrent;
using System.Threading;

namespace PollSystem.Services.Diagnostics
{
    public class MetricDataManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, MetricTimeSeriesData> _metricData;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _metricLocks;
        private readonly ReaderWriterLockSlim _globalLock;
        private readonly ILogger<MetricDataManager> _logger;
        private readonly int _maxConcurrentWrites;
        private readonly TimeSpan _dataRetentionPeriod;
        private readonly Timer _cleanupTimer;

        public MetricDataManager(
            ILogger<MetricDataManager> logger,
            TimeSpan? dataRetentionPeriod = null,
            int maxConcurrentWrites = 10)
        {
            _logger = logger;
            _metricData = new ConcurrentDictionary<string, MetricTimeSeriesData>();
            _metricLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            _globalLock = new ReaderWriterLockSlim();
            _maxConcurrentWrites = maxConcurrentWrites;
            _dataRetentionPeriod = dataRetentionPeriod ?? TimeSpan.FromDays(7);

            // Initialize cleanup timer
            _cleanupTimer = new Timer(
                CleanupOldData,
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromHours(1));
        }

        public async Task AddDataPointAsync(string metricName, MetricDataPoint dataPoint)
        {
            var metricLock = _metricLocks.GetOrAdd(metricName,
                _ => new SemaphoreSlim(_maxConcurrentWrites));

            await metricLock.WaitAsync();
            try
            {
                var timeSeries = _metricData.GetOrAdd(metricName,
                    _ => new MetricTimeSeriesData(metricName));
                timeSeries.AddDataPoint(dataPoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error adding data point for metric {MetricName}",
                    metricName);
                throw;
            }
            finally
            {
                metricLock.Release();
            }
        }

        public async Task<List<MetricDataPoint>> GetDataPointsAsync(
            string metricName,
            TimeSpan window,
            CancellationToken cancellationToken = default)
        {
            if (!_metricData.TryGetValue(metricName, out var timeSeries))
            {
                return new List<MetricDataPoint>();
            }

            var metricLock = _metricLocks.GetOrAdd(metricName,
                _ => new SemaphoreSlim(_maxConcurrentWrites));

            await metricLock.WaitAsync(cancellationToken);
            try
            {
                return timeSeries.GetDataPoints(window);
            }
            finally
            {
                metricLock.Release();
            }
        }

        public async Task<Dictionary<string, List<MetricDataPoint>>> GetAllMetricDataAsync(
            TimeSpan window,
            CancellationToken cancellationToken = default)
        {
            _globalLock.EnterReadLock();
            try
            {
                var result = new Dictionary<string, List<MetricDataPoint>>();
                var tasks = _metricData.Select(async kvp =>
                {
                    var data = await GetDataPointsAsync(kvp.Key, window, cancellationToken);
                    return (kvp.Key, data);
                });

                var dataPoints = await Task.WhenAll(tasks);
                foreach (var (metric, data) in dataPoints)
                {
                    result[metric] = data;
                }

                return result;
            }
            finally
            {
                _globalLock.ExitReadLock();
            }
        }

        public async Task<MetricStatistics> GetMetricStatisticsAsync(
            string metricName,
            TimeSpan window,
            CancellationToken cancellationToken = default)
        {
            var dataPoints = await GetDataPointsAsync(metricName, window, cancellationToken);
            if (!dataPoints.Any())
            {
                return new MetricStatistics();
            }

            var values = dataPoints.Select(p => p.Value).ToList();
            return new MetricStatistics
            {
                Mean = values.Average(),
                Median = values.Median(),
                StdDev = values.StandardDeviation(),
                Min = values.Min(),
                Max = values.Max(),
                P95 = values.Percentile(95),
                P99 = values.Percentile(99),
                SampleCount = values.Count
            };
        }

        private void CleanupOldData(object state)
        {
            try
            {
                _globalLock.EnterWriteLock();
                var cutoffTime = DateTime.UtcNow - _dataRetentionPeriod;
                var metricsToClean = _metricData.Keys.ToList();

                foreach (var metricName in metricsToClean)
                {
                    if (_metricData.TryGetValue(metricName, out var timeSeries))
                    {
                        var dataPoints = timeSeries.GetDataPoints(_dataRetentionPeriod);
                        if (!dataPoints.Any())
                        {
                            // Remove metrics with no recent data
                            _metricData.TryRemove(metricName, out _);
                            _metricLocks.TryRemove(metricName, out var semaphore);
                            semaphore?.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during metric data cleanup");
            }
            finally
            {
                _globalLock.ExitWriteLock();
            }
        }

        public async Task<List<string>> GetAllMetricNamesAsync()
        {
            _globalLock.EnterReadLock();
            try
            {
                return _metricData.Keys.ToList();
            }
            finally
            {
                _globalLock.ExitReadLock();
            }
        }

        public async Task<CleanupResult> CleanupMetricDataAsync(
            string metricName,
            DateTime cutoffTime,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            if (!_metricData.TryGetValue(metricName, out var timeSeries))
            {
                return new CleanupResult
                {
                    RemovedDataPoints = 0,
                    FreedMemoryBytes = 0,
                    Duration = TimeSpan.Zero
                };
            }

            var metricLock = _metricLocks.GetOrAdd(metricName,
                _ => new SemaphoreSlim(_maxConcurrentWrites));

            await metricLock.WaitAsync(cancellationToken);
            try
            {
                var startTime = DateTime.UtcNow;
                var startMemory = GC.GetTotalMemory(false);

                var dataPoints = timeSeries.GetDataPointsInRange(
                    DateTime.MinValue,
                    cutoffTime);

                var removedCount = 0;
                foreach (var batch in dataPoints.Chunk(batchSize))
                {
                    foreach (var point in batch)
                    {
                        timeSeries.RemoveDataPoint(point.Timestamp);
                        removedCount++;
                    }

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Allow other operations to proceed between batches
                    await Task.Delay(10, cancellationToken);
                }

                var endMemory = GC.GetTotalMemory(false);
                var duration = DateTime.UtcNow - startTime;

                return new CleanupResult
                {
                    RemovedDataPoints = removedCount,
                    FreedMemoryBytes = startMemory - endMemory,
                    Duration = duration
                };
            }
            finally
            {
                metricLock.Release();
            }
        }

        public async Task<long> GetEstimatedMemoryUsageAsync(
            string metricName,
            CancellationToken cancellationToken = default)
        {
            if (!_metricData.TryGetValue(metricName, out var timeSeries))
            {
                return 0;
            }

            var metricLock = _metricLocks.GetOrAdd(metricName,
                _ => new SemaphoreSlim(_maxConcurrentWrites));

            await metricLock.WaitAsync(cancellationToken);
            try
            {
                var sampleSize = Math.Min(100, timeSeries.GetDataPointCount());
                if (sampleSize == 0) return 0;

                var sample = await GetDataPointsAsync(metricName, TimeSpan.FromDays(1), cancellationToken);
                sample = sample.Take(sampleSize).ToList();

                var sampleMemory = sample.Sum(p => 
                    System.Text.Encoding.UTF8.GetByteCount(p.ToString()) +
                    p.Tags?.Sum(t => 
                        System.Text.Encoding.UTF8.GetByteCount(t.Key) +
                        System.Text.Encoding.UTF8.GetByteCount(t.Value)) ?? 0);

                var averagePointSize = sampleMemory / sampleSize;
                return averagePointSize * timeSeries.GetDataPointCount();
            }
            finally
            {
                metricLock.Release();
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _globalLock?.Dispose();

            foreach (var semaphore in _metricLocks.Values)
            {
                semaphore?.Dispose();
            }
        }
    }
} 