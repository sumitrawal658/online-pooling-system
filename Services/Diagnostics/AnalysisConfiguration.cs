using System;

namespace PollSystem.Services.Diagnostics
{
    public class AnalysisConfiguration
    {
        public TimeWindowConfiguration ShortTerm { get; set; } = new()
        {
            WindowDuration = TimeSpan.FromMinutes(5),
            SampleInterval = TimeSpan.FromSeconds(10),
            MinimumSamples = 10
        };

        public TimeWindowConfiguration MediumTerm { get; set; } = new()
        {
            WindowDuration = TimeSpan.FromHours(1),
            SampleInterval = TimeSpan.FromMinutes(1),
            MinimumSamples = 30
        };

        public TimeWindowConfiguration LongTerm { get; set; } = new()
        {
            WindowDuration = TimeSpan.FromDays(1),
            SampleInterval = TimeSpan.FromMinutes(5),
            MinimumSamples = 100
        };

        public AnalysisSchedule Schedule { get; set; } = new()
        {
            ShortTermInterval = TimeSpan.FromMinutes(1),
            MediumTermInterval = TimeSpan.FromMinutes(15),
            LongTermInterval = TimeSpan.FromHours(1)
        };

        public Dictionary<string, MetricAnalysisSettings> MetricSettings { get; set; } = new()
        {
            ["cpu_usage"] = new MetricAnalysisSettings
            {
                EnableShortTerm = true,
                EnableMediumTerm = true,
                EnableLongTerm = true,
                Thresholds = new MetricThresholds
                {
                    Warning = 0.70,
                    Critical = 0.85,
                    TrendAlertThreshold = 0.10
                }
            },
            ["memory_usage"] = new MetricAnalysisSettings
            {
                EnableShortTerm = true,
                EnableMediumTerm = true,
                EnableLongTerm = true,
                Thresholds = new MetricThresholds
                {
                    Warning = 0.75,
                    Critical = 0.90,
                    TrendAlertThreshold = 0.15
                }
            },
            ["response_time"] = new MetricAnalysisSettings
            {
                EnableShortTerm = true,
                EnableMediumTerm = true,
                EnableLongTerm = false,
                Thresholds = new MetricThresholds
                {
                    Warning = 200,
                    Critical = 500,
                    TrendAlertThreshold = 50
                }
            }
        };
    }

    public class TimeWindowConfiguration
    {
        public TimeSpan WindowDuration { get; set; }
        public TimeSpan SampleInterval { get; set; }
        public int MinimumSamples { get; set; }
        public bool EnableOutlierDetection { get; set; } = true;
        public double OutlierDeviationThreshold { get; set; } = 3.0;
    }

    public class AnalysisSchedule
    {
        public TimeSpan ShortTermInterval { get; set; }
        public TimeSpan MediumTermInterval { get; set; }
        public TimeSpan LongTermInterval { get; set; }
        public bool EnableParallelAnalysis { get; set; } = true;
        public int MaxConcurrentAnalysis { get; set; } = 3;
    }

    public class MetricAnalysisSettings
    {
        public bool EnableShortTerm { get; set; }
        public bool EnableMediumTerm { get; set; }
        public bool EnableLongTerm { get; set; }
        public MetricThresholds Thresholds { get; set; } = new();
        public bool EnableSeasonalAnalysis { get; set; } = true;
        public TimeSpan? SeasonalityPeriod { get; set; }
        public bool EnableBaselineComparison { get; set; } = true;
        public int BaselinePercentile { get; set; } = 95;
    }

    public class MetricThresholds
    {
        public double Warning { get; set; }
        public double Critical { get; set; }
        public double TrendAlertThreshold { get; set; }
        public double SeasonalDeviationThreshold { get; set; } = 0.25;
        public double BaselineDeviationThreshold { get; set; } = 0.20;
    }
} 