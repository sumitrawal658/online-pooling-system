using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;

namespace PollSystem.Services.Diagnostics
{
    public class OptimizationRecommendationService
    {
        private readonly ILogger<OptimizationRecommendationService> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly ConcurrentDictionary<string, MetricTimeSeriesData> _metricHistory;
        private readonly AnalysisIntervalConfiguration _intervalConfig;
        private readonly List<Timer> _timers;
        private readonly MetricConfiguration _metricConfig;

        public OptimizationRecommendationService(
            ILogger<OptimizationRecommendationService> logger,
            IMetricsCollector metricsCollector,
            IOptions<AnalysisIntervalConfiguration> intervalConfig,
            IOptions<MetricConfiguration> metricConfig)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _intervalConfig = intervalConfig.Value;
            _metricConfig = metricConfig.Value;
            _metricHistory = new ConcurrentDictionary<string, MetricTimeSeriesData>();
            _timers = new List<Timer>();

            InitializeTimers();
        }

        private void InitializeTimers()
        {
            foreach (var metricSettings in _intervalConfig.MetricIntervals)
            {
                var metric = metricSettings.Key;
                var intervals = metricSettings.Value;

                if (intervals.ShortTerm.Enabled)
                {
                    var timer = new Timer(
                        async _ => await TriggerAnalysis(metric, AnalysisTimeWindow.ShortTerm),
                        null,
                        TimeSpan.Zero,
                        intervals.ShortTerm.AnalysisInterval);
                    _timers.Add(timer);
                }

                if (intervals.MediumTerm.Enabled)
                {
                    var timer = new Timer(
                        async _ => await TriggerAnalysis(metric, AnalysisTimeWindow.MediumTerm),
                        null,
                        TimeSpan.Zero,
                        intervals.MediumTerm.AnalysisInterval);
                    _timers.Add(timer);
                }

                if (intervals.LongTerm.Enabled)
                {
                    var timer = new Timer(
                        async _ => await TriggerAnalysis(metric, AnalysisTimeWindow.LongTerm),
                        null,
                        TimeSpan.Zero,
                        intervals.LongTerm.AnalysisInterval);
                    _timers.Add(timer);
                }
            }
        }

        private async Task TriggerAnalysis(string metricName, AnalysisTimeWindow window)
        {
            try
            {
                if (!_intervalConfig.MetricIntervals.TryGetValue(metricName, out var intervals))
                {
                    _logger.LogWarning("No interval configuration found for metric {MetricName}", metricName);
                    return;
                }

                var settings = intervals.GetSettings(window);
                if (!settings.Enabled)
                    return;

                var metrics = await _metricsCollector.GetRecentMetricsAsync();
                var metric = metrics.FirstOrDefault(m => m.Name == metricName);
                if (metric == null)
                    return;

                var request = new AnalysisRequest
                {
                    MetricName = metricName,
                    TimeWindow = window,
                    Timestamp = DateTime.UtcNow
                };

                var timeout = Task.Delay(_intervalConfig.ScheduleSettings.ProcessingTimeout);
                var sendTask = _analysisBlock.SendAsync(request);
                
                await Task.WhenAny(sendTask, timeout);
                if (!sendTask.IsCompleted)
                {
                    _logger.LogWarning(
                        "Analysis request for {MetricName} ({Window}) timed out after {Timeout}",
                        metricName, window, _intervalConfig.ScheduleSettings.ProcessingTimeout);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error triggering {Window} analysis for {MetricName}", 
                    window, metricName);
            }
        }

        private async Task ProcessAnalysisRequestAsync(AnalysisRequest request)
        {
            try
            {
                var intervals = _intervalConfig.MetricIntervals[request.MetricName];
                var settings = intervals.GetSettings(request.TimeWindow);
                var features = _intervalConfig.MetricFeatures.GetValueOrDefault(request.MetricName);

                var metricData = _metricHistory.GetOrAdd(request.MetricName,
                    _ => new MetricTimeSeriesData(request.MetricName, settings.RetentionPeriod));

                var analysisResult = await AnalyzeMetricDataAsync(
                    request.MetricName,
                    metricData,
                    settings,
                    features);

                if (analysisResult.RequiresOptimization)
                {
                    await GenerateAndPublishRecommendations(
                        request.MetricName,
                        analysisResult,
                        request.TimeWindow,
                        settings);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error processing analysis request for {MetricName}",
                    request.MetricName);
            }
        }

        private async Task<AnalysisResult> AnalyzeMetricDataAsync(
            string metricName,
            MetricTimeSeriesData metricData,
            IntervalSettings settings,
            AnalysisFeatures features)
        {
            var dataPoints = metricData.GetDataPoints(settings.WindowDuration);
            if (dataPoints.Count < settings.MinimumSamples)
            {
                return new AnalysisResult { RequiresOptimization = false };
            }

            var metricDef = _metricConfig.Metrics.GetValueOrDefault(metricName);
            if (metricDef == null)
            {
                _logger.LogWarning("No metric definition found for {MetricName}", metricName);
                return new AnalysisResult { RequiresOptimization = false };
            }

            var result = new AnalysisResult
            {
                RequiresOptimization = false,
                Statistics = CalculateStatistics(dataPoints),
                Trends = features?.EnableTrendAnalysis == true 
                    ? AnalyzeTrends(dataPoints, metricDef, settings.WindowDuration)
                    : null,
                Seasonality = features?.EnableSeasonalAnalysis == true
                    ? AnalyzeSeasonality(dataPoints, metricDef.AnomalySettings.SeasonalitySettings)
                    : null,
                Outliers = features?.EnableOutlierDetection == true
                    ? DetectOutliers(dataPoints, metricDef.AnomalySettings.OutlierThresholds)
                    : new List<MetricDataPoint>()
            };

            result.RequiresOptimization = DetermineOptimizationRequired(
                result, metricDef, settings.WindowDuration);

            return result;
        }

        private MetricStatistics CalculateStatistics(List<MetricDataPoint> dataPoints)
        {
            var values = dataPoints.Select(p => p.Value).ToList();
            return new MetricStatistics
            {
                Mean = values.Average(),
                Median = values.Median(),
                StdDev = values.StandardDeviation(),
                Min = values.Min(),
                Max = values.Max(),
                P95 = values.Percentile(95),
                P99 = values.Percentile(99)
            };
        }

        private TrendAnalysis AnalyzeTrends(
            List<MetricDataPoint> dataPoints,
            MetricDefinition metricDef,
            TimeSpan window)
        {
            var x = Enumerable.Range(0, dataPoints.Count).Select(i => (double)i).ToArray();
            var y = dataPoints.Select(p => p.Value).ToArray();
            var regression = SimpleRegression.Fit(x, y);

            var thresholds = GetTrendThresholds(metricDef, window);
            return new TrendAnalysis
            {
                IsIncreasing = regression.Item2 > thresholds.IncreaseRate,
                Slope = regression.Item2,
                RSquared = CalculateRSquared(x, y, regression.Item1, regression.Item2)
            };
        }

        private double CalculateRSquared(double[] x, double[] y, double intercept, double slope)
        {
            var yMean = y.Average();
            var totalSS = y.Sum(yi => Math.Pow(yi - yMean, 2));
            var residualSS = y.Zip(x, (yi, xi) => 
                Math.Pow(yi - (intercept + slope * xi), 2)).Sum();

            return 1 - (residualSS / totalSS);
        }

        private SeasonalityAnalysis AnalyzeSeasonality(
            List<MetricDataPoint> dataPoints,
            MetricAnalysisSettings settings)
        {
            if (settings.SeasonalityPeriod.HasValue)
            {
                return AnalyzeKnownSeasonality(dataPoints, settings);
            }

            return DetectSeasonality(dataPoints, settings.Thresholds);
        }

        private List<MetricDataPoint> DetectOutliers(
            List<MetricDataPoint> dataPoints,
            OutlierThresholds thresholds)
        {
            var values = dataPoints.Select(p => p.Value).ToList();
            var mean = values.Average();
            var stdDev = values.StandardDeviation();

            var outliers = dataPoints
                .Where(p => Math.Abs((p.Value - mean) / stdDev) > thresholds.DeviationMultiplier)
                .ToList();

            // Check if we have enough outliers
            if (outliers.Count < thresholds.MinimumOutliers)
                return new List<MetricDataPoint>();

            // Check if outlier percentage is significant
            var outlierPercentage = (double)outliers.Count / dataPoints.Count;
            if (outlierPercentage > thresholds.OutlierPercentage)
                return outliers;

            // Check for consecutive outliers
            var consecutiveOutliers = CountConsecutiveOutliers(outliers);
            if (consecutiveOutliers >= thresholds.ConsecutiveOutliers)
                return outliers;

            return new List<MetricDataPoint>();
        }

        private bool DetermineOptimizationRequired(
            AnalysisResult result,
            MetricDefinition metricDef,
            TimeSpan window)
        {
            var thresholds = GetThresholdRange(metricDef, window);

            // Check if current values exceed thresholds
            if (result.Statistics.P95 > thresholds.Upper)
                return true;

            // Check if trend is concerning
            if (result.Trends?.IsIncreasing == true &&
                result.Trends.RSquared > 0.7)
            {
                var trendThresholds = GetTrendThresholds(metricDef, window);
                if (result.Trends.Slope > trendThresholds.ShortTerm)
                    return true;
            }

            // Check for seasonal anomalies
            if (result.Seasonality?.HasAnomaly == true &&
                result.Seasonality.AnomalyScore > metricDef.AnomalySettings.SeasonalitySettings.DeviationThreshold)
                return true;

            // Check for significant outliers
            if (result.Outliers.Count >= metricDef.AnomalySettings.OutlierThresholds.MinimumOutliers)
            {
                var consecutiveOutliers = CountConsecutiveOutliers(result.Outliers);
                if (consecutiveOutliers >= metricDef.AnomalySettings.OutlierThresholds.ConsecutiveOutliers)
                    return true;
            }

            return false;
        }

        private int CountConsecutiveOutliers(List<MetricDataPoint> outliers)
        {
            if (!outliers.Any())
                return 0;

            var maxConsecutive = 1;
            var currentConsecutive = 1;
            var lastTimestamp = outliers[0].Timestamp;

            for (int i = 1; i < outliers.Count; i++)
            {
                var timeDiff = outliers[i].Timestamp - lastTimestamp;
                if (timeDiff <= TimeSpan.FromMinutes(1)) // Adjust this threshold as needed
                {
                    currentConsecutive++;
                    maxConsecutive = Math.Max(maxConsecutive, currentConsecutive);
                }
                else
                {
                    currentConsecutive = 1;
                }
                lastTimestamp = outliers[i].Timestamp;
            }

            return maxConsecutive;
        }

        private async Task GenerateAndPublishRecommendations(
            string metricName,
            AnalysisResult result,
            AnalysisTimeWindow window,
            IntervalSettings settings)
        {
            var metricDef = _metricConfig.Metrics.GetValueOrDefault(metricName);
            if (metricDef == null)
                return;

            var recommendations = GenerateRecommendations(metricName, result, window, metricDef);
            foreach (var recommendation in recommendations)
            {
                await PublishRecommendation(recommendation, metricDef.AlertSettings);
            }
        }

        private List<OptimizationRecommendation> GenerateRecommendations(
            string metricName,
            AnalysisResult result,
            AnalysisTimeWindow window,
            MetricDefinition metricDef)
        {
            var recommendations = new List<OptimizationRecommendation>();
            var features = _intervalConfig.MetricFeatures.GetValueOrDefault(metricName);

            // Add threshold-based recommendations
            if (result.Statistics.P95 > metricDef.ThresholdSettings.WarningThresholds.ShortTerm.Upper)
            {
                recommendations.Add(CreateThresholdRecommendation(
                    metricName, result, metricDef.ThresholdSettings.WarningThresholds.ShortTerm));
            }

            // Add trend-based recommendations
            if (features?.EnableTrendAnalysis == true && result.Trends.RSquared > 0.7)
            {
                recommendations.Add(CreateTrendRecommendation(
                    metricName, result, metricDef.ThresholdSettings.WarningThresholds.ShortTerm));
            }

            // Add seasonality-based recommendations
            if (features?.EnableSeasonalAnalysis == true && result.Seasonality?.HasAnomaly == true)
            {
                recommendations.Add(CreateSeasonalityRecommendation(
                    metricName, result, metricDef.ThresholdSettings.WarningThresholds.ShortTerm));
            }

            return recommendations;
        }

        private async Task PublishRecommendation(
            OptimizationRecommendation recommendation,
            MetricAlertSettings alertSettings)
        {
            try
            {
                // Check cooldown period
                var lastAlert = GetLastAlert(recommendation.MetricName);
                if (lastAlert != null && 
                    DateTime.UtcNow - lastAlert.Timestamp < alertSettings.AlertCooldown)
                {
                    return;
                }

                // Implement alert delay based on severity
                var delay = recommendation.Priority == OptimizationPriority.Critical
                    ? alertSettings.CriticalAlertDelay
                    : alertSettings.WarningAlertDelay;

                await Task.Delay(delay);

                // Check if condition still exists
                if (await ValidateRecommendation(recommendation))
                {
                    _logger.LogInformation(
                        "Publishing optimization recommendation: {Title}",
                        recommendation.Title);
                    // Implement actual publishing logic here
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error publishing optimization recommendation");
            }
        }

        private async Task<bool> ValidateRecommendation(OptimizationRecommendation recommendation)
        {
            // Implement validation logic here
            // This could involve checking if the condition that triggered the recommendation still exists
            return true;
        }

        public void Dispose()
        {
            foreach (var timer in _timers)
            {
                timer?.Dispose();
            }
            _analysisBlock.Complete();
        }
    }

    public enum AnalysisTimeWindow
    {
        ShortTerm,
        MediumTerm,
        LongTerm
    }

    public class AnalysisRequest
    {
        public string MetricName { get; init; }
        public AnalysisTimeWindow TimeWindow { get; init; }
        public DateTime Timestamp { get; init; }
    }

    public class AnalysisResult
    {
        public bool RequiresOptimization { get; set; }
        public MetricStatistics Statistics { get; set; }
        public TrendAnalysis Trends { get; set; }
        public SeasonalityAnalysis Seasonality { get; set; }
        public List<MetricDataPoint> Outliers { get; set; }
    }

    public class MetricStatistics
    {
        public double Mean { get; init; }
        public double Median { get; init; }
        public double StdDev { get; init; }
        public double Min { get; init; }
        public double Max { get; init; }
        public double P95 { get; init; }
        public double P99 { get; init; }
        public int SampleCount { get; init; }
    }

    public class SeasonalityAnalysis
    {
        public bool HasPattern { get; init; }
        public TimeSpan? Period { get; init; }
        public bool HasAnomaly { get; init; }
        public double AnomalyScore { get; init; }
        public double Confidence { get; init; }
    }

    public class MetricTimeSeriesData
    {
        private readonly string _metricName;
        private readonly ConcurrentQueue<MetricDataPoint> _dataPoints;
        private readonly ReaderWriterLockSlim _lock;

        public MetricTimeSeriesData(string metricName)
        {
            _metricName = metricName;
            _dataPoints = new ConcurrentQueue<MetricDataPoint>();
            _lock = new ReaderWriterLockSlim();
        }

        public void AddDataPoint(MetricDataPoint point)
        {
            _lock.EnterWriteLock();
            try
            {
                _dataPoints.Enqueue(point);
                // Cleanup old data points
                while (_dataPoints.TryPeek(out var oldestPoint) &&
                       DateTime.UtcNow - oldestPoint.Timestamp > TimeSpan.FromDays(7))
                {
                    _dataPoints.TryDequeue(out _);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public List<MetricDataPoint> GetDataPoints(TimeSpan window)
        {
            _lock.EnterReadLock();
            try
            {
                var cutoff = DateTime.UtcNow - window;
                return _dataPoints
                    .Where(p => p.Timestamp >= cutoff)
                    .OrderBy(p => p.Timestamp)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }
} 