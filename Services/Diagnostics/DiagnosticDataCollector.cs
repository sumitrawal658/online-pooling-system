using System.Diagnostics;
using System.Runtime;
using Microsoft.Extensions.Hosting;
using System.Management;
using System.Runtime.InteropServices;

namespace PollSystem.Services.Diagnostics
{
    public class DiagnosticDataCollector : IHostedService
    {
        private readonly ILogger<DiagnosticDataCollector> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly Timer _collectionTimer;
        private readonly Process _currentProcess;
        private readonly PerformanceCounter _cpuCounter;
        private readonly string _machineName;

        public DiagnosticDataCollector(
            ILogger<DiagnosticDataCollector> logger,
            IMetricsCollector metricsCollector)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _currentProcess = Process.GetCurrentProcess();
            _machineName = Environment.MachineName;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            }

            _collectionTimer = new Timer(CollectDiagnosticData, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        private void CollectDiagnosticData(object state)
        {
            try
            {
                CollectProcessInformation();
                CollectGCStatistics();
                CollectSystemInformation();
                CollectResourceUtilization();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting diagnostic data");
            }
        }

        private void CollectProcessInformation()
        {
            var processInfo = new Dictionary<string, object>
            {
                ["processId"] = _currentProcess.Id,
                ["processName"] = _currentProcess.ProcessName,
                ["startTime"] = _currentProcess.StartTime,
                ["threadCount"] = _currentProcess.Threads.Count,
                ["handleCount"] = _currentProcess.HandleCount,
                ["workingSet"] = _currentProcess.WorkingSet64,
                ["privateMemory"] = _currentProcess.PrivateMemorySize64,
                ["virtualMemory"] = _currentProcess.VirtualMemorySize64,
                ["privilegedProcessorTime"] = _currentProcess.PrivilegedProcessorTime.TotalMilliseconds,
                ["userProcessorTime"] = _currentProcess.UserProcessorTime.TotalMilliseconds,
                ["totalProcessorTime"] = _currentProcess.TotalProcessorTime.TotalMilliseconds
            };

            foreach (var (key, value) in processInfo)
            {
                _metricsCollector.RecordMetricWithTags(
                    $"process.{key}",
                    Convert.ToDouble(value),
                    new Dictionary<string, object>
                    {
                        ["machine"] = _machineName,
                        ["process"] = _currentProcess.ProcessName
                    });
            }
        }

        private void CollectGCStatistics()
        {
            var gcInfo = GC.GetGCMemoryInfo();
            var gcStats = new Dictionary<string, object>
            {
                ["heapSize"] = gcInfo.HeapSizeBytes,
                ["memoryLoad"] = gcInfo.MemoryLoadBytes,
                ["totalMemory"] = GC.GetTotalMemory(false),
                ["gen0Collections"] = GC.CollectionCount(0),
                ["gen1Collections"] = GC.CollectionCount(1),
                ["gen2Collections"] = GC.CollectionCount(2),
                ["totalPauseDuration"] = gcInfo.PauseDurations.Sum(),
                ["fragmentedBytes"] = gcInfo.FragmentedBytes,
                ["promotedBytes"] = gcInfo.PromotedBytes,
                ["pinnedObjectsCount"] = gcInfo.PinnedObjectsCount
            };

            foreach (var (key, value) in gcStats)
            {
                _metricsCollector.RecordMetricWithTags(
                    $"gc.{key}",
                    Convert.ToDouble(value),
                    new Dictionary<string, object>
                    {
                        ["machine"] = _machineName,
                        ["process"] = _currentProcess.ProcessName
                    });
            }
        }

        private void CollectSystemInformation()
        {
            var systemInfo = new Dictionary<string, object>
            {
                ["processorCount"] = Environment.ProcessorCount,
                ["systemPageSize"] = Environment.SystemPageSize,
                ["is64BitProcess"] = Environment.Is64BitProcess,
                ["is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem,
                ["osVersion"] = Environment.OSVersion.Version.ToString(),
                ["workingSet"] = Environment.WorkingSet,
                ["systemMemoryTotal"] = GetTotalPhysicalMemory()
            };

            foreach (var (key, value) in systemInfo)
            {
                _metricsCollector.RecordMetricWithTags(
                    $"system.{key}",
                    value is bool b ? (b ? 1 : 0) : Convert.ToDouble(value),
                    new Dictionary<string, object>
                    {
                        ["machine"] = _machineName,
                        ["os"] = RuntimeInformation.OSDescription
                    });
            }
        }

        private void CollectResourceUtilization()
        {
            var resourceInfo = new Dictionary<string, object>();

            // CPU Usage
            if (_cpuCounter != null)
            {
                resourceInfo["cpuUsage"] = _cpuCounter.NextValue();
            }

            // Memory Usage
            var memoryInfo = GC.GetGCMemoryInfo();
            var totalMemory = GetTotalPhysicalMemory();
            resourceInfo["memoryUsagePercent"] = (double)memoryInfo.MemoryLoadBytes / totalMemory * 100;

            // Disk Usage
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var freeSpacePercent = (double)drive.TotalFreeSpace / drive.TotalSize * 100;
                resourceInfo[$"diskFreeSpace_{drive.Name.Replace(":\\", "")}"] = freeSpacePercent;
            }

            // Network Statistics
            var networkStats = GetNetworkStatistics();
            foreach (var (key, value) in networkStats)
            {
                resourceInfo[$"network_{key}"] = value;
            }

            foreach (var (key, value) in resourceInfo)
            {
                _metricsCollector.RecordMetricWithTags(
                    $"resource.{key}",
                    Convert.ToDouble(value),
                    new Dictionary<string, object>
                    {
                        ["machine"] = _machineName,
                        ["timestamp"] = DateTime.UtcNow.ToString("o")
                    });
            }
        }

        private Dictionary<string, long> GetNetworkStatistics()
        {
            var stats = new Dictionary<string, long>();
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up);

            foreach (var ni in networkInterfaces)
            {
                var statistics = ni.GetIPv4Statistics();
                stats[$"{ni.Name}_bytesSent"] = statistics.BytesSent;
                stats[$"{ni.Name}_bytesReceived"] = statistics.BytesReceived;
                stats[$"{ni.Name}_errorsSent"] = statistics.OutboundErrors;
                stats[$"{ni.Name}_errorsReceived"] = statistics.InboundErrors;
            }

            return stats;
        }

        private long GetTotalPhysicalMemory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var computerInfo = new ComputerInfo();
                    return (long)computerInfo.TotalPhysicalMemory;
                }
                else
                {
                    // For Unix-like systems, read from /proc/meminfo
                    var memInfo = File.ReadAllText("/proc/meminfo");
                    var match = System.Text.RegularExpressions.Regex.Match(memInfo, @"MemTotal:\s+(\d+)");
                    if (match.Success)
                    {
                        return long.Parse(match.Groups[1].Value) * 1024; // Convert KB to bytes
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting total physical memory");
            }

            return 0;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Diagnostic Data Collector started");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Diagnostic Data Collector stopped");
            _collectionTimer?.Change(Timeout.Infinite, 0);
            _cpuCounter?.Dispose();
            return Task.CompletedTask;
        }
    }
} 