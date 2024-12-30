using System.Collections.Concurrent;

namespace PollSystem.Services.Diagnostics
{
    public class MetricTimeSeriesData
    {
        private readonly string _metricName;
        private readonly ConcurrentQueue<MetricDataPoint> _dataPoints;
        private readonly ReaderWriterLockSlim _lock;
        private readonly int _maxDataPoints;
        private volatile int _currentCount;

        public MetricTimeSeriesData(
            string metricName,
            int maxDataPoints = 10000)
        {
            _metricName = metricName;
            _maxDataPoints = maxDataPoints;
            _dataPoints = new ConcurrentQueue<MetricDataPoint>();
            _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            _currentCount = 0;
        }

        public void AddDataPoint(MetricDataPoint point)
        {
            _lock.EnterWriteLock();
            try
            {
                // Ensure we don't exceed max capacity
                while (_currentCount >= _maxDataPoints)
                {
                    if (_dataPoints.TryDequeue(out _))
                    {
                        Interlocked.Decrement(ref _currentCount);
                    }
                }

                _dataPoints.Enqueue(point);
                Interlocked.Increment(ref _currentCount);

                // Cleanup old data points
                var cutoffTime = DateTime.UtcNow - TimeSpan.FromDays(7);
                while (_dataPoints.TryPeek(out var oldestPoint) &&
                       oldestPoint.Timestamp < cutoffTime)
                {
                    if (_dataPoints.TryDequeue(out _))
                    {
                        Interlocked.Decrement(ref _currentCount);
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public List<MetricDataPoint> GetDataPoints(TimeSpan window)
        {
            _lock.EnterReadLock();
            try
            {
                var cutoff = DateTime.UtcNow - window;
                return _dataPoints
                    .Where(p => p.Timestamp >= cutoff)
                    .OrderBy(p => p.Timestamp)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public List<MetricDataPoint> GetDataPointsInRange(DateTime start, DateTime end)
        {
            _lock.EnterReadLock();
            try
            {
                return _dataPoints
                    .Where(p => p.Timestamp >= start && p.Timestamp <= end)
                    .OrderBy(p => p.Timestamp)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public int GetDataPointCount()
        {
            return _currentCount;
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                while (_dataPoints.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref _currentCount);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            _lock?.Dispose();
        }

        public bool RemoveDataPoint(DateTime timestamp)
        {
            _lock.EnterWriteLock();
            try
            {
                var point = _dataPoints.FirstOrDefault(p => p.Timestamp == timestamp);
                if (point == null)
                    return false;

                var tempQueue = new ConcurrentQueue<MetricDataPoint>();
                var removed = false;

                while (_dataPoints.TryDequeue(out var currentPoint))
                {
                    if (currentPoint.Timestamp != timestamp)
                    {
                        tempQueue.Enqueue(currentPoint);
                    }
                    else
                    {
                        removed = true;
                        Interlocked.Decrement(ref _currentCount);
                    }
                }

                _dataPoints = tempQueue;
                return removed;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void RemoveDataPointsOlderThan(DateTime cutoffTime)
        {
            _lock.EnterWriteLock();
            try
            {
                var tempQueue = new ConcurrentQueue<MetricDataPoint>();
                var removedCount = 0;

                while (_dataPoints.TryDequeue(out var point))
                {
                    if (point.Timestamp >= cutoffTime)
                    {
                        tempQueue.Enqueue(point);
                    }
                    else
                    {
                        removedCount++;
                    }
                }

                _dataPoints = tempQueue;
                Interlocked.Add(ref _currentCount, -removedCount);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void CompactData(TimeSpan resolution)
        {
            _lock.EnterWriteLock();
            try
            {
                var groups = _dataPoints
                    .GroupBy(p => new DateTime(
                        p.Timestamp.Ticks - (p.Timestamp.Ticks % resolution.Ticks),
                        p.Timestamp.Kind))
                    .OrderBy(g => g.Key);

                var compacted = new ConcurrentQueue<MetricDataPoint>();
                foreach (var group in groups)
                {
                    var values = group.Select(p => p.Value).ToList();
                    var tags = group.SelectMany(p => p.Tags)
                        .GroupBy(kvp => kvp.Key)
                        .ToDictionary(g => g.Key, g => g.First().Value);

                    compacted.Enqueue(new MetricDataPoint(
                        group.Key,
                        values.Average(),
                        tags));
                }

                _dataPoints = compacted;
                _currentCount = compacted.Count;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public long EstimateMemoryUsage()
        {
            _lock.EnterReadLock();
            try
            {
                const int METADATA_SIZE = 48; // Approximate size of MetricDataPoint object overhead
                const int TIMESTAMP_SIZE = 8; // Size of DateTime
                const int DOUBLE_SIZE = 8;    // Size of double
                const int DICTIONARY_OVERHEAD = 32; // Approximate size of Dictionary overhead

                var sampleSize = Math.Min(100, _currentCount);
                if (sampleSize == 0) return 0;

                var sample = _dataPoints.Take(sampleSize).ToList();
                var totalSize = sample.Sum(p =>
                    METADATA_SIZE +
                    TIMESTAMP_SIZE +
                    DOUBLE_SIZE +
                    DICTIONARY_OVERHEAD +
                    (p.Tags?.Sum(t =>
                        System.Text.Encoding.UTF8.GetByteCount(t.Key) +
                        System.Text.Encoding.UTF8.GetByteCount(t.Value)) ?? 0));

                return (totalSize / sampleSize) * _currentCount;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public class MetricDataPoint
    {
        public DateTime Timestamp { get; init; }
        public double Value { get; init; }
        public Dictionary<string, string> Tags { get; init; }
        public MetricDataPoint(DateTime timestamp, double value, Dictionary<string, string> tags = null)
        {
            Timestamp = timestamp;
            Value = value;
            Tags = tags ?? new Dictionary<string, string>();
        }
    }
} 