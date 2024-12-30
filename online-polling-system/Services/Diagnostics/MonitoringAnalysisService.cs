using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Text;

namespace PollSystem.Services.Diagnostics
{
    public class MonitoringAnalysisService : IHostedService
    {
        private readonly ILogger<MonitoringAnalysisService> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly IAlertManagementService _alertService;
        private readonly Timer _analysisTimer;
        private readonly MetricHistoryManager _metricHistoryManager;
        private readonly AnomalyDetectionService _anomalyDetector;
        private readonly TrendAnalysisService _trendAnalyzer;
        private readonly TimeSpan _analysisInterval = TimeSpan.FromMinutes(5);
        private readonly int _historicalDataPoints = 288; // 24 hours worth of 5-minute intervals

        public MonitoringAnalysisService(
            ILogger<MonitoringAnalysisService> logger,
            IMetricsCollector metricsCollector,
            IAlertManagementService alertService,
            IOptions<AnomalyDetectionOptions> anomalyOptions,
            IOptions<TrendAnalysisOptions> trendOptions)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _alertService = alertService;
            _metricHistoryManager = new MetricHistoryManager(
                logger,
                TimeSpan.FromDays(7),
                _historicalDataPoints);
            _anomalyDetector = new AnomalyDetectionService(logger, anomalyOptions);
            _trendAnalyzer = new TrendAnalysisService(logger, trendOptions);
            _analysisTimer = new Timer(AnalyzeMetrics, null, TimeSpan.Zero, _analysisInterval);
        }

        private async void AnalyzeMetrics(object state)
        {
            try
            {
                var metrics = await _metricsCollector.GetLatestMetricsAsync();
                foreach (var metric in metrics)
                {
                    await UpdateMetricHistoryAsync(metric);
                    await AnalyzeMetricTrendsAsync(metric);
                    await CheckResourceOptimizationAsync(metric);
                }

                await GeneratePerformanceInsightsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing metrics");
            }
        }

        private async Task UpdateMetricHistoryAsync(MetricSample metric)
        {
            await _metricHistoryManager.AddMetricDataPointAsync(metric.MetricName, new MetricDataPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = metric.Value,
                Tags = metric.Tags
            });
        }

        private async Task AnalyzeMetricTrendsAsync(MetricSample metric)
        {
            var history = await _metricHistoryManager.GetMetricHistoryAsync(
                metric.MetricName,
                DateTime.UtcNow - TimeSpan.FromHours(24));

            if (!history.Any())
                return;

            // Analyze trends
            var trendAnalysis = await _trendAnalyzer.AnalyzeTrendAsync(
                metric.MetricName,
                history,
                TimeSpan.FromHours(24));

            if (trendAnalysis.Confidence > 0.7)
            {
                await _alertService.RaiseAlert(new Alert
                {
                    Severity = DetermineTrendAlertSeverity(trendAnalysis),
                    Source = "TrendAnalysis",
                    Message = GenerateTrendMessage(metric.MetricName, trendAnalysis),
                    Context = new Dictionary<string, object>
                    {
                        ["metricName"] = metric.MetricName,
                        ["trendType"] = trendAnalysis.TrendType.ToString(),
                        ["slope"] = trendAnalysis.Slope,
                        ["confidence"] = trendAnalysis.Confidence,
                        ["changeRate"] = trendAnalysis.ChangeRate,
                        ["hasSeasonality"] = trendAnalysis.Seasonality.HasSeasonality,
                        ["seasonalityPeriod"] = trendAnalysis.Seasonality.Period,
                        ["predictions"] = trendAnalysis.PredictedValues
                            .Select(p => new { p.Timestamp, p.Value, p.Confidence })
                            .ToList()
                    }
                });
            }

            // Record trend metrics
            await _metricsCollector.RecordMetricWithTags(
                $"{metric.MetricName}.trend",
                trendAnalysis.Slope,
                new Dictionary<string, object>
                {
                    ["trendType"] = trendAnalysis.TrendType.ToString(),
                    ["confidence"] = trendAnalysis.Confidence,
                    ["changeRate"] = trendAnalysis.ChangeRate,
                    ["hasSeasonality"] = trendAnalysis.Seasonality.HasSeasonality
                });

            // Check for anomalies
            var currentPoint = new MetricDataPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = metric.Value,
                Tags = metric.Tags
            };

            var anomalyResult = await _anomalyDetector.AnalyzeMetricAsync(
                metric.MetricName,
                history,
                currentPoint);

            if (anomalyResult.IsAnomaly)
            {
                await _alertService.RaiseAlert(new Alert
                {
                    Severity = DetermineAlertSeverity(anomalyResult),
                    Source = "AnomalyDetection",
                    Message = GenerateAnomalyMessage(metric.MetricName, anomalyResult),
                    Context = new Dictionary<string, object>
                    {
                        ["metricName"] = metric.MetricName,
                        ["anomalyScore"] = anomalyResult.AnomalyScore,
                        ["confidence"] = anomalyResult.Confidence,
                        ["currentValue"] = anomalyResult.Value,
                        ["detectionMethods"] = string.Join(", ", anomalyResult.DetectionMethods),
                        ["contributingFactors"] = anomalyResult.ContributingFactors
                            .Select(f => $"{f.FactorType}: {f.Description}")
                            .ToList()
                    }
                });
            }
        }

        private AlertSeverity DetermineTrendAlertSeverity(TrendAnalysisResult trend)
        {
            if (trend.Confidence < 0.7)
                return AlertSeverity.Information;

            if (Math.Abs(trend.ChangeRate) > 0.5 && trend.Confidence > 0.9)
                return AlertSeverity.Warning;

            if (trend.TrendType != TrendType.Stable && trend.Confidence > 0.8)
                return AlertSeverity.Information;

            return AlertSeverity.Information;
        }

        private string GenerateTrendMessage(string metricName, TrendAnalysisResult trend)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Trend detected in {metricName}");
            sb.AppendLine($"Type: {trend.TrendType} (Confidence: {trend.Confidence:P0})");
            sb.AppendLine($"Change Rate: {trend.ChangeRate:P2} per hour");

            if (trend.Seasonality.HasSeasonality)
            {
                sb.AppendLine($"Seasonality detected with period of {trend.Seasonality.Period} intervals");
                sb.AppendLine($"Seasonal strength: {trend.Seasonality.Strength:F2}");
            }

            if (trend.PredictedValues.Any())
            {
                sb.AppendLine("\nPredictions:");
                foreach (var prediction in trend.PredictedValues.Take(3))
                {
                    sb.AppendLine($"- {prediction.Timestamp:HH:mm}: {prediction.Value:F2} (Confidence: {prediction.Confidence:P0})");
                }
            }

            return sb.ToString();
        }

        private AlertSeverity DetermineAlertSeverity(AnomalyDetectionResult result)
        {
            if (result.AnomalyScore > 0.8 && result.Confidence > 0.8)
                return AlertSeverity.Critical;
            if (result.AnomalyScore > 0.6 && result.Confidence > 0.6)
                return AlertSeverity.Warning;
            return AlertSeverity.Information;
        }

        private string GenerateAnomalyMessage(string metricName, AnomalyDetectionResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Anomaly detected in {metricName}");
            sb.AppendLine($"Score: {result.AnomalyScore:F2} (Confidence: {result.Confidence:P0})");
            sb.AppendLine("Contributing Factors:");
            
            foreach (var factor in result.ContributingFactors.Take(3)) // Top 3 factors
            {
                sb.AppendLine($"- {factor.FactorType}: {factor.Description}");
            }

            return sb.ToString();
        }

        private async Task CheckResourceOptimizationAsync(MetricSample metric)
        {
            var history = await _metricHistoryManager.GetMetricHistoryAsync(
                metric.MetricName,
                DateTime.UtcNow - TimeSpan.FromHours(24));

            if (!history.Any())
                return;

            var optimization = AnalyzeResourceOptimization(metric, history);
            if (optimization != null)
            {
                await _alertService.RaiseAlert(new Alert
                {
                    Severity = AlertSeverity.Information,
                    Source = "ResourceOptimization",
                    Message = optimization.Recommendation,
                    Context = optimization.Context
                });
            }
        }

        private OptimizationRecommendation AnalyzeResourceOptimization(
            MetricSample metric,
            IReadOnlyList<MetricDataPoint> history)
        {
            var analysis = AnalyzeUtilization(history);

            // CPU Optimization
            if (metric.MetricName == "resource.cpuUsage")
            {
                if (analysis.AverageUtilization > 80)
                {
                    return new OptimizationRecommendation
                    {
                        ResourceType = "CPU",
                        Recommendation = "Consider scaling up CPU resources or optimizing CPU-intensive operations",
                        Context = new Dictionary<string, object>
                        {
                            ["currentUsage"] = metric.Value,
                            ["averageUsage"] = analysis.AverageUtilization,
                            ["peakUsage"] = analysis.PeakUtilization
                        }
                    };
                }
            }

            // Memory Optimization
            if (metric.MetricName == "resource.memoryUsagePercent")
            {
                if (analysis.AverageUtilization > 85)
                {
                    return new OptimizationRecommendation
                    {
                        ResourceType = "Memory",
                        Recommendation = "Consider increasing memory allocation or implementing memory optimization strategies",
                        Context = new Dictionary<string, object>
                        {
                            ["currentUsage"] = metric.Value,
                            ["averageUsage"] = analysis.AverageUtilization,
                            ["leakProbability"] = analysis.TrendSlope > 0.1 ? "High" : "Low"
                        }
                    };
                }
            }

            return null;
        }

        private async Task GeneratePerformanceInsightsAsync()
        {
            var insights = new List<PerformanceInsight>();

            // Analyze response times
            var responseTimeHistory = await _metricHistoryManager.GetMetricHistoryAsync(
                "process.responseTime",
                DateTime.UtcNow - TimeSpan.FromHours(24));

            if (responseTimeHistory.Any())
            {
                var analysis = AnalyzePerformance(responseTimeHistory);
                if (analysis.HasAnomalies)
                {
                    insights.Add(new PerformanceInsight
                    {
                        Category = "Response Time",
                        Description = "Response time anomalies detected",
                        Severity = analysis.AnomalyCount > 5 ? InsightSeverity.High : InsightSeverity.Medium,
                        Recommendations = new[]
                        {
                            "Review database query performance",
                            "Check for network latency issues",
                            "Monitor external service dependencies"
                        }
                    });
                }
            }

            // Analyze error rates
            var errorRateHistory = await _metricHistoryManager.GetMetricHistoryAsync(
                "process.errorRate",
                DateTime.UtcNow - TimeSpan.FromHours(24));

            if (errorRateHistory.Any())
            {
                var analysis = AnalyzeErrors(errorRateHistory);
                if (analysis.ErrorRate > 0.01) // More than 1% error rate
                {
                    insights.Add(new PerformanceInsight
                    {
                        Category = "Error Rate",
                        Description = $"High error rate detected: {analysis.ErrorRate:P2}",
                        Severity = analysis.ErrorRate > 0.05 ? InsightSeverity.High : InsightSeverity.Medium,
                        Recommendations = new[]
                        {
                            "Review error logs for common patterns",
                            "Check for recent deployments or changes",
                            "Monitor external dependencies"
                        }
                    });
                }
            }

            // Record insights
            foreach (var insight in insights)
            {
                await _alertService.RaiseAlert(new Alert
                {
                    Severity = ConvertInsightSeverityToAlertSeverity(insight.Severity),
                    Source = "PerformanceInsights",
                    Message = insight.Description,
                    Context = new Dictionary<string, object>
                    {
                        ["category"] = insight.Category,
                        ["recommendations"] = string.Join("; ", insight.Recommendations)
                    }
                });
            }
        }

        private TrendAnalysis AnalyzeTrend(IReadOnlyList<MetricDataPoint> history)
        {
            if (history.Count < 2)
                return new TrendAnalysis();

            var values = history.Select(p => p.Value).ToList();
            var mean = values.Average();
            var slope = CalculateSlope(history);

            return new TrendAnalysis
            {
                TrendType = slope > 0 ? TrendType.Increasing : TrendType.Decreasing,
                ChangeRate = Math.Abs(slope),
                HistoricalMean = mean
            };
        }

        private UtilizationAnalysis AnalyzeUtilization(IReadOnlyList<MetricDataPoint> history)
        {
            if (!history.Any())
                return new UtilizationAnalysis();

            var values = history.Select(p => p.Value).ToList();
            return new UtilizationAnalysis
            {
                AverageUtilization = values.Average(),
                PeakUtilization = values.Max(),
                TrendSlope = CalculateSlope(history)
            };
        }

        private PerformanceAnalysis AnalyzePerformance(IReadOnlyList<MetricDataPoint> history)
        {
            if (history.Count < 2)
                return new PerformanceAnalysis();

            var values = history.Select(p => p.Value).ToList();
            var mean = values.Average();
            var stdDev = CalculateStandardDeviation(values, mean);
            var anomalies = values.Count(v => Math.Abs(v - mean) > 2 * stdDev);

            return new PerformanceAnalysis
            {
                HasAnomalies = anomalies > 0,
                AnomalyCount = anomalies,
                AverageMeasurement = mean,
                StandardDeviation = stdDev
            };
        }

        private ErrorAnalysis AnalyzeErrors(IReadOnlyList<MetricDataPoint> history)
        {
            if (!history.Any())
                return new ErrorAnalysis();

            var totalErrors = history.Sum(p => p.Value);
            var totalRequests = history.Count;

            return new ErrorAnalysis
            {
                ErrorRate = totalErrors / totalRequests,
                TotalErrors = totalErrors,
                TimeSpan = history.Last().Timestamp - history.First().Timestamp
            };
        }

        private double CalculateSlope(IReadOnlyList<MetricDataPoint> points)
        {
            if (points.Count < 2)
                return 0;

            var x = Enumerable.Range(0, points.Count).Select(i => (double)i).ToList();
            var y = points.Select(p => p.Value).ToList();

            var meanX = x.Average();
            var meanY = y.Average();

            var numerator = x.Zip(y, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
            var denominator = x.Sum(xi => Math.Pow(xi - meanX, 2));

            return denominator == 0 ? 0 : numerator / denominator;
        }

        private double CalculateStandardDeviation(List<double> values, double mean)
        {
            if (values.Count < 2)
                return 0;

            var sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }

        private AlertSeverity ConvertInsightSeverityToAlertSeverity(InsightSeverity severity)
        {
            return severity switch
            {
                InsightSeverity.High => AlertSeverity.Warning,
                InsightSeverity.Medium => AlertSeverity.Information,
                InsightSeverity.Low => AlertSeverity.Information,
                _ => AlertSeverity.Information
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Monitoring Analysis Service started");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Monitoring Analysis Service stopped");
            _analysisTimer?.Change(Timeout.Infinite, 0);
            (_metricHistoryManager as IDisposable)?.Dispose();
            return Task.CompletedTask;
        }
    }

    public class MetricHistory
    {
        private readonly Queue<MetricDataPoint> _dataPoints;
        private readonly int _maxDataPoints;
        private readonly object _lock = new();

        public MetricHistory(int maxDataPoints)
        {
            _maxDataPoints = maxDataPoints;
            _dataPoints = new Queue<MetricDataPoint>(_maxDataPoints);
        }

        public void AddDataPoint(MetricDataPoint point)
        {
            lock (_lock)
            {
                _dataPoints.Enqueue(point);
                while (_dataPoints.Count > _maxDataPoints)
                {
                    _dataPoints.Dequeue();
                }
            }
        }

        public TrendAnalysis AnalyzeTrend()
        {
            lock (_lock)
            {
                var points = _dataPoints.ToList();
                if (points.Count < 2)
                    return new TrendAnalysis();

                var values = points.Select(p => p.Value).ToList();
                var mean = values.Average();
                var slope = CalculateSlope(points);

                return new TrendAnalysis
                {
                    TrendType = slope > 0 ? TrendType.Increasing : TrendType.Decreasing,
                    ChangeRate = Math.Abs(slope),
                    HistoricalMean = mean
                };
            }
        }

        public UtilizationAnalysis AnalyzeUtilization()
        {
            lock (_lock)
            {
                var points = _dataPoints.ToList();
                if (!points.Any())
                    return new UtilizationAnalysis();

                var values = points.Select(p => p.Value).ToList();
                return new UtilizationAnalysis
                {
                    AverageUtilization = values.Average(),
                    PeakUtilization = values.Max(),
                    TrendSlope = CalculateSlope(points)
                };
            }
        }

        public PerformanceAnalysis AnalyzePerformance()
        {
            lock (_lock)
            {
                var points = _dataPoints.ToList();
                if (points.Count < 2)
                    return new PerformanceAnalysis();

                var values = points.Select(p => p.Value).ToList();
                var mean = values.Average();
                var stdDev = CalculateStandardDeviation(values, mean);
                var anomalies = values.Count(v => Math.Abs(v - mean) > 2 * stdDev);

                return new PerformanceAnalysis
                {
                    HasAnomalies = anomalies > 0,
                    AnomalyCount = anomalies,
                    AverageMeasurement = mean,
                    StandardDeviation = stdDev
                };
            }
        }

        public ErrorAnalysis AnalyzeErrors()
        {
            lock (_lock)
            {
                var points = _dataPoints.ToList();
                if (!points.Any())
                    return new ErrorAnalysis();

                var totalErrors = points.Sum(p => p.Value);
                var totalRequests = points.Count;

                return new ErrorAnalysis
                {
                    ErrorRate = totalErrors / totalRequests,
                    TotalErrors = totalErrors,
                    TimeSpan = points.Last().Timestamp - points.First().Timestamp
                };
            }
        }

        private double CalculateSlope(List<MetricDataPoint> points)
        {
            if (points.Count < 2)
                return 0;

            var x = Enumerable.Range(0, points.Count).Select(i => (double)i).ToList();
            var y = points.Select(p => p.Value).ToList();

            var meanX = x.Average();
            var meanY = y.Average();

            var numerator = x.Zip(y, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
            var denominator = x.Sum(xi => Math.Pow(xi - meanX, 2));

            return denominator == 0 ? 0 : numerator / denominator;
        }

        private double CalculateStandardDeviation(List<double> values, double mean)
        {
            if (values.Count < 2)
                return 0;

            var sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }
    }

    public class MetricDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
        public Dictionary<string, object> Tags { get; set; }
    }

    public class TrendAnalysis
    {
        public TrendType TrendType { get; set; }
        public double ChangeRate { get; set; }
        public double HistoricalMean { get; set; }
    }

    public class UtilizationAnalysis
    {
        public double AverageUtilization { get; set; }
        public double PeakUtilization { get; set; }
        public double TrendSlope { get; set; }
    }

    public class PerformanceAnalysis
    {
        public bool HasAnomalies { get; set; }
        public int AnomalyCount { get; set; }
        public double AverageMeasurement { get; set; }
        public double StandardDeviation { get; set; }
    }

    public class ErrorAnalysis
    {
        public double ErrorRate { get; set; }
        public double TotalErrors { get; set; }
        public TimeSpan TimeSpan { get; set; }
    }

    public class OptimizationRecommendation
    {
        public string ResourceType { get; set; }
        public string Recommendation { get; set; }
        public Dictionary<string, object> Context { get; set; }
    }

    public class PerformanceInsight
    {
        public string Category { get; set; }
        public string Description { get; set; }
        public InsightSeverity Severity { get; set; }
        public string[] Recommendations { get; set; }
    }

    public enum TrendType
    {
        Increasing,
        Decreasing,
        Stable
    }

    public enum InsightSeverity
    {
        Low,
        Medium,
        High
    }
} 