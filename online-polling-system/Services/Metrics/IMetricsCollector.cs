using System;
using System.Collections.Generic;

namespace PollSystem.Services.Metrics
{
    public interface IMetricsCollector
    {
        void RecordOperationDuration(string operationName, TimeSpan duration);
        void IncrementOperationCounter(string operationName, bool success);
        void RecordSlowOperation(SlowOperationData data);
        void RecordMetric(string name, double value);
        void RecordMetricWithTags(string name, double value, Dictionary<string, object> tags);
        IMetricsScope CreateMetricsScope(string scopeName);
        Dictionary<string, MetricSummary> GetMetricsSummary();
    }

    public interface IMetricsScope : IDisposable
    {
        void AddTag(string key, object value);
        void AddMetric(string name, double value);
    }

    public class SlowOperationData
    {
        public string OperationName { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public Dictionary<string, object> Context { get; set; }
    }

    public class MetricSummary
    {
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public double AverageValue { get; set; }
        public long SampleCount { get; set; }
        public DateTime LastUpdated { get; set; }
        public Dictionary<string, object> LastTags { get; set; }
    }
} 