using System.Collections.Concurrent;
using MathNet.Numerics.Statistics;

namespace PollSystem.Services.Diagnostics
{
    public class PerformanceAnomalyDetectionService
    {
        private readonly ILogger<PerformanceAnomalyDetectionService> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly IAlertManagementService _alertService;
        private readonly ConcurrentDictionary<string, PerformanceBaseline> _baselines;
        private readonly AnomalyDetectionOptions _options;

        public PerformanceAnomalyDetectionService(
            ILogger<PerformanceAnomalyDetectionService> logger,
            IMetricsCollector metricsCollector,
            IAlertManagementService alertService,
            IOptions<AnomalyDetectionOptions> options)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _alertService = alertService;
            _options = options.Value;
            _baselines = new ConcurrentDictionary<string, PerformanceBaseline>();
        }

        public async Task<AnomalyDetectionResult> AnalyzeMetricAsync(
            string metricName,
            IReadOnlyList<MetricDataPoint> history,
            MetricDataPoint currentPoint)
        {
            try
            {
                var baseline = _baselines.GetOrAdd(metricName, _ => new PerformanceBaseline());
                baseline.UpdateBaseline(history);

                var detectors = new List<IAnomalyDetector>
                {
                    new StatisticalAnomalyDetector(_options),
                    new ThresholdAnomalyDetector(_options),
                    new TrendBasedAnomalyDetector(_options),
                    new SeasonalAnomalyDetector(_options)
                };

                var context = new AnomalyDetectionContext
                {
                    MetricName = metricName,
                    History = history,
                    CurrentPoint = currentPoint,
                    Baseline = baseline
                };

                var results = await Task.WhenAll(
                    detectors.Select(detector => 
                        DetectWithMethodAsync(detector, context)));

                var combinedResult = CombineDetectionResults(results, context);

                if (combinedResult.IsAnomaly)
                {
                    await RaiseAnomalyAlert(combinedResult);
                }

                return combinedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing metric {MetricName} for anomalies", metricName);
                throw;
            }
        }

        private async Task<AnomalyDetectionResult> DetectWithMethodAsync(
            IAnomalyDetector detector,
            AnomalyDetectionContext context)
        {
            return await Task.Run(() => detector.DetectAnomaly(context));
        }

        private AnomalyDetectionResult CombineDetectionResults(
            IEnumerable<AnomalyDetectionResult> results,
            AnomalyDetectionContext context)
        {
            var anomalyResults = results.Where(r => r.IsAnomaly).ToList();
            var overallConfidence = CalculateOverallConfidence(anomalyResults);
            var contributingFactors = AggregateContributingFactors(anomalyResults);

            return new AnomalyDetectionResult
            {
                MetricName = context.MetricName,
                IsAnomaly = anomalyResults.Any(),
                AnomalyScore = CalculateAnomalyScore(anomalyResults),
                Confidence = overallConfidence,
                Value = context.CurrentPoint.Value,
                Timestamp = context.CurrentPoint.Timestamp,
                DetectionMethods = anomalyResults.Select(r => r.DetectionMethod).ToList(),
                ContributingFactors = contributingFactors,
                Severity = DetermineAnomalySeverity(anomalyResults, overallConfidence),
                Recommendations = GenerateRecommendations(anomalyResults, context)
            };
        }

        private double CalculateOverallConfidence(List<AnomalyDetectionResult> results)
        {
            if (!results.Any())
                return 0;

            // Weight confidences by detection method reliability
            var weightedConfidences = results.Select(r => r.Confidence * GetMethodWeight(r.DetectionMethod));
            return weightedConfidences.Average();
        }

        private double GetMethodWeight(string detectionMethod)
        {
            return detectionMethod switch
            {
                "Statistical" => 0.4,
                "Threshold" => 0.2,
                "TrendBased" => 0.3,
                "Seasonal" => 0.1,
                _ => 0.1
            };
        }

        private double CalculateAnomalyScore(List<AnomalyDetectionResult> results)
        {
            if (!results.Any())
                return 0;

            var weightedScores = results.Select(r => r.AnomalyScore * GetMethodWeight(r.DetectionMethod));
            return weightedScores.Average();
        }

        private List<AnomalyFactor> AggregateContributingFactors(
            List<AnomalyDetectionResult> results)
        {
            var factorGroups = results
                .SelectMany(r => r.ContributingFactors)
                .GroupBy(f => f.FactorType)
                .Select(g => new AnomalyFactor
                {
                    FactorType = g.Key,
                    Description = CombineFactorDescriptions(g),
                    Confidence = g.Average(f => f.Confidence),
                    Impact = g.Max(f => f.Impact)
                })
                .OrderByDescending(f => f.Impact)
                .ThenByDescending(f => f.Confidence)
                .ToList();

            return factorGroups;
        }

        private string CombineFactorDescriptions(IEnumerable<AnomalyFactor> factors)
        {
            var descriptions = factors.Select(f => f.Description).Distinct();
            return string.Join("; ", descriptions);
        }

        private AnomalySeverity DetermineAnomalySeverity(
            List<AnomalyDetectionResult> results,
            double overallConfidence)
        {
            if (!results.Any())
                return AnomalySeverity.None;

            var maxScore = results.Max(r => r.AnomalyScore);
            var methodCount = results.Count;

            if (maxScore > 0.9 && overallConfidence > 0.8)
                return AnomalySeverity.Critical;
            if (maxScore > 0.7 && overallConfidence > 0.7)
                return AnomalySeverity.High;
            if (maxScore > 0.5 && overallConfidence > 0.6)
                return AnomalySeverity.Medium;
            return AnomalySeverity.Low;
        }

        private List<AnomalyRecommendation> GenerateRecommendations(
            List<AnomalyDetectionResult> results,
            AnomalyDetectionContext context)
        {
            var recommendations = new List<AnomalyRecommendation>();

            // Analyze severity and patterns
            var severity = DetermineAnomalySeverity(results, CalculateOverallConfidence(results));
            if (severity >= AnomalySeverity.High)
            {
                recommendations.Add(new AnomalyRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Category = "Performance",
                    Description = "Critical performance anomaly detected",
                    Action = "Investigate system resources and recent changes",
                    Impact = "System stability and user experience"
                });
            }

            // Analyze contributing factors
            var factors = AggregateContributingFactors(results);
            foreach (var factor in factors.Where(f => f.Impact > 0.7))
            {
                recommendations.Add(GenerateFactorBasedRecommendation(factor));
            }

            // Analyze trends
            var trend = AnalyzeMetricTrend(context.History);
            if (trend.TrendType == TrendType.Increasing && trend.Confidence > 0.7)
            {
                recommendations.Add(new AnomalyRecommendation
                {
                    Priority = RecommendationPriority.Medium,
                    Category = "Trend",
                    Description = "Increasing performance degradation trend",
                    Action = "Review system capacity and implement scaling strategies",
                    Impact = "Long-term system performance"
                });
            }

            return recommendations;
        }

        private AnomalyRecommendation GenerateFactorBasedRecommendation(AnomalyFactor factor)
        {
            return new AnomalyRecommendation
            {
                Priority = factor.Impact > 0.8 ? RecommendationPriority.High : RecommendationPriority.Medium,
                Category = factor.FactorType,
                Description = $"Performance impact detected: {factor.Description}",
                Action = GenerateActionForFactor(factor),
                Impact = $"System {factor.FactorType.ToLower()} performance"
            };
        }

        private string GenerateActionForFactor(AnomalyFactor factor)
        {
            return factor.FactorType switch
            {
                "CPU" => "Optimize CPU-intensive operations or scale compute resources",
                "Memory" => "Investigate memory leaks and optimize memory usage",
                "Disk" => "Review I/O patterns and optimize storage operations",
                "Network" => "Analyze network traffic and optimize data transfer",
                _ => "Investigate and optimize affected component"
            };
        }

        private TrendAnalysis AnalyzeMetricTrend(IReadOnlyList<MetricDataPoint> history)
        {
            if (history.Count < 2)
                return new TrendAnalysis();

            var x = Enumerable.Range(0, history.Count).Select(i => (double)i).ToArray();
            var y = history.Select(p => p.Value).ToArray();
            var slope = SimpleRegression.Fit(x, y).Item2;

            var values = history.Select(p => p.Value).ToList();
            var volatility = values.StandardDeviation() / values.Average();
            var confidence = Math.Max(0, 1 - (volatility / _options.VolatilityThreshold));

            return new TrendAnalysis
            {
                TrendType = DetermineTrendType(slope),
                Slope = slope,
                Volatility = volatility,
                PredictedValue = history.Last().Value + (slope * _options.PredictionHorizon),
                Confidence = confidence
            };
        }

        private TrendType DetermineTrendType(double slope)
        {
            if (Math.Abs(slope) < _options.TrendThreshold)
                return TrendType.Stable;
            return slope > 0 ? TrendType.Increasing : TrendType.Decreasing;
        }

        private async Task RaiseAnomalyAlert(AnomalyDetectionResult anomaly)
        {
            await _alertService.RaiseAlert(new Alert
            {
                Severity = ConvertAnomalySeverityToAlertSeverity(anomaly.Severity),
                Source = "PerformanceAnomalyDetection",
                Message = GenerateAlertMessage(anomaly),
                Context = new Dictionary<string, object>
                {
                    ["metricName"] = anomaly.MetricName,
                    ["anomalyScore"] = anomaly.AnomalyScore,
                    ["confidence"] = anomaly.Confidence,
                    ["detectionMethods"] = anomaly.DetectionMethods,
                    ["contributingFactors"] = anomaly.ContributingFactors
                        .Select(f => $"{f.FactorType}: {f.Description}")
                        .ToList()
                }
            });
        }

        private AlertSeverity ConvertAnomalySeverityToAlertSeverity(AnomalySeverity severity)
        {
            return severity switch
            {
                AnomalySeverity.Critical => AlertSeverity.Critical,
                AnomalySeverity.High => AlertSeverity.High,
                AnomalySeverity.Medium => AlertSeverity.Warning,
                _ => AlertSeverity.Information
            };
        }

        private string GenerateAlertMessage(AnomalyDetectionResult anomaly)
        {
            var message = new StringBuilder();
            message.AppendLine($"Performance anomaly detected in {anomaly.MetricName}");
            message.AppendLine($"Score: {anomaly.AnomalyScore:F2} (Confidence: {anomaly.Confidence:P0})");
            
            if (anomaly.ContributingFactors.Any())
            {
                message.AppendLine("\nContributing Factors:");
                foreach (var factor in anomaly.ContributingFactors.Take(3))
                {
                    message.AppendLine($"- {factor.FactorType}: {factor.Description}");
                }
            }

            return message.ToString();
        }
    }

    public interface IAnomalyDetector
    {
        string DetectionMethod { get; }
        AnomalyDetectionResult DetectAnomaly(AnomalyDetectionContext context);
    }

    public class AnomalyDetectionContext
    {
        public string MetricName { get; init; }
        public IReadOnlyList<MetricDataPoint> History { get; init; }
        public MetricDataPoint CurrentPoint { get; init; }
        public PerformanceBaseline Baseline { get; init; }
    }

    public class PerformanceBaseline
    {
        private readonly ConcurrentDictionary<string, double> _baselineValues = new();

        public void UpdateBaseline(IReadOnlyList<MetricDataPoint> points)
        {
            if (!points.Any())
                return;

            var values = points.Select(p => p.Value).ToList();
            _baselineValues["mean"] = values.Average();
            _baselineValues["median"] = values.Median();
            _baselineValues["stddev"] = values.StandardDeviation();
            _baselineValues["p95"] = values.Percentile(95);
            _baselineValues["p99"] = values.Percentile(99);
        }

        public double GetBaselineValue(string key) =>
            _baselineValues.TryGetValue(key, out var value) ? value : 0;
    }

    public class AnomalyDetectionResult
    {
        public string MetricName { get; init; }
        public bool IsAnomaly { get; init; }
        public double AnomalyScore { get; init; }
        public double Confidence { get; init; }
        public double Value { get; init; }
        public DateTime Timestamp { get; init; }
        public string DetectionMethod { get; init; }
        public List<string> DetectionMethods { get; init; } = new();
        public List<AnomalyFactor> ContributingFactors { get; init; } = new();
        public AnomalySeverity Severity { get; init; }
        public List<AnomalyRecommendation> Recommendations { get; init; } = new();
    }

    public class AnomalyFactor
    {
        public string FactorType { get; init; }
        public string Description { get; init; }
        public double Confidence { get; init; }
        public double Impact { get; init; }
    }

    public class AnomalyRecommendation
    {
        public RecommendationPriority Priority { get; init; }
        public string Category { get; init; }
        public string Description { get; init; }
        public string Action { get; init; }
        public string Impact { get; init; }
    }

    public class AnomalyDetectionOptions
    {
        public double StatisticalThreshold { get; set; } = 3.0;
        public double TrendThreshold { get; set; } = 0.01;
        public double VolatilityThreshold { get; set; } = 0.3;
        public int PredictionHorizon { get; set; } = 12;
        public double MinConfidenceThreshold { get; set; } = 0.6;
        public int MinDataPointsForAnalysis { get; set; } = 10;
    }

    public enum AnomalySeverity
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }
} 