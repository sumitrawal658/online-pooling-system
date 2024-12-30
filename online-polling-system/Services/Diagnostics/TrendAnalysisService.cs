using System.Collections.Concurrent;
using MathNet.Numerics.Statistics;
using MathNet.Numerics.LinearRegression;

namespace PollSystem.Services.Diagnostics
{
    public class TrendAnalysisService
    {
        private readonly ILogger<TrendAnalysisService> _logger;
        private readonly ConcurrentDictionary<string, TrendBaseline> _trendBaselines;
        private readonly TrendAnalysisOptions _options;

        public TrendAnalysisService(
            ILogger<TrendAnalysisService> logger,
            IOptions<TrendAnalysisOptions> options)
        {
            _logger = logger;
            _options = options.Value;
            _trendBaselines = new ConcurrentDictionary<string, TrendBaseline>();
        }

        public async Task<TrendAnalysisResult> AnalyzeTrendAsync(
            string metricName,
            IReadOnlyList<MetricDataPoint> history,
            TimeSpan? timeWindow = null)
        {
            try
            {
                var baseline = _trendBaselines.GetOrAdd(metricName, _ => new TrendBaseline());
                var analysisWindow = timeWindow ?? _options.DefaultAnalysisWindow;
                var relevantPoints = FilterRelevantPoints(history, analysisWindow);

                if (!relevantPoints.Any())
                    return new TrendAnalysisResult { TrendType = TrendType.Stable };

                var analyses = new List<ITrendAnalysis>
                {
                    new LinearRegressionAnalysis(_options),
                    new MovingAverageTrendAnalysis(_options),
                    new ExponentialSmoothingAnalysis(_options),
                    new SeasonalTrendAnalysis(_options)
                };

                var results = await Task.WhenAll(
                    analyses.Select(analysis => 
                        AnalyzeWithMethodAsync(analysis, relevantPoints, baseline)));

                return CombineAnalysisResults(results, relevantPoints);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing trend for {MetricName}", metricName);
                throw;
            }
        }

        private List<MetricDataPoint> FilterRelevantPoints(
            IReadOnlyList<MetricDataPoint> history,
            TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return history
                .Where(p => p.Timestamp >= cutoff)
                .OrderBy(p => p.Timestamp)
                .ToList();
        }

        private async Task<TrendAnalysisResult> AnalyzeWithMethodAsync(
            ITrendAnalysis analysis,
            List<MetricDataPoint> points,
            TrendBaseline baseline)
        {
            return await Task.Run(() => analysis.Analyze(points, baseline));
        }

        private TrendAnalysisResult CombineAnalysisResults(
            IEnumerable<TrendAnalysisResult> results,
            List<MetricDataPoint> points)
        {
            var slopes = results.Select(r => r.Slope).Where(s => !double.IsNaN(s)).ToList();
            var meanSlope = slopes.Any() ? slopes.Average() : 0;
            var dominantTrend = DetermineDominantTrend(results);
            var confidence = CalculateTrendConfidence(results, points);

            return new TrendAnalysisResult
            {
                TrendType = dominantTrend,
                Slope = meanSlope,
                Confidence = confidence,
                ChangeRate = CalculateChangeRate(points),
                Seasonality = DetectSeasonality(points),
                TrendComponents = results.SelectMany(r => r.TrendComponents).ToList(),
                PredictedValues = GeneratePredictions(points, meanSlope, dominantTrend)
            };
        }

        private TrendType DetermineDominantTrend(IEnumerable<TrendAnalysisResult> results)
        {
            var trendCounts = results
                .GroupBy(r => r.TrendType)
                .ToDictionary(g => g.Key, g => g.Count());

            return trendCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => Math.Abs((int)kvp.Key - (int)TrendType.Stable))
                .First()
                .Key;
        }

        private double CalculateTrendConfidence(
            IEnumerable<TrendAnalysisResult> results,
            List<MetricDataPoint> points)
        {
            var trendAgreement = results
                .GroupBy(r => r.TrendType)
                .Max(g => (double)g.Count() / results.Count());

            var rSquared = CalculateRSquared(points);
            return (trendAgreement + rSquared) / 2;
        }

        private double CalculateRSquared(List<MetricDataPoint> points)
        {
            if (points.Count < 2)
                return 0;

            var x = Enumerable.Range(0, points.Count).Select(i => (double)i).ToArray();
            var y = points.Select(p => p.Value).ToArray();

            try
            {
                var regression = SimpleRegression.Fit(x, y);
                var predictedY = x.Select(xi => regression.Item1 + regression.Item2 * xi);
                var meanY = y.Average();

                var totalSumSquares = y.Sum(yi => Math.Pow(yi - meanY, 2));
                var residualSumSquares = y.Zip(predictedY, (yi, fi) => Math.Pow(yi - fi, 2)).Sum();

                return 1 - (residualSumSquares / totalSumSquares);
            }
            catch
            {
                return 0;
            }
        }

        private double CalculateChangeRate(List<MetricDataPoint> points)
        {
            if (points.Count < 2)
                return 0;

            var firstValue = points.First().Value;
            var lastValue = points.Last().Value;
            var timeSpan = points.Last().Timestamp - points.First().Timestamp;

            if (timeSpan.TotalSeconds == 0 || firstValue == 0)
                return 0;

            return (lastValue - firstValue) / (firstValue * timeSpan.TotalHours);
        }

        private SeasonalityInfo DetectSeasonality(List<MetricDataPoint> points)
        {
            if (points.Count < _options.MinPointsForSeasonality)
                return new SeasonalityInfo { HasSeasonality = false };

            var values = points.Select(p => p.Value).ToArray();
            var autocorrelation = CalculateAutocorrelation(values);
            var peaks = FindAutocorrelationPeaks(autocorrelation);

            if (!peaks.Any())
                return new SeasonalityInfo { HasSeasonality = false };

            var dominantPeriod = peaks.OrderByDescending(p => p.Value).First().Key;
            return new SeasonalityInfo
            {
                HasSeasonality = true,
                Period = dominantPeriod,
                Strength = autocorrelation[dominantPeriod]
            };
        }

        private double[] CalculateAutocorrelation(double[] values)
        {
            var maxLag = Math.Min(values.Length / 2, _options.MaxSeasonalityPeriod);
            var result = new double[maxLag];
            var mean = values.Average();
            var variance = values.Sum(x => Math.Pow(x - mean, 2));

            for (int lag = 1; lag < maxLag; lag++)
            {
                var sum = 0.0;
                for (int i = 0; i < values.Length - lag; i++)
                {
                    sum += (values[i] - mean) * (values[i + lag] - mean);
                }
                result[lag] = sum / variance;
            }

            return result;
        }

        private Dictionary<int, double> FindAutocorrelationPeaks(double[] autocorrelation)
        {
            var peaks = new Dictionary<int, double>();
            for (int i = 2; i < autocorrelation.Length - 1; i++)
            {
                if (autocorrelation[i] > autocorrelation[i - 1] &&
                    autocorrelation[i] > autocorrelation[i + 1] &&
                    autocorrelation[i] > _options.SeasonalityThreshold)
                {
                    peaks[i] = autocorrelation[i];
                }
            }
            return peaks;
        }

        private List<PredictedValue> GeneratePredictions(
            List<MetricDataPoint> points,
            double slope,
            TrendType trendType)
        {
            if (!points.Any() || trendType == TrendType.Stable)
                return new List<PredictedValue>();

            var predictions = new List<PredictedValue>();
            var lastPoint = points.Last();
            var baseValue = lastPoint.Value;

            for (int i = 1; i <= _options.PredictionHorizon; i++)
            {
                var predictedValue = baseValue + (slope * i);
                predictions.Add(new PredictedValue
                {
                    Timestamp = lastPoint.Timestamp.AddMinutes(i * _options.PredictionIntervalMinutes),
                    Value = predictedValue,
                    Confidence = Math.Max(0, 1 - (i / (double)_options.PredictionHorizon))
                });
            }

            return predictions;
        }
    }

    public interface ITrendAnalysis
    {
        string MethodName { get; }
        TrendAnalysisResult Analyze(List<MetricDataPoint> points, TrendBaseline baseline);
    }

    public class LinearRegressionAnalysis : ITrendAnalysis
    {
        private readonly TrendAnalysisOptions _options;

        public LinearRegressionAnalysis(TrendAnalysisOptions options)
        {
            _options = options;
        }

        public string MethodName => "Linear Regression";

        public TrendAnalysisResult Analyze(List<MetricDataPoint> points, TrendBaseline baseline)
        {
            if (points.Count < 2)
                return new TrendAnalysisResult { TrendType = TrendType.Stable };

            var x = Enumerable.Range(0, points.Count).Select(i => (double)i).ToArray();
            var y = points.Select(p => p.Value).ToArray();
            var regression = SimpleRegression.Fit(x, y);
            var slope = regression.Item2;

            return new TrendAnalysisResult
            {
                TrendType = DetermineTrendType(slope),
                Slope = slope,
                TrendComponents = new List<TrendComponent>
                {
                    new TrendComponent
                    {
                        ComponentType = "Linear",
                        Coefficient = slope,
                        Description = $"Linear slope: {slope:F4}"
                    }
                }
            };
        }

        private TrendType DetermineTrendType(double slope)
        {
            if (Math.Abs(slope) < _options.TrendThreshold)
                return TrendType.Stable;
            return slope > 0 ? TrendType.Increasing : TrendType.Decreasing;
        }
    }

    public class TrendAnalysisOptions
    {
        public TimeSpan DefaultAnalysisWindow { get; set; } = TimeSpan.FromHours(24);
        public double TrendThreshold { get; set; } = 0.01;
        public int MinPointsForSeasonality { get; set; } = 48;
        public int MaxSeasonalityPeriod { get; set; } = 168;
        public double SeasonalityThreshold { get; set; } = 0.3;
        public int PredictionHorizon { get; set; } = 12;
        public int PredictionIntervalMinutes { get; set; } = 5;
    }

    public class TrendBaseline
    {
        private readonly ConcurrentDictionary<string, double> _baselineValues = new();

        public void UpdateBaseline(List<MetricDataPoint> points)
        {
            if (!points.Any())
                return;

            var values = points.Select(p => p.Value).ToList();
            _baselineValues["mean"] = values.Average();
            _baselineValues["stddev"] = values.StandardDeviation();
            _baselineValues["slope"] = CalculateBaselineSlope(points);
        }

        private double CalculateBaselineSlope(List<MetricDataPoint> points)
        {
            if (points.Count < 2)
                return 0;

            var x = Enumerable.Range(0, points.Count).Select(i => (double)i).ToArray();
            var y = points.Select(p => p.Value).ToArray();
            return SimpleRegression.Fit(x, y).Item2;
        }

        public double GetBaselineValue(string key) =>
            _baselineValues.TryGetValue(key, out var value) ? value : 0;
    }

    public class TrendAnalysisResult
    {
        public TrendType TrendType { get; init; }
        public double Slope { get; init; }
        public double Confidence { get; init; }
        public double ChangeRate { get; init; }
        public SeasonalityInfo Seasonality { get; init; }
        public List<TrendComponent> TrendComponents { get; init; } = new();
        public List<PredictedValue> PredictedValues { get; init; } = new();
    }

    public class TrendComponent
    {
        public string ComponentType { get; init; }
        public double Coefficient { get; init; }
        public string Description { get; init; }
    }

    public class SeasonalityInfo
    {
        public bool HasSeasonality { get; init; }
        public int Period { get; init; }
        public double Strength { get; init; }
    }

    public class PredictedValue
    {
        public DateTime Timestamp { get; init; }
        public double Value { get; init; }
        public double Confidence { get; init; }
    }
} 