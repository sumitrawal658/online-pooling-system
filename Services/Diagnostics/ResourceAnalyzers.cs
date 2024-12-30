using System.Collections.Concurrent;

namespace PollSystem.Services.Diagnostics
{
    public class CpuAnalysis : IResourceAnalysis
    {
        private readonly ResourceUtilizationOptions _options;

        public CpuAnalysis(ResourceUtilizationOptions options)
        {
            _options = options;
        }

        public string AnalysisType => "CPU";

        public ResourceAnalysisResult Analyze(ResourceAnalysisContext context)
        {
            var currentUtilization = context.CurrentPoint.Value;
            var utilizationLevel = DetermineCpuUtilizationLevel(currentUtilization);
            var recommendations = GenerateCpuRecommendations(context);

            return new ResourceAnalysisResult
            {
                ResourceType = "CPU",
                UtilizationLevel = utilizationLevel,
                CurrentUtilization = currentUtilization,
                AverageUtilization = context.History.Average(p => p.Value),
                PeakUtilization = context.History.Max(p => p.Value),
                Recommendations = recommendations
            };
        }

        private UtilizationLevel DetermineCpuUtilizationLevel(double utilization)
        {
            return utilization switch
            {
                > 90 => UtilizationLevel.Critical,
                > 80 => UtilizationLevel.High,
                > 60 => UtilizationLevel.Normal,
                > 20 => UtilizationLevel.Normal,
                > 0 => UtilizationLevel.Low,
                _ => UtilizationLevel.Unknown
            };
        }

        private List<ResourceRecommendation> GenerateCpuRecommendations(ResourceAnalysisContext context)
        {
            var recommendations = new List<ResourceRecommendation>();
            var currentUtilization = context.CurrentPoint.Value;
            var avgUtilization = context.History.Average(p => p.Value);

            if (currentUtilization > 80)
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Category = "Performance",
                    Description = "High CPU utilization detected",
                    Impact = "System performance may be degraded",
                    Implementation = "Consider scaling up CPU resources or optimizing CPU-intensive operations"
                });
            }

            if (avgUtilization > 70)
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.Medium,
                    Category = "Capacity",
                    Description = "Sustained high CPU usage",
                    Impact = "Long-term performance impact",
                    Implementation = "Plan for CPU capacity expansion"
                });
            }

            return recommendations;
        }
    }

    public class MemoryAnalysis : IResourceAnalysis
    {
        private readonly ResourceUtilizationOptions _options;

        public MemoryAnalysis(ResourceUtilizationOptions options)
        {
            _options = options;
        }

        public string AnalysisType => "Memory";

        public ResourceAnalysisResult Analyze(ResourceAnalysisContext context)
        {
            var currentUtilization = context.CurrentPoint.Value;
            var utilizationLevel = DetermineMemoryUtilizationLevel(currentUtilization);
            var recommendations = GenerateMemoryRecommendations(context);

            return new ResourceAnalysisResult
            {
                ResourceType = "Memory",
                UtilizationLevel = utilizationLevel,
                CurrentUtilization = currentUtilization,
                AverageUtilization = context.History.Average(p => p.Value),
                PeakUtilization = context.History.Max(p => p.Value),
                Recommendations = recommendations
            };
        }

        private UtilizationLevel DetermineMemoryUtilizationLevel(double utilization)
        {
            return utilization switch
            {
                > 90 => UtilizationLevel.Critical,
                > 80 => UtilizationLevel.High,
                > 60 => UtilizationLevel.Normal,
                > 20 => UtilizationLevel.Normal,
                > 0 => UtilizationLevel.Low,
                _ => UtilizationLevel.Unknown
            };
        }

        private List<ResourceRecommendation> GenerateMemoryRecommendations(ResourceAnalysisContext context)
        {
            var recommendations = new List<ResourceRecommendation>();
            var currentUtilization = context.CurrentPoint.Value;
            var trend = AnalyzeMemoryTrend(context.History);

            if (currentUtilization > 85)
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Category = "Memory Management",
                    Description = "High memory utilization detected",
                    Impact = "Risk of out-of-memory conditions",
                    Implementation = "Increase memory allocation or implement memory optimization strategies"
                });
            }

            if (trend.TrendType == TrendType.Increasing && trend.Slope > 0.1)
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.Medium,
                    Category = "Memory Leak",
                    Description = "Potential memory leak detected",
                    Impact = "Gradual degradation of system performance",
                    Implementation = "Investigate memory usage patterns and implement memory cleanup"
                });
            }

            return recommendations;
        }

        private UtilizationTrend AnalyzeMemoryTrend(IReadOnlyList<MetricDataPoint> history)
        {
            if (history.Count < 2)
                return new UtilizationTrend { TrendType = TrendType.Stable };

            var x = Enumerable.Range(0, history.Count).Select(i => (double)i).ToArray();
            var y = history.Select(p => p.Value).ToArray();
            var slope = SimpleRegression.Fit(x, y).Item2;

            return new UtilizationTrend
            {
                TrendType = Math.Abs(slope) < _options.TrendThreshold 
                    ? TrendType.Stable 
                    : slope > 0 ? TrendType.Increasing : TrendType.Decreasing,
                Slope = slope,
                Volatility = CalculateVolatility(history)
            };
        }

        private double CalculateVolatility(IReadOnlyList<MetricDataPoint> history)
        {
            var values = history.Select(p => p.Value).ToList();
            return values.StandardDeviation() / values.Average();
        }
    }

    public class DiskAnalysis : IResourceAnalysis
    {
        private readonly ResourceUtilizationOptions _options;

        public DiskAnalysis(ResourceUtilizationOptions options)
        {
            _options = options;
        }

        public string AnalysisType => "Disk";

        public ResourceAnalysisResult Analyze(ResourceAnalysisContext context)
        {
            var currentUtilization = context.CurrentPoint.Value;
            var utilizationLevel = DetermineDiskUtilizationLevel(currentUtilization);
            var recommendations = GenerateDiskRecommendations(context);

            return new ResourceAnalysisResult
            {
                ResourceType = "Disk",
                UtilizationLevel = utilizationLevel,
                CurrentUtilization = currentUtilization,
                AverageUtilization = context.History.Average(p => p.Value),
                PeakUtilization = context.History.Max(p => p.Value),
                Recommendations = recommendations
            };
        }

        private UtilizationLevel DetermineDiskUtilizationLevel(double utilization)
        {
            return utilization switch
            {
                > 90 => UtilizationLevel.Critical,
                > 80 => UtilizationLevel.High,
                > 60 => UtilizationLevel.Normal,
                > 20 => UtilizationLevel.Normal,
                > 0 => UtilizationLevel.Low,
                _ => UtilizationLevel.Unknown
            };
        }

        private List<ResourceRecommendation> GenerateDiskRecommendations(ResourceAnalysisContext context)
        {
            var recommendations = new List<ResourceRecommendation>();
            var currentUtilization = context.CurrentPoint.Value;
            var trend = AnalyzeDiskTrend(context.History);

            if (currentUtilization > 85)
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Category = "Storage",
                    Description = "High disk utilization detected",
                    Impact = "Risk of disk space exhaustion",
                    Implementation = "Increase storage capacity or implement cleanup procedures"
                });
            }

            if (trend.TrendType == TrendType.Increasing && trend.Slope > 0.1)
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.Medium,
                    Category = "Storage Growth",
                    Description = "Rapid storage growth detected",
                    Impact = "Potential disk space issues in near future",
                    Implementation = "Review data retention policies and implement storage optimization"
                });
            }

            return recommendations;
        }

        private UtilizationTrend AnalyzeDiskTrend(IReadOnlyList<MetricDataPoint> history)
        {
            if (history.Count < 2)
                return new UtilizationTrend { TrendType = TrendType.Stable };

            var x = Enumerable.Range(0, history.Count).Select(i => (double)i).ToArray();
            var y = history.Select(p => p.Value).ToArray();
            var slope = SimpleRegression.Fit(x, y).Item2;

            return new UtilizationTrend
            {
                TrendType = Math.Abs(slope) < _options.TrendThreshold 
                    ? TrendType.Stable 
                    : slope > 0 ? TrendType.Increasing : TrendType.Decreasing,
                Slope = slope,
                Volatility = CalculateVolatility(history)
            };
        }

        private double CalculateVolatility(IReadOnlyList<MetricDataPoint> history)
        {
            var values = history.Select(p => p.Value).ToList();
            return values.StandardDeviation() / values.Average();
        }
    }

    public class NetworkAnalysis : IResourceAnalysis
    {
        private readonly ResourceUtilizationOptions _options;

        public NetworkAnalysis(ResourceUtilizationOptions options)
        {
            _options = options;
        }

        public string AnalysisType => "Network";

        public ResourceAnalysisResult Analyze(ResourceAnalysisContext context)
        {
            var currentUtilization = context.CurrentPoint.Value;
            var utilizationLevel = DetermineNetworkUtilizationLevel(currentUtilization);
            var recommendations = GenerateNetworkRecommendations(context);

            return new ResourceAnalysisResult
            {
                ResourceType = "Network",
                UtilizationLevel = utilizationLevel,
                CurrentUtilization = currentUtilization,
                AverageUtilization = context.History.Average(p => p.Value),
                PeakUtilization = context.History.Max(p => p.Value),
                Recommendations = recommendations
            };
        }

        private UtilizationLevel DetermineNetworkUtilizationLevel(double utilization)
        {
            return utilization switch
            {
                > 90 => UtilizationLevel.Critical,
                > 80 => UtilizationLevel.High,
                > 60 => UtilizationLevel.Normal,
                > 20 => UtilizationLevel.Normal,
                > 0 => UtilizationLevel.Low,
                _ => UtilizationLevel.Unknown
            };
        }

        private List<ResourceRecommendation> GenerateNetworkRecommendations(ResourceAnalysisContext context)
        {
            var recommendations = new List<ResourceRecommendation>();
            var currentUtilization = context.CurrentPoint.Value;
            var trend = AnalyzeNetworkTrend(context.History);

            if (currentUtilization > 85)
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Category = "Network Performance",
                    Description = "High network utilization detected",
                    Impact = "Network congestion and increased latency",
                    Implementation = "Optimize network usage or increase bandwidth capacity"
                });
            }

            if (trend.TrendType == TrendType.Increasing && trend.Slope > 0.1)
            {
                recommendations.Add(new ResourceRecommendation
                {
                    Priority = RecommendationPriority.Medium,
                    Category = "Network Growth",
                    Description = "Increasing network usage trend detected",
                    Impact = "Potential network bottlenecks in near future",
                    Implementation = "Review network traffic patterns and plan for capacity expansion"
                });
            }

            return recommendations;
        }

        private UtilizationTrend AnalyzeNetworkTrend(IReadOnlyList<MetricDataPoint> history)
        {
            if (history.Count < 2)
                return new UtilizationTrend { TrendType = TrendType.Stable };

            var x = Enumerable.Range(0, history.Count).Select(i => (double)i).ToArray();
            var y = history.Select(p => p.Value).ToArray();
            var slope = SimpleRegression.Fit(x, y).Item2;

            return new UtilizationTrend
            {
                TrendType = Math.Abs(slope) < _options.TrendThreshold 
                    ? TrendType.Stable 
                    : slope > 0 ? TrendType.Increasing : TrendType.Decreasing,
                Slope = slope,
                Volatility = CalculateVolatility(history)
            };
        }

        private double CalculateVolatility(IReadOnlyList<MetricDataPoint> history)
        {
            var values = history.Select(p => p.Value).ToList();
            return values.StandardDeviation() / values.Average();
        }
    }
} 