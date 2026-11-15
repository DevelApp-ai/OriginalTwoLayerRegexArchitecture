namespace DevelApp.TwoLayerRegex.Pattern
{
    /// <summary>
    /// The abstract syntax tree of a parsed pattern. The tree is a faithful,
    /// structure-only representation: capture groups, lookarounds and
    /// backreferences are ordinary nodes here. Phase 1 compiles them into
    /// non-consuming markers on the NFA and Phase 2 gives them their
    /// semantics — the core decoupling of the two-layer architecture.
    /// </summary>
    public abstract record PatternNode
    {
        /// <summary>A single-character matcher (literal, class, escape, dot).</summary>
        public sealed record CharMatcher(CharPredicate Predicate) : PatternNode;

        /// <summary>A concatenation of parts, matched in order.</summary>
        public sealed record Sequence(IReadOnlyList<PatternNode> Parts) : PatternNode;

        /// <summary>An alternation; branches are preferred in source order.</summary>
        public sealed record Alternation(IReadOnlyList<PatternNode> Branches) : PatternNode;

        /// <summary>A quantifier over an inner node; Max = -1 means unbounded.</summary>
        public sealed record Quantifier(PatternNode Inner, int Min, int Max, bool Greedy) : PatternNode;

        /// <summary>A capturing group with its 1-based number.</summary>
        public sealed record CaptureGroup(int Number, PatternNode Inner) : PatternNode;

        /// <summary>A non-capturing group, used only for grouping.</summary>
        public sealed record NonCaptureGroup(PatternNode Inner) : PatternNode;

        /// <summary>A lookaround assertion; validated in Phase 2, not compiled into matching logic.</summary>
        public sealed record Lookaround(PatternNode Inner, LookaroundKind Kind) : PatternNode;

        /// <summary>A backreference to a capture group by its 1-based number; content is verified in Phase 2.</summary>
        public sealed record Backreference(int Number) : PatternNode;

        /// <summary>The start-of-input anchor (^), valid only at position 0.</summary>
        public sealed record StartAnchor : PatternNode;

        /// <summary>The end-of-input anchor ($), valid only at the end of the scan window.</summary>
        public sealed record EndAnchor : PatternNode;
    }

    /// <summary>The four lookaround kinds.</summary>
    public enum LookaroundKind
    {
        /// <summary>Positive lookahead (?=...).</summary>
        PositiveAhead,

        /// <summary>Negative lookahead (?!...).</summary>
        NegativeAhead,

        /// <summary>Positive lookbehind (?&lt;=...), including variable length.</summary>
        PositiveBehind,

        /// <summary>Negative lookbehind (?&lt;!...), including variable length.</summary>
        NegativeBehind,
    }

    /// <summary>
    /// A single-character predicate. Holds no parse state — used both in the
    /// AST and on the compiled NFA character transitions.
    /// </summary>
    public readonly struct CharPredicate
    {
        private readonly Func<char, bool> _predicate;

        private CharPredicate(Func<char, bool> predicate, string description)
        {
            _predicate = predicate;
            Description = description;
        }

        /// <summary>A human-readable description, used by the demo trace.</summary>
        public string Description { get; }

        /// <summary>Tests a character against this predicate.</summary>
        public bool Matches(char c) => _predicate?.Invoke(c) ?? false;

        /// <summary>Creates a predicate matching exactly one character.</summary>
        public static CharPredicate Literal(char c) => new(_ => _ == c, $"'{Escape(c)}'");

        /// <summary>Creates a predicate matching any character except newline (the dot).</summary>
        public static CharPredicate Dot() => new(c => c != '\n', ".");

        /// <summary>Creates a predicate matching a decimal digit (\d).</summary>
        public static CharPredicate Digit() => new(char.IsDigit, "\\d");

        /// <summary>Creates a predicate matching a word character (\w).</summary>
        public static CharPredicate Word() => new(c => char.IsLetterOrDigit(c) || c == '_', "\\w");

        /// <summary>Creates a predicate matching whitespace (\s).</summary>
        public static CharPredicate Whitespace() => new(char.IsWhiteSpace, "\\s");

        /// <summary>Creates a predicate from a membership test with a description.</summary>
        public static CharPredicate Custom(Func<char, bool> predicate, string description)
            => new(predicate, description);

        /// <summary>Creates a predicate matching none of the members of another predicate (negated class).</summary>
        public CharPredicate Negated()
        {
            // Copy members to locals: lambdas in structs cannot capture 'this'.
            var predicate = _predicate;
            var inner = Description.Trim('[', ']');
            return new(c => !(predicate?.Invoke(c) ?? false), "[^" + inner + "]");
        }

        private static string Escape(char c) => c switch
        {
            '\n' => "\\n",
            '\t' => "\\t",
            '\r' => "\\r",
            _ => c.ToString(),
        };
    }
}
