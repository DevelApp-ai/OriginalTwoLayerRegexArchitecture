using System.Diagnostics;
using DevelApp.TwoLayerRegex;
using Xunit;

namespace DevelApp.TwoLayerRegex.Tests.Redos
{
    /// <summary>
    /// The documented characteristic of the showcase, measured and pinned:
    /// unanchored leftmost search runs one Phase 1 scan per candidate start
    /// position, so a dense candidate set degrades the total to O(m·n²)
    /// worst case. This is a search-strategy cost, not catastrophic
    /// backtracking — each individual scan stays linear and a no-match
    /// always terminates. The anchored entry points (MatchExact, leading-^
    /// patterns, the IsMatch fast path) keep the pure O(m·n) guarantee.
    /// The tests cap the length, mirroring how the ENFA suite pins its
    /// many-token characteristic.
    /// </summary>
    public sealed class UnanchoredSearchCharacteristicTests
    {
        [Fact]
        public void Unanchored_dense_candidate_search_is_quadratic_but_bounded()
        {
            var engine = new TwoLayerRegex(@"a+b");
            double ms512 = Run(engine, 512);
            double ms1024 = Run(engine, 1_024);
            double ms2048 = Run(engine, 2_048);

            // Quadratic predicts 4× time per 2× length, so t(2048)/t(512)
            // = 16×; the cap of 30 admits the documented quadratic with
            // noise but fails on cubic (64×) or worse.
            double growth = ms2048 / ms512;
            Assert.True(growth <= 30.0,
                $"Unanchored search growth t(2048)/t(512) = {growth:F2} exceeds the quadratic envelope.");
            Assert.True(ms2048 < 5_000, $"Unanchored search took {ms2048:F1} ms at 2048 chars.");

            static double Run(TwoLayerRegex engine, int length)
            {
                string input = new string('a', length);
                var sw = Stopwatch.StartNew();
                var result = engine.Match(input);
                sw.Stop();
                Assert.False(result.Success); // no 'b' anywhere: no match, but every start scans
                return sw.Elapsed.TotalMilliseconds;
            }
        }

        [Fact]
        public void The_anchors_that_carried_the_security_claims_stay_pure_linear()
        {
            // Leading-^ patterns collapse to a single Phase 1 scan even
            // through the unanchored Match() entry point — that is the
            // property the article's evil-pattern claims rest on.
            var engine = new TwoLayerRegex(@"^(a+)+$");
            string attack = new string('a', 99_999) + "!";

            var sw = Stopwatch.StartNew();
            var result = engine.Match(attack);
            sw.Stop();

            Assert.False(result.Success);
            Assert.True(sw.Elapsed.TotalMilliseconds < 10_000,
                $"Leading-^ evil pattern took {sw.Elapsed.TotalMilliseconds:F1} ms at 100 KB.");
        }
    }
}
