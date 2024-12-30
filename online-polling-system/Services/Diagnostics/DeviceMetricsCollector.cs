using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using System.Diagnostics.Metrics;

namespace PollSystem.Services.Diagnostics
{
    public class DeviceMetricsCollector : IHostedService, IDisposable
    {
        private readonly ILogger<DeviceMetricsCollector> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly Process _currentProcess;
        private Timer _collectionTimer;
        private readonly PerformanceCounter _cpuCounter;
        private readonly ConcurrentDictionary<string, Meter> _meters;
        private readonly string _instanceName;
        private readonly object _lockObject = new();
        private DateTime _lastCpuCheck = DateTime.UtcNow;
        private TimeSpan _lastCpuTime = TimeSpan.Zero;

        public DeviceMetricsCollector(
            ILogger<DeviceMetricsCollector> logger,
            IMetricsCollector metricsCollector)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _currentProcess = Process.GetCurrentProcess();
            _instanceName = $"PollSystem_{_currentProcess.Id}";
            _meters = new ConcurrentDictionary<string, Meter>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    _cpuCounter = new PerformanceCounter(
                        "Process",
                        "% Processor Time",
                        Process.GetCurrentProcess().ProcessName,
                        true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize CPU performance counter");
                }
            }

            InitializeMeters();
        }

        private void InitializeMeters()
        {
            var deviceMeter = new Meter("Device", "1.0");
            _meters["device"] = deviceMeter;

            // CPU Metrics
            var cpuGauge = deviceMeter.CreateObservableGauge(
                "cpu_usage_percent",
                () => new[] { new Measurement<double>(GetDetailedCpuUsage()) },
                description: "CPU usage percentage");

            // Memory Metrics
            var memoryGauge = deviceMeter.CreateObservableGauge(
                "memory_usage_bytes",
                () => new[] { new Measurement<long>(_currentProcess.WorkingSet64) },
                description: "Memory usage in bytes");

            // GC Metrics
            var gcMeter = new Meter("GC", "1.0");
            _meters["gc"] = gcMeter;

            var gcCollectionsGauge = gcMeter.CreateObservableGauge(
                "collections",
                () => new[]
                {
                    new Measurement<long>(GC.CollectionCount(0), new("generation", "0")),
                    new Measurement<long>(GC.CollectionCount(1), new("generation", "1")),
                    new Measurement<long>(GC.CollectionCount(2), new("generation", "2"))
                });
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Start collecting metrics every 5 seconds
            _collectionTimer = new Timer(
                CollectMetrics,
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5));

            return Task.CompletedTask;
        }

        private void CollectMetrics(object state)
        {
            try
            {
                var metrics = CollectDetailedMetrics();
                RecordMetrics(metrics);
                DetectAnomalies(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting device metrics");
            }
        }

        private DeviceMetrics CollectDetailedMetrics()
        {
            var metrics = new DeviceMetrics();

            try
            {
                // CPU Usage (more accurate)
                metrics.CpuUsagePercent = GetDetailedCpuUsage();

                // Memory Usage (detailed)
                var memoryInfo = GC.GetGCMemoryInfo();
                metrics.MemoryUsageBytes = _currentProcess.WorkingSet64;
                metrics.AvailableMemoryBytes = memoryInfo.TotalAvailableMemoryBytes;

                // Disk Usage (per drive)
                var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                metrics.DiskUsagePercent = 100 * (1 - (double)systemDrive.AvailableFreeSpace / systemDrive.TotalSize);

                // Thread Details
                metrics.ThreadCount = _currentProcess.Threads.Count;
                metrics.CustomMetrics["activeThreads"] = 
                    _currentProcess.Threads.Cast<ProcessThread>().Count(t => t.ThreadState == ThreadState.Running);

                // System Uptime
                metrics.SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

                // GC Metrics
                metrics.CustomMetrics["gen0Collections"] = GC.CollectionCount(0);
                metrics.CustomMetrics["gen1Collections"] = GC.CollectionCount(1);
                metrics.CustomMetrics["gen2Collections"] = GC.CollectionCount(2);
                metrics.CustomMetrics["totalMemory"] = GC.GetTotalMemory(false);
                metrics.CustomMetrics["largeObjectHeapSize"] = memoryInfo.HeapSizeBytes;

                // Process Metrics
                metrics.CustomMetrics["handleCount"] = _currentProcess.HandleCount;
                metrics.CustomMetrics["threadPoolThreads"] = ThreadPool.ThreadCount;
                metrics.CustomMetrics["completionPortThreads"] = 0;

                ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
                metrics.CustomMetrics["minWorkerThreads"] = workerThreads;
                metrics.CustomMetrics["minCompletionPortThreads"] = completionPortThreads;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    metrics.CustomMetrics["pagedMemory"] = _currentProcess.PagedMemorySize64;
                    metrics.CustomMetrics["peakWorkingSet"] = _currentProcess.PeakWorkingSet64;
                    metrics.CustomMetrics["privateMemory"] = _currentProcess.PrivateMemorySize64;
                }

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting detailed metrics");
                return new DeviceMetrics();
            }
        }

        private double GetDetailedCpuUsage()
        {
            try
            {
                if (_cpuCounter != null)
                {
                    return _cpuCounter.NextValue();
                }

                var currentTime = DateTime.UtcNow;
                var currentCpuTime = _currentProcess.TotalProcessorTime;

                var timeDiff = currentTime - _lastCpuCheck;
                var cpuDiff = currentCpuTime - _lastCpuTime;

                _lastCpuCheck = currentTime;
                _lastCpuTime = currentCpuTime;

                var cpuUsagePercent = (cpuDiff.TotalMilliseconds / 
                    (timeDiff.TotalMilliseconds * Environment.ProcessorCount)) * 100;

                return Math.Round(cpuUsagePercent, 2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating CPU usage");
                return 0;
            }
        }

        private void RecordMetrics(DeviceMetrics metrics)
        {
            // Record base metrics
            _metricsCollector.RecordMetricWithTags("device.cpu.usage", metrics.CpuUsagePercent,
                new Dictionary<string, object> { ["instance"] = _instanceName });

            _metricsCollector.RecordMetricWithTags("device.memory.usage", metrics.MemoryUsageBytes,
                new Dictionary<string, object> { ["instance"] = _instanceName });

            _metricsCollector.RecordMetricWithTags("device.memory.available", metrics.AvailableMemoryBytes,
                new Dictionary<string, object> { ["instance"] = _instanceName });

            _metricsCollector.RecordMetricWithTags("device.disk.usage", metrics.DiskUsagePercent,
                new Dictionary<string, object> { ["instance"] = _instanceName });

            // Record thread metrics
            _metricsCollector.RecordMetricWithTags("device.threads.total", metrics.ThreadCount,
                new Dictionary<string, object> { ["instance"] = _instanceName });

            // Record custom metrics
            foreach (var metric in metrics.CustomMetrics)
            {
                _metricsCollector.RecordMetricWithTags($"device.custom.{metric.Key}", metric.Value,
                    new Dictionary<string, object> { ["instance"] = _instanceName });
            }
        }

        private void DetectAnomalies(DeviceMetrics metrics)
        {
            // CPU Spikes
            if (metrics.CpuUsagePercent > 80)
            {
                _logger.LogWarning("High CPU usage detected: {CpuUsage}%", metrics.CpuUsagePercent);
            }

            // Memory Leaks
            var memoryUsagePercent = (double)metrics.MemoryUsageBytes / 
                (metrics.MemoryUsageBytes + metrics.AvailableMemoryBytes) * 100;
            if (memoryUsagePercent > 85)
            {
                _logger.LogWarning("High memory usage detected: {MemoryUsage}%", memoryUsagePercent);
            }

            // Thread Pool Starvation
            ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);

            var threadPoolUsagePercent = 100 * (1 - (double)workerThreads / maxWorkerThreads);
            if (threadPoolUsagePercent > 90)
            {
                _logger.LogWarning("Thread pool saturation detected: {ThreadPoolUsage}%", threadPoolUsagePercent);
            }

            // GC Pressure
            var gen2Collections = GC.CollectionCount(2);
            if (gen2Collections > 10) // Threshold for 5-second window
            {
                _logger.LogWarning("High GC pressure detected: {Gen2Collections} Gen2 collections", gen2Collections);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _collectionTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _collectionTimer?.Dispose();
            _cpuCounter?.Dispose();
            _currentProcess?.Dispose();
            
            foreach (var meter in _meters.Values)
            {
                meter.Dispose();
            }
        }
    }
} 