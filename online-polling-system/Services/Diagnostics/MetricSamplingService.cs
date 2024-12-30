using System.Collections.Concurrent;

namespace PollSystem.Services.Diagnostics
{
    public class MetricSamplingService : IHostedService, IDisposable
    {
        private readonly ILogger<MetricSamplingService> _logger;
        private readonly MetricDataManager _dataManager;
        private readonly IOptions<MetricSamplingConfiguration> _config;
        private readonly ConcurrentDictionary<string, AdaptiveSamplingState> _adaptiveStates;
        private readonly ConcurrentDictionary<string, Timer> _samplingTimers;
        private readonly ConcurrentDictionary<string, DateTime> _lastSampleTimes;
        private bool _isRunning;

        public MetricSamplingService(
            ILogger<MetricSamplingService> logger,
            MetricDataManager dataManager,
            IOptions<MetricSamplingConfiguration> config)
        {
            _logger = logger;
            _dataManager = dataManager;
            _config = config;
            _adaptiveStates = new ConcurrentDictionary<string, AdaptiveSamplingState>();
            _samplingTimers = new ConcurrentDictionary<string, Timer>();
            _lastSampleTimes = new ConcurrentDictionary<string, DateTime>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting metric sampling service");
            _isRunning = true;
            InitializeSamplingTimers();
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping metric sampling service");
            _isRunning = false;

            var disposeTasks = _samplingTimers.Values.Select(async timer =>
            {
                await timer.DisposeAsync();
            });

            await Task.WhenAll(disposeTasks);
        }

        private void InitializeSamplingTimers()
        {
            foreach (var (metricName, strategy) in _config.Value.MetricSamplingStrategies)
            {
                InitializeMetricSampling(metricName, strategy);
            }
        }

        private void InitializeMetricSampling(string metricName, SamplingStrategy strategy)
        {
            var timer = new Timer(
                async _ => await ExecuteSamplingAsync(metricName),
                null,
                TimeSpan.Zero,
                strategy.SamplingRate);

            _samplingTimers.TryAdd(metricName, timer);

            if (strategy.AdaptiveSamplingSettings.EnableAdaptiveSampling)
            {
                _adaptiveStates.TryAdd(metricName, new AdaptiveSamplingState());
                InitializeAdaptiveSampling(metricName, strategy);
            }
        }

        private void InitializeAdaptiveSampling(string metricName, SamplingStrategy strategy)
        {
            var timer = new Timer(
                async _ => await AdaptSamplingRateAsync(metricName),
                null,
                strategy.AdaptiveSamplingSettings.AdaptationInterval,
                strategy.AdaptiveSamplingSettings.AdaptationInterval);

            _samplingTimers.TryAdd($"{metricName}_adaptive", timer);
        }

        private async Task ExecuteSamplingAsync(string metricName)
        {
            try
            {
                var strategy = _config.Value.GetStrategyForMetric(metricName);
                var now = DateTime.UtcNow;

                // Check if we should sample based on the current rate
                var lastSample = _lastSampleTimes.GetOrAdd(metricName, now);
                if (now - lastSample < strategy.SamplingRate)
                    return;

                // Get the metric value
                var metricValue = await GetMetricValueAsync(metricName);
                
                // Store the sample with appropriate downsampling
                await StoreSampleAsync(metricName, metricValue, strategy);

                // Update adaptive sampling state if enabled
                if (strategy.AdaptiveSamplingSettings.EnableAdaptiveSampling)
                {
                    UpdateAdaptiveSamplingState(metricName, metricValue);
                }

                _lastSampleTimes.AddOrUpdate(metricName, now, (_, _) => now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error executing sampling for metric {MetricName}",
                    metricName);
            }
        }

        private async Task StoreSampleAsync(
            string metricName,
            double value,
            SamplingStrategy strategy)
        {
            var dataPoint = new MetricDataPoint(DateTime.UtcNow, value);

            // Apply downsampling rules based on age
            var age = DateTime.UtcNow - _lastSampleTimes.GetOrAdd(metricName, DateTime.UtcNow);
            var samplingRate = strategy.GetSamplingRateForAge(age);

            if (age >= samplingRate)
            {
                await _dataManager.AddDataPointAsync(metricName, dataPoint);
            }
        }

        private async Task AdaptSamplingRateAsync(string metricName)
        {
            try
            {
                var strategy = _config.Value.GetStrategyForMetric(metricName);
                if (!strategy.AdaptiveSamplingSettings.EnableAdaptiveSampling)
                    return;

                var state = _adaptiveStates.GetOrAdd(metricName, new AdaptiveSamplingState());
                if (state.RecentValues.Count < strategy.AdaptiveSamplingSettings.MinSamplesForAdaptation)
                    return;

                var variation = CalculateVariation(state.RecentValues);
                var newRate = CalculateNewSamplingRate(
                    variation,
                    strategy.SamplingRate,
                    strategy.AdaptiveSamplingSettings);

                if (newRate != strategy.SamplingRate)
                {
                    await UpdateSamplingRateAsync(metricName, newRate);
                    _logger.LogInformation(
                        "Adapted sampling rate for {MetricName} to {NewRate}",
                        metricName,
                        newRate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error adapting sampling rate for metric {MetricName}",
                    metricName);
            }
        }

        private void UpdateAdaptiveSamplingState(string metricName, double value)
        {
            if (_adaptiveStates.TryGetValue(metricName, out var state))
            {
                state.AddValue(value);
            }
        }

        private double CalculateVariation(Queue<double> values)
        {
            if (values.Count < 2)
                return 0;

            var mean = values.Average();
            var variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
            return Math.Sqrt(variance) / mean; // Coefficient of variation
        }

        private TimeSpan CalculateNewSamplingRate(
            double variation,
            TimeSpan currentRate,
            AdaptiveSamplingSettings settings)
        {
            if (variation > settings.VariationThreshold)
            {
                // Increase sampling rate (decrease interval)
                var newTicks = (long)(currentRate.Ticks * (1 - settings.ChangeThreshold));
                return TimeSpan.FromTicks(Math.Max(
                    settings.MinSamplingRate.Ticks,
                    newTicks));
            }
            else
            {
                // Decrease sampling rate (increase interval)
                var newTicks = (long)(currentRate.Ticks * (1 + settings.ChangeThreshold));
                return TimeSpan.FromTicks(Math.Min(
                    settings.MaxSamplingRate.Ticks,
                    newTicks));
            }
        }

        private async Task UpdateSamplingRateAsync(string metricName, TimeSpan newRate)
        {
            if (_samplingTimers.TryGetValue(metricName, out var timer))
            {
                await timer.DisposeAsync();
                timer = new Timer(
                    async _ => await ExecuteSamplingAsync(metricName),
                    null,
                    TimeSpan.Zero,
                    newRate);
                _samplingTimers.TryUpdate(metricName, timer, timer);
            }
        }

        private async Task<double> GetMetricValueAsync(string metricName)
        {
            // Implement actual metric collection logic here
            return 0.0;
        }

        public void Dispose()
        {
            foreach (var timer in _samplingTimers.Values)
            {
                timer?.Dispose();
            }
        }
    }

    internal class AdaptiveSamplingState
    {
        private const int MAX_RECENT_VALUES = 100;
        public Queue<double> RecentValues { get; } = new();

        public void AddValue(double value)
        {
            RecentValues.Enqueue(value);
            while (RecentValues.Count > MAX_RECENT_VALUES)
            {
                RecentValues.Dequeue();
            }
        }
    }
} 