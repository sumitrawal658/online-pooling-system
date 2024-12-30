using System;

namespace PollSystem.Services.Diagnostics
{
    public class AnalysisIntervalConfiguration
    {
        public Dictionary<string, MetricIntervalSettings> MetricIntervals { get; set; } = new()
        {
            ["cpu_usage"] = new MetricIntervalSettings
            {
                ShortTerm = new IntervalSettings
                {
                    Enabled = true,
                    WindowDuration = TimeSpan.FromMinutes(5),
                    AnalysisInterval = TimeSpan.FromMinutes(1),
                    SampleInterval = TimeSpan.FromSeconds(10),
                    MinimumSamples = 20,
                    RetentionPeriod = TimeSpan.FromHours(1),
                    Thresholds = new IntervalThresholds
                    {
                        Warning = 0.75,
                        Critical = 0.90,
                        ChangeRate = 0.15
                    }
                },
                MediumTerm = new IntervalSettings
                {
                    Enabled = true,
                    WindowDuration = TimeSpan.FromHours(1),
                    AnalysisInterval = TimeSpan.FromMinutes(5),
                    SampleInterval = TimeSpan.FromMinutes(1),
                    MinimumSamples = 30,
                    RetentionPeriod = TimeSpan.FromDays(1),
                    Thresholds = new IntervalThresholds
                    {
                        Warning = 0.70,
                        Critical = 0.85,
                        ChangeRate = 0.10
                    }
                },
                LongTerm = new IntervalSettings
                {
                    Enabled = true,
                    WindowDuration = TimeSpan.FromDays(1),
                    AnalysisInterval = TimeSpan.FromHours(1),
                    SampleInterval = TimeSpan.FromMinutes(5),
                    MinimumSamples = 100,
                    RetentionPeriod = TimeSpan.FromDays(7),
                    Thresholds = new IntervalThresholds
                    {
                        Warning = 0.65,
                        Critical = 0.80,
                        ChangeRate = 0.05
                    }
                }
            },
            ["memory_usage"] = new MetricIntervalSettings
            {
                ShortTerm = new IntervalSettings
                {
                    Enabled = true,
                    WindowDuration = TimeSpan.FromMinutes(5),
                    AnalysisInterval = TimeSpan.FromMinutes(1),
                    SampleInterval = TimeSpan.FromSeconds(15),
                    MinimumSamples = 15,
                    RetentionPeriod = TimeSpan.FromHours(1),
                    Thresholds = new IntervalThresholds
                    {
                        Warning = 0.80,
                        Critical = 0.95,
                        ChangeRate = 0.20
                    }
                },
                MediumTerm = new IntervalSettings
                {
                    Enabled = true,
                    WindowDuration = TimeSpan.FromHours(2),
                    AnalysisInterval = TimeSpan.FromMinutes(10),
                    SampleInterval = TimeSpan.FromMinutes(1),
                    MinimumSamples = 60,
                    RetentionPeriod = TimeSpan.FromDays(1),
                    Thresholds = new IntervalThresholds
                    {
                        Warning = 0.75,
                        Critical = 0.90,
                        ChangeRate = 0.15
                    }
                },
                LongTerm = new IntervalSettings
                {
                    Enabled = true,
                    WindowDuration = TimeSpan.FromDays(1),
                    AnalysisInterval = TimeSpan.FromHours(1),
                    SampleInterval = TimeSpan.FromMinutes(5),
                    MinimumSamples = 150,
                    RetentionPeriod = TimeSpan.FromDays(7),
                    Thresholds = new IntervalThresholds
                    {
                        Warning = 0.70,
                        Critical = 0.85,
                        ChangeRate = 0.10
                    }
                }
            },
            ["response_time"] = new MetricIntervalSettings
            {
                ShortTerm = new IntervalSettings
                {
                    Enabled = true,
                    WindowDuration = TimeSpan.FromMinutes(5),
                    AnalysisInterval = TimeSpan.FromMinutes(1),
                    SampleInterval = TimeSpan.FromSeconds(5),
                    MinimumSamples = 30,
                    RetentionPeriod = TimeSpan.FromHours(1),
                    Thresholds = new IntervalThresholds
                    {
                        Warning = 200,
                        Critical = 500,
                        ChangeRate = 50
                    }
                },
                MediumTerm = new IntervalSettings
                {
                    Enabled = true,
                    WindowDuration = TimeSpan.FromHours(1),
                    AnalysisInterval = TimeSpan.FromMinutes(5),
                    SampleInterval = TimeSpan.FromMinutes(1),
                    MinimumSamples = 45,
                    RetentionPeriod = TimeSpan.FromDays(1),
                    Thresholds = new IntervalThresholds
                    {
                        Warning = 150,
                        Critical = 400,
                        ChangeRate = 30
                    }
                },
                LongTerm = new IntervalSettings
                {
                    Enabled = false,
                    WindowDuration = TimeSpan.FromDays(1),
                    AnalysisInterval = TimeSpan.FromHours(1),
                    SampleInterval = TimeSpan.FromMinutes(5),
                    MinimumSamples = 100,
                    RetentionPeriod = TimeSpan.FromDays(7),
                    Thresholds = new IntervalThresholds
                    {
                        Warning = 100,
                        Critical = 300,
                        ChangeRate = 20
                    }
                }
            }
        };

        public AnalysisScheduleSettings ScheduleSettings { get; set; } = new()
        {
            MaxConcurrentAnalysis = 3,
            EnableParallelAnalysis = true,
            DefaultRetentionPeriod = TimeSpan.FromDays(7),
            AnalysisQueueCapacity = 1000,
            ProcessingTimeout = TimeSpan.FromMinutes(5)
        };

        public Dictionary<string, AnalysisFeatures> MetricFeatures { get; set; } = new()
        {
            ["cpu_usage"] = new AnalysisFeatures
            {
                EnableOutlierDetection = true,
                EnableTrendAnalysis = true,
                EnableSeasonalAnalysis = true,
                EnableBaselineComparison = true,
                OutlierDetectionSettings = new OutlierDetectionSettings
                {
                    DeviationThreshold = 3.0,
                    MinimumOutliersForAlert = 3,
                    ConsecutiveOutliersThreshold = 2
                },
                SeasonalitySettings = new SeasonalitySettings
                {
                    AutoDetectPeriod = true,
                    DefaultPeriod = TimeSpan.FromHours(24),
                    MinimumPeriodConfidence = 0.7
                }
            }
        };
    }

    public class MetricIntervalSettings
    {
        public IntervalSettings ShortTerm { get; set; }
        public IntervalSettings MediumTerm { get; set; }
        public IntervalSettings LongTerm { get; set; }

        public IntervalSettings GetSettings(AnalysisTimeWindow window) => window switch
        {
            AnalysisTimeWindow.ShortTerm => ShortTerm,
            AnalysisTimeWindow.MediumTerm => MediumTerm,
            AnalysisTimeWindow.LongTerm => LongTerm,
            _ => throw new ArgumentException($"Invalid time window: {window}")
        };
    }

    public class IntervalSettings
    {
        public bool Enabled { get; set; }
        public TimeSpan WindowDuration { get; set; }
        public TimeSpan AnalysisInterval { get; set; }
        public TimeSpan SampleInterval { get; set; }
        public int MinimumSamples { get; set; }
        public TimeSpan RetentionPeriod { get; set; }
        public IntervalThresholds Thresholds { get; set; }
    }

    public class IntervalThresholds
    {
        public double Warning { get; set; }
        public double Critical { get; set; }
        public double ChangeRate { get; set; }
    }

    public class AnalysisScheduleSettings
    {
        public int MaxConcurrentAnalysis { get; set; }
        public bool EnableParallelAnalysis { get; set; }
        public TimeSpan DefaultRetentionPeriod { get; set; }
        public int AnalysisQueueCapacity { get; set; }
        public TimeSpan ProcessingTimeout { get; set; }
    }

    public class AnalysisFeatures
    {
        public bool EnableOutlierDetection { get; set; }
        public bool EnableTrendAnalysis { get; set; }
        public bool EnableSeasonalAnalysis { get; set; }
        public bool EnableBaselineComparison { get; set; }
        public OutlierDetectionSettings OutlierDetectionSettings { get; set; }
        public SeasonalitySettings SeasonalitySettings { get; set; }
    }

    public class OutlierDetectionSettings
    {
        public double DeviationThreshold { get; set; }
        public int MinimumOutliersForAlert { get; set; }
        public int ConsecutiveOutliersThreshold { get; set; }
    }

    public class SeasonalitySettings
    {
        public bool AutoDetectPeriod { get; set; }
        public TimeSpan DefaultPeriod { get; set; }
        public double MinimumPeriodConfidence { get; set; }
    }
} 