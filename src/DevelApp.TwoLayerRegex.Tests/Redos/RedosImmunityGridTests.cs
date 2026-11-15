using System.Diagnostics;
using DevelApp.TwoLayerRegex;
using Xunit;

namespace DevelApp.TwoLayerRegex.Tests.Redos
{
    /// <summary>
    /// The security guarantee, measured: every article/corpus pattern
    /// against its adversarial input at 10 KB and 100 KB, evaluated as an
    /// anchored full-input match. Phase 1 alone rejects the non-matching
    /// attacks (the linear scan is the security boundary); the matching
    /// runs add the bounded Phase 2 walk. Nothing may exceed the generous
    /// absolute caps, and the Phase 2 step budget must never be exhausted.
    /// </summary>
    public sealed class RedosImmunityGridTests
    {
        [Theory]
        [MemberData(nameof(CasesAt10Kb))]
        public void Attack_input_at_10Kb_finishes_in_bounded_time(RedosCase testCase)
            => RunCase(testCase, 10_240, capMilliseconds: 2_000);

        [Theory]
        [MemberData(nameof(CasesAt100Kb))]
        public void Attack_input_at_100Kb_finishes_in_bounded_time(RedosCase testCase)
            => RunCase(testCase, 102_400, capMilliseconds: 10_000);

        public static TheoryData<RedosCase> CasesAt10Kb => CasesAt(10_240);
        public static TheoryData<RedosCase> CasesAt100Kb => CasesAt(102_400);

        public static TheoryData<RedosCase> CasesAt(int length) =>
            Build(RedosPatternCatalog.ArticleAndCorpusCases, length);

        private static TheoryData<RedosCase> Build(IEnumerable<RedosCase> cases, int length)
        {
            var data = new TheoryData<RedosCase>();
            foreach (var c in cases)
            {
                data.Add(c);
            }

            return data;
        }

        private static void RunCase(RedosCase testCase, int length, double capMilliseconds)
        {
            string input = RedosPatternCatalog.BuildInput(testCase.Input, length);
            var engine = new TwoLayerRegex(testCase.Pattern);

            var sw = Stopwatch.StartNew();
            var result = engine.MatchExact(input);
            sw.Stop();

            Assert.True(
                sw.Elapsed.TotalMilliseconds < capMilliseconds,
                $"{testCase.Name} ({testCase.Pattern}) took {sw.Elapsed.TotalMilliseconds:F1} ms " +
                $"on a {length}-char adversarial input (cap {capMilliseconds} ms).");
            Assert.Equal(testCase.ExpectMatch, result.Success);
            Assert.False(engine.LastPhase2BudgetExceeded,
                $"{testCase.Name} exhausted the Phase 2 step budget at {length} chars.");
        }
    }
}
