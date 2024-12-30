using System;

namespace PollSystem.Services.Diagnostics
{
    public class MetricSamplingConfiguration
    {
        public Dictionary<string, SamplingStrategy> MetricSamplingStrategies { get; set; } = new()
        {
            ["cpu_usage"] = new SamplingStrategy
            {
                SamplingRate = TimeSpan.FromSeconds(10),
                DownsamplingRules = new[]
                {
                    new DownsamplingRule
                    {
                        AgeThreshold = TimeSpan.FromHours(1),
                        NewSamplingRate = TimeSpan.FromMinutes(1)
                    },
                    new DownsamplingRule
                    {
                        AgeThreshold = TimeSpan.FromDays(1),
                        NewSamplingRate = TimeSpan.FromMinutes(5)
                    },
                    new DownsamplingRule
                    {
                        AgeThreshold = TimeSpan.FromDays(7),
                        NewSamplingRate = TimeSpan.FromMinutes(15)
                    }
                },
                AdaptiveSamplingSettings = new AdaptiveSamplingSettings
                {
                    EnableAdaptiveSampling = true,
                    MinSamplingRate = TimeSpan.FromSeconds(5),
                    MaxSamplingRate = TimeSpan.FromMinutes(1),
                    ChangeThreshold = 0.2,
                    AdaptationInterval = TimeSpan.FromMinutes(5)
                }
            },
            ["memory_usage"] = new SamplingStrategy
            {
                SamplingRate = TimeSpan.FromSeconds(30),
                DownsamplingRules = new[]
                {
                    new DownsamplingRule
                    {
                        AgeThreshold = TimeSpan.FromHours(2),
                        NewSamplingRate = TimeSpan.FromMinutes(2)
                    },
                    new DownsamplingRule
                    {
                        AgeThreshold = TimeSpan.FromDays(2),
                        NewSamplingRate = TimeSpan.FromMinutes(10)
                    }
                },
                AdaptiveSamplingSettings = new AdaptiveSamplingSettings
                {
                    EnableAdaptiveSampling = true,
                    MinSamplingRate = TimeSpan.FromSeconds(15),
                    MaxSamplingRate = TimeSpan.FromMinutes(2),
                    ChangeThreshold = 0.15,
                    AdaptationInterval = TimeSpan.FromMinutes(10)
                }
            }
        };

        public SamplingStrategy DefaultStrategy { get; set; } = new()
        {
            SamplingRate = TimeSpan.FromMinutes(1),
            DownsamplingRules = new[]
            {
                new DownsamplingRule
                {
                    AgeThreshold = TimeSpan.FromHours(6),
                    NewSamplingRate = TimeSpan.FromMinutes(5)
                },
                new DownsamplingRule
                {
                    AgeThreshold = TimeSpan.FromDays(3),
                    NewSamplingRate = TimeSpan.FromMinutes(15)
                }
            },
            AdaptiveSamplingSettings = new AdaptiveSamplingSettings
            {
                EnableAdaptiveSampling = false
            }
        };

        public SamplingStrategy GetStrategyForMetric(string metricName)
        {
            return MetricSamplingStrategies.GetValueOrDefault(metricName, DefaultStrategy);
        }
    }

    public class SamplingStrategy
    {
        public TimeSpan SamplingRate { get; set; }
        public DownsamplingRule[] DownsamplingRules { get; set; }
        public AdaptiveSamplingSettings AdaptiveSamplingSettings { get; set; }
        public Dictionary<string, object> CustomSettings { get; set; } = new();

        public TimeSpan GetSamplingRateForAge(TimeSpan age)
        {
            if (DownsamplingRules == null || !DownsamplingRules.Any())
                return SamplingRate;

            var applicableRule = DownsamplingRules
                .Where(r => age >= r.AgeThreshold)
                .OrderByDescending(r => r.AgeThreshold)
                .FirstOrDefault();

            return applicableRule?.NewSamplingRate ?? SamplingRate;
        }
    }

    public class DownsamplingRule
    {
        public TimeSpan AgeThreshold { get; set; }
        public TimeSpan NewSamplingRate { get; set; }
        public AggregationType AggregationType { get; set; } = AggregationType.Average;
    }

    public class AdaptiveSamplingSettings
    {
        public bool EnableAdaptiveSampling { get; set; }
        public TimeSpan MinSamplingRate { get; set; }
        public TimeSpan MaxSamplingRate { get; set; }
        public double ChangeThreshold { get; set; }
        public TimeSpan AdaptationInterval { get; set; }
        public int MinSamplesForAdaptation { get; set; } = 10;
        public double VariationThreshold { get; set; } = 0.1;
    }

    public enum AggregationType
    {
        Average,
        Sum,
        Min,
        Max,
        First,
        Last,
        Custom
    }
} 