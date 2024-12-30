using System.Collections.Concurrent;
using System.Threading;

namespace PollSystem.Services.Diagnostics
{
    public class MetricHistoryManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, MetricHistoryStore> _metricStores;
        private readonly SemaphoreSlim _cleanupLock;
        private readonly Timer _cleanupTimer;
        private readonly ILogger<MetricHistoryManager> _logger;
        private readonly TimeSpan _retentionPeriod;
        private readonly int _maxDataPointsPerMetric;
        private volatile bool _isDisposed;

        public MetricHistoryManager(
            ILogger<MetricHistoryManager> logger,
            TimeSpan? retentionPeriod = null,
            int? maxDataPointsPerMetric = null)
        {
            _logger = logger;
            _metricStores = new ConcurrentDictionary<string, MetricHistoryStore>();
            _cleanupLock = new SemaphoreSlim(1, 1);
            _retentionPeriod = retentionPeriod ?? TimeSpan.FromDays(7);
            _maxDataPointsPerMetric = maxDataPointsPerMetric ?? 10000;

            // Schedule periodic cleanup
            _cleanupTimer = new Timer(
                CleanupCallback,
                null,
                TimeSpan.FromMinutes(30),
                TimeSpan.FromHours(1));
        }

        public async Task AddMetricDataPointAsync(string metricName, MetricDataPoint dataPoint)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(MetricHistoryManager));

            try
            {
                var store = _metricStores.GetOrAdd(metricName, CreateNewMetricStore);
                await store.AddDataPointAsync(dataPoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding metric data point for {MetricName}", metricName);
                throw;
            }
        }

        public async Task<IReadOnlyList<MetricDataPoint>> GetMetricHistoryAsync(
            string metricName,
            DateTime? startTime = null,
            DateTime? endTime = null,
            int? maxPoints = null)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(MetricHistoryManager));

            if (!_metricStores.TryGetValue(metricName, out var store))
                return Array.Empty<MetricDataPoint>();

            try
            {
                return await store.GetDataPointsAsync(startTime, endTime, maxPoints);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving metric history for {MetricName}", metricName);
                throw;
            }
        }

        private MetricHistoryStore CreateNewMetricStore(string metricName)
        {
            return new MetricHistoryStore(
                metricName,
                _maxDataPointsPerMetric,
                _retentionPeriod,
                _logger);
        }

        private async void CleanupCallback(object state)
        {
            if (_isDisposed || !await _cleanupLock.WaitAsync(0))
                return;

            try
            {
                var cutoffTime = DateTime.UtcNow - _retentionPeriod;
                var storeKeys = _metricStores.Keys.ToList();

                foreach (var key in storeKeys)
                {
                    if (_metricStores.TryGetValue(key, out var store))
                    {
                        await store.RemoveExpiredDataPointsAsync(cutoffTime);
                        
                        // Remove empty stores
                        if (await store.IsEmptyAsync())
                            _metricStores.TryRemove(key, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during metric history cleanup");
            }
            finally
            {
                _cleanupLock.Release();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _cleanupTimer?.Dispose();
            _cleanupLock?.Dispose();

            foreach (var store in _metricStores.Values)
            {
                store.Dispose();
            }

            _metricStores.Clear();
        }
    }

    public class MetricHistoryStore : IDisposable
    {
        private readonly string _metricName;
        private readonly int _maxDataPoints;
        private readonly TimeSpan _retentionPeriod;
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<MetricDataPoint> _dataPoints;
        private readonly SemaphoreSlim _accessLock;
        private readonly ReaderWriterLockSlim _dataLock;
        private volatile bool _isDisposed;

        public MetricHistoryStore(
            string metricName,
            int maxDataPoints,
            TimeSpan retentionPeriod,
            ILogger logger)
        {
            _metricName = metricName;
            _maxDataPoints = maxDataPoints;
            _retentionPeriod = retentionPeriod;
            _logger = logger;
            _dataPoints = new ConcurrentQueue<MetricDataPoint>();
            _accessLock = new SemaphoreSlim(1, 1);
            _dataLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        }

        public async Task AddDataPointAsync(MetricDataPoint dataPoint)
        {
            if (_isDisposed)
                throw new ObjectDisposedException($"MetricHistoryStore:{_metricName}");

            await _accessLock.WaitAsync();
            try
            {
                _dataLock.EnterWriteLock();
                try
                {
                    _dataPoints.Enqueue(dataPoint);

                    // Maintain size limit
                    while (_dataPoints.Count > _maxDataPoints)
                    {
                        _dataPoints.TryDequeue(out _);
                    }
                }
                finally
                {
                    _dataLock.ExitWriteLock();
                }
            }
            finally
            {
                _accessLock.Release();
            }
        }

        public async Task<IReadOnlyList<MetricDataPoint>> GetDataPointsAsync(
            DateTime? startTime = null,
            DateTime? endTime = null,
            int? maxPoints = null)
        {
            if (_isDisposed)
                throw new ObjectDisposedException($"MetricHistoryStore:{_metricName}");

            _dataLock.EnterReadLock();
            try
            {
                var query = _dataPoints.AsEnumerable();

                if (startTime.HasValue)
                    query = query.Where(p => p.Timestamp >= startTime.Value);
                if (endTime.HasValue)
                    query = query.Where(p => p.Timestamp <= endTime.Value);
                if (maxPoints.HasValue)
                    query = query.TakeLast(maxPoints.Value);

                return query.ToList();
            }
            finally
            {
                _dataLock.ExitReadLock();
            }
        }

        public async Task RemoveExpiredDataPointsAsync(DateTime cutoffTime)
        {
            if (_isDisposed)
                return;

            await _accessLock.WaitAsync();
            try
            {
                _dataLock.EnterWriteLock();
                try
                {
                    var validPoints = new ConcurrentQueue<MetricDataPoint>(
                        _dataPoints.Where(p => p.Timestamp > cutoffTime));
                    
                    Interlocked.Exchange(ref _dataPoints, validPoints);
                }
                finally
                {
                    _dataLock.ExitWriteLock();
                }
            }
            finally
            {
                _accessLock.Release();
            }
        }

        public async Task<bool> IsEmptyAsync()
        {
            if (_isDisposed)
                return true;

            _dataLock.EnterReadLock();
            try
            {
                return !_dataPoints.Any();
            }
            finally
            {
                _dataLock.ExitReadLock();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _accessLock?.Dispose();
            _dataLock?.Dispose();
            _dataPoints.Clear();
        }
    }
} 