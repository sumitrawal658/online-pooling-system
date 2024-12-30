using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PollSystem.Services.Diagnostics
{
    public class HealthMonitoringService : IHostedService, IDisposable
    {
        private readonly ILogger<HealthMonitoringService> _logger;
        private readonly IDeviceDiagnostics _deviceDiagnostics;
        private readonly IMetricsCollector _metricsCollector;
        private readonly HealthMonitoringOptions _options;
        private readonly ConcurrentDictionary<string, MetricHistory> _metricHistory;
        private readonly ConcurrentDictionary<string, DateTime> _lastAlertTime;
        private Timer _monitoringTimer;
        private Timer _cleanupTimer;

        public event EventHandler<HealthStatusChangedEventArgs> OnHealthStatusChanged;

        public HealthMonitoringService(
            ILogger<HealthMonitoringService> logger,
            IDeviceDiagnostics deviceDiagnostics,
            IMetricsCollector metricsCollector,
            IOptions<HealthMonitoringOptions> options)
        {
            _logger = logger;
            _deviceDiagnostics = deviceDiagnostics;
            _metricsCollector = metricsCollector;
            _options = options.Value;
            _metricHistory = new ConcurrentDictionary<string, MetricHistory>();
            _lastAlertTime = new ConcurrentDictionary<string, DateTime>();

            // Subscribe to device alerts
            _deviceDiagnostics.OnResourceAlert += HandleResourceAlert;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Monitor health every minute
            _monitoringTimer = new Timer(
                MonitorHealth,
                null,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1));

            // Clean up old metrics every hour
            _cleanupTimer = new Timer(
                CleanupOldMetrics,
                null,
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1));

            return Task.CompletedTask;
        }

        private async void MonitorHealth(object state)
        {
            try
            {
                var report = await _deviceDiagnostics.GenerateHealthReportAsync();
                var analysis = AnalyzeHealth(report);
                UpdateMetricHistory(report);
                ProcessHealthAnalysis(analysis);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring health");
            }
        }

        private HealthAnalysis AnalyzeHealth(DeviceHealthReport report)
        {
            var analysis = new HealthAnalysis
            {
                Timestamp = DateTime.UtcNow,
                Issues = new List<HealthIssue>(),
                Metrics = new Dictionary<string, MetricStatus>()
            };

            // Analyze CPU
            AnalyzeMetric(analysis, "CPU", report.CurrentMetrics.CpuUsagePercent,
                _options.CpuWarningThreshold, _options.CpuCriticalThreshold);

            // Analyze Memory
            var memoryUsagePercent = (double)report.CurrentMetrics.MemoryUsageBytes /
                (report.CurrentMetrics.MemoryUsageBytes + report.CurrentMetrics.AvailableMemoryBytes) * 100;
            AnalyzeMetric(analysis, "Memory", memoryUsagePercent,
                _options.MemoryWarningThreshold, _options.MemoryCriticalThreshold);

            // Analyze Disk
            AnalyzeMetric(analysis, "Disk", report.CurrentMetrics.DiskUsagePercent,
                _options.DiskWarningThreshold, _options.DiskCriticalThreshold);

            // Analyze Thread Pool
            ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);
            var threadPoolUsage = 100 * (1 - (double)workerThreads / maxWorkerThreads);
            AnalyzeMetric(analysis, "ThreadPool", threadPoolUsage,
                _options.ThreadPoolWarningThreshold, _options.ThreadPoolCriticalThreshold);

            // Analyze GC Pressure
            var gcPressure = CalculateGCPressure(report);
            AnalyzeMetric(analysis, "GC", gcPressure,
                _options.GCPressureWarningThreshold, _options.GCPressureCriticalThreshold);

            return analysis;
        }

        private void AnalyzeMetric(HealthAnalysis analysis, string metricName, double value,
            double warningThreshold, double criticalThreshold)
        {
            var status = new MetricStatus
            {
                Name = metricName,
                Value = value,
                Threshold = warningThreshold,
                Status = GetHealthStatusForValue(value, warningThreshold, criticalThreshold)
            };

            analysis.Metrics[metricName] = status;

            if (status.Status != HealthStatus.Healthy)
            {
                analysis.Issues.Add(new HealthIssue
                {
                    Component = metricName,
                    Severity = status.Status == HealthStatus.Critical ? 
                        AlertSeverity.Critical : AlertSeverity.Warning,
                    Message = $"{metricName} usage is {value:F1}% (Threshold: {warningThreshold}%)",
                    Timestamp = DateTime.UtcNow
                });
            }

            // Check for trends
            if (TryGetMetricHistory(metricName, out var history))
            {
                var trend = AnalyzeTrend(history);
                if (trend.IsSignificant)
                {
                    analysis.Issues.Add(new HealthIssue
                    {
                        Component = metricName,
                        Severity = AlertSeverity.Warning,
                        Message = $"{metricName} shows {trend.Direction} trend: {trend.ChangeRate:F1}% per hour",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }

        private void UpdateMetricHistory(DeviceHealthReport report)
        {
            UpdateMetricValue("CPU", report.CurrentMetrics.CpuUsagePercent);
            
            var memoryUsagePercent = (double)report.CurrentMetrics.MemoryUsageBytes /
                (report.CurrentMetrics.MemoryUsageBytes + report.CurrentMetrics.AvailableMemoryBytes) * 100;
            UpdateMetricValue("Memory", memoryUsagePercent);
            
            UpdateMetricValue("Disk", report.CurrentMetrics.DiskUsagePercent);
            
            ThreadPool.GetAvailableThreads(out int workerThreads, out int _);
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int _);
            var threadPoolUsage = 100 * (1 - (double)workerThreads / maxWorkerThreads);
            UpdateMetricValue("ThreadPool", threadPoolUsage);
            
            var gcPressure = CalculateGCPressure(report);
            UpdateMetricValue("GC", gcPressure);
        }

        private void UpdateMetricValue(string metricName, double value)
        {
            var history = _metricHistory.GetOrAdd(metricName, _ => new MetricHistory());
            history.AddValue(value);
        }

        private void ProcessHealthAnalysis(HealthAnalysis analysis)
        {
            foreach (var issue in analysis.Issues)
            {
                // Check alert throttling
                if (ShouldThrottleAlert(issue.Component))
                    continue;

                // Record the alert
                _lastAlertTime[issue.Component] = DateTime.UtcNow;

                // Log the issue
                var logLevel = issue.Severity == AlertSeverity.Critical ? 
                    LogLevel.Error : LogLevel.Warning;
                _logger.Log(logLevel, issue.Message);

                // Record metric
                _metricsCollector.RecordMetricWithTags(
                    $"health.{issue.Component.ToLower()}.status",
                    (int)analysis.Metrics[issue.Component].Status,
                    new Dictionary<string, object>
                    {
                        ["component"] = issue.Component,
                        ["severity"] = issue.Severity.ToString()
                    });
            }

            // Notify status changes
            OnHealthStatusChanged?.Invoke(this, new HealthStatusChangedEventArgs
            {
                Analysis = analysis,
                Timestamp = DateTime.UtcNow
            });
        }

        private bool ShouldThrottleAlert(string component)
        {
            if (!_lastAlertTime.TryGetValue(component, out var lastAlert))
                return false;

            return DateTime.UtcNow - lastAlert < _options.AlertThrottleInterval;
        }

        private double CalculateGCPressure(DeviceHealthReport report)
        {
            var gen2Collections = report.CurrentMetrics.CustomMetrics["gen2Collections"];
            var totalMemory = report.CurrentMetrics.CustomMetrics["totalMemory"];
            var availableMemory = report.CurrentMetrics.AvailableMemoryBytes;

            // Calculate GC pressure based on Gen2 collections and memory usage
            return (gen2Collections * 10) + (totalMemory / (double)availableMemory * 100);
        }

        private (bool IsSignificant, string Direction, double ChangeRate) AnalyzeTrend(MetricHistory history)
        {
            var values = history.GetRecentValues(TimeSpan.FromHours(1));
            if (values.Count < 2)
                return (false, string.Empty, 0);

            var firstValue = values.First();
            var lastValue = values.Last();
            var changeRate = (lastValue - firstValue) / values.Count * 60; // Change per hour

            if (Math.Abs(changeRate) > _options.TrendSignificanceThreshold)
            {
                return (true, changeRate > 0 ? "increasing" : "decreasing", Math.Abs(changeRate));
            }

            return (false, string.Empty, 0);
        }

        private void CleanupOldMetrics(object state)
        {
            try
            {
                var cutoff = DateTime.UtcNow - _options.MetricRetentionPeriod;
                foreach (var history in _metricHistory.Values)
                {
                    history.RemoveOlderThan(cutoff);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old metrics");
            }
        }

        private static HealthStatus GetHealthStatusForValue(double value, double warningThreshold, double criticalThreshold)
        {
            if (value >= criticalThreshold)
                return HealthStatus.Critical;
            if (value >= warningThreshold)
                return HealthStatus.Degraded;
            return HealthStatus.Healthy;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _monitoringTimer?.Change(Timeout.Infinite, 0);
            _cleanupTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _monitoringTimer?.Dispose();
            _cleanupTimer?.Dispose();
            _deviceDiagnostics.OnResourceAlert -= HandleResourceAlert;
        }

        private void HandleResourceAlert(object sender, DeviceAlertEventArgs e)
        {
            // Process device-level alerts
            _logger.LogWarning("Resource alert received: {ResourceType} - {Message}",
                e.Alert.ResourceType, e.Alert.Message);
        }
    }

    public class HealthMonitoringOptions
    {
        public double CpuWarningThreshold { get; set; } = 80;
        public double CpuCriticalThreshold { get; set; } = 90;
        public double MemoryWarningThreshold { get; set; } = 85;
        public double MemoryCriticalThreshold { get; set; } = 95;
        public double DiskWarningThreshold { get; set; } = 85;
        public double DiskCriticalThreshold { get; set; } = 95;
        public double ThreadPoolWarningThreshold { get; set; } = 80;
        public double ThreadPoolCriticalThreshold { get; set; } = 90;
        public double GCPressureWarningThreshold { get; set; } = 70;
        public double GCPressureCriticalThreshold { get; set; } = 90;
        public TimeSpan AlertThrottleInterval { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan MetricRetentionPeriod { get; set; } = TimeSpan.FromDays(7);
        public double TrendSignificanceThreshold { get; set; } = 10;
    }

    public class MetricHistory
    {
        private readonly ConcurrentQueue<(DateTime Timestamp, double Value)> _values;
        private readonly object _lock = new();

        public MetricHistory()
        {
            _values = new ConcurrentQueue<(DateTime, double)>();
        }

        public void AddValue(double value)
        {
            _values.Enqueue((DateTime.UtcNow, value));
        }

        public List<double> GetRecentValues(TimeSpan duration)
        {
            var cutoff = DateTime.UtcNow - duration;
            return _values
                .Where(v => v.Timestamp >= cutoff)
                .Select(v => v.Value)
                .ToList();
        }

        public void RemoveOlderThan(DateTime cutoff)
        {
            while (_values.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                _values.TryDequeue(out _);
            }
        }
    }

    public class HealthAnalysis
    {
        public DateTime Timestamp { get; set; }
        public List<HealthIssue> Issues { get; set; }
        public Dictionary<string, MetricStatus> Metrics { get; set; }
    }

    public class HealthIssue
    {
        public string Component { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class MetricStatus
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public double Threshold { get; set; }
        public HealthStatus Status { get; set; }
    }

    public class HealthStatusChangedEventArgs : EventArgs
    {
        public HealthAnalysis Analysis { get; set; }
        public DateTime Timestamp { get; set; }
    }
} 