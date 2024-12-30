using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace PollSystem.Services.Metrics
{
    public class MetricsCollector : IMetricsCollector
    {
        private readonly ILogger<MetricsCollector> _logger;
        private readonly ConcurrentDictionary<string, OperationMetrics> _operationMetrics;
        private readonly ConcurrentDictionary<string, MetricData> _metrics;
        private readonly ConcurrentQueue<SlowOperationData> _slowOperations;
        private const int MaxSlowOperations = 1000;

        public MetricsCollector(ILogger<MetricsCollector> logger)
        {
            _logger = logger;
            _operationMetrics = new ConcurrentDictionary<string, OperationMetrics>();
            _metrics = new ConcurrentDictionary<string, MetricData>();
            _slowOperations = new ConcurrentQueue<SlowOperationData>();
        }

        public void RecordOperationDuration(string operationName, TimeSpan duration)
        {
            var metrics = _operationMetrics.GetOrAdd(operationName, _ => new OperationMetrics());
            metrics.RecordDuration(duration);
        }

        public void IncrementOperationCounter(string operationName, bool success)
        {
            var metrics = _operationMetrics.GetOrAdd(operationName, _ => new OperationMetrics());
            if (success)
                metrics.IncrementSuccess();
            else
                metrics.IncrementFailure();
        }

        public void RecordMetric(string name, double value)
        {
            RecordMetricWithTags(name, value, null);
        }

        public void RecordMetricWithTags(string name, double value, Dictionary<string, object> tags)
        {
            var metric = _metrics.GetOrAdd(name, _ => new MetricData());
            metric.RecordValue(value, tags);

            // Log significant changes or threshold crossings
            var summary = metric.GetSummary();
            if (IsSignificantChange(value, summary.AverageValue))
            {
                _logger.LogInformation(
                    "Significant change in metric {MetricName}: Current={Value}, Avg={Average}",
                    name, value, summary.AverageValue);
            }
        }

        public void RecordSlowOperation(SlowOperationData data)
        {
            _slowOperations.Enqueue(data);

            // Keep queue size under control
            while (_slowOperations.Count > MaxSlowOperations)
            {
                _slowOperations.TryDequeue(out _);
            }

            _logger.LogWarning(
                "Slow operation detected: {OperationName} took {Duration}ms",
                data.OperationName,
                data.Duration.TotalMilliseconds);
        }

        public Dictionary<string, MetricSummary> GetMetricsSummary()
        {
            var summary = new Dictionary<string, MetricSummary>();
            
            foreach (var metric in _metrics)
            {
                summary[metric.Key] = metric.Value.GetSummary();
            }

            return summary;
        }

        public IMetricsScope CreateMetricsScope(string scopeName)
        {
            return new MetricsScope(this, scopeName, _logger);
        }

        private bool IsSignificantChange(double currentValue, double averageValue)
        {
            if (averageValue == 0)
                return currentValue != 0;

            var percentageChange = Math.Abs((currentValue - averageValue) / averageValue) * 100;
            return percentageChange > 20; // Consider 20% change as significant
        }

        private class MetricData
        {
            private readonly object _lock = new object();
            private double _minValue = double.MaxValue;
            private double _maxValue = double.MinValue;
            private double _sum;
            private long _count;
            private Dictionary<string, object> _lastTags;
            private DateTime _lastUpdated;

            public void RecordValue(double value, Dictionary<string, object> tags)
            {
                lock (_lock)
                {
                    _minValue = Math.Min(_minValue, value);
                    _maxValue = Math.Max(_maxValue, value);
                    _sum += value;
                    _count++;
                    _lastTags = tags;
                    _lastUpdated = DateTime.UtcNow;
                }
            }

            public MetricSummary GetSummary()
            {
                lock (_lock)
                {
                    return new MetricSummary
                    {
                        MinValue = _minValue,
                        MaxValue = _maxValue,
                        AverageValue = _count > 0 ? _sum / _count : 0,
                        SampleCount = _count,
                        LastUpdated = _lastUpdated,
                        LastTags = _lastTags
                    };
                }
            }
        }

        private class OperationMetrics
        {
            private long _totalOperations;
            private long _successfulOperations;
            private long _totalDurationTicks;
            private readonly object _lock = new object();

            public void RecordDuration(TimeSpan duration)
            {
                Interlocked.Add(ref _totalDurationTicks, duration.Ticks);
                Interlocked.Increment(ref _totalOperations);
            }

            public void IncrementSuccess()
            {
                Interlocked.Increment(ref _successfulOperations);
            }

            public void IncrementFailure()
            {
                Interlocked.Increment(ref _totalOperations);
            }

            public (long total, long successful, TimeSpan avgDuration) GetStats()
            {
                return (
                    _totalOperations,
                    _successfulOperations,
                    TimeSpan.FromTicks(_totalOperations > 0 
                        ? _totalDurationTicks / _totalOperations 
                        : 0)
                );
            }
        }
    }

    internal class MetricsScope : IMetricsScope
    {
        private readonly IMetricsCollector _collector;
        private readonly string _scopeName;
        private readonly ILogger _logger;
        private readonly Stopwatch _stopwatch;
        private readonly Dictionary<string, object> _tags;
        private readonly Dictionary<string, double> _metrics;
        private bool _disposed;

        public MetricsScope(IMetricsCollector collector, string scopeName, ILogger logger)
        {
            _collector = collector;
            _scopeName = scopeName;
            _logger = logger;
            _stopwatch = Stopwatch.StartNew();
            _tags = new Dictionary<string, object>();
            _metrics = new Dictionary<string, double>();
        }

        public void AddTag(string key, object value)
        {
            if (!_disposed)
            {
                _tags[key] = value;
            }
        }

        public void AddMetric(string name, double value)
        {
            if (!_disposed)
            {
                _metrics[name] = value;
                _collector.RecordMetricWithTags($"{_scopeName}.{name}", value, _tags);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _stopwatch.Stop();
            _collector.RecordOperationDuration(_scopeName, _stopwatch.Elapsed);

            // Log scope completion with all collected metrics
            var metricsData = new Dictionary<string, object>(_tags)
            {
                ["duration_ms"] = _stopwatch.Elapsed.TotalMilliseconds
            };

            foreach (var metric in _metrics)
            {
                metricsData[metric.Key] = metric.Value;
            }

            _logger.LogInformation(
                "Scope {ScopeName} completed: {@MetricsData}",
                _scopeName,
                metricsData);

            _disposed = true;
        }
    }
} 