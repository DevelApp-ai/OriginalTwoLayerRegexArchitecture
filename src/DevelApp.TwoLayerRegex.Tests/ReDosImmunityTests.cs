using System.Diagnostics;
using DevelApp.TwoLayerRegex;

namespace DevelApp.TwoLayerRegex.Tests
{
    /// <summary>
    /// The security guarantee of the architecture: the classic
    /// catastrophic-backtracking patterns finish in bounded, linear time on
    /// attack inputs that would hang a classical backtracking engine.
    /// </summary>
    public sealed class ReDosImmunityTests
    {
        private const int AttackLength = 10_000;

        public static TheoryData<string> EvilPatterns => new TheoryData<string>
        {
            @"^(a+)+$",
            @"^(a|aa)+$",
            @"^([a-z]+)*$",
            @"^([a-zA-Z]+)*$",
            @"^(a*)*b$",
        };

        [Theory]
        [MemberData(nameof(EvilPatterns))]
        public void Non_matching_attack_inputs_finish_fast(string pattern)
        {
            var engine = new TwoLayerRegex(pattern);
            var attack = new string('a', AttackLength - 1) + "!";

            var sw = Stopwatch.StartNew();
            var result = engine.Match(attack);
            sw.Stop();

            Assert.False(result.Success);
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"Pattern {pattern} took {sw.ElapsedMilliseconds} ms on a {AttackLength}-char attack input.");
        }

        [Theory]
        [MemberData(nameof(EvilPatterns))]
        public void Matching_inputs_also_finish_fast(string pattern)
        {
            var engine = new TwoLayerRegex(pattern);
            // The attack shape that matters: a long run that ALMOST matches.
            // Patterns that require a final 'b' get one; the rest match the
            // pure run.
            var valid = pattern.Contains('b')
                ? new string('a', AttackLength - 1) + "b"
                : new string('a', AttackLength);

            var sw = Stopwatch.StartNew();
            var result = engine.Match(valid);
            sw.Stop();

            Assert.True(result.Success);
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"Pattern {pattern} took {sw.ElapsedMilliseconds} ms on a {AttackLength}-char matching input.");
        }

        [Fact]
        public void IsMatch_fast_path_is_linear_for_pure_regular_evil_patterns()
        {
            var engine = new TwoLayerRegex(@"^(a+)+$");
            var attack = new string('a', AttackLength - 1) + "!";

            var sw = Stopwatch.StartNew();
            bool isMatch = engine.IsMatch(attack);
            sw.Stop();

            Assert.False(isMatch);
            Assert.True(sw.ElapsedMilliseconds < 1000);
        }

        [Fact]
        public void Phase1_never_revisits_a_pair_so_work_is_bounded_by_states_times_positions()
        {
            var engine = new TwoLayerRegex(@"^(a+)+$");
            engine.Match(new string('a', AttackLength - 1) + "!");

            var trace = engine.LastPhase1Trace!;
            // m states × n positions is the hard ceiling; a '!' near the end
            // kills the a-runs, so the scan sees fewer — but always linear.
            Assert.True(trace.AliveNodes <= 40 * AttackLength,
                $"Alive nodes {trace.AliveNodes} exceed the linear ceiling.");
            Assert.Empty(trace.AcceptPositions);
        }
    }
}
