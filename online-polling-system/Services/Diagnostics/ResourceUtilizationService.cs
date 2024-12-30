using System.Collections.Concurrent;
using MathNet.Numerics.Statistics;

namespace PollSystem.Services.Diagnostics
{
    public class ResourceUtilizationService
    {
        private readonly ILogger<ResourceUtilizationService> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly ConcurrentDictionary<string, ResourceBaseline> _resourceBaselines;
        private readonly ResourceUtilizationOptions _options;

        public ResourceUtilizationService(
            ILogger<ResourceUtilizationService> logger,
            IMetricsCollector metricsCollector,
            IOptions<ResourceUtilizationOptions> options)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _options = options.Value;
            _resourceBaselines = new ConcurrentDictionary<string, ResourceBaseline>();
        }

        public async Task<ResourceAnalysisResult> AnalyzeResourceUtilizationAsync(
            string resourceType,
            IReadOnlyList<MetricDataPoint> history,
            MetricDataPoint currentPoint)
        {
            try
            {
                var baseline = _resourceBaselines.GetOrAdd(resourceType, _ => new ResourceBaseline());
                baseline.UpdateBaseline(history);

                var analyses = new List<IResourceAnalysis>
                {
                    new CpuAnalysis(_options),
                    new MemoryAnalysis(_options),
                    new DiskAnalysis(_options),
                    new NetworkAnalysis(_options)
                };

                var analysisContext = new ResourceAnalysisContext
                {
                    ResourceType = resourceType,
                    History = history,
                    CurrentPoint = currentPoint,
                    Baseline = baseline
                };

                var results = await Task.WhenAll(
                    analyses.Select(analysis => 
                        AnalyzeWithMethodAsync(analysis, analysisContext)));

                return CombineAnalysisResults(results, analysisContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing resource utilization for {ResourceType}", resourceType);
                throw;
            }
        }

        private async Task<ResourceAnalysisResult> AnalyzeWithMethodAsync(
            IResourceAnalysis analysis,
            ResourceAnalysisContext context)
        {
            return await Task.Run(() => analysis.Analyze(context));
        }

        private ResourceAnalysisResult CombineAnalysisResults(
            IEnumerable<ResourceAnalysisResult> results,
            ResourceAnalysisContext context)
        {
            var utilizationLevels = results
                .Where(r => r.UtilizationLevel != UtilizationLevel.Unknown)
                .ToList();

            var dominantLevel = DetermineDominantUtilizationLevel(utilizationLevels);
            var recommendations = GenerateRecommendations(utilizationLevels, context);
            var bottlenecks = IdentifyBottlenecks(utilizationLevels);

            return new ResourceAnalysisResult
            {
                ResourceType = context.ResourceType,
                UtilizationLevel = dominantLevel,
                CurrentUtilization = context.CurrentPoint.Value,
                AverageUtilization = context.History.Average(p => p.Value),
                PeakUtilization = context.History.Max(p => p.Value),
                BottleneckInfo = bottlenecks,
                Recommendations = recommendations,
                ResourceMetrics = CalculateResourceMetrics(context),
                UtilizationTrend = AnalyzeUtilizationTrend(context),
                CapacityInfo = AnalyzeCapacity(context, utilizationLevels)
            };
        }

        private UtilizationLevel DetermineDominantUtilizationLevel(
            List<ResourceAnalysisResult> results)
        {
            if (!results.Any())
                return UtilizationLevel.Normal;

            var levelCounts = results
                .GroupBy(r => r.UtilizationLevel)
                .ToDictionary(g => g.Key, g => g.Count());

            return levelCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenByDescending(kvp => (int)kvp.Key)
                .First()
                .Key;
        }

        private List<ResourceRecommendation> GenerateRecommendations(
            List<ResourceAnalysisResult> results,
            ResourceAnalysisContext context)
        {
            var recommendations = new List<ResourceRecommendation>();

            // Analyze high utilization patterns
            if (results.Any(r => r.UtilizationLevel == UtilizationLevel.High))
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Category = "Scaling",
                    Description = "Consider scaling up resources due to sustained high utilization",
                    Impact = "Improve performance and reduce bottlenecks",
                    Implementation = GenerateScalingRecommendation(context)
                });
            }

            // Check for efficiency improvements
            if (results.Any(r => r.UtilizationLevel == UtilizationLevel.Inefficient))
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.Medium,
                    Category = "Optimization",
                    Description = "Resource usage patterns indicate potential inefficiencies",
                    Impact = "Reduce resource waste and improve cost efficiency",
                    Implementation = GenerateOptimizationRecommendation(context)
                });
            }

            // Analyze resource distribution
            var utilizationVariance = CalculateUtilizationVariance(context);
            if (utilizationVariance > _options.VarianceThreshold)
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.Medium,
                    Category = "Distribution",
                    Description = "High variance in resource utilization detected",
                    Impact = "Improve resource distribution and stability",
                    Implementation = "Consider implementing load balancing or resource pooling"
                });
            }

            return recommendations;
        }

        private BottleneckInfo IdentifyBottlenecks(List<ResourceAnalysisResult> results)
        {
            var bottlenecks = results
                .Where(r => r.UtilizationLevel == UtilizationLevel.High)
                .Select(r => new ResourceBottleneck
                {
                    ResourceType = r.ResourceType,
                    Severity = CalculateBottleneckSeverity(r),
                    Impact = "Performance degradation and increased latency",
                    Resolution = GenerateBottleneckResolution(r)
                })
                .ToList();

            return new BottleneckInfo
            {
                HasBottlenecks = bottlenecks.Any(),
                Bottlenecks = bottlenecks,
                OverallSeverity = bottlenecks.Any() 
                    ? bottlenecks.Max(b => b.Severity) 
                    : BottleneckSeverity.None
            };
        }

        private ResourceMetrics CalculateResourceMetrics(ResourceAnalysisContext context)
        {
            var values = context.History.Select(p => p.Value).ToList();
            var timeSpan = context.History.Last().Timestamp - context.History.First().Timestamp;

            return new ResourceMetrics
            {
                Mean = values.Average(),
                Median = values.Median(),
                StandardDeviation = values.StandardDeviation(),
                Percentile95 = values.Percentile(95),
                Percentile99 = values.Percentile(99),
                UtilizationRate = values.Average() / 100,
                SamplePeriod = timeSpan,
                SampleCount = values.Count
            };
        }

        private UtilizationTrend AnalyzeUtilizationTrend(ResourceAnalysisContext context)
        {
            var recentPoints = context.History.TakeLast(_options.TrendWindowSize).ToList();
            if (recentPoints.Count < 2)
                return new UtilizationTrend { TrendType = TrendType.Stable };

            var slope = CalculateUtilizationSlope(recentPoints);
            var volatility = CalculateUtilizationVolatility(recentPoints);

            return new UtilizationTrend
            {
                TrendType = DetermineTrendType(slope),
                Slope = slope,
                Volatility = volatility,
                PredictedUtilization = PredictFutureUtilization(recentPoints, slope),
                Confidence = CalculateTrendConfidence(volatility)
            };
        }

        private CapacityInfo AnalyzeCapacity(
            ResourceAnalysisContext context,
            List<ResourceAnalysisResult> results)
        {
            var currentUtilization = context.CurrentPoint.Value;
            var peakUtilization = context.History.Max(p => p.Value);
            var utilizationTrend = AnalyzeUtilizationTrend(context);

            return new CapacityInfo
            {
                CurrentCapacity = 100 - currentUtilization,
                PeakCapacityUsed = peakUtilization,
                EstimatedTimeToCapacity = EstimateTimeToCapacity(
                    currentUtilization,
                    utilizationTrend),
                RiskLevel = DetermineCapacityRiskLevel(
                    currentUtilization,
                    peakUtilization,
                    utilizationTrend),
                RecommendedAction = GenerateCapacityRecommendation(
                    currentUtilization,
                    utilizationTrend)
            };
        }

        private double CalculateUtilizationVariance(ResourceAnalysisContext context)
        {
            var values = context.History.Select(p => p.Value).ToList();
            return values.Variance();
        }

        private BottleneckSeverity CalculateBottleneckSeverity(ResourceAnalysisResult result)
        {
            if (result.CurrentUtilization > 90)
                return BottleneckSeverity.Critical;
            if (result.CurrentUtilization > 80)
                return BottleneckSeverity.High;
            if (result.CurrentUtilization > 70)
                return BottleneckSeverity.Medium;
            return BottleneckSeverity.Low;
        }

        private string GenerateBottleneckResolution(ResourceAnalysisResult result)
        {
            return result.ResourceType switch
            {
                "CPU" => "Scale up CPU resources or optimize CPU-intensive operations",
                "Memory" => "Increase memory allocation or implement memory optimization",
                "Disk" => "Optimize I/O operations or increase storage capacity",
                "Network" => "Optimize network usage or increase bandwidth",
                _ => "Analyze resource usage patterns and implement appropriate optimizations"
            };
        }

        private string GenerateScalingRecommendation(ResourceAnalysisContext context)
        {
            var currentUtilization = context.CurrentPoint.Value;
            var utilizationTrend = AnalyzeUtilizationTrend(context);

            if (utilizationTrend.TrendType == TrendType.Increasing && currentUtilization > 80)
                return "Implement vertical scaling to handle increasing load";
            if (currentUtilization > 90)
                return "Immediate resource expansion recommended";
            return "Monitor resource usage and plan for capacity expansion";
        }

        private string GenerateOptimizationRecommendation(ResourceAnalysisContext context)
        {
            var metrics = CalculateResourceMetrics(context);
            var variance = CalculateUtilizationVariance(context);

            if (variance > _options.VarianceThreshold)
                return "Implement resource pooling to improve utilization stability";
            if (metrics.UtilizationRate < 0.3)
                return "Consider resource consolidation to improve efficiency";
            return "Review resource allocation policies and implement optimizations";
        }

        private double CalculateUtilizationSlope(List<MetricDataPoint> points)
        {
            var x = Enumerable.Range(0, points.Count).Select(i => (double)i).ToArray();
            var y = points.Select(p => p.Value).ToArray();
            return SimpleRegression.Fit(x, y).Item2;
        }

        private double CalculateUtilizationVolatility(List<MetricDataPoint> points)
        {
            var values = points.Select(p => p.Value).ToList();
            return values.StandardDeviation() / values.Average();
        }

        private TrendType DetermineTrendType(double slope)
        {
            if (Math.Abs(slope) < _options.TrendThreshold)
                return TrendType.Stable;
            return slope > 0 ? TrendType.Increasing : TrendType.Decreasing;
        }

        private double PredictFutureUtilization(List<MetricDataPoint> points, double slope)
        {
            var lastValue = points.Last().Value;
            return lastValue + (slope * _options.PredictionHorizon);
        }

        private double CalculateTrendConfidence(double volatility)
        {
            return Math.Max(0, 1 - (volatility / _options.VolatilityThreshold));
        }

        private TimeSpan? EstimateTimeToCapacity(double currentUtilization, UtilizationTrend trend)
        {
            if (trend.TrendType != TrendType.Increasing || trend.Slope <= 0)
                return null;

            var remainingCapacity = 100 - currentUtilization;
            var hoursToCapacity = remainingCapacity / (trend.Slope * 60);
            return TimeSpan.FromHours(hoursToCapacity);
        }

        private CapacityRiskLevel DetermineCapacityRiskLevel(
            double currentUtilization,
            double peakUtilization,
            UtilizationTrend trend)
        {
            if (currentUtilization > 90 || peakUtilization > 95)
                return CapacityRiskLevel.Critical;
            if (currentUtilization > 80 && trend.TrendType == TrendType.Increasing)
                return CapacityRiskLevel.High;
            if (currentUtilization > 70)
                return CapacityRiskLevel.Medium;
            return CapacityRiskLevel.Low;
        }

        private string GenerateCapacityRecommendation(
            double currentUtilization,
            UtilizationTrend trend)
        {
            if (currentUtilization > 90)
                return "Immediate capacity expansion required";
            if (currentUtilization > 80 && trend.TrendType == TrendType.Increasing)
                return "Plan for capacity expansion within next maintenance window";
            if (currentUtilization > 70)
                return "Monitor capacity trends and prepare expansion plans";
            return "Current capacity is sufficient";
        }
    }

    public interface IResourceAnalysis
    {
        string AnalysisType { get; }
        ResourceAnalysisResult Analyze(ResourceAnalysisContext context);
    }

    public class ResourceAnalysisContext
    {
        public string ResourceType { get; init; }
        public IReadOnlyList<MetricDataPoint> History { get; init; }
        public MetricDataPoint CurrentPoint { get; init; }
        public ResourceBaseline Baseline { get; init; }
    }

    public class ResourceAnalysisResult
    {
        public string ResourceType { get; init; }
        public UtilizationLevel UtilizationLevel { get; init; }
        public double CurrentUtilization { get; init; }
        public double AverageUtilization { get; init; }
        public double PeakUtilization { get; init; }
        public BottleneckInfo BottleneckInfo { get; init; }
        public List<ResourceRecommendation> Recommendations { get; init; } = new();
        public ResourceMetrics ResourceMetrics { get; init; }
        public UtilizationTrend UtilizationTrend { get; init; }
        public CapacityInfo CapacityInfo { get; init; }
    }

    public class ResourceBaseline
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

    public class ResourceMetrics
    {
        public double Mean { get; init; }
        public double Median { get; init; }
        public double StandardDeviation { get; init; }
        public double Percentile95 { get; init; }
        public double Percentile99 { get; init; }
        public double UtilizationRate { get; init; }
        public TimeSpan SamplePeriod { get; init; }
        public int SampleCount { get; init; }
    }

    public class UtilizationTrend
    {
        public TrendType TrendType { get; init; }
        public double Slope { get; init; }
        public double Volatility { get; init; }
        public double PredictedUtilization { get; init; }
        public double Confidence { get; init; }
    }

    public class BottleneckInfo
    {
        public bool HasBottlenecks { get; init; }
        public List<ResourceBottleneck> Bottlenecks { get; init; } = new();
        public BottleneckSeverity OverallSeverity { get; init; }
    }

    public class ResourceBottleneck
    {
        public string ResourceType { get; init; }
        public BottleneckSeverity Severity { get; init; }
        public string Impact { get; init; }
        public string Resolution { get; init; }
    }

    public class ResourceRecommendation
    {
        public RecommendationPriority Priority { get; init; }
        public string Category { get; init; }
        public string Description { get; init; }
        public string Impact { get; init; }
        public string Implementation { get; init; }
    }

    public class CapacityInfo
    {
        public double CurrentCapacity { get; init; }
        public double PeakCapacityUsed { get; init; }
        public TimeSpan? EstimatedTimeToCapacity { get; init; }
        public CapacityRiskLevel RiskLevel { get; init; }
        public string RecommendedAction { get; init; }
    }

    public class ResourceUtilizationOptions
    {
        public int TrendWindowSize { get; set; } = 60;
        public double TrendThreshold { get; set; } = 0.01;
        public double VarianceThreshold { get; set; } = 0.2;
        public double VolatilityThreshold { get; set; } = 0.3;
        public int PredictionHorizon { get; set; } = 12;
    }

    public enum UtilizationLevel
    {
        Unknown,
        Low,
        Normal,
        High,
        Critical,
        Inefficient
    }

    public enum BottleneckSeverity
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }

    public enum RecommendationPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum CapacityRiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
} 