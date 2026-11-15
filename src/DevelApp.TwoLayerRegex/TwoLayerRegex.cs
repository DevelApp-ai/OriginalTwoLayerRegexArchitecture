using DevelApp.TwoLayerRegex.Compilation;
using DevelApp.TwoLayerRegex.Pattern;
using DevelApp.TwoLayerRegex.Phase1;
using DevelApp.TwoLayerRegex.Phase2;

namespace DevelApp.TwoLayerRegex
{
    /// <summary>
    /// The two-layer regex engine. Layer 1 (<see cref="PathfindingScan"/>)
    /// finds every structurally possible match in guaranteed linear time;
    /// layer 2 (<see cref="GroupingResolver"/>) finalizes capture groups and
    /// validates backreferences and lookarounds as bounded post-processing
    /// over the Phase 1 records. This is the architecture described in
    /// "A Two-Phase Architecture for Secure and Expressive Regular
    /// Expression Matching", recreated as a runnable showcase of the way
    /// the predecessor of the ENFA StepLexer/StepParser line worked.
    /// </summary>
    public sealed class TwoLayerRegex
    {
        private readonly Nfa _nfa;
        private readonly PatternNode _ast;
        private readonly List<CharPredicate> _firstChars;
        private readonly bool _emptyMatchPossible;
        private readonly bool _leadingStartAnchor;

        /// <summary>Compiles a pattern.</summary>
        public TwoLayerRegex(string pattern)
        {
            if (pattern is null)
            {
                throw new ArgumentNullException(nameof(pattern));
            }

            Pattern = pattern;
            _ast = PatternParser.Parse(pattern);
            _nfa = NfaCompiler.Compile(_ast);
            (_firstChars, _emptyMatchPossible) = ComputeFirstCharCandidates();
            _leadingStartAnchor = _ast is PatternNode.Sequence { Parts: { Count: > 0 } } seq
                && seq.Parts[0] is PatternNode.StartAnchor;
        }

        /// <summary>The pattern this engine was compiled from.</summary>
        public string Pattern { get; }

        /// <summary>The Phase 1 trace of the most recent scan (for the demo).</summary>
        public ScanTrace? LastPhase1Trace { get; private set; }

        /// <summary>Whether the most recent Phase 2 walk exhausted its step budget.</summary>
        public bool LastPhase2BudgetExceeded { get; private set; }

        /// <summary>Convenience: match a pattern against an input, leftmost-first.</summary>
        public static MatchResult Match(string pattern, string input)
            => new TwoLayerRegex(pattern).Match(input);

        /// <summary>Convenience: test whether a pattern matches anywhere in the input.</summary>
        public static bool IsMatch(string pattern, string input)
            => new TwoLayerRegex(pattern).IsMatch(input);

        /// <summary>
        /// Searches for the leftmost match. Start positions are tried in
        /// order; for each, Phase 1 scans structurally and Phase 2 resolves
        /// the first record that satisfies every semantic constraint. When
        /// Phase 1 alone can prove no match from a position, Phase 2 never
        /// runs for it — the linear scan is the security boundary.
        /// </summary>
        public MatchResult Match(string input)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            LastPhase1Trace = null;
            LastPhase2BudgetExceeded = false;

            foreach (int start in CandidateStarts(input))
            {
                var scan = new PathfindingScan().Scan(_nfa, input, 0, input.Length, new[] { start });
                LastPhase1Trace = scan.Trace;
                if (!scan.AcceptAlive)
                {
                    continue;
                }

                var record = GroupingResolver.ResolveFirst(this, _nfa, input, scan, start, out bool budget);
                LastPhase2BudgetExceeded = budget;
                if (record is null)
                {
                    continue;
                }

                return BuildResult(input, record);
            }

            return MatchResult.Failed;
        }

        /// <summary>
        /// Tests whether the pattern matches anywhere in the input. For
        /// patterns without backreferences or lookarounds, structure and
        /// semantics coincide, so a single linear multi-start Phase 1 scan
        /// answers the question outright — the pure O(m·n) guarantee.
        /// </summary>
        public bool IsMatch(string input)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (!GroupingResolver.HasSemanticFeatures(_nfa))
            {
                var starts = Enumerable.Range(0, input.Length + 1).ToArray();
                var scan = new PathfindingScan().Scan(_nfa, input, 0, input.Length, starts);
                LastPhase1Trace = scan.Trace;
                return scan.AcceptAlive;
            }

            return Match(input).Success;
        }

        /// <summary>Requires the whole input to be the match (like ^pattern$).</summary>
        public MatchResult MatchExact(string input)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            LastPhase1Trace = null;
            LastPhase2BudgetExceeded = false;
            var scan = new PathfindingScan().Scan(_nfa, input, 0, input.Length, new[] { 0 }, requireAcceptAt: input.Length);
            LastPhase1Trace = scan.Trace;
            if (!scan.AcceptAlive)
            {
                return MatchResult.Failed;
            }

            var record = GroupingResolver.ResolveFirst(this, _nfa, input, scan, 0, out bool budget, acceptAt: input.Length);
            LastPhase2BudgetExceeded = budget;
            return record is null ? MatchResult.Failed : BuildResult(input, record);
        }

        private MatchResult BuildResult(string input, PotentialMatchRecord record)
        {
            var groups = new List<GroupResult>(_nfa.GroupCount);
            for (int g = 0; g < _nfa.GroupCount; g++)
            {
                int start = record.GroupStarts[g];
                int end = record.GroupEnds[g];
                groups.Add(start >= 0 && end >= start
                    ? new GroupResult(g + 1, start, end - start, input.Substring(start, end - start))
                    : new GroupResult(g + 1, -1, 0, string.Empty));
            }

            return new MatchResult(
                true,
                record.Start,
                record.End - record.Start,
                input.Substring(record.Start, record.End - record.Start),
                groups);
        }

        private IEnumerable<int> CandidateStarts(string input)
        {
            if (_leadingStartAnchor)
            {
                yield return 0;
                yield break;
            }

            for (int s = 0; s <= input.Length; s++)
            {
                if (_emptyMatchPossible || s == input.Length)
                {
                    yield return s;
                }
                else if (_firstChars.Count > 0 && _firstChars.Any(p => p.Matches(input[s])))
                {
                    yield return s;
                }
            }
        }

        private (List<CharPredicate> First, bool EmptyPossible) ComputeFirstCharCandidates()
        {
            var first = new List<CharPredicate>();
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(_nfa.Start);
            while (queue.Count > 0)
            {
                int state = queue.Dequeue();
                if (!seen.Add(state))
                {
                    continue;
                }

                foreach (var t in _nfa.States[state].Transitions)
                {
                    switch (t.Kind)
                    {
                        case TransitionKind.Char:
                            // Consuming edges determine possible first characters.
                            // A backreference loop consumes anything, and its
                            // predicate already says so.
                            first.Add(t.CharPredicate);
                            break;
                        case TransitionKind.Epsilon when t.Marker is null or Marker.GroupStart or Marker.GroupEnd:
                            queue.Enqueue(t.Target);
                            break;
                        // Assertions and anchors are zero-width: keep walking
                        // through them for first-character purposes.
                        case TransitionKind.Epsilon when t.Marker is Marker.Assertion:
                        case TransitionKind.AnchorStart or TransitionKind.AnchorEnd:
                            queue.Enqueue(t.Target);
                            break;
                    }
                }
            }

            return (first, IsEmptyMatchPossible());
        }

        private bool IsEmptyMatchPossible()
        {
            // Empty match possible iff the accept state is reachable from the
            // start through zero-width edges only (anchors included — they
            // hold at some position, and the candidate loop tries all).
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(_nfa.Start);
            while (queue.Count > 0)
            {
                int state = queue.Dequeue();
                if (!seen.Add(state))
                {
                    continue;
                }

                if (state == _nfa.Accept)
                {
                    return true;
                }

                foreach (var t in _nfa.States[state].Transitions)
                {
                    if (t.Kind is TransitionKind.Epsilon or TransitionKind.AnchorStart or TransitionKind.AnchorEnd)
                    {
                        queue.Enqueue(t.Target);
                    }
                }
            }

            return false;
        }
    }
}
