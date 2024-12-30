using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;

namespace PollSystem.Services.Diagnostics
{
    public class ParallelMetricAnalyzer : IDisposable
    {
        private readonly ILogger<ParallelMetricAnalyzer> _logger;
        private readonly MetricDataManager _dataManager;
        private readonly MetricConfiguration _metricConfig;
        private readonly ActionBlock<AnalysisRequest> _analysisBlock;
        private readonly ConcurrentDictionary<string, DateTime> _lastAnalysisTime;
        private readonly int _maxDegreeOfParallelism;
        private readonly int _boundedCapacity;

        public ParallelMetricAnalyzer(
            ILogger<ParallelMetricAnalyzer> logger,
            MetricDataManager dataManager,
            IOptions<MetricConfiguration> metricConfig,
            int maxDegreeOfParallelism = 4,
            int boundedCapacity = 100)
        {
            _logger = logger;
            _dataManager = dataManager;
            _metricConfig = metricConfig.Value;
            _maxDegreeOfParallelism = maxDegreeOfParallelism;
            _boundedCapacity = boundedCapacity;
            _lastAnalysisTime = new ConcurrentDictionary<string, DateTime>();

            var options = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                BoundedCapacity = _boundedCapacity,
                SingleProducerConstrained = false,
                EnsureOrdered = false
            };

            _analysisBlock = new ActionBlock<AnalysisRequest>(
                ProcessAnalysisRequestAsync, options);
        }

        public async Task SubmitAnalysisRequestAsync(
            string metricName,
            AnalysisTimeWindow window,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new AnalysisRequest
                {
                    MetricName = metricName,
                    TimeWindow = window,
                    Timestamp = DateTime.UtcNow
                };

                // Check if we should throttle this analysis
                if (!ShouldProcessAnalysis(metricName, window))
                {
                    _logger.LogDebug(
                        "Skipping analysis for {MetricName} due to throttling",
                        metricName);
                    return;
                }

                var timeout = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                var sendTask = _analysisBlock.SendAsync(request, cancellationToken);
                
                await Task.WhenAny(sendTask, timeout);
                if (!sendTask.IsCompleted)
                {
                    _logger.LogWarning(
                        "Analysis request for {MetricName} timed out after 30 seconds",
                        metricName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error submitting analysis request for {MetricName}",
                    metricName);
            }
        }

        public async Task SubmitBatchAnalysisAsync(
            IEnumerable<string> metricNames,
            AnalysisTimeWindow window,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var tasks = metricNames.Select(metricName =>
                    SubmitAnalysisRequestAsync(metricName, window, cancellationToken));

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting batch analysis request");
            }
        }

        private bool ShouldProcessAnalysis(string metricName, AnalysisTimeWindow window)
        {
            if (!_metricConfig.Metrics.TryGetValue(metricName, out var metricDef))
                return false;

            var minInterval = window switch
            {
                AnalysisTimeWindow.ShortTerm => TimeSpan.FromMinutes(1),
                AnalysisTimeWindow.MediumTerm => TimeSpan.FromMinutes(5),
                AnalysisTimeWindow.LongTerm => TimeSpan.FromMinutes(15),
                _ => TimeSpan.FromMinutes(5)
            };

            var lastTime = _lastAnalysisTime.GetOrAdd(metricName, DateTime.MinValue);
            var timeSinceLastAnalysis = DateTime.UtcNow - lastTime;

            return timeSinceLastAnalysis >= minInterval;
        }

        private async Task ProcessAnalysisRequestAsync(AnalysisRequest request)
        {
            try
            {
                _lastAnalysisTime.AddOrUpdate(
                    request.MetricName,
                    DateTime.UtcNow,
                    (_, _) => DateTime.UtcNow);

                var window = GetTimeWindowDuration(request.TimeWindow);
                var dataPoints = await _dataManager.GetDataPointsAsync(
                    request.MetricName, window);

                if (!dataPoints.Any())
                {
                    _logger.LogDebug(
                        "No data points found for {MetricName} in {Window} window",
                        request.MetricName, request.TimeWindow);
                    return;
                }

                var analysisContext = new AnalysisContext
                {
                    MetricName = request.MetricName,
                    Window = request.TimeWindow,
                    DataPoints = dataPoints,
                    Timestamp = request.Timestamp
                };

                await Task.WhenAll(
                    AnalyzeStatisticsAsync(analysisContext),
                    AnalyzeTrendsAsync(analysisContext),
                    AnalyzeAnomaliesAsync(analysisContext),
                    AnalyzeSeasonalityAsync(analysisContext)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing analysis request for {MetricName}",
                    request.MetricName);
            }
        }

        private TimeSpan GetTimeWindowDuration(AnalysisTimeWindow window)
        {
            return window switch
            {
                AnalysisTimeWindow.ShortTerm => TimeSpan.FromMinutes(15),
                AnalysisTimeWindow.MediumTerm => TimeSpan.FromHours(1),
                AnalysisTimeWindow.LongTerm => TimeSpan.FromHours(4),
                _ => TimeSpan.FromHours(1)
            };
        }

        private async Task AnalyzeStatisticsAsync(AnalysisContext context)
        {
            try
            {
                var stats = await _dataManager.GetMetricStatisticsAsync(
                    context.MetricName,
                    GetTimeWindowDuration(context.Window));

                // Process statistics and raise alerts if needed
                if (_metricConfig.Metrics.TryGetValue(context.MetricName, out var metricDef))
                {
                    var thresholds = GetThresholds(metricDef, context.Window);
                    if (stats.P95 > thresholds.Upper)
                    {
                        // Raise alert for high metric value
                        await RaiseMetricAlert(context, stats, AlertType.HighValue);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error analyzing statistics for {MetricName}",
                    context.MetricName);
            }
        }

        private async Task AnalyzeTrendsAsync(AnalysisContext context)
        {
            try
            {
                var values = context.DataPoints.Select(p => p.Value).ToArray();
                var timestamps = context.DataPoints.Select(p => p.Timestamp).ToArray();

                var trendAnalysis = await Task.Run(() =>
                {
                    var x = Enumerable.Range(0, values.Length).Select(i => (double)i).ToArray();
                    var regression = SimpleRegression.Fit(x, values);
                    return new TrendAnalysis
                    {
                        Slope = regression.Item2,
                        RSquared = CalculateRSquared(x, values, regression.Item1, regression.Item2)
                    };
                });

                if (_metricConfig.Metrics.TryGetValue(context.MetricName, out var metricDef))
                {
                    var trendThresholds = GetTrendThresholds(metricDef, context.Window);
                    if (trendAnalysis.Slope > trendThresholds.IncreaseRate.ShortTerm &&
                        trendAnalysis.RSquared > 0.7)
                    {
                        // Raise alert for concerning trend
                        await RaiseMetricAlert(context, trendAnalysis, AlertType.IncreasingTrend);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error analyzing trends for {MetricName}",
                    context.MetricName);
            }
        }

        private async Task AnalyzeAnomaliesAsync(AnalysisContext context)
        {
            try
            {
                if (!_metricConfig.Metrics.TryGetValue(context.MetricName, out var metricDef) ||
                    !metricDef.AnomalySettings.EnableOutlierDetection)
                {
                    return;
                }

                var values = context.DataPoints.Select(p => p.Value).ToArray();
                var mean = values.Average();
                var stdDev = CalculateStandardDeviation(values);

                var outliers = context.DataPoints
                    .Where(p => Math.Abs((p.Value - mean) / stdDev) >
                               metricDef.AnomalySettings.OutlierThresholds.DeviationMultiplier)
                    .ToList();

                if (outliers.Count >= metricDef.AnomalySettings.OutlierThresholds.MinimumOutliers)
                {
                    // Raise alert for anomalies
                    await RaiseMetricAlert(context, outliers, AlertType.Anomaly);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error analyzing anomalies for {MetricName}",
                    context.MetricName);
            }
        }

        private async Task AnalyzeSeasonalityAsync(AnalysisContext context)
        {
            try
            {
                if (!_metricConfig.Metrics.TryGetValue(context.MetricName, out var metricDef) ||
                    metricDef.AnomalySettings.SeasonalitySettings.ExpectedPatterns == null)
                {
                    return;
                }

                foreach (var pattern in metricDef.AnomalySettings.SeasonalitySettings.ExpectedPatterns)
                {
                    var seasonalityAnalysis = await Task.Run(() =>
                        AnalyzeSeasonalPattern(context.DataPoints, pattern));

                    if (seasonalityAnalysis.HasAnomaly)
                    {
                        // Raise alert for seasonal anomaly
                        await RaiseMetricAlert(context, seasonalityAnalysis, AlertType.SeasonalAnomaly);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error analyzing seasonality for {MetricName}",
                    context.MetricName);
            }
        }

        private async Task RaiseMetricAlert(
            AnalysisContext context,
            object analysisResult,
            AlertType alertType)
        {
            // Implement alert raising logic
            _logger.LogWarning(
                "Alert raised for {MetricName}: {AlertType}",
                context.MetricName,
                alertType);
        }

        public void Dispose()
        {
            _analysisBlock.Complete();
            try
            {
                _analysisBlock.Completion.Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing analysis block");
            }
        }
    }

    public class AnalysisContext
    {
        public string MetricName { get; init; }
        public AnalysisTimeWindow Window { get; init; }
        public List<MetricDataPoint> DataPoints { get; init; }
        public DateTime Timestamp { get; init; }
    }

    public enum AlertType
    {
        HighValue,
        IncreasingTrend,
        Anomaly,
        SeasonalAnomaly
    }
} 