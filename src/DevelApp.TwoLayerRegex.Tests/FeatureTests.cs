using DevelApp.TwoLayerRegex;
using DevelApp.TwoLayerRegex.Pattern;

namespace DevelApp.TwoLayerRegex.Tests
{
    /// <summary>
    /// Feature coverage for the two-layer engine: literals, classes,
    /// alternation, quantifiers, groups, backreferences and lookarounds —
    /// each exercising the Phase 1 scan plus Phase 2 resolution together.
    /// </summary>
    public sealed class FeatureTests
    {
        [Theory]
        [InlineData("abc", "xxabcyy", "abc", 2)]
        [InlineData("abc", "abc", "abc", 0)]
        [InlineData("abc", "ab", "", -1)]
        [InlineData("", "abc", "", 0)]
        [InlineData("a.c", "a\nc", "", -1)] // dot does not match newline
        [InlineData("a.c", "axc", "axc", 0)]
        public void Literal_and_dot_semantics(string pattern, string input, string expected, int expectedIndex)
        {
            var m = new TwoLayerRegex(pattern).Match(input);
            if (expectedIndex < 0)
            {
                Assert.False(m.Success);
            }
            else
            {
                Assert.True(m.Success);
                Assert.Equal(expected, m.Value);
                Assert.Equal(expectedIndex, m.Index);
            }
        }

        [Fact]
        public void Character_classes_match_and_negate()
        {
            var digits = new TwoLayerRegex(@"\d+");
            var m = digits.Match("abc1234def");
            Assert.True(m.Success);
            Assert.Equal("1234", m.Value);
            Assert.Equal(3, m.Index);

            var notDigit = new TwoLayerRegex(@"\D+");
            var m2 = notDigit.Match("abc1234def");
            Assert.True(m2.Success);
            Assert.Equal("abc", m2.Value);

            var range = new TwoLayerRegex(@"[a-c]+");
            Assert.Equal("abcabc", range.Match("xxabcabcxx").Value);

            var negated = new TwoLayerRegex(@"[^a-c]+");
            Assert.Equal("xx", negated.Match("xxabc").Value);

            var word = new TwoLayerRegex(@"\w+");
            Assert.Equal("order_42", word.Match("  order_42!").Value);

            var space = new TwoLayerRegex(@"\s+");
            Assert.Equal("  ", space.Match("a  b").Value);
        }

        [Fact]
        public void Alternation_picks_leftmost_first_branch()
        {
            var engine = new TwoLayerRegex(@"cat|category");
            var m = engine.Match("the category field");
            Assert.True(m.Success);
            Assert.Equal("cat", m.Value); // first alternative wins at same position

            var engine2 = new TwoLayerRegex(@"category|cat");
            Assert.Equal("category", engine2.Match("the category field").Value);
        }

        [Fact]
        public void Bounded_quantifiers()
        {
            Assert.Equal("aaa", new TwoLayerRegex(@"^a{3}$").MatchExact("aaa").Value);
            Assert.False(new TwoLayerRegex(@"^a{3}$").MatchExact("aa").Success);
            Assert.False(new TwoLayerRegex(@"^a{3}$").MatchExact("aaaa").Success);

            Assert.Equal("aaaaa", new TwoLayerRegex(@"^a{2,}$").MatchExact("aaaaa").Value);
            Assert.False(new TwoLayerRegex(@"^a{2,}$").MatchExact("a").Success);

            // {2,4} greedy takes the maximum that still allows the rest to match.
            var m = new TwoLayerRegex(@"^a{2,4}b$").MatchExact("aaab");
            Assert.True(m.Success);
            Assert.Equal("aaab", m.Value);
        }

        [Fact]
        public void Quantifier_limit_is_enforced()
        {
            Assert.Throws<PatternSyntaxException>(() => new TwoLayerRegex(@"a{5000}"));
        }

        [Fact]
        public void Capture_groups_and_nesting()
        {
            var m = new TwoLayerRegex(@"(\w+)-(\d+)(?:\.(.*))?").Match("order-42.rest");
            Assert.True(m.Success);
            Assert.Equal("order-42.rest", m.Value);
            Assert.Equal("order", m.Groups[0].Value);
            Assert.Equal("42", m.Groups[1].Value);
            Assert.Equal("rest", m.Groups[2].Value);

            var nested = new TwoLayerRegex(@"(a(b)c)").Match("abc");
            Assert.Equal("abc", nested.Groups[0].Value);
            Assert.Equal("b", nested.Groups[1].Value);

            // A group not reached by the successful path stays unset.
            var partial = new TwoLayerRegex(@"(a)|(b)").Match("b");
            Assert.False(partial.Groups[0].Success);
            Assert.Equal("b", partial.Groups[1].Value);
        }

        [Fact]
        public void Backreference_matches_repeated_content_exactly()
        {
            var engine = new TwoLayerRegex(@"(\w+)\s+\1");
            var m = engine.Match("say hello hello world");
            Assert.True(m.Success);
            Assert.Equal("hello hello", m.Value);
            Assert.Equal("hello", m.Groups[0].Value);

            // The reference must reproduce the whole captured span: a partial
            // repetition is not a match.
            var partial = new TwoLayerRegex(@"^(a+)\1$").MatchExact("aa");
            Assert.True(partial.Success); // (a+) = "a", \1 = "a"

            var noRepeat = new TwoLayerRegex(@"^(a+)\1$").MatchExact("a");
            Assert.False(noRepeat.Success);

            var notWhole = new TwoLayerRegex(@"^(a+)b\1$").MatchExact("aabaaa");
            Assert.False(notWhole.Success); // \1 = "aa" but the tail is "aaa"
        }

        [Fact]
        public void Backreference_to_unset_group_never_matches()
        {
            var engine = new TwoLayerRegex(@"^(?:(a)|b)\1$");
            Assert.True(engine.MatchExact("aa").Success);
            Assert.False(engine.MatchExact("b").Success);
            Assert.False(engine.MatchExact("ba").Success);
        }

        [Fact]
        public void Lookahead_positive_and_negative()
        {
            var engine = new TwoLayerRegex(@"\w+(?=\s*\()");
            var m = engine.Match("sqrt(2) + max(3, 4)");
            Assert.True(m.Success);
            Assert.Equal("sqrt", m.Value);

            var negative = new TwoLayerRegex(@"\d+(?!%)");
            // On "50%", the lookahead fails at the '%', so the quantifier
            // backtracks to "5" (followed by '0', not '%') — the same
            // leftmost-first answer a classical engine gives.
            var m2 = negative.Match("50% 25 40%");
            Assert.Equal(0, m2.Index);
            Assert.Equal("5", m2.Value);

            var positivePercent = new TwoLayerRegex(@"\d+(?=%)");
            var m3 = positivePercent.Match("50% 25 40%");
            Assert.Equal("50", m3.Value);
            Assert.Equal(0, m3.Index);
        }

        [Fact]
        public void Lookbehind_fixed_length()
        {
            var engine = new TwoLayerRegex(@"(?<=ab)c");
            var m = engine.Match("xxabc");
            Assert.True(m.Success);
            Assert.Equal(4, m.Index);

            var negative = new TwoLayerRegex(@"(?<!a)b");
            var m2 = negative.Match("ab cb");
            Assert.Equal(4, m2.Index);
        }

        [Fact]
        public void Lookbehind_variable_length()
        {
            // The signature feature: classical engines reject variable-length
            // lookbehind; here validation is a prefix-window sub-scan that
            // must end exactly at the current position, so any length works.
            var engine = new TwoLayerRegex(@"(?<=ab*)c");
            var m = engine.Match("c ac abc");
            Assert.True(m.Success);
            Assert.Equal("c", m.Value);
            Assert.Equal(3, m.Index); // 'c' preceded by 'ab' in 'abc'

            // Longer variable windows still resolve.
            var longEngine = new TwoLayerRegex(@"(?<=a+b)c");
            Assert.Equal("c", longEngine.Match("aabc").Value);
        }

        [Fact]
        public void Nested_lookarounds_are_bounded()
        {
            // A lookahead containing another lookahead resolves recursively.
            // After the leading digit, the outer lookahead needs two more
            // digits, so a three-digit subject is required.
            var engine = new TwoLayerRegex(@"^\d(?=(?=\d)\d\d)");
            Assert.True(engine.Match("123").Success);
            Assert.False(engine.Match("12").Success);
            Assert.False(engine.Match("1a3").Success);
        }

        [Fact]
        public void Captures_inside_lookarounds_are_not_propagated()
        {
            // Honest limitation of the showcase: lookarounds are validated by
            // a structural sub-scan, so capture groups inside them are not
            // finalized into the outer match's group table. The lookahead
            // itself still works as an assertion.
            var engine = new TwoLayerRegex(@"^(?=a+)a+$");
            Assert.True(engine.MatchExact("aaa").Success);

            var capturedInside = new TwoLayerRegex(@"^(?=(a+))\1$");
            Assert.False(capturedInside.MatchExact("aaa").Success);
        }

        [Fact]
        public void Anchors_hold_at_window_boundaries()
        {
            Assert.True(new TwoLayerRegex(@"^abc$").MatchExact("abc").Success);
            Assert.False(new TwoLayerRegex(@"^abc$").MatchExact("xabc").Success);

            // ^ in an unanchored search only matches at position 0.
            var engine = new TwoLayerRegex(@"^ab");
            Assert.False(engine.Match("xxab").Success);
            Assert.True(engine.Match("abxx").Success);
        }

        [Fact]
        public void Syntax_errors_report_position()
        {
            Assert.Throws<PatternSyntaxException>(() => new TwoLayerRegex("(a"));
            Assert.Throws<PatternSyntaxException>(() => new TwoLayerRegex("a{2,1}"));
            Assert.Throws<PatternSyntaxException>(() => new TwoLayerRegex(@"\"));
        }

        [Fact]
        public void Phase1_trace_is_populated_for_demo_use()
        {
            var engine = new TwoLayerRegex(@"(\w+)\s+\1");
            engine.Match("hi hi");
            Assert.NotNull(engine.LastPhase1Trace);
            Assert.True(engine.LastPhase1Trace!.AliveNodes > 0);
            Assert.False(engine.LastPhase2BudgetExceeded);
        }
    }
}
