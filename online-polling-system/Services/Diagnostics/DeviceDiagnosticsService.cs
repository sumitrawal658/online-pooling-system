using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace PollSystem.Services.Diagnostics
{
    public class DeviceDiagnosticsService : IDeviceDiagnostics, IHostedService, IDisposable
    {
        private readonly ILogger<DeviceDiagnosticsService> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly ConcurrentQueue<ResourceAlert> _recentAlerts;
        private readonly Process _currentProcess;
        private Timer _monitoringTimer;
        private readonly object _lockObject = new();
        private bool _isMonitoring;
        private const int MaxAlerts = 100;
        private const double CpuWarningThreshold = 80;
        private const double MemoryWarningThreshold = 85;
        private const double DiskWarningThreshold = 90;

        public event EventHandler<DeviceAlertEventArgs> OnResourceAlert;

        public DeviceDiagnosticsService(
            ILogger<DeviceDiagnosticsService> logger,
            IMetricsCollector metricsCollector)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _recentAlerts = new ConcurrentQueue<ResourceAlert>();
            _currentProcess = Process.GetCurrentProcess();
        }

        public DeviceMetrics GetCurrentMetrics()
        {
            var metrics = new DeviceMetrics();

            try
            {
                // CPU Usage
                metrics.CpuUsagePercent = GetCpuUsage();

                // Memory Usage
                var memoryInfo = GC.GetGCMemoryInfo();
                metrics.MemoryUsageBytes = _currentProcess.WorkingSet64;
                metrics.AvailableMemoryBytes = memoryInfo.TotalAvailableMemoryBytes;

                // Disk Usage
                var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                metrics.DiskUsagePercent = 100 * (1 - (double)systemDrive.AvailableFreeSpace / systemDrive.TotalSize);

                // Thread Count
                metrics.ThreadCount = _currentProcess.Threads.Count;

                // System Uptime
                metrics.SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

                // Active Connections (if applicable)
                metrics.ActiveConnections = GetActiveConnections();

                // Add custom metrics
                metrics.CustomMetrics["gcCollections"] = GC.CollectionCount(0);
                metrics.CustomMetrics["handleCount"] = _currentProcess.HandleCount;
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    metrics.CustomMetrics["pagedMemory"] = _currentProcess.PagedMemorySize64;
                }

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting device metrics");
                return new DeviceMetrics();
            }
        }

        public async Task<DeviceHealthReport> GenerateHealthReportAsync()
        {
            var metrics = GetCurrentMetrics();
            var report = new DeviceHealthReport
            {
                Timestamp = DateTime.UtcNow,
                CurrentMetrics = metrics,
                ComponentStatus = new Dictionary<string, HealthStatus>(),
                RecentAlerts = _recentAlerts.ToList(),
                DiagnosticData = new Dictionary<string, object>()
            };

            // Assess CPU health
            report.ComponentStatus["CPU"] = GetHealthStatus(
                metrics.CpuUsagePercent,
                CpuWarningThreshold,
                95);

            // Assess Memory health
            var memoryUsagePercent = 100.0 * metrics.MemoryUsageBytes / (metrics.MemoryUsageBytes + metrics.AvailableMemoryBytes);
            report.ComponentStatus["Memory"] = GetHealthStatus(
                memoryUsagePercent,
                MemoryWarningThreshold,
                95);

            // Assess Disk health
            report.ComponentStatus["Disk"] = GetHealthStatus(
                metrics.DiskUsagePercent,
                DiskWarningThreshold,
                95);

            // Add diagnostic data
            report.DiagnosticData["processStartTime"] = _currentProcess.StartTime;
            report.DiagnosticData["totalProcessorTime"] = _currentProcess.TotalProcessorTime;
            report.DiagnosticData["gcServerMode"] = GCSettings.IsServerGC;
            report.DiagnosticData["operatingSystem"] = RuntimeInformation.OSDescription;
            report.DiagnosticData["processorCount"] = Environment.ProcessorCount;

            return report;
        }

        public void StartMonitoring(TimeSpan interval)
        {
            lock (_lockObject)
            {
                if (_isMonitoring)
                    return;

                _monitoringTimer = new Timer(MonitoringCallback, null, TimeSpan.Zero, interval);
                _isMonitoring = true;
                _logger.LogInformation("Device monitoring started with interval: {Interval}", interval);
            }
        }

        public void StopMonitoring()
        {
            lock (_lockObject)
            {
                if (!_isMonitoring)
                    return;

                _monitoringTimer?.Change(Timeout.Infinite, 0);
                _isMonitoring = false;
                _logger.LogInformation("Device monitoring stopped");
            }
        }

        private void MonitoringCallback(object state)
        {
            try
            {
                var metrics = GetCurrentMetrics();

                // Check thresholds and raise alerts
                if (metrics.CpuUsagePercent > CpuWarningThreshold)
                {
                    RaiseResourceAlert("CPU", $"High CPU usage: {metrics.CpuUsagePercent:F1}%",
                        metrics.CpuUsagePercent > 95 ? AlertSeverity.Critical : AlertSeverity.Warning,
                        new Dictionary<string, object> { ["cpuUsage"] = metrics.CpuUsagePercent });
                }

                var memoryUsagePercent = 100.0 * metrics.MemoryUsageBytes / (metrics.MemoryUsageBytes + metrics.AvailableMemoryBytes);
                if (memoryUsagePercent > MemoryWarningThreshold)
                {
                    RaiseResourceAlert("Memory", $"High memory usage: {memoryUsagePercent:F1}%",
                        memoryUsagePercent > 95 ? AlertSeverity.Critical : AlertSeverity.Warning,
                        new Dictionary<string, object> { ["memoryUsage"] = memoryUsagePercent });
                }

                if (metrics.DiskUsagePercent > DiskWarningThreshold)
                {
                    RaiseResourceAlert("Disk", $"High disk usage: {metrics.DiskUsagePercent:F1}%",
                        metrics.DiskUsagePercent > 95 ? AlertSeverity.Critical : AlertSeverity.Warning,
                        new Dictionary<string, object> { ["diskUsage"] = metrics.DiskUsagePercent });
                }

                // Record metrics
                _metricsCollector.RecordMetric("device.cpu.usage", metrics.CpuUsagePercent);
                _metricsCollector.RecordMetric("device.memory.usage", metrics.MemoryUsageBytes);
                _metricsCollector.RecordMetric("device.disk.usage", metrics.DiskUsagePercent);
                _metricsCollector.RecordMetric("device.threads", metrics.ThreadCount);
                _metricsCollector.RecordMetric("device.connections", metrics.ActiveConnections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in monitoring callback");
            }
        }

        private void RaiseResourceAlert(string resourceType, string message, AlertSeverity severity, Dictionary<string, object> context)
        {
            var alert = new ResourceAlert
            {
                ResourceType = resourceType,
                Message = message,
                Severity = severity,
                Timestamp = DateTime.UtcNow,
                Context = context
            };

            _recentAlerts.Enqueue(alert);
            while (_recentAlerts.Count > MaxAlerts)
            {
                _recentAlerts.TryDequeue(out _);
            }

            OnResourceAlert?.Invoke(this, new DeviceAlertEventArgs { Alert = alert });
            _logger.LogWarning("Resource alert: {ResourceType} - {Message}", resourceType, message);
        }

        private double GetCpuUsage()
        {
            // This is a simplified implementation. For more accurate CPU measurement,
            // you might want to use platform-specific APIs or performance counters
            var startTime = _currentProcess.TotalProcessorTime;
            Thread.Sleep(100); // Sample for 100ms
            var endTime = _currentProcess.TotalProcessorTime;
            var elapsedCpu = (endTime - startTime).TotalMilliseconds;
            return (elapsedCpu / (Environment.ProcessorCount * 100.0)) * 100;
        }

        private int GetActiveConnections()
        {
            try
            {
                // This is a placeholder. Implement actual connection counting based on your needs
                // For example, you might want to track SignalR connections or HTTP connections
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static HealthStatus GetHealthStatus(double value, double warningThreshold, double criticalThreshold)
        {
            if (value >= criticalThreshold)
                return HealthStatus.Critical;
            if (value >= warningThreshold)
                return HealthStatus.Degraded;
            return HealthStatus.Healthy;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartMonitoring(TimeSpan.FromMinutes(1));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopMonitoring();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _monitoringTimer?.Dispose();
            _currentProcess?.Dispose();
        }
    }
} 