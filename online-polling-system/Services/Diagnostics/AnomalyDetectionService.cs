using System.Collections.Concurrent;
using MathNet.Numerics.Statistics;

namespace PollSystem.Services.Diagnostics
{
    public class AnomalyDetectionService
    {
        private readonly ILogger<AnomalyDetectionService> _logger;
        private readonly ConcurrentDictionary<string, MetricBaseline> _metricBaselines;
        private readonly AnomalyDetectionOptions _options;

        public AnomalyDetectionService(
            ILogger<AnomalyDetectionService> logger,
            IOptions<AnomalyDetectionOptions> options)
        {
            _logger = logger;
            _options = options.Value;
            _metricBaselines = new ConcurrentDictionary<string, MetricBaseline>();
        }

        public async Task<AnomalyDetectionResult> AnalyzeMetricAsync(
            string metricName,
            IReadOnlyList<MetricDataPoint> history,
            MetricDataPoint currentPoint)
        {
            try
            {
                var baseline = _metricBaselines.GetOrAdd(metricName, _ => new MetricBaseline());
                baseline.UpdateBaseline(history);

                var detectionMethods = new List<AnomalyDetectionMethod>
                {
                    new ZScoreDetection(_options.ZScoreThreshold),
                    new MovingAverageDetection(_options.MovingAverageWindow),
                    new SeasonalDecomposition(_options.SeasonalityPeriod),
                    new IsolationForest(_options.IsolationSampleSize)
                };

                var anomalyResults = await Task.WhenAll(
                    detectionMethods.Select(method => 
                        DetectAnomalyAsync(method, history, currentPoint, baseline)));

                return CombineDetectionResults(anomalyResults, currentPoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing metric {MetricName} for anomalies", metricName);
                throw;
            }
        }

        private async Task<AnomalyDetectionResult> DetectAnomalyAsync(
            AnomalyDetectionMethod method,
            IReadOnlyList<MetricDataPoint> history,
            MetricDataPoint currentPoint,
            MetricBaseline baseline)
        {
            return await Task.Run(() =>
            {
                var context = new DetectionContext
                {
                    History = history,
                    CurrentPoint = currentPoint,
                    Baseline = baseline
                };

                return method.DetectAnomaly(context);
            });
        }

        private AnomalyDetectionResult CombineDetectionResults(
            IEnumerable<AnomalyDetectionResult> results,
            MetricDataPoint point)
        {
            var anomalyScores = results.Select(r => r.AnomalyScore).ToList();
            var isAnomaly = results.Count(r => r.IsAnomaly) >= _options.MinimumDetectionsRequired;
            var confidence = CalculateConfidence(anomalyScores);

            return new AnomalyDetectionResult
            {
                IsAnomaly = isAnomaly,
                AnomalyScore = anomalyScores.Average(),
                Confidence = confidence,
                Timestamp = point.Timestamp,
                Value = point.Value,
                ContributingFactors = GetContributingFactors(results),
                DetectionMethods = results.Where(r => r.IsAnomaly)
                    .Select(r => r.DetectionMethod)
                    .ToList()
            };
        }

        private double CalculateConfidence(List<double> anomalyScores)
        {
            if (!anomalyScores.Any())
                return 0;

            var mean = anomalyScores.Average();
            var stdDev = anomalyScores.StandardDeviation();
            var consistency = 1 - (stdDev / (mean + double.Epsilon));

            return Math.Min(1, Math.Max(0, consistency));
        }

        private List<AnomalyFactor> GetContributingFactors(IEnumerable<AnomalyDetectionResult> results)
        {
            return results
                .Where(r => r.IsAnomaly)
                .SelectMany(r => r.ContributingFactors)
                .GroupBy(f => f.FactorType)
                .Select(g => new AnomalyFactor
                {
                    FactorType = g.Key,
                    Contribution = g.Average(f => f.Contribution),
                    Description = string.Join("; ", g.Select(f => f.Description).Distinct())
                })
                .OrderByDescending(f => f.Contribution)
                .ToList();
        }
    }

    public abstract class AnomalyDetectionMethod
    {
        public abstract string MethodName { get; }
        public abstract AnomalyDetectionResult DetectAnomaly(DetectionContext context);

        protected double CalculateZScore(double value, double mean, double stdDev)
        {
            return stdDev == 0 ? 0 : Math.Abs(value - mean) / stdDev;
        }
    }

    public class ZScoreDetection : AnomalyDetectionMethod
    {
        private readonly double _threshold;

        public ZScoreDetection(double threshold)
        {
            _threshold = threshold;
        }

        public override string MethodName => "Z-Score";

        public override AnomalyDetectionResult DetectAnomaly(DetectionContext context)
        {
            var values = context.History.Select(p => p.Value).ToList();
            var mean = values.Average();
            var stdDev = values.StandardDeviation();
            var zScore = CalculateZScore(context.CurrentPoint.Value, mean, stdDev);

            var isAnomaly = zScore > _threshold;
            var anomalyScore = Math.Min(1, zScore / (_threshold * 2));

            return new AnomalyDetectionResult
            {
                IsAnomaly = isAnomaly,
                AnomalyScore = anomalyScore,
                DetectionMethod = MethodName,
                ContributingFactors = new List<AnomalyFactor>
                {
                    new AnomalyFactor
                    {
                        FactorType = "Statistical",
                        Contribution = anomalyScore,
                        Description = $"Z-Score: {zScore:F2} (threshold: {_threshold})"
                    }
                }
            };
        }
    }

    public class MovingAverageDetection : AnomalyDetectionMethod
    {
        private readonly int _windowSize;

        public MovingAverageDetection(int windowSize)
        {
            _windowSize = windowSize;
        }

        public override string MethodName => "Moving Average";

        public override AnomalyDetectionResult DetectAnomaly(DetectionContext context)
        {
            var recentPoints = context.History.TakeLast(_windowSize).ToList();
            if (recentPoints.Count < _windowSize)
                return new AnomalyDetectionResult { IsAnomaly = false, AnomalyScore = 0 };

            var movingAverage = recentPoints.Select(p => p.Value).Average();
            var movingStdDev = recentPoints.Select(p => p.Value).StandardDeviation();
            var deviation = Math.Abs(context.CurrentPoint.Value - movingAverage);
            var deviationScore = deviation / (movingStdDev + double.Epsilon);

            var isAnomaly = deviationScore > 2;
            var anomalyScore = Math.Min(1, deviationScore / 4);

            return new AnomalyDetectionResult
            {
                IsAnomaly = isAnomaly,
                AnomalyScore = anomalyScore,
                DetectionMethod = MethodName,
                ContributingFactors = new List<AnomalyFactor>
                {
                    new AnomalyFactor
                    {
                        FactorType = "Trend",
                        Contribution = anomalyScore,
                        Description = $"Deviation from moving average: {deviation:F2}"
                    }
                }
            };
        }
    }

    public class SeasonalDecomposition : AnomalyDetectionMethod
    {
        private readonly int _seasonalityPeriod;

        public SeasonalDecomposition(int seasonalityPeriod)
        {
            _seasonalityPeriod = seasonalityPeriod;
        }

        public override string MethodName => "Seasonal Decomposition";

        public override AnomalyDetectionResult DetectAnomaly(DetectionContext context)
        {
            if (context.History.Count < _seasonalityPeriod * 2)
                return new AnomalyDetectionResult { IsAnomaly = false, AnomalyScore = 0 };

            var seasonalPattern = CalculateSeasonalPattern(context.History);
            var expectedValue = PredictNextValue(context.History, seasonalPattern);
            var deviation = Math.Abs(context.CurrentPoint.Value - expectedValue);
            var historicalDeviations = context.History.Select(p => Math.Abs(p.Value - expectedValue)).ToList();
            var deviationThreshold = historicalDeviations.Percentile(95);

            var isAnomaly = deviation > deviationThreshold;
            var anomalyScore = Math.Min(1, deviation / (deviationThreshold * 2));

            return new AnomalyDetectionResult
            {
                IsAnomaly = isAnomaly,
                AnomalyScore = anomalyScore,
                DetectionMethod = MethodName,
                ContributingFactors = new List<AnomalyFactor>
                {
                    new AnomalyFactor
                    {
                        FactorType = "Seasonality",
                        Contribution = anomalyScore,
                        Description = $"Seasonal deviation: {deviation:F2}"
                    }
                }
            };
        }

        private double[] CalculateSeasonalPattern(IReadOnlyList<MetricDataPoint> history)
        {
            var pattern = new double[_seasonalityPeriod];
            var counts = new int[_seasonalityPeriod];

            for (int i = 0; i < history.Count; i++)
            {
                var index = i % _seasonalityPeriod;
                pattern[index] += history[i].Value;
                counts[index]++;
            }

            for (int i = 0; i < _seasonalityPeriod; i++)
            {
                pattern[i] = counts[i] > 0 ? pattern[i] / counts[i] : 0;
            }

            return pattern;
        }

        private double PredictNextValue(IReadOnlyList<MetricDataPoint> history, double[] seasonalPattern)
        {
            var index = history.Count % _seasonalityPeriod;
            return seasonalPattern[index];
        }
    }

    public class IsolationForest : AnomalyDetectionMethod
    {
        private readonly int _sampleSize;
        private const int TreeCount = 100;
        private const double AnomalyThreshold = 0.6;

        public IsolationForest(int sampleSize)
        {
            _sampleSize = sampleSize;
        }

        public override string MethodName => "Isolation Forest";

        public override AnomalyDetectionResult DetectAnomaly(DetectionContext context)
        {
            if (context.History.Count < _sampleSize)
                return new AnomalyDetectionResult { IsAnomaly = false, AnomalyScore = 0 };

            var samples = context.History
                .OrderBy(_ => Guid.NewGuid())
                .Take(_sampleSize)
                .Select(p => p.Value)
                .ToList();

            var anomalyScore = CalculateAnomalyScore(context.CurrentPoint.Value, samples);
            var isAnomaly = anomalyScore > AnomalyThreshold;

            return new AnomalyDetectionResult
            {
                IsAnomaly = isAnomaly,
                AnomalyScore = anomalyScore,
                DetectionMethod = MethodName,
                ContributingFactors = new List<AnomalyFactor>
                {
                    new AnomalyFactor
                    {
                        FactorType = "Isolation",
                        Contribution = anomalyScore,
                        Description = $"Isolation score: {anomalyScore:F2}"
                    }
                }
            };
        }

        private double CalculateAnomalyScore(double value, List<double> samples)
        {
            var pathLengths = new List<int>();
            for (int i = 0; i < TreeCount; i++)
            {
                pathLengths.Add(CalculateIsolationTreePath(value, samples));
            }

            var avgPathLength = pathLengths.Average();
            var normalizedScore = Math.Pow(2, -avgPathLength / CalculateAveragePathLength(samples.Count));
            return normalizedScore;
        }

        private int CalculateIsolationTreePath(double value, List<double> samples, int depth = 0, int maxDepth = 10)
        {
            if (depth >= maxDepth || samples.Count <= 1)
                return depth;

            var min = samples.Min();
            var max = samples.Max();
            if (min == max)
                return depth;

            var splitValue = min + (max - min) * Random.Shared.NextDouble();
            var subsamples = samples.Where(x => x < splitValue).ToList();
            return CalculateIsolationTreePath(value, subsamples, depth + 1, maxDepth);
        }

        private double CalculateAveragePathLength(int n)
        {
            if (n <= 1) return 0;
            return 2 * (Math.Log(n - 1) + 0.5772156649) - (2 * (n - 1) / n);
        }
    }

    public class DetectionContext
    {
        public IReadOnlyList<MetricDataPoint> History { get; init; }
        public MetricDataPoint CurrentPoint { get; init; }
        public MetricBaseline Baseline { get; init; }
    }

    public class MetricBaseline
    {
        private readonly ConcurrentDictionary<string, double> _baselineValues = new();

        public void UpdateBaseline(IReadOnlyList<MetricDataPoint> history)
        {
            if (!history.Any())
                return;

            var values = history.Select(p => p.Value).ToList();
            _baselineValues["mean"] = values.Average();
            _baselineValues["stddev"] = values.StandardDeviation();
            _baselineValues["median"] = values.Median();
            _baselineValues["p95"] = values.Percentile(95);
            _baselineValues["p99"] = values.Percentile(99);
        }

        public double GetBaselineValue(string key) => 
            _baselineValues.TryGetValue(key, out var value) ? value : 0;
    }

    public class AnomalyDetectionResult
    {
        public bool IsAnomaly { get; init; }
        public double AnomalyScore { get; init; }
        public double Confidence { get; init; }
        public DateTime Timestamp { get; init; }
        public double Value { get; init; }
        public string DetectionMethod { get; init; }
        public List<AnomalyFactor> ContributingFactors { get; init; } = new();
    }

    public class AnomalyFactor
    {
        public string FactorType { get; init; }
        public double Contribution { get; init; }
        public string Description { get; init; }
    }

    public class AnomalyDetectionOptions
    {
        public double ZScoreThreshold { get; set; } = 3.0;
        public int MovingAverageWindow { get; set; } = 60;
        public int SeasonalityPeriod { get; set; } = 24;
        public int IsolationSampleSize { get; set; } = 100;
        public int MinimumDetectionsRequired { get; set; } = 2;
    }
} 