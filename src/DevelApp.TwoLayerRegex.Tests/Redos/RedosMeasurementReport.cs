using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevelApp.TwoLayerRegex.Tests.Redos
{
    /// <summary>
    /// Builds the ReDoS-immunity measurement report (JSON) for the two-layer
    /// engine: the pattern grid at 1/10/100 KB, the scaling ladder fits, and
    /// the unanchored-search characteristic. The committed copy lives in
    /// docs/measurements/redos-immunity/ and backs the article's ReDoS
    /// tables; the CI suite enforces the same properties on every build.
    /// </summary>
    public static class RedosMeasurementReport
    {
        /// <summary>Full grid lengths (input characters): 1 KB, 10 KB, 100 KB.</summary>
        public static readonly int[] FullGridLengths = { 1_024, 10_240, 102_400 };

        /// <summary>Smoke-mode grid lengths, used when the environment flag is not set (CI).</summary>
        public static readonly int[] SmokeGridLengths = { 1_024, 10_240 };

        /// <summary>
        /// Whether to run the full report: set REDOS_REPORT=full. CI runs the
        /// smoke mode by default; the committed report is regenerated with
        /// the full mode on a quiet machine.
        /// </summary>
        public static bool FullMode => Environment.GetEnvironmentVariable("REDOS_REPORT") == "full";

        /// <summary>Builds the full report JSON for the two-layer engine.</summary>
        public static string Build()
        {
            bool full = FullMode;
            int repetitions = full ? 5 : 1;
            int[] gridLengths = full ? FullGridLengths : SmokeGridLengths;
            int[] ladder = full
                ? RedosPatternCatalog.ScalingLadder.ToArray()
                : new[] { 1_024, 16_384 };

            var grid = new List<object>();
            foreach (var testCase in RedosPatternCatalog.ArticleAndCorpusCases)
            {
                foreach (int length in gridLengths)
                {
                    var measurement = RedosMeasurementHarness.MeasureOnce(testCase, length);
                    grid.Add(ToGridRow(measurement, testCase.ExpectMatch));
                }
            }

            var scaling = new List<object>();
            foreach (var testCase in RedosPatternCatalog.ScalingCases)
            {
                var fit = RedosMeasurementHarness.MeasureScaling(testCase, ladder, repetitions);
                scaling.Add(new
                {
                    caseName = fit.CaseName,
                    pattern = fit.Pattern,
                    slopeNsPerChar = Math.Round(fit.SlopeNsPerChar, 2),
                    r2 = Math.Round(fit.R2, 4),
                    growthRatio = Math.Round(fit.GrowthRatio, 2),
                    points = fit.Points.Select(p => new { length = p.Length, ms = Math.Round(p.Milliseconds, 4) }),
                });
            }

            var characteristic = MeasureUnanchoredCharacteristic(full);

            var report = new
            {
                engine = "DevelApp.TwoLayerRegex (OriginalTwoLayerRegexArchitecture showcase)",
                method = "MatchExact — anchored full-input evaluation: one Phase 1 scan (O(m·n)) plus, only on structural accept, the bounded Phase 2 walk",
                generatedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                mode = full ? "full" : "smoke",
                repetitions = repetitions,
                environment = new
                {
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                },
                note = "The .NET backtracking and NonBacktracking baselines for the identical pattern catalog and inputs are committed in the ENFAStepLexer-StepParser repository (docs/measurements/redos-immunity/); this report adds the two-layer engine's own column.",
                grid,
                scaling,
                unanchoredCharacteristic = characteristic,
            };

            var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };
            return JsonSerializer.Serialize(report, options);
        }

        private static object ToGridRow(CaseMeasurement m, bool expectMatch) => new
        {
            caseName = m.CaseName,
            pattern = m.Pattern,
            input = m.InputKind,
            length = m.Length,
            ms = Math.Round(m.Milliseconds, 4),
            matched = m.Matched,
            expected = expectMatch,
            verdict = m.Matched == expectMatch
                ? (m.BudgetExceeded ? "matched-but-budget-exceeded" : "correct")
                : "WRONG-VERDICT",
            budgetExceeded = m.BudgetExceeded,
        };

        /// <summary>
        /// The known characteristic of the architecture, measured honestly:
        /// unanchored leftmost search runs one Phase 1 scan per candidate
        /// start position, so a pattern whose candidate set is dense (every
        /// position starts a potential match) degrades to O(m·n²) worst case.
        /// Capped at small lengths; this is a documented limitation of the
        /// showcase, not a ReDoS exposure — the per-scan work stays linear.
        /// </summary>
        private static object MeasureUnanchoredCharacteristic(bool full)
        {
            int[] lengths = full ? new[] { 512, 1_024, 2_048 } : new[] { 512, 1_024 };
            var engine = new TwoLayerRegex(@"a+b");
            var points = new List<object>();
            foreach (int length in lengths)
            {
                string input = new string('a', length); // 'a' run, no 'b': every start scans to the end
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = engine.Match(input);
                sw.Stop();
                points.Add(new { length, ms = Math.Round(sw.Elapsed.TotalMilliseconds, 3), matched = result.Success });
            }

            return new
            {
                pattern = "a+b",
                input = "'a' × n via unanchored Match() (not MatchExact)",
                points,
                expected = "quadratic in n (O(m·n²) documented limitation); measured at capped lengths only",
            };
        }
    }
}
