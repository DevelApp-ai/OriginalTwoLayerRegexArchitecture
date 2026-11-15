using DevelApp.TwoLayerRegex;

namespace DevelApp.TwoLayerRegex.Tests
{
    /// <summary>
    /// Search semantics: leftmost-first matching, greedy vs lazy preference,
    /// and the anchored <see cref="TwoLayerRegex.MatchExact"/> entry point.
    /// </summary>
    public sealed class SemanticsTests
    {
        [Fact]
        public void Leftmost_start_wins_over_longer_later_match()
        {
            var engine = new TwoLayerRegex(@"ab|abc");
            var m = engine.Match("xxabc");
            Assert.True(m.Success);
            Assert.Equal(2, m.Index);
            Assert.Equal("ab", m.Value); // leftmost, and the first alternative
        }

        [Fact]
        public void Greedy_quantifier_prefers_longest_first()
        {
            var greedy = new TwoLayerRegex(@"<a.+>");
            var m = greedy.Match("<ab><cd>");
            Assert.Equal("<ab><cd>", m.Value);
        }

        [Fact]
        public void Lazy_quantifier_prefers_shortest_first()
        {
            var lazy = new TwoLayerRegex(@"<a.+?>");
            var m = lazy.Match("<ab><cd>");
            Assert.True(m.Success);
            Assert.Equal("<ab>", m.Value); // exits the loop at the first '>'
        }

        [Fact]
        public void Lazy_star_matches_empty_when_possible()
        {
            var lazy = new TwoLayerRegex(@"^a*?b$");
            var m = new TwoLayerRegex(lazy.Pattern).MatchExact("aab");
            Assert.True(m.Success);
        }

        [Fact]
        public void Optional_quantifier_is_greedy_by_default()
        {
            var engine = new TwoLayerRegex(@"^a?ab$");
            var m = engine.MatchExact("aab");
            Assert.True(m.Success);
        }

        [Fact]
        public void MatchExact_requires_the_whole_input()
        {
            var engine = new TwoLayerRegex(@"\d+");
            Assert.Equal("123", engine.MatchExact("123").Value);
            Assert.False(engine.MatchExact("a123").Success);
            Assert.False(engine.MatchExact("123a").Success);
            Assert.False(engine.MatchExact("").Success);
        }

        [Fact]
        public void Empty_input_and_empty_pattern()
        {
            Assert.True(new TwoLayerRegex(@"").Match("").Success);
            Assert.True(new TwoLayerRegex(@"a*").Match("").Success);
            Assert.False(new TwoLayerRegex(@"a+").Match("").Success);
            Assert.True(new TwoLayerRegex(@"^$").MatchExact("").Success);
        }

        [Fact]
        public void Static_convenience_methods()
        {
            Assert.True(TwoLayerRegex.IsMatch(@"\d{3}", "ab123"));
            var m = TwoLayerRegex.Match(@"(\d)(\d)", "ab56");
            Assert.True(m.Success);
            Assert.Equal("56", m.Value);
            Assert.Equal("5", m.Groups[0].Value);
        }
    }
}
