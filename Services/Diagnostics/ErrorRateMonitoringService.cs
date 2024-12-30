using System.Collections.Concurrent;
using MathNet.Numerics.Statistics;

namespace PollSystem.Services.Diagnostics
{
    public class ErrorRateMonitoringService
    {
        private readonly ILogger<ErrorRateMonitoringService> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly IAlertManagementService _alertService;
        private readonly ConcurrentDictionary<string, ErrorMetrics> _errorMetrics;
        private readonly ErrorRateOptions _options;

        public ErrorRateMonitoringService(
            ILogger<ErrorRateMonitoringService> logger,
            IMetricsCollector metricsCollector,
            IAlertManagementService alertService,
            IOptions<ErrorRateOptions> options)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _alertService = alertService;
            _options = options.Value;
            _errorMetrics = new ConcurrentDictionary<string, ErrorMetrics>();
        }

        public async Task RecordErrorAsync(ErrorEvent errorEvent)
        {
            try
            {
                var metrics = _errorMetrics.GetOrAdd(errorEvent.Category, _ => new ErrorMetrics());
                metrics.RecordError(errorEvent);

                await _metricsCollector.RecordMetricWithTags(
                    "error.rate",
                    1,
                    new Dictionary<string, object>
                    {
                        ["category"] = errorEvent.Category,
                        ["severity"] = errorEvent.Severity.ToString(),
                        ["source"] = errorEvent.Source
                    });

                await AnalyzeErrorPatternAsync(errorEvent.Category, metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording error event for category {Category}", errorEvent.Category);
            }
        }

        public async Task<ErrorAnalysisReport> AnalyzeErrorsAsync(string category, TimeSpan window)
        {
            if (!_errorMetrics.TryGetValue(category, out var metrics))
                return new ErrorAnalysisReport { Category = category };

            var analysis = metrics.AnalyzeErrors(window);
            var patterns = DetectErrorPatterns(metrics, window);
            var trends = AnalyzeErrorTrends(metrics, window);

            var report = new ErrorAnalysisReport
            {
                Category = category,
                TotalErrors = analysis.TotalErrors,
                ErrorRate = analysis.ErrorRate,
                PeakErrorRate = analysis.PeakErrorRate,
                TimeWindow = window,
                Patterns = patterns,
                Trends = trends,
                Severity = DetermineOverallSeverity(analysis, patterns, trends),
                Recommendations = GenerateRecommendations(analysis, patterns, trends)
            };

            if (ShouldRaiseAlert(report))
            {
                await RaiseErrorAlert(report);
            }

            return report;
        }

        private async Task AnalyzeErrorPatternAsync(string category, ErrorMetrics metrics)
        {
            var recentWindow = TimeSpan.FromMinutes(_options.RecentWindowMinutes);
            var analysis = metrics.AnalyzeErrors(recentWindow);

            if (analysis.ErrorRate > _options.HighErrorRateThreshold)
            {
                await _alertService.RaiseAlert(new Alert
                {
                    Severity = AlertSeverity.High,
                    Source = "ErrorRateMonitoring",
                    Message = $"High error rate detected in category {category}",
                    Context = new Dictionary<string, object>
                    {
                        ["category"] = category,
                        ["errorRate"] = analysis.ErrorRate,
                        ["timeWindow"] = recentWindow,
                        ["totalErrors"] = analysis.TotalErrors
                    }
                });
            }
        }

        private List<ErrorPattern> DetectErrorPatterns(ErrorMetrics metrics, TimeSpan window)
        {
            var patterns = new List<ErrorPattern>();
            var recentErrors = metrics.GetRecentErrors(window);

            // Detect error bursts
            var bursts = DetectErrorBursts(recentErrors);
            if (bursts.Any())
            {
                patterns.Add(new ErrorPattern
                {
                    Type = ErrorPatternType.Burst,
                    Description = "Sudden increase in error frequency detected",
                    Frequency = bursts.Count,
                    TimeRanges = bursts
                });
            }

            // Detect periodic patterns
            var periodicPatterns = DetectPeriodicPatterns(recentErrors);
            if (periodicPatterns.HasPattern)
            {
                patterns.Add(new ErrorPattern
                {
                    Type = ErrorPatternType.Periodic,
                    Description = $"Periodic error pattern detected with interval {periodicPatterns.Interval.TotalMinutes:F1} minutes",
                    Frequency = periodicPatterns.Occurrences,
                    Confidence = periodicPatterns.Confidence
                });
            }

            // Detect correlated errors
            var correlations = DetectErrorCorrelations(recentErrors);
            foreach (var correlation in correlations)
            {
                patterns.Add(new ErrorPattern
                {
                    Type = ErrorPatternType.Correlation,
                    Description = $"Correlated errors detected: {correlation.Description}",
                    Frequency = correlation.Frequency,
                    RelatedFactors = correlation.RelatedFactors
                });
            }

            return patterns;
        }

        private List<TimeRange> DetectErrorBursts(IReadOnlyList<ErrorEvent> errors)
        {
            var bursts = new List<TimeRange>();
            if (!errors.Any())
                return bursts;

            var burstThreshold = _options.BurstThresholdPerMinute;
            var burstWindow = TimeSpan.FromMinutes(1);
            var currentBurstStart = errors[0].Timestamp;
            var currentBurstCount = 1;

            for (int i = 1; i < errors.Count; i++)
            {
                var timeSinceStart = errors[i].Timestamp - currentBurstStart;
                if (timeSinceStart <= burstWindow)
                {
                    currentBurstCount++;
                }
                else
                {
                    if (currentBurstCount >= burstThreshold)
                    {
                        bursts.Add(new TimeRange
                        {
                            Start = currentBurstStart,
                            End = errors[i - 1].Timestamp,
                            Count = currentBurstCount
                        });
                    }
                    currentBurstStart = errors[i].Timestamp;
                    currentBurstCount = 1;
                }
            }

            // Check final burst
            if (currentBurstCount >= burstThreshold)
            {
                bursts.Add(new TimeRange
                {
                    Start = currentBurstStart,
                    End = errors[^1].Timestamp,
                    Count = currentBurstCount
                });
            }

            return bursts;
        }

        private PeriodicPattern DetectPeriodicPatterns(IReadOnlyList<ErrorEvent> errors)
        {
            if (errors.Count < _options.MinErrorsForPatternDetection)
                return new PeriodicPattern { HasPattern = false };

            var intervals = new List<double>();
            for (int i = 1; i < errors.Count; i++)
            {
                intervals.Add((errors[i].Timestamp - errors[i - 1].Timestamp).TotalMinutes);
            }

            var intervalGroups = intervals
                .GroupBy(i => Math.Round(i, 1))
                .OrderByDescending(g => g.Count())
                .ToList();

            if (!intervalGroups.Any())
                return new PeriodicPattern { HasPattern = false };

            var dominantInterval = intervalGroups.First();
            var patternConfidence = (double)dominantInterval.Count() / intervals.Count;

            return new PeriodicPattern
            {
                HasPattern = patternConfidence > _options.PatternConfidenceThreshold,
                Interval = TimeSpan.FromMinutes(dominantInterval.Key),
                Occurrences = dominantInterval.Count(),
                Confidence = patternConfidence
            };
        }

        private List<ErrorCorrelation> DetectErrorCorrelations(IReadOnlyList<ErrorEvent> errors)
        {
            var correlations = new List<ErrorCorrelation>();
            if (errors.Count < _options.MinErrorsForCorrelation)
                return correlations;

            // Group errors by common factors
            var sourceGroups = errors
                .GroupBy(e => e.Source)
                .Where(g => g.Count() >= _options.MinErrorsForCorrelation);

            foreach (var group in sourceGroups)
            {
                correlations.Add(new ErrorCorrelation
                {
                    Description = $"Multiple errors from source: {group.Key}",
                    Frequency = group.Count(),
                    RelatedFactors = new Dictionary<string, object>
                    {
                        ["source"] = group.Key,
                        ["errorCount"] = group.Count(),
                        ["timeSpan"] = (group.Max(e => e.Timestamp) - group.Min(e => e.Timestamp)).TotalMinutes
                    }
                });
            }

            return correlations;
        }

        private ErrorTrends AnalyzeErrorTrends(ErrorMetrics metrics, TimeSpan window)
        {
            var recentErrors = metrics.GetRecentErrors(window);
            if (!recentErrors.Any())
                return new ErrorTrends();

            var errorRates = CalculateErrorRates(recentErrors, TimeSpan.FromMinutes(5));
            var trend = CalculateErrorTrend(errorRates);

            return new ErrorTrends
            {
                TrendType = trend.TrendType,
                ChangeRate = trend.Slope,
                Volatility = trend.Volatility,
                PredictedRate = trend.PredictedValue,
                Confidence = trend.Confidence
            };
        }

        private List<ErrorRatePoint> CalculateErrorRates(
            IReadOnlyList<ErrorEvent> errors,
            TimeSpan interval)
        {
            var rates = new List<ErrorRatePoint>();
            if (!errors.Any())
                return rates;

            var startTime = errors.Min(e => e.Timestamp);
            var endTime = errors.Max(e => e.Timestamp);
            var currentTime = startTime;

            while (currentTime <= endTime)
            {
                var periodErrors = errors.Count(e =>
                    e.Timestamp >= currentTime &&
                    e.Timestamp < currentTime.Add(interval));

                rates.Add(new ErrorRatePoint
                {
                    Timestamp = currentTime,
                    Rate = periodErrors / interval.TotalMinutes
                });

                currentTime = currentTime.Add(interval);
            }

            return rates;
        }

        private TrendAnalysis CalculateErrorTrend(List<ErrorRatePoint> rates)
        {
            if (rates.Count < 2)
                return new TrendAnalysis();

            var x = Enumerable.Range(0, rates.Count).Select(i => (double)i).ToArray();
            var y = rates.Select(r => r.Rate).ToArray();
            var slope = SimpleRegression.Fit(x, y).Item2;

            var values = rates.Select(r => r.Rate).ToList();
            var volatility = values.StandardDeviation() / values.Average();
            var confidence = Math.Max(0, 1 - (volatility / _options.VolatilityThreshold));

            return new TrendAnalysis
            {
                TrendType = DetermineTrendType(slope),
                Slope = slope,
                Volatility = volatility,
                PredictedValue = rates.Last().Rate + (slope * _options.PredictionHorizon),
                Confidence = confidence
            };
        }

        private TrendType DetermineTrendType(double slope)
        {
            if (Math.Abs(slope) < _options.TrendThreshold)
                return TrendType.Stable;
            return slope > 0 ? TrendType.Increasing : TrendType.Decreasing;
        }

        private ErrorSeverity DetermineOverallSeverity(
            ErrorAnalysis analysis,
            List<ErrorPattern> patterns,
            ErrorTrends trends)
        {
            if (analysis.ErrorRate > _options.CriticalErrorRateThreshold)
                return ErrorSeverity.Critical;

            if (analysis.ErrorRate > _options.HighErrorRateThreshold ||
                (trends.TrendType == TrendType.Increasing && trends.Confidence > 0.8))
                return ErrorSeverity.High;

            if (analysis.ErrorRate > _options.MediumErrorRateThreshold ||
                patterns.Any(p => p.Type == ErrorPatternType.Burst))
                return ErrorSeverity.Medium;

            return ErrorSeverity.Low;
        }

        private List<ErrorRecommendation> GenerateRecommendations(
            ErrorAnalysis analysis,
            List<ErrorPattern> patterns,
            ErrorTrends trends)
        {
            var recommendations = new List<ErrorRecommendation>();

            // High error rate recommendations
            if (analysis.ErrorRate > _options.HighErrorRateThreshold)
            {
                recommendations.Add(new ErrorRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Category = "Error Rate",
                    Description = "High error rate detected",
                    Action = "Investigate root cause and implement immediate error handling improvements",
                    Impact = "System reliability and performance"
                });
            }

            // Pattern-based recommendations
            foreach (var pattern in patterns)
            {
                switch (pattern.Type)
                {
                    case ErrorPatternType.Burst:
                        recommendations.Add(new ErrorRecommendation
                        {
                            Priority = RecommendationPriority.High,
                            Category = "Error Burst",
                            Description = "Sudden burst of errors detected",
                            Action = "Implement rate limiting and circuit breaker patterns",
                            Impact = "System stability"
                        });
                        break;

                    case ErrorPatternType.Periodic:
                        recommendations.Add(new ErrorRecommendation
                        {
                            Priority = RecommendationPriority.Medium,
                            Category = "Periodic Errors",
                            Description = "Regular pattern of errors detected",
                            Action = "Review scheduled tasks and resource usage patterns",
                            Impact = "System reliability"
                        });
                        break;

                    case ErrorPatternType.Correlation:
                        recommendations.Add(new ErrorRecommendation
                        {
                            Priority = RecommendationPriority.Medium,
                            Category = "Correlated Errors",
                            Description = "Related errors detected across components",
                            Action = "Review component dependencies and implement resilience patterns",
                            Impact = "System resilience"
                        });
                        break;
                }
            }

            // Trend-based recommendations
            if (trends.TrendType == TrendType.Increasing && trends.Confidence > 0.7)
            {
                recommendations.Add(new ErrorRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Category = "Error Trend",
                    Description = "Increasing error rate trend detected",
                    Action = "Review recent changes and implement preventive measures",
                    Impact = "System health"
                });
            }

            return recommendations;
        }

        private bool ShouldRaiseAlert(ErrorAnalysisReport report)
        {
            return report.Severity >= ErrorSeverity.High ||
                   report.ErrorRate > _options.HighErrorRateThreshold ||
                   (report.Trends.TrendType == TrendType.Increasing && report.Trends.Confidence > 0.8);
        }

        private async Task RaiseErrorAlert(ErrorAnalysisReport report)
        {
            await _alertService.RaiseAlert(new Alert
            {
                Severity = ConvertErrorSeverityToAlertSeverity(report.Severity),
                Source = "ErrorRateMonitoring",
                Message = GenerateAlertMessage(report),
                Context = new Dictionary<string, object>
                {
                    ["category"] = report.Category,
                    ["errorRate"] = report.ErrorRate,
                    ["timeWindow"] = report.TimeWindow,
                    ["patterns"] = report.Patterns.Select(p => p.Description).ToList(),
                    ["recommendations"] = report.Recommendations.Select(r => r.Action).ToList()
                }
            });
        }

        private AlertSeverity ConvertErrorSeverityToAlertSeverity(ErrorSeverity severity)
        {
            return severity switch
            {
                ErrorSeverity.Critical => AlertSeverity.Critical,
                ErrorSeverity.High => AlertSeverity.High,
                ErrorSeverity.Medium => AlertSeverity.Warning,
                _ => AlertSeverity.Information
            };
        }

        private string GenerateAlertMessage(ErrorAnalysisReport report)
        {
            var message = new StringBuilder();
            message.AppendLine($"Error rate alert for category: {report.Category}");
            message.AppendLine($"Current error rate: {report.ErrorRate:F2} errors/minute");
            
            if (report.Patterns.Any())
            {
                message.AppendLine("\nDetected patterns:");
                foreach (var pattern in report.Patterns)
                {
                    message.AppendLine($"- {pattern.Description}");
                }
            }

            if (report.Recommendations.Any())
            {
                message.AppendLine("\nRecommendations:");
                foreach (var recommendation in report.Recommendations.Take(3))
                {
                    message.AppendLine($"- {recommendation.Action}");
                }
            }

            return message.ToString();
        }
    }

    public class ErrorMetrics
    {
        private readonly ConcurrentQueue<ErrorEvent> _recentErrors = new();
        private readonly object _lock = new();

        public void RecordError(ErrorEvent error)
        {
            _recentErrors.Enqueue(error);
            TrimOldErrors();
        }

        public ErrorAnalysis AnalyzeErrors(TimeSpan window)
        {
            var relevantErrors = GetRecentErrors(window);
            if (!relevantErrors.Any())
                return new ErrorAnalysis();

            var timeSpan = window.TotalMinutes;
            var errorRate = relevantErrors.Count / timeSpan;

            // Calculate peak error rate using 1-minute windows
            var peakErrorRate = CalculatePeakErrorRate(relevantErrors);

            return new ErrorAnalysis
            {
                TotalErrors = relevantErrors.Count,
                ErrorRate = errorRate,
                PeakErrorRate = peakErrorRate,
                TimeSpan = window
            };
        }

        public IReadOnlyList<ErrorEvent> GetRecentErrors(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return _recentErrors
                .Where(e => e.Timestamp >= cutoff)
                .OrderBy(e => e.Timestamp)
                .ToList();
        }

        private void TrimOldErrors()
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(1);
            while (_recentErrors.TryPeek(out var error) && error.Timestamp < cutoff)
            {
                _recentErrors.TryDequeue(out _);
            }
        }

        private double CalculatePeakErrorRate(IReadOnlyList<ErrorEvent> errors)
        {
            if (!errors.Any())
                return 0;

            var windowSize = TimeSpan.FromMinutes(1);
            var maxErrors = 0;
            var startTime = errors.Min(e => e.Timestamp);
            var endTime = errors.Max(e => e.Timestamp);
            var currentTime = startTime;

            while (currentTime <= endTime)
            {
                var windowErrors = errors.Count(e =>
                    e.Timestamp >= currentTime &&
                    e.Timestamp < currentTime.Add(windowSize));

                maxErrors = Math.Max(maxErrors, windowErrors);
                currentTime = currentTime.Add(windowSize);
            }

            return maxErrors / windowSize.TotalMinutes;
        }
    }

    public class ErrorEvent
    {
        public string Category { get; init; }
        public string Source { get; init; }
        public ErrorSeverity Severity { get; init; }
        public DateTime Timestamp { get; init; }
        public string Message { get; init; }
        public Dictionary<string, object> Context { get; init; }
    }

    public class ErrorAnalysis
    {
        public int TotalErrors { get; init; }
        public double ErrorRate { get; init; }
        public double PeakErrorRate { get; init; }
        public TimeSpan TimeSpan { get; init; }
    }

    public class ErrorAnalysisReport
    {
        public string Category { get; init; }
        public int TotalErrors { get; init; }
        public double ErrorRate { get; init; }
        public double PeakErrorRate { get; init; }
        public TimeSpan TimeWindow { get; init; }
        public List<ErrorPattern> Patterns { get; init; } = new();
        public ErrorTrends Trends { get; init; }
        public ErrorSeverity Severity { get; init; }
        public List<ErrorRecommendation> Recommendations { get; init; } = new();
    }

    public class ErrorPattern
    {
        public ErrorPatternType Type { get; init; }
        public string Description { get; init; }
        public int Frequency { get; init; }
        public double Confidence { get; init; }
        public List<TimeRange> TimeRanges { get; init; } = new();
        public Dictionary<string, object> RelatedFactors { get; init; } = new();
    }

    public class TimeRange
    {
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
        public int Count { get; init; }
    }

    public class ErrorCorrelation
    {
        public string Description { get; init; }
        public int Frequency { get; init; }
        public Dictionary<string, object> RelatedFactors { get; init; } = new();
    }

    public class PeriodicPattern
    {
        public bool HasPattern { get; init; }
        public TimeSpan Interval { get; init; }
        public int Occurrences { get; init; }
        public double Confidence { get; init; }
    }

    public class ErrorRatePoint
    {
        public DateTime Timestamp { get; init; }
        public double Rate { get; init; }
    }

    public class ErrorTrends
    {
        public TrendType TrendType { get; init; }
        public double ChangeRate { get; init; }
        public double Volatility { get; init; }
        public double PredictedRate { get; init; }
        public double Confidence { get; init; }
    }

    public class ErrorRecommendation
    {
        public RecommendationPriority Priority { get; init; }
        public string Category { get; init; }
        public string Description { get; init; }
        public string Action { get; init; }
        public string Impact { get; init; }
    }

    public class ErrorRateOptions
    {
        public int RecentWindowMinutes { get; set; } = 5;
        public double HighErrorRateThreshold { get; set; } = 10;
        public double MediumErrorRateThreshold { get; set; } = 5;
        public double CriticalErrorRateThreshold { get; set; } = 20;
        public int BurstThresholdPerMinute { get; set; } = 10;
        public int MinErrorsForPatternDetection { get; set; } = 5;
        public double PatternConfidenceThreshold { get; set; } = 0.7;
        public int MinErrorsForCorrelation { get; set; } = 3;
        public double TrendThreshold { get; set; } = 0.01;
        public double VolatilityThreshold { get; set; } = 0.3;
        public int PredictionHorizon { get; set; } = 12;
    }

    public enum ErrorPatternType
    {
        Burst,
        Periodic,
        Correlation
    }

    public enum ErrorSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
} 