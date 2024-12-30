using System;

namespace PollSystem.Services.Diagnostics
{
    public class MetricConfiguration
    {
        public Dictionary<string, MetricDefinition> Metrics { get; set; } = new()
        {
            ["cpu_usage"] = new MetricDefinition
            {
                Name = "CPU Usage",
                Unit = "%",
                Category = MetricCategory.Resource,
                Criticality = MetricCriticality.High,
                ThresholdSettings = new MetricThresholdSettings
                {
                    WarningThresholds = new RangeThresholds
                    {
                        ShortTerm = new ThresholdRange { Lower = 0.70, Upper = 0.80 },
                        MediumTerm = new ThresholdRange { Lower = 0.65, Upper = 0.75 },
                        LongTerm = new ThresholdRange { Lower = 0.60, Upper = 0.70 }
                    },
                    CriticalThresholds = new RangeThresholds
                    {
                        ShortTerm = new ThresholdRange { Lower = 0.85, Upper = 0.95 },
                        MediumTerm = new ThresholdRange { Lower = 0.80, Upper = 0.90 },
                        LongTerm = new ThresholdRange { Lower = 0.75, Upper = 0.85 }
                    },
                    TrendThresholds = new TrendThresholds
                    {
                        IncreaseRate = new RateThreshold
                        {
                            ShortTerm = 0.15,
                            MediumTerm = 0.10,
                            LongTerm = 0.05
                        },
                        DecreaseRate = new RateThreshold
                        {
                            ShortTerm = -0.20,
                            MediumTerm = -0.15,
                            LongTerm = -0.10
                        }
                    }
                },
                AnomalySettings = new AnomalyDetectionSettings
                {
                    EnableOutlierDetection = true,
                    OutlierThresholds = new OutlierThresholds
                    {
                        DeviationMultiplier = 3.0,
                        MinimumOutliers = 3,
                        ConsecutiveOutliers = 2,
                        OutlierPercentage = 0.1
                    },
                    SeasonalitySettings = new SeasonalitySettings
                    {
                        ExpectedPatterns = new[]
                        {
                            new SeasonalPattern
                            {
                                Period = TimeSpan.FromHours(24),
                                PeakHours = new[] { 9, 10, 11, 14, 15, 16 },
                                LowHours = new[] { 0, 1, 2, 3, 4 }
                            }
                        },
                        DeviationThreshold = 0.25
                    }
                },
                AlertSettings = new MetricAlertSettings
                {
                    WarningAlertDelay = TimeSpan.FromMinutes(5),
                    CriticalAlertDelay = TimeSpan.FromMinutes(1),
                    AlertCooldown = TimeSpan.FromHours(1),
                    RequireConsecutiveViolations = 3,
                    AutoResolveAfter = TimeSpan.FromMinutes(15)
                },
                BaselineSettings = new BaselineSettings
                {
                    EnableBaselineComparison = true,
                    BaselinePeriod = TimeSpan.FromDays(7),
                    DeviationThreshold = 0.20,
                    MinimumDataPoints = 1000,
                    UpdateInterval = TimeSpan.FromHours(1)
                }
            },
            ["memory_usage"] = new MetricDefinition
            {
                Name = "Memory Usage",
                Unit = "%",
                Category = MetricCategory.Resource,
                Criticality = MetricCriticality.High,
                ThresholdSettings = new MetricThresholdSettings
                {
                    WarningThresholds = new RangeThresholds
                    {
                        ShortTerm = new ThresholdRange { Lower = 0.75, Upper = 0.85 },
                        MediumTerm = new ThresholdRange { Lower = 0.70, Upper = 0.80 },
                        LongTerm = new ThresholdRange { Lower = 0.65, Upper = 0.75 }
                    },
                    CriticalThresholds = new RangeThresholds
                    {
                        ShortTerm = new ThresholdRange { Lower = 0.90, Upper = 0.95 },
                        MediumTerm = new ThresholdRange { Lower = 0.85, Upper = 0.90 },
                        LongTerm = new ThresholdRange { Lower = 0.80, Upper = 0.85 }
                    },
                    TrendThresholds = new TrendThresholds
                    {
                        IncreaseRate = new RateThreshold
                        {
                            ShortTerm = 0.20,
                            MediumTerm = 0.15,
                            LongTerm = 0.10
                        },
                        DecreaseRate = new RateThreshold
                        {
                            ShortTerm = -0.25,
                            MediumTerm = -0.20,
                            LongTerm = -0.15
                        }
                    }
                },
                AnomalySettings = new AnomalyDetectionSettings
                {
                    EnableOutlierDetection = true,
                    OutlierThresholds = new OutlierThresholds
                    {
                        DeviationMultiplier = 2.5,
                        MinimumOutliers = 4,
                        ConsecutiveOutliers = 3,
                        OutlierPercentage = 0.15
                    },
                    SeasonalitySettings = new SeasonalitySettings
                    {
                        ExpectedPatterns = new[]
                        {
                            new SeasonalPattern
                            {
                                Period = TimeSpan.FromHours(24),
                                PeakHours = new[] { 13, 14, 15, 16, 17 },
                                LowHours = new[] { 1, 2, 3, 4, 5 }
                            }
                        },
                        DeviationThreshold = 0.30
                    }
                },
                AlertSettings = new MetricAlertSettings
                {
                    WarningAlertDelay = TimeSpan.FromMinutes(10),
                    CriticalAlertDelay = TimeSpan.FromMinutes(2),
                    AlertCooldown = TimeSpan.FromHours(2),
                    RequireConsecutiveViolations = 4,
                    AutoResolveAfter = TimeSpan.FromMinutes(30)
                },
                BaselineSettings = new BaselineSettings
                {
                    EnableBaselineComparison = true,
                    BaselinePeriod = TimeSpan.FromDays(7),
                    DeviationThreshold = 0.25,
                    MinimumDataPoints = 1000,
                    UpdateInterval = TimeSpan.FromHours(2)
                }
            }
        };
    }

    public class MetricDefinition
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public MetricCategory Category { get; set; }
        public MetricCriticality Criticality { get; set; }
        public MetricThresholdSettings ThresholdSettings { get; set; }
        public AnomalyDetectionSettings AnomalySettings { get; set; }
        public MetricAlertSettings AlertSettings { get; set; }
        public BaselineSettings BaselineSettings { get; set; }
    }

    public class MetricThresholdSettings
    {
        public RangeThresholds WarningThresholds { get; set; }
        public RangeThresholds CriticalThresholds { get; set; }
        public TrendThresholds TrendThresholds { get; set; }
    }

    public class RangeThresholds
    {
        public ThresholdRange ShortTerm { get; set; }
        public ThresholdRange MediumTerm { get; set; }
        public ThresholdRange LongTerm { get; set; }
    }

    public class ThresholdRange
    {
        public double Lower { get; set; }
        public double Upper { get; set; }
    }

    public class TrendThresholds
    {
        public RateThreshold IncreaseRate { get; set; }
        public RateThreshold DecreaseRate { get; set; }
    }

    public class RateThreshold
    {
        public double ShortTerm { get; set; }
        public double MediumTerm { get; set; }
        public double LongTerm { get; set; }
    }

    public class AnomalyDetectionSettings
    {
        public bool EnableOutlierDetection { get; set; }
        public OutlierThresholds OutlierThresholds { get; set; }
        public SeasonalitySettings SeasonalitySettings { get; set; }
    }

    public class OutlierThresholds
    {
        public double DeviationMultiplier { get; set; }
        public int MinimumOutliers { get; set; }
        public int ConsecutiveOutliers { get; set; }
        public double OutlierPercentage { get; set; }
    }

    public class SeasonalitySettings
    {
        public SeasonalPattern[] ExpectedPatterns { get; set; }
        public double DeviationThreshold { get; set; }
    }

    public class SeasonalPattern
    {
        public TimeSpan Period { get; set; }
        public int[] PeakHours { get; set; }
        public int[] LowHours { get; set; }
    }

    public class MetricAlertSettings
    {
        public TimeSpan WarningAlertDelay { get; set; }
        public TimeSpan CriticalAlertDelay { get; set; }
        public TimeSpan AlertCooldown { get; set; }
        public int RequireConsecutiveViolations { get; set; }
        public TimeSpan AutoResolveAfter { get; set; }
    }

    public class BaselineSettings
    {
        public bool EnableBaselineComparison { get; set; }
        public TimeSpan BaselinePeriod { get; set; }
        public double DeviationThreshold { get; set; }
        public int MinimumDataPoints { get; set; }
        public TimeSpan UpdateInterval { get; set; }
    }

    public enum MetricCategory
    {
        Resource,
        Performance,
        Business,
        Security,
        Availability
    }

    public enum MetricCriticality
    {
        Low,
        Medium,
        High,
        Critical
    }
} 