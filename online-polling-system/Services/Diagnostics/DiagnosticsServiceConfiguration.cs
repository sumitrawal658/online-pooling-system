using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks.Dataflow;

namespace PollSystem.Services.Diagnostics
{
    public static class DiagnosticsServiceConfiguration
    {
        public static IServiceCollection AddDiagnosticsServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Register options
            services.Configure<MetricsAnalysisOptions>(
                configuration.GetSection("Metrics:Analysis"));
            services.Configure<AlertOptions>(
                configuration.GetSection("Alerts"));
            services.Configure<DeviceDiagnosticsOptions>(
                configuration.GetSection("DeviceDiagnostics"));

            // Configure metrics buffer
            var metricsBufferOptions = new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 10000, // Maximum items in buffer
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                SingleProducerConstrained = false
            };

            var metricsBuffer = new BufferBlock<MetricSample>(metricsBufferOptions);
            services.AddSingleton(metricsBuffer);

            // Register core services
            services.AddSingleton<IMetricsProcessor, MetricsProcessor>();
            services.AddSingleton<IDeviceDiagnostics, DeviceDiagnosticsService>();
            services.AddSingleton<IMetricsCollector, MetricsCollector>();
            services.AddHostedService<MetricsAnalysisService>();
            services.AddHostedService<AlertManagementService>();
            services.AddHostedService<DiagnosticDataCollector>();
            services.AddHostedService<MonitoringAnalysisService>();

            // Register cleanup service
            services.AddHostedService<DiagnosticsCleanupService>();

            return services;
        }
    }

    public class MetricsProcessor : IMetricsProcessor, IDisposable
    {
        private readonly BufferBlock<MetricSample> _metricsBuffer;
        private readonly IMetricsCollector _metricsCollector;
        private readonly ILogger<MetricsProcessor> _logger;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processingTask;
        private readonly SemaphoreSlim _processingSemaphore;
        private readonly MemoryCache _metricsCache;
        private readonly MemoryCacheOptions _cacheOptions;

        public MetricsProcessor(
            BufferBlock<MetricSample> metricsBuffer,
            IMetricsCollector metricsCollector,
            ILogger<MetricsProcessor> logger)
        {
            _metricsBuffer = metricsBuffer;
            _metricsCollector = metricsCollector;
            _logger = logger;
            _cancellationTokenSource = new CancellationTokenSource();
            _processingSemaphore = new SemaphoreSlim(Environment.ProcessorCount);
            
            _cacheOptions = new MemoryCacheOptions
            {
                SizeLimit = 100000, // Maximum cache entries
                ExpirationScanFrequency = TimeSpan.FromMinutes(5)
            };
            _metricsCache = new MemoryCache(_cacheOptions);

            // Start processing loop
            _processingTask = ProcessMetricsAsync(_cancellationTokenSource.Token);
        }

        public async Task EnqueueMetricAsync(MetricSample sample)
        {
            try
            {
                if (!await _metricsBuffer.SendAsync(sample, _cancellationTokenSource.Token))
                {
                    _logger.LogWarning("Failed to enqueue metric: Buffer full");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enqueueing metric");
            }
        }

        private async Task ProcessMetricsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _processingSemaphore.WaitAsync(cancellationToken);

                    try
                    {
                        var sample = await _metricsBuffer.ReceiveAsync(cancellationToken);
                        await ProcessMetricSampleAsync(sample);
                    }
                    finally
                    {
                        _processingSemaphore.Release();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing metrics");
                    await Task.Delay(1000, cancellationToken); // Back off on error
                }
            }
        }

        private async Task ProcessMetricSampleAsync(MetricSample sample)
        {
            var cacheKey = $"{sample.MetricName}:{DateTime.UtcNow:yyyyMMddHH}";
            var metrics = await GetOrCreateMetricsCacheAsync(cacheKey);

            metrics.AddSample(sample);
            await UpdateMetricsStoreAsync(metrics);
        }

        private async Task<MetricsStore> GetOrCreateMetricsCacheAsync(string key)
        {
            if (!_metricsCache.TryGetValue(key, out MetricsStore metrics))
            {
                metrics = new MetricsStore(key);
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetSize(1)
                    .SetSlidingExpiration(TimeSpan.FromHours(2))
                    .SetAbsoluteExpiration(TimeSpan.FromHours(24));

                _metricsCache.Set(key, metrics, cacheEntryOptions);
            }
            return metrics;
        }

        private async Task UpdateMetricsStoreAsync(MetricsStore metrics)
        {
            try
            {
                await metrics.SaveAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating metrics store");
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            try
            {
                _processingTask.Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during metrics processor shutdown");
            }

            _cancellationTokenSource.Dispose();
            _processingSemaphore.Dispose();
            _metricsCache.Dispose();
        }
    }

    public class DiagnosticsCleanupService : BackgroundService
    {
        private readonly ILogger<DiagnosticsCleanupService> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly MemoryCache _metricsCache;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

        public DiagnosticsCleanupService(
            ILogger<DiagnosticsCleanupService> logger,
            IMetricsCollector metricsCollector,
            MemoryCache metricsCache)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _metricsCache = metricsCache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformCleanupAsync();
                    await Task.Delay(_cleanupInterval, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during diagnostics cleanup");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        private async Task PerformCleanupAsync()
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            var memoryPressure = (double)memoryInfo.MemoryLoadBytes / memoryInfo.TotalAvailableMemoryBytes;

            if (memoryPressure > 0.85) // High memory pressure
            {
                _logger.LogWarning("High memory pressure detected: {Pressure:P2}", memoryPressure);
                await ForceCleanupAsync();
            }
            else
            {
                await NormalCleanupAsync();
            }

            // Record cleanup metrics
            _metricsCollector.RecordMetricWithTags(
                "diagnostics.cleanup",
                1,
                new Dictionary<string, object>
                {
                    ["memoryPressure"] = memoryPressure,
                    ["totalMemory"] = GC.GetTotalMemory(false)
                });
        }

        private async Task NormalCleanupAsync()
        {
            // Normal cleanup operations
            GC.Collect(1, GCCollectionMode.Default, false);
        }

        private async Task ForceCleanupAsync()
        {
            // Aggressive cleanup under memory pressure
            _metricsCache.Compact(0.5); // Remove 50% of cache entries
            GC.Collect(2, GCCollectionMode.Aggressive, true);
        }
    }

    public class MetricsStore
    {
        private readonly string _key;
        private readonly ConcurrentQueue<MetricSample> _samples;
        private readonly SemaphoreSlim _lock;
        private readonly int _maxSamples = 10000;

        public MetricsStore(string key)
        {
            _key = key;
            _samples = new ConcurrentQueue<MetricSample>();
            _lock = new SemaphoreSlim(1, 1);
        }

        public async Task AddSample(MetricSample sample)
        {
            await _lock.WaitAsync();
            try
            {
                _samples.Enqueue(sample);
                while (_samples.Count > _maxSamples)
                {
                    _samples.TryDequeue(out _);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveAsync()
        {
            await _lock.WaitAsync();
            try
            {
                // Implement persistence logic if needed
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public interface IMetricsProcessor
    {
        Task EnqueueMetricAsync(MetricSample sample);
    }

    public class DeviceDiagnosticsOptions
    {
        public int MaxConcurrentOperations { get; set; } = Environment.ProcessorCount;
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public int MaxRetryAttempts { get; set; } = 3;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    }
} 