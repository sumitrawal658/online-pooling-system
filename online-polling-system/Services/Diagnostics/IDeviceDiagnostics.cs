using System;
using System.Collections.Generic;

namespace PollSystem.Services.Diagnostics
{
    public interface IDeviceDiagnostics
    {
        DeviceMetrics GetCurrentMetrics();
        Task<DeviceHealthReport> GenerateHealthReportAsync();
        void StartMonitoring(TimeSpan interval);
        void StopMonitoring();
        event EventHandler<DeviceAlertEventArgs> OnResourceAlert;
    }

    public class DeviceMetrics
    {
        public double CpuUsagePercent { get; set; }
        public long MemoryUsageBytes { get; set; }
        public long AvailableMemoryBytes { get; set; }
        public double DiskUsagePercent { get; set; }
        public int ActiveConnections { get; set; }
        public int ThreadCount { get; set; }
        public TimeSpan SystemUptime { get; set; }
        public Dictionary<string, double> CustomMetrics { get; set; } = new();
    }

    public class DeviceHealthReport
    {
        public DateTime Timestamp { get; set; }
        public DeviceMetrics CurrentMetrics { get; set; }
        public Dictionary<string, HealthStatus> ComponentStatus { get; set; }
        public List<ResourceAlert> RecentAlerts { get; set; }
        public Dictionary<string, object> DiagnosticData { get; set; }
    }

    public class ResourceAlert
    {
        public string ResourceType { get; set; }
        public string Message { get; set; }
        public AlertSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Context { get; set; }
    }

    public class DeviceAlertEventArgs : EventArgs
    {
        public ResourceAlert Alert { get; set; }
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }

    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Critical,
        Unknown
    }
} 