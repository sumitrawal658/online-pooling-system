using MathNet.Numerics.Statistics;

namespace PollSystem.Services.Diagnostics
{
    public class StatisticalAnomalyDetector : IAnomalyDetector
    {
        private readonly AnomalyDetectionOptions _options;

        public StatisticalAnomalyDetector(AnomalyDetectionOptions options)
        {
            _options = options;
        }

        public string DetectionMethod => "Statistical";

        public AnomalyDetectionResult DetectAnomaly(AnomalyDetectionContext context)
        {
            if (context.History.Count < _options.MinDataPointsForAnalysis)
                return new AnomalyDetectionResult { IsAnomaly = false };

            var values = context.History.Select(p => p.Value).ToList();
            var mean = values.Average();
            var stdDev = values.StandardDeviation();
            var currentValue = context.CurrentPoint.Value;

            var zScore = Math.Abs((currentValue - mean) / stdDev);
            var isAnomaly = zScore > _options.StatisticalThreshold;

            if (!isAnomaly)
                return new AnomalyDetectionResult { IsAnomaly = false };

            var anomalyScore = Math.Min(1.0, zScore / (_options.StatisticalThreshold * 2));
            var confidence = CalculateConfidence(zScore, values.Count);

            return new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = anomalyScore,
                Confidence = confidence,
                Value = currentValue,
                Timestamp = context.CurrentPoint.Timestamp,
                DetectionMethod = DetectionMethod,
                ContributingFactors = new List<AnomalyFactor>
                {
                    new AnomalyFactor
                    {
                        FactorType = "Statistical",
                        Description = $"Value deviates {zScore:F2} standard deviations from mean",
                        Confidence = confidence,
                        Impact = anomalyScore
                    }
                }
            };
        }

        private double CalculateConfidence(double zScore, int sampleSize)
        {
            // Adjust confidence based on sample size and z-score
            var sampleSizeFactor = Math.Min(1.0, sampleSize / 30.0);
            var zScoreFactor = Math.Min(1.0, zScore / (_options.StatisticalThreshold * 2));
            return (sampleSizeFactor + zScoreFactor) / 2;
        }
    }

    public class ThresholdAnomalyDetector : IAnomalyDetector
    {
        private readonly AnomalyDetectionOptions _options;

        public ThresholdAnomalyDetector(AnomalyDetectionOptions options)
        {
            _options = options;
        }

        public string DetectionMethod => "Threshold";

        public AnomalyDetectionResult DetectAnomaly(AnomalyDetectionContext context)
        {
            var baseline = context.Baseline;
            var currentValue = context.CurrentPoint.Value;

            var p95Threshold = baseline.GetBaselineValue("p95");
            var p99Threshold = baseline.GetBaselineValue("p99");

            if (currentValue <= p95Threshold)
                return new AnomalyDetectionResult { IsAnomaly = false };

            var exceedsP99 = currentValue > p99Threshold;
            var anomalyScore = exceedsP99 ? 0.9 : 0.7;
            var confidence = CalculateConfidence(currentValue, p95Threshold, p99Threshold);

            return new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = anomalyScore,
                Confidence = confidence,
                Value = currentValue,
                Timestamp = context.CurrentPoint.Timestamp,
                DetectionMethod = DetectionMethod,
                ContributingFactors = new List<AnomalyFactor>
                {
                    new AnomalyFactor
                    {
                        FactorType = "Threshold",
                        Description = exceedsP99 
                            ? "Value exceeds 99th percentile threshold"
                            : "Value exceeds 95th percentile threshold",
                        Confidence = confidence,
                        Impact = anomalyScore
                    }
                }
            };
        }

        private double CalculateConfidence(double value, double p95, double p99)
        {
            if (value > p99)
                return 0.9;
            if (value > p95)
                return 0.7;
            return 0.0;
        }
    }

    public class TrendBasedAnomalyDetector : IAnomalyDetector
    {
        private readonly AnomalyDetectionOptions _options;

        public TrendBasedAnomalyDetector(AnomalyDetectionOptions options)
        {
            _options = options;
        }

        public string DetectionMethod => "TrendBased";

        public AnomalyDetectionResult DetectAnomaly(AnomalyDetectionContext context)
        {
            if (context.History.Count < _options.MinDataPointsForAnalysis)
                return new AnomalyDetectionResult { IsAnomaly = false };

            var trend = AnalyzeTrend(context.History);
            var currentValue = context.CurrentPoint.Value;
            var expectedValue = PredictValue(trend, context.History);
            
            var deviation = Math.Abs(currentValue - expectedValue);
            var relativeDeviation = deviation / expectedValue;

            if (relativeDeviation <= _options.TrendThreshold)
                return new AnomalyDetectionResult { IsAnomaly = false };

            var anomalyScore = Math.Min(1.0, relativeDeviation);
            var confidence = CalculateConfidence(relativeDeviation, trend.Confidence);

            return new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = anomalyScore,
                Confidence = confidence,
                Value = currentValue,
                Timestamp = context.CurrentPoint.Timestamp,
                DetectionMethod = DetectionMethod,
                ContributingFactors = new List<AnomalyFactor>
                {
                    new AnomalyFactor
                    {
                        FactorType = "Trend",
                        Description = $"Value deviates {relativeDeviation:P0} from expected trend",
                        Confidence = confidence,
                        Impact = anomalyScore
                    }
                }
            };
        }

        private TrendAnalysis AnalyzeTrend(IReadOnlyList<MetricDataPoint> history)
        {
            var x = Enumerable.Range(0, history.Count).Select(i => (double)i).ToArray();
            var y = history.Select(p => p.Value).ToArray();
            var regression = SimpleRegression.Fit(x, y);

            var values = history.Select(p => p.Value).ToList();
            var volatility = values.StandardDeviation() / values.Average();
            var confidence = Math.Max(0, 1 - (volatility / _options.VolatilityThreshold));

            return new TrendAnalysis
            {
                Slope = regression.Item2,
                Intercept = regression.Item1,
                Confidence = confidence
            };
        }

        private double PredictValue(TrendAnalysis trend, IReadOnlyList<MetricDataPoint> history)
        {
            var timePoint = history.Count;
            return trend.Intercept + (trend.Slope * timePoint);
        }

        private double CalculateConfidence(double deviation, double trendConfidence)
        {
            var deviationFactor = Math.Max(0, 1 - (deviation / 2));
            return (deviationFactor + trendConfidence) / 2;
        }
    }

    public class SeasonalAnomalyDetector : IAnomalyDetector
    {
        private readonly AnomalyDetectionOptions _options;

        public SeasonalAnomalyDetector(AnomalyDetectionOptions options)
        {
            _options = options;
        }

        public string DetectionMethod => "Seasonal";

        public AnomalyDetectionResult DetectAnomaly(AnomalyDetectionContext context)
        {
            if (context.History.Count < _options.MinDataPointsForAnalysis * 2)
                return new AnomalyDetectionResult { IsAnomaly = false };

            var seasonality = DetectSeasonality(context.History);
            if (!seasonality.HasPattern)
                return new AnomalyDetectionResult { IsAnomaly = false };

            var expectedValue = PredictSeasonalValue(context.History, seasonality);
            var currentValue = context.CurrentPoint.Value;
            
            var deviation = Math.Abs(currentValue - expectedValue);
            var relativeDeviation = deviation / expectedValue;

            if (relativeDeviation <= _options.TrendThreshold)
                return new AnomalyDetectionResult { IsAnomaly = false };

            var anomalyScore = Math.Min(1.0, relativeDeviation);
            var confidence = CalculateConfidence(relativeDeviation, seasonality.Confidence);

            return new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = anomalyScore,
                Confidence = confidence,
                Value = currentValue,
                Timestamp = context.CurrentPoint.Timestamp,
                DetectionMethod = DetectionMethod,
                ContributingFactors = new List<AnomalyFactor>
                {
                    new AnomalyFactor
                    {
                        FactorType = "Seasonal",
                        Description = $"Value deviates {relativeDeviation:P0} from seasonal pattern",
                        Confidence = confidence,
                        Impact = anomalyScore
                    }
                }
            };
        }

        private SeasonalityInfo DetectSeasonality(IReadOnlyList<MetricDataPoint> history)
        {
            var values = history.Select(p => p.Value).ToArray();
            var autocorrelation = CalculateAutocorrelation(values);
            var peaks = FindAutocorrelationPeaks(autocorrelation);

            if (!peaks.Any())
                return new SeasonalityInfo { HasPattern = false };

            var dominantPeriod = peaks.OrderByDescending(p => p.Value).First();
            return new SeasonalityInfo
            {
                HasPattern = true,
                Period = dominantPeriod.Key,
                Confidence = dominantPeriod.Value
            };
        }

        private double[] CalculateAutocorrelation(double[] values)
        {
            var maxLag = values.Length / 2;
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
                    autocorrelation[i] > 0.3)
                {
                    peaks[i] = autocorrelation[i];
                }
            }
            return peaks;
        }

        private double PredictSeasonalValue(
            IReadOnlyList<MetricDataPoint> history,
            SeasonalityInfo seasonality)
        {
            var period = seasonality.Period;
            var values = history.Select(p => p.Value).ToList();
            var seasonalValues = new List<double>();

            for (int i = history.Count - period; i >= 0; i -= period)
            {
                seasonalValues.Add(values[i]);
            }

            return seasonalValues.Average();
        }

        private double CalculateConfidence(double deviation, double seasonalConfidence)
        {
            var deviationFactor = Math.Max(0, 1 - (deviation / 2));
            return (deviationFactor + seasonalConfidence) / 2;
        }
    }

    public class TrendAnalysis
    {
        public double Slope { get; init; }
        public double Intercept { get; init; }
        public double Confidence { get; init; }
    }

    public class SeasonalityInfo
    {
        public bool HasPattern { get; init; }
        public int Period { get; init; }
        public double Confidence { get; init; }
    }
} 