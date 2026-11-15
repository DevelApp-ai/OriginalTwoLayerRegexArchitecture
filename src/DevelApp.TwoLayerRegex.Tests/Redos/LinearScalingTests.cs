using Xunit;

namespace DevelApp.TwoLayerRegex.Tests.Redos
{
    /// <summary>
    /// Linear scaling of the anchored evaluation, enforced continuously:
    /// each scaling pattern runs a ladder from 4 KB to 32 KB (minimum of 3
    /// repetitions per length). The growth ratio t(32 KB)/t(4 KB) must stay
    /// within the linear envelope: linear predicts 8, the asserted cap is
    /// 12, and quadratic would already show 64. The per-character slope is
    /// also capped, so a constant-factor regression fails the build too.
    /// </summary>
    public sealed class LinearScalingTests
    {
        private static readonly int[] CiLadder = { 4_096, 8_192, 16_384, 32_768 };

        [Theory]
        [MemberData(nameof(ScalingCasesData))]
        public void Anchored_evaluation_scales_linearly(RedosCase testCase)
        {
            var fit = RedosMeasurementHarness.MeasureScaling(testCase, CiLadder, repetitions: 3);

            // Every point must also give the correct verdict.
            foreach (var point in fit.Points)
            {
                Assert.True(point.Matched == testCase.ExpectMatch,
                    $"{testCase.Name}: wrong verdict at {point.Length} chars.");
                Assert.False(point.BudgetExceeded, $"{testCase.Name}: budget exceeded at {point.Length} chars.");
            }

            Assert.True(fit.GrowthRatio <= 14.0,
                $"{testCase.Name}: growth t(32 KB)/t(4 KB) = {fit.GrowthRatio:F2} exceeds the linear envelope " +
                "(linear predicts 8 with allocation noise, quadratic 64).");
            Assert.True(fit.SlopeNsPerChar is >= 0 and <= 5000,
                $"{testCase.Name}: slope {fit.SlopeNsPerChar:F1} ns/char exceeds the constant-factor cap.");
        }

        [Fact]
        public void The_evil_anchored_patterns_stay_linear_to_100Kb()
        {
            // The article's security claim is about the anchored evil
            // patterns: measure the full ladder for the two most
            // structurally complex ones and pin the linear envelope.
            var evil = new[]
            {
                RedosPatternCatalog.ScalingCases.First(c => c.Name == "scale-evil-(a+)+"),
                RedosPatternCatalog.ScalingCases.First(c => c.Name == "scale-evil-so-email"),
            };

            foreach (var testCase in evil)
            {
                var fit = RedosMeasurementHarness.MeasureScaling(
                    testCase, RedosPatternCatalog.ScalingLadder, repetitions: 3);
                Assert.True(fit.GrowthRatio <= 12.0,
                    $"{testCase.Name}: growth t(100 KB)/t(16 KB) = {fit.GrowthRatio:F2} exceeds the linear envelope " +
                    $"(linear predicts 6.25, quadratic 39).");
                Assert.All(fit.Points, p => Assert.True(p.Milliseconds < 10_000,
                    $"{testCase.Name}: {p.Milliseconds:F1} ms at {p.Length} chars exceeds the 10 s cap."));
            }
        }

        public static TheoryData<RedosCase> ScalingCasesData()
        {
            var data = new TheoryData<RedosCase>();
            foreach (var c in RedosPatternCatalog.ScalingCases)
            {
                data.Add(c);
            }

            return data;
        }
    }
}
