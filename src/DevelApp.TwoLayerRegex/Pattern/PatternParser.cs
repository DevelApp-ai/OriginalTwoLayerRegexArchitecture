using System.Text;

namespace DevelApp.TwoLayerRegex.Pattern
{
    /// <summary>
    /// Recursive-descent parser from pattern text to a <see cref="PatternNode"/> tree.
    /// Supports the showcase subset of PCRE2 syntax: literals, escapes
    /// (\d \D \w \W \s \S \n \t \r and escaped metacharacters), dot,
    /// character classes [a-z0-9_] and [^...], anchors ^ $, alternation |,
    /// greedy and lazy quantifiers (* + ? {n} {n,} {n,m} and their ?
    /// variants), capturing and non-capturing groups, lookarounds
    /// (?=...) (?!...) (?&lt;=...) (?&lt;!...) and backreferences \1..\9.
    /// </summary>
    public sealed class PatternParser
    {
        private readonly string _pattern;
        private int _pos;
        private int _groupCount;

        private PatternParser(string pattern)
        {
            _pattern = pattern;
        }

        /// <summary>Parses a pattern into its AST, numbering capture groups in source order.</summary>
        public static PatternNode Parse(string pattern)
        {
            if (pattern is null)
            {
                throw new ArgumentNullException(nameof(pattern));
            }

            var parser = new PatternParser(pattern);
            var ast = parser.ParseAlternation();
            if (parser._pos != pattern.Length)
            {
                throw new PatternSyntaxException(
                    $"Unexpected '{pattern[parser._pos]}' at index {parser._pos}.", pattern, parser._pos);
            }

            return ast;
        }

        private PatternNode ParseAlternation()
        {
            var branches = new List<PatternNode> { ParseSequence() };
            while (Peek() == '|')
            {
                _pos++;
                branches.Add(ParseSequence());
            }

            return branches.Count == 1 ? branches[0] : new PatternNode.Alternation(branches);
        }

        private PatternNode ParseSequence()
        {
            var parts = new List<PatternNode>();
            while (_pos < _pattern.Length && Peek() != '|' && Peek() != ')')
            {
                parts.Add(ParseQuantified());
            }

            return parts.Count == 0 ? new PatternNode.Sequence(Array.Empty<PatternNode>()) : new PatternNode.Sequence(parts);
        }

        private PatternNode ParseQuantified()
        {
            var atom = ParseAtom();
            while (_pos < _pattern.Length)
            {
                int min;
                int max;
                switch (Peek())
                {
                    case '*':
                        min = 0;
                        max = -1;
                        _pos++;
                        break;
                    case '+':
                        min = 1;
                        max = -1;
                        _pos++;
                        break;
                    case '?':
                        min = 0;
                        max = 1;
                        _pos++;
                        break;
                    case '{':
                        var (lo, hi) = TryParseBoundedQuantifier();
                        if (lo is null)
                        {
                            return atom; // A literal '{' was meant; stop quantifying.
                        }

                        min = lo.Value;
                        max = hi;
                        break;
                    default:
                        return atom;
                }

                bool greedy = true;
                if (Peek() == '?')
                {
                    greedy = false;
                    _pos++;
                }

                if (atom is PatternNode.Quantifier or PatternNode.StartAnchor or PatternNode.EndAnchor)
                {
                    // Allowed but pointless for anchors; harmless for stacked quantifiers.
                }

                atom = new PatternNode.Quantifier(atom, min, max, greedy);
            }

            return atom;
        }

        private (int? min, int max) TryParseBoundedQuantifier()
        {
            int save = _pos;
            _pos++; // Consume '{'.
            var minText = ReadDigits();
            int min;
            if (minText.Length == 0 || !int.TryParse(minText, out min))
            {
                _pos = save;
                return (null, 0);
            }

            int max;
            if (Peek() == ',')
            {
                _pos++;
                var maxText = ReadDigits();
                max = maxText.Length == 0 ? -1 : int.Parse(maxText);
            }
            else
            {
                max = min;
            }

            if (Peek() != '}')
            {
                _pos = save;
                return (null, 0);
            }

            _pos++;
            if (max != -1 && max < min)
            {
                throw new PatternSyntaxException($"Quantifier bounds inverted ({{{min},{max}}}).", _pattern, _pos);
            }

            return (min, max);
        }

        private string ReadDigits()
        {
            var sb = new StringBuilder();
            while (Peek() is >= '0' and <= '9')
            {
                sb.Append(_pattern[_pos++]);
            }

            return sb.ToString();
        }

        private PatternNode ParseAtom()
        {
            char c = _pattern[_pos];
            switch (c)
            {
                case '(':
                    return ParseGroup();
                case '[':
                    return new PatternNode.CharMatcher(ParseCharClass());
                case '.':
                    _pos++;
                    return new PatternNode.CharMatcher(CharPredicate.Dot());
                case '^':
                    _pos++;
                    return new PatternNode.StartAnchor();
                case '$':
                    _pos++;
                    return new PatternNode.EndAnchor();
                case '\\':
                    return ParseEscape();
                case '*' or '+' or '?':
                    throw new PatternSyntaxException($"Quantifier '{c}' has nothing to repeat.", _pattern, _pos);
                default:
                    _pos++;
                    return new PatternNode.CharMatcher(CharPredicate.Literal(c));
            }
        }

        private PatternNode ParseGroup()
        {
            _pos++; // Consume '('.
            if (Peek() == '?')
            {
                _pos++;
                switch (Peek())
                {
                    case ':':
                        _pos++;
                        var inner = ParseAlternation();
                        Expect(')');
                        return new PatternNode.NonCaptureGroup(inner);
                    case '=':
                        _pos++;
                        return FinishLookaround(LookaroundKind.PositiveAhead);
                    case '!':
                        _pos++;
                        return FinishLookaround(LookaroundKind.NegativeAhead);
                    case '<':
                        _pos++;
                        if (Peek() == '=')
                        {
                            _pos++;
                            return FinishLookaround(LookaroundKind.PositiveBehind);
                        }

                        if (Peek() == '!')
                        {
                            _pos++;
                            return FinishLookaround(LookaroundKind.NegativeBehind);
                        }

                        throw new PatternSyntaxException("Expected '=' or '!' after '(?<'.", _pattern, _pos);
                    default:
                        throw new PatternSyntaxException(
                            $"Unsupported group modifier '(?{Peek()}'.", _pattern, _pos);
                }
            }

            // Capturing group: number groups in source order, like PCRE2.
            int number = ++_groupCount;
            var body = ParseAlternation();
            Expect(')');
            return new PatternNode.CaptureGroup(number, body);
        }

        private PatternNode FinishLookaround(LookaroundKind kind)
        {
            var body = ParseAlternation();
            Expect(')');
            return new PatternNode.Lookaround(body, kind);
        }

        private CharPredicate ParseCharClass()
        {
            _pos++; // Consume '['.
            bool negated = false;
            if (Peek() == '^')
            {
                negated = true;
                _pos++;
            }

            var members = new HashSet<char>();
            var ranges = new List<(char Lo, char Hi)>();
            var first = true;
            while (_pos < _pattern.Length && (Peek() != ']' || first))
            {
                first = false;
                if (Peek() == '\\' && _pos + 1 < _pattern.Length)
                {
                    // Class escapes: consume the escape as a predicate and union it.
                    var sub = ParseEscape();
                    if (sub is PatternNode.CharMatcher matcher)
                    {
                        // Approximate a class escape by sampling its description ranges.
                        UnionMatcher(matcher.Predicate, members, ranges);
                        continue;
                    }

                    throw new PatternSyntaxException("Unsupported escape inside character class.", _pattern, _pos);
                }

                char lo = _pattern[_pos++];
                if (Peek() == '-' && _pos + 1 < _pattern.Length && _pattern[_pos + 1] != ']')
                {
                    _pos++; // Consume '-'.
                    char hi = _pattern[_pos++];
                    if (hi < lo)
                    {
                        throw new PatternSyntaxException($"Range [{lo}-{hi}] is inverted.", _pattern, _pos);
                    }

                    ranges.Add((lo, hi));
                }
                else
                {
                    members.Add(lo);
                }
            }

            Expect(']');
            var predicate = CharPredicate.Custom(
                c => members.Contains(c) || ranges.Any(r => c >= r.Lo && c <= r.Hi),
                DescribeClass(members, ranges, negated));
            return negated ? predicate.Negated() : predicate;
        }

        private static void UnionMatcher(CharPredicate matcher, HashSet<char> members, List<(char Lo, char Hi)> ranges)
        {
            // Class escapes (\d, \w, \s) are small and well-known; sample the
            // printable range once and store hits as members/ranges.
            for (char c = '\0'; c < (char)0x80; c++)
            {
                if (matcher.Matches(c))
                {
                    members.Add(c);
                }
            }
        }

        private static string DescribeClass(HashSet<char> members, List<(char Lo, char Hi)> ranges, bool negated)
        {
            var sb = new StringBuilder("[");
            if (negated)
            {
                sb.Append('^');
            }

            foreach (var (lo, hi) in ranges)
            {
                sb.Append(lo).Append('-').Append(hi);
            }

            foreach (var c in members.Where(c => !ranges.Any(r => c >= r.Lo && c <= r.Hi)).OrderBy(c => c))
            {
                sb.Append(c);
            }

            sb.Append(']');
            return sb.ToString();
        }

        private PatternNode ParseEscape()
        {
            _pos++; // Consume '\'.
            if (_pos >= _pattern.Length)
            {
                throw new PatternSyntaxException("Trailing backslash.", _pattern, _pos - 1);
            }

            char c = _pattern[_pos++];
            switch (c)
            {
                case 'd':
                    return new PatternNode.CharMatcher(CharPredicate.Digit());
                case 'D':
                    return new PatternNode.CharMatcher(CharPredicate.Digit().Negated());
                case 'w':
                    return new PatternNode.CharMatcher(CharPredicate.Word());
                case 'W':
                    return new PatternNode.CharMatcher(CharPredicate.Word().Negated());
                case 's':
                    return new PatternNode.CharMatcher(CharPredicate.Whitespace());
                case 'S':
                    return new PatternNode.CharMatcher(CharPredicate.Whitespace().Negated());
                case 'n':
                    return new PatternNode.CharMatcher(CharPredicate.Literal('\n'));
                case 't':
                    return new PatternNode.CharMatcher(CharPredicate.Literal('\t'));
                case 'r':
                    return new PatternNode.CharMatcher(CharPredicate.Literal('\r'));
                case >= '1' and <= '9':
                    return new PatternNode.Backreference(c - '0');
                default:
                    return new PatternNode.CharMatcher(CharPredicate.Literal(c));
            }
        }

        private char Peek() => _pos < _pattern.Length ? _pattern[_pos] : '\0';

        private void Expect(char c)
        {
            if (Peek() != c)
            {
                throw new PatternSyntaxException($"Expected '{c}' at index {_pos}.", _pattern, _pos);
            }

            _pos++;
        }
    }

    /// <summary>Thrown when a pattern is not valid under the supported subset.</summary>
    public sealed class PatternSyntaxException : Exception
    {
        /// <summary>Creates a syntax exception pointing at the offending position.</summary>
        public PatternSyntaxException(string message, string pattern, int position)
            : base($"{message} (pattern: \"{pattern}\", index: {position})")
        {
            Pattern = pattern;
            Position = position;
        }

        /// <summary>The pattern that failed to parse.</summary>
        public string Pattern { get; }

        /// <summary>The zero-based position in the pattern where parsing failed.</summary>
        public int Position { get; }
    }
}
