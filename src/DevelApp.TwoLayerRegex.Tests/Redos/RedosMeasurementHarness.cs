using System.Diagnostics;

namespace DevelApp.TwoLayerRegex.Tests.Redos
{
    /// <summary>One measured pattern/input/length cell of the grid.</summary>
    public sealed record CaseMeasurement(
        string CaseName,
        string Pattern,
        string InputKind,
        int Length,
        double Milliseconds,
        bool Matched,
        bool BudgetExceeded);

    /// <summary>One scaling-fit result over the ladder.</summary>
    public sealed record ScalingFit(
        string CaseName,
        string Pattern,
        double SlopeNsPerChar,
        double R2,
        double GrowthRatio,
        IReadOnlyList<CaseMeasurement> Points);

    /// <summary>
    /// Measurement harness for the two-layer engine. Every case is an
    /// anchored full-input evaluation (<see cref="TwoLayerRegex.MatchExact"/>):
    /// one Phase 1 scan over the whole input plus, only when the scan finds
    /// a structural accept, the bounded Phase 2 walk. The ENFA StepLexer
    /// suite measures tokenization (search) because that is its entry
    /// point; the two-layer engine's entry point with the full O(m·n)
    /// guarantee is the anchored scan, and that is what is measured and
    /// documented in the article.
    /// </summary>
    public static class RedosMeasurementHarness
    {
        /// <summary>Runs one case once and returns the measurement.</summary>
        public static CaseMeasurement MeasureOnce(RedosCase testCase, int length)
        {
            string input = RedosPatternCatalog.BuildInput(testCase.Input, length);
            var engine = new TwoLayerRegex(testCase.Pattern);
            return MeasureOnce(engine, testCase, input, length);
        }

        /// <summary>Runs one case once against a pre-built engine and input.</summary>
        public static CaseMeasurement MeasureOnce(TwoLayerRegex engine, RedosCase testCase, string input, int length)
        {
            var sw = Stopwatch.StartNew();
            var result = engine.MatchExact(input);
            sw.Stop();
            return new CaseMeasurement(
                testCase.Name,
                testCase.Pattern,
                testCase.Input.ToString(),
                length,
                sw.Elapsed.TotalMilliseconds,
                result.Success,
                engine.LastPhase2BudgetExceeded);
        }

        /// <summary>
        /// Runs one case min-of-N times per ladder length and fits time
        /// against length. Returns the fit: least-squares slope in ns/char,
        /// R², and the growth ratio t(100 KB)/t(16 KB).
        /// </summary>
        public static ScalingFit MeasureScaling(RedosCase testCase, IReadOnlyList<int> ladder, int repetitions)
        {
            var engine = new TwoLayerRegex(testCase.Pattern);
            var points = new List<CaseMeasurement>(ladder.Count);
            foreach (int length in ladder)
            {
                string input = RedosPatternCatalog.BuildInput(testCase.Input, length);
                CaseMeasurement best = MeasureOnce(engine, testCase, input, length);
                for (int i = 1; i < repetitions; i++)
                {
                    var candidate = MeasureOnce(engine, testCase, input, length);
                    if (candidate.Milliseconds < best.Milliseconds)
                    {
                        best = candidate;
                    }
                }

                points.Add(best);
            }

            double slope = LeastSquaresSlopeNsPerChar(points);
            double r2 = RSquared(points);
            double growth = GrowthRatio(points, 16384, 102400);
            return new ScalingFit(testCase.Name, testCase.Pattern, slope, r2, growth, points);
        }

        /// <summary>
        /// Least-squares slope of time against length, in nanoseconds per
        /// input character.
        /// </summary>
        public static double LeastSquaresSlopeNsPerChar(IReadOnlyList<CaseMeasurement> points)
        {
            int n = points.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
            foreach (var p in points)
            {
                double x = p.Length;
                double y = p.Milliseconds * 1_000_000.0; // ms → ns
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }

            double denominator = n * sumXX - sumX * sumX;
            return denominator == 0 ? 0 : (n * sumXY - sumX * sumY) / denominator;
        }

        /// <summary>Coefficient of determination of the linear fit.</summary>
        public static double RSquared(IReadOnlyList<CaseMeasurement> points)
        {
            double meanY = points.Average(p => p.Milliseconds);
            double ssTot = points.Sum(p => Math.Pow(p.Milliseconds - meanY, 2));
            double slope = LeastSquaresSlopeNsPerChar(points) / 1_000_000.0; // ns/char → ms/char
            double intercept = points.Average(p => p.Milliseconds) - slope * points.Average(p => p.Length);
            double ssRes = points.Sum(p => Math.Pow(p.Milliseconds - (slope * p.Length + intercept), 2));
            return ssTot == 0 ? 1.0 : 1.0 - ssRes / ssTot;
        }

        /// <summary>t(big)/t(small) over the measured points; linear predicts 6.25 for 100 KB/16 KB, quadratic 39.</summary>
        public static double GrowthRatio(IReadOnlyList<CaseMeasurement> points, int smallLength, int bigLength)
        {
            double? small = FirstAt(points, smallLength)?.Milliseconds;
            double? big = FirstAt(points, bigLength)?.Milliseconds;
            return small is > 0 && big.HasValue ? big.Value / small.Value : 0;
        }

        private static CaseMeasurement? FirstAt(IReadOnlyList<CaseMeasurement> points, int length)
            => points.FirstOrDefault(p => p.Length == length);
    }
}
