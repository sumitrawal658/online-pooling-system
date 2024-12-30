using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using MathNet.Numerics.Statistics;

namespace PollSystem.Services.Diagnostics
{
    public class MetricsAnalysisService : IHostedService, IDisposable
    {
        private readonly ILogger<MetricsAnalysisService> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly MetricsAnalysisOptions _options;
        private readonly ConcurrentDictionary<string, MetricAnalyzer> _analyzers;
        private Timer _analysisTimer;

        public event EventHandler<MetricAnomalyEventArgs> OnAnomalyDetected;
        public event EventHandler<MetricTrendEventArgs> OnTrendDetected;

        public MetricsAnalysisService(
            ILogger<MetricsAnalysisService> logger,
            IMetricsCollector metricsCollector,
            IOptions<MetricsAnalysisOptions> options)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _options = options.Value;
            _analyzers = new ConcurrentDictionary<string, MetricAnalyzer>();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _analysisTimer = new Timer(
                AnalyzeMetrics,
                null,
                TimeSpan.Zero,
                _options.AnalysisInterval);

            return Task.CompletedTask;
        }

        public void RecordMetric(string name, double value, Dictionary<string, object> tags = null)
        {
            var analyzer = _analyzers.GetOrAdd(name, _ => new MetricAnalyzer(_options));
            analyzer.AddSample(new MetricSample(value, DateTime.UtcNow, tags));
        }

        private void AnalyzeMetrics(object state)
        {
            foreach (var analyzer in _analyzers)
            {
                try
                {
                    var analysis = analyzer.Value.Analyze();
                    ProcessAnalysis(analyzer.Key, analysis);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error analyzing metric: {MetricName}", analyzer.Key);
                }
            }
        }

        private void ProcessAnalysis(string metricName, MetricAnalysis analysis)
        {
            // Record statistical summaries
            RecordStatistics(metricName, analysis.Statistics);

            // Check for anomalies
            if (analysis.Anomalies.Any())
            {
                foreach (var anomaly in analysis.Anomalies)
                {
                    OnAnomalyDetected?.Invoke(this, new MetricAnomalyEventArgs
                    {
                        MetricName = metricName,
                        Anomaly = anomaly
                    });

                    _logger.LogWarning(
                        "Anomaly detected in {MetricName}: {AnomalyType} - Value: {Value}, Threshold: {Threshold}",
                        metricName, anomaly.Type, anomaly.Value, anomaly.Threshold);
                }
            }

            // Check for trends
            if (analysis.Trend.IsSignificant)
            {
                OnTrendDetected?.Invoke(this, new MetricTrendEventArgs
                {
                    MetricName = metricName,
                    Trend = analysis.Trend
                });

                _logger.LogInformation(
                    "Trend detected in {MetricName}: {Direction} trend with slope {Slope:F2} per hour",
                    metricName, analysis.Trend.Direction, analysis.Trend.Slope);
            }
        }

        private void RecordStatistics(string metricName, MetricStatistics stats)
        {
            var tags = new Dictionary<string, object>
            {
                ["metric"] = metricName,
                ["window"] = _options.AnalysisWindow.TotalMinutes
            };

            _metricsCollector.RecordMetricWithTags($"{metricName}.mean", stats.Mean, tags);
            _metricsCollector.RecordMetricWithTags($"{metricName}.stddev", stats.StandardDeviation, tags);
            _metricsCollector.RecordMetricWithTags($"{metricName}.min", stats.Min, tags);
            _metricsCollector.RecordMetricWithTags($"{metricName}.max", stats.Max, tags);
            _metricsCollector.RecordMetricWithTags($"{metricName}.p95", stats.Percentile95, tags);
        }

        public MetricSummary GetMetricSummary(string metricName)
        {
            if (_analyzers.TryGetValue(metricName, out var analyzer))
            {
                return analyzer.GetSummary();
            }

            return null;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _analysisTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _analysisTimer?.Dispose();
        }
    }

    public class MetricAnalyzer
    {
        private readonly ConcurrentQueue<MetricSample> _samples;
        private readonly MetricsAnalysisOptions _options;
        private readonly object _lock = new();

        public MetricAnalyzer(MetricsAnalysisOptions options)
        {
            _samples = new ConcurrentQueue<MetricSample>();
            _options = options;
        }

        public void AddSample(MetricSample sample)
        {
            _samples.Enqueue(sample);
            CleanupOldSamples();
        }

        public MetricAnalysis Analyze()
        {
            var samples = GetRecentSamples();
            if (!samples.Any())
                return new MetricAnalysis();

            var values = samples.Select(s => s.Value).ToList();
            var timestamps = samples.Select(s => s.Timestamp).ToList();

            var analysis = new MetricAnalysis
            {
                Statistics = CalculateStatistics(values),
                Trend = AnalyzeTrend(timestamps, values),
                Anomalies = DetectAnomalies(values, timestamps)
            };

            return analysis;
        }

        private MetricStatistics CalculateStatistics(List<double> values)
        {
            return new MetricStatistics
            {
                Mean = values.Mean(),
                Median = values.Median(),
                StandardDeviation = values.StandardDeviation(),
                Min = values.Min(),
                Max = values.Max(),
                Percentile95 = values.Percentile(95),
                SampleCount = values.Count,
                Skewness = values.Skewness(),
                Kurtosis = values.Kurtosis()
            };
        }

        private MetricTrend AnalyzeTrend(List<DateTime> timestamps, List<double> values)
        {
            if (values.Count < _options.MinimumSamplesForTrend)
                return new MetricTrend { IsSignificant = false };

            // Convert timestamps to hours from start
            var hours = timestamps.Select(t => (t - timestamps.First()).TotalHours).ToList();

            // Calculate linear regression
            var correlation = Correlation.Pearson(hours.ToArray(), values.ToArray());
            var slope = CalculateSlope(hours, values);

            return new MetricTrend
            {
                IsSignificant = Math.Abs(correlation) > _options.TrendSignificanceThreshold,
                Direction = slope > 0 ? TrendDirection.Increasing : TrendDirection.Decreasing,
                Slope = slope,
                Correlation = correlation
            };
        }

        private List<MetricAnomaly> DetectAnomalies(List<double> values, List<DateTime> timestamps)
        {
            var anomalies = new List<MetricAnomaly>();

            if (values.Count < _options.MinimumSamplesForAnomaly)
                return anomalies;

            var stats = CalculateStatistics(values);
            var upperBound = stats.Mean + (_options.AnomalyThreshold * stats.StandardDeviation);
            var lowerBound = stats.Mean - (_options.AnomalyThreshold * stats.StandardDeviation);

            for (int i = 0; i < values.Count; i++)
            {
                var value = values[i];
                if (value > upperBound)
                {
                    anomalies.Add(new MetricAnomaly
                    {
                        Type = AnomalyType.Spike,
                        Value = value,
                        Threshold = upperBound,
                        Timestamp = timestamps[i],
                        Deviation = (value - stats.Mean) / stats.StandardDeviation
                    });
                }
                else if (value < lowerBound)
                {
                    anomalies.Add(new MetricAnomaly
                    {
                        Type = AnomalyType.Drop,
                        Value = value,
                        Threshold = lowerBound,
                        Timestamp = timestamps[i],
                        Deviation = (stats.Mean - value) / stats.StandardDeviation
                    });
                }
            }

            return anomalies;
        }

        private double CalculateSlope(List<double> x, List<double> y)
        {
            var meanX = x.Mean();
            var meanY = y.Mean();
            var numerator = 0.0;
            var denominator = 0.0;

            for (int i = 0; i < x.Count; i++)
            {
                numerator += (x[i] - meanX) * (y[i] - meanY);
                denominator += Math.Pow(x[i] - meanX, 2);
            }

            return denominator == 0 ? 0 : numerator / denominator;
        }

        private List<MetricSample> GetRecentSamples()
        {
            var cutoff = DateTime.UtcNow - _options.AnalysisWindow;
            return _samples.Where(s => s.Timestamp >= cutoff).ToList();
        }

        private void CleanupOldSamples()
        {
            var cutoff = DateTime.UtcNow - _options.RetentionPeriod;
            while (_samples.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                _samples.TryDequeue(out _);
            }
        }

        public MetricSummary GetSummary()
        {
            var analysis = Analyze();
            return new MetricSummary
            {
                Statistics = analysis.Statistics,
                RecentTrend = analysis.Trend,
                RecentAnomalies = analysis.Anomalies,
                LastUpdated = _samples.LastOrDefault()?.Timestamp ?? DateTime.UtcNow
            };
        }
    }

    public class MetricsAnalysisOptions
    {
        public TimeSpan AnalysisInterval { get; set; } = TimeSpan.FromMinutes(1);
        public TimeSpan AnalysisWindow { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
        public double AnomalyThreshold { get; set; } = 3.0; // Number of standard deviations
        public double TrendSignificanceThreshold { get; set; } = 0.7; // Correlation coefficient threshold
        public int MinimumSamplesForTrend { get; set; } = 10;
        public int MinimumSamplesForAnomaly { get; set; } = 30;
    }

    public class MetricSample
    {
        public double Value { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Tags { get; set; }

        public MetricSample(double value, DateTime timestamp, Dictionary<string, object> tags = null)
        {
            Value = value;
            Timestamp = timestamp;
            Tags = tags ?? new Dictionary<string, object>();
        }
    }

    public class MetricAnalysis
    {
        public MetricStatistics Statistics { get; set; } = new();
        public MetricTrend Trend { get; set; } = new();
        public List<MetricAnomaly> Anomalies { get; set; } = new();
    }

    public class MetricStatistics
    {
        public double Mean { get; set; }
        public double Median { get; set; }
        public double StandardDeviation { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Percentile95 { get; set; }
        public int SampleCount { get; set; }
        public double Skewness { get; set; }
        public double Kurtosis { get; set; }
    }

    public class MetricTrend
    {
        public bool IsSignificant { get; set; }
        public TrendDirection Direction { get; set; }
        public double Slope { get; set; }
        public double Correlation { get; set; }
    }

    public class MetricAnomaly
    {
        public AnomalyType Type { get; set; }
        public double Value { get; set; }
        public double Threshold { get; set; }
        public DateTime Timestamp { get; set; }
        public double Deviation { get; set; }
    }

    public class MetricSummary
    {
        public MetricStatistics Statistics { get; set; }
        public MetricTrend RecentTrend { get; set; }
        public List<MetricAnomaly> RecentAnomalies { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public enum TrendDirection
    {
        Increasing,
        Decreasing
    }

    public enum AnomalyType
    {
        Spike,
        Drop
    }

    public class MetricAnomalyEventArgs : EventArgs
    {
        public string MetricName { get; set; }
        public MetricAnomaly Anomaly { get; set; }
    }

    public class MetricTrendEventArgs : EventArgs
    {
        public string MetricName { get; set; }
        public MetricTrend Trend { get; set; }
    }
} 