using System.Linq;
using DevelApp.TwoLayerRegex.Compilation;
using DevelApp.TwoLayerRegex.Pattern;
using DevelApp.TwoLayerRegex.Phase1;

namespace DevelApp.TwoLayerRegex.Phase2
{
    /// <summary>
    /// One potential match record, finalized by Phase 2: the overall span
    /// plus the resolved spans of every capture group.
    /// </summary>
    public sealed class PotentialMatchRecord
    {
        /// <summary>Creates a record.</summary>
        public PotentialMatchRecord(int start, int end, int[] groupStarts, int[] groupEnds)
        {
            Start = start;
            End = end;
            GroupStarts = groupStarts;
            GroupEnds = groupEnds;
        }

        /// <summary>The input position where the match starts.</summary>
        public int Start { get; }

        /// <summary>The input position where the match ends (exclusive).</summary>
        public int End { get; }

        /// <summary>Resolved start spans per capture group index (0-based slots, -1 = unset).</summary>
        public int[] GroupStarts { get; }

        /// <summary>Resolved end spans per capture group index (0-based slots, -1 = unset).</summary>
        public int[] GroupEnds { get; }
    }

    /// <summary>
    /// Phase 2: Grouping and Content Resolution. Walks the Phase 1
    /// aliveness DAG in backtracking-preference order — but never
    /// re-scans the input and never steps onto a node Phase 1 proved
    /// unreachable — finalizing capture groups from the recorded markers,
    /// validating lookarounds with bounded sub-scans of the same engine,
    /// and verifying backreferences by direct string comparison. Failures
    /// are memoized per (state, position, capture context) and the whole
    /// walk is budget-guarded, so this phase is bounded polynomial work
    /// over the DAG, not an open-ended search over the input.
    /// </summary>
    public sealed class GroupingResolver
    {
        private const long DefaultStepBudget = 2_000_000;
        private const int MaxLookaroundDepth = 8;

        private readonly TwoLayerRegex _engine;
        private readonly Nfa _nfa;
        private readonly string _input;
        private readonly ScanResult _scan;
        private readonly int[] _groupStarts;
        private readonly int[] _groupEnds;
        private readonly HashSet<string> _failed = new();
        private readonly int _acceptAt;
        private readonly int _lookaroundDepth;
        private readonly int _matchStart;

        private long _budget = DefaultStepBudget;
        private readonly HashSet<long> _inProgress = new();

        private GroupingResolver(
            TwoLayerRegex engine,
            Nfa nfa,
            string input,
            ScanResult scan,
            int acceptAt,
            int lookaroundDepth,
            int matchStart)
        {
            _engine = engine;
            _nfa = nfa;
            _input = input;
            _scan = scan;
            _groupStarts = new int[nfa.GroupCount];
            _groupEnds = new int[nfa.GroupCount];
            _acceptAt = acceptAt;
            _lookaroundDepth = lookaroundDepth;
            _matchStart = matchStart;
        }

        /// <summary>Whether the step budget was exhausted (bounded, never a hang).</summary>
        public bool BudgetExceeded { get; private set; }

        /// <summary>
        /// Resolves the first valid record for the scan, starting at the scan's
        /// seed position, in backtracking-preference order.
        /// </summary>
        /// <param name="engine">The owning engine, used for bounded lookaround sub-matches.</param>
        /// <param name="nfa">The compiled pattern.</param>
        /// <param name="input">The subject text.</param>
        /// <param name="scan">The Phase 1 result to walk.</param>
        /// <param name="start">The position the potential match must begin at.</param>
        /// <param name="acceptAt">If not -1, the record must end exactly here (lookbehind windows).</param>
        /// <param name="lookaroundDepth">The current lookaround recursion depth.</param>
        /// <returns>The finalized record, or null when every candidate fails.</returns>
        public static PotentialMatchRecord? ResolveFirst(
            TwoLayerRegex engine,
            Nfa nfa,
            string input,
            ScanResult scan,
            int start,
            int acceptAt = -1,
            int lookaroundDepth = 0)
            => ResolveFirst(engine, nfa, input, scan, start, out _, acceptAt, lookaroundDepth);

        /// <summary>
        /// Resolves the first valid record for the scan, reporting whether the
        /// step budget was exhausted (the caller can surface this instead of a hang).
        /// </summary>
        public static PotentialMatchRecord? ResolveFirst(
            TwoLayerRegex engine,
            Nfa nfa,
            string input,
            ScanResult scan,
            int start,
            out bool budgetExceeded,
            int acceptAt = -1,
            int lookaroundDepth = 0)
        {
            var resolver = new GroupingResolver(engine, nfa, input, scan, acceptAt, lookaroundDepth, start);
            for (int g = 0; g < nfa.GroupCount; g++)
            {
                resolver._groupStarts[g] = -1;
                resolver._groupEnds[g] = -1;
            }

            var record = resolver.Search(nfa.Start, start);
            budgetExceeded = resolver.BudgetExceeded;
            return record;
        }

        /// <summary>Whether the pattern carries semantic (non-regular) features Phase 2 must validate.</summary>
        public static bool HasSemanticFeatures(Nfa nfa)
        {
            foreach (var state in nfa.States)
            {
                foreach (var t in state.Transitions)
                {
                    if (t.BackrefGroup >= 0 || t.Marker is Marker.Assertion)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private PotentialMatchRecord? Search(int startState, int startPos)
        {
            // Iterative backtracking walk over the Phase 1 DAG, in
            // backtracking-preference edge order. Frames live on the heap, so
            // a long match can never overflow the call stack; the step budget
            // bounds total work instead.
            var stack = new Stack<Frame>();
            if (!TryPush(stack, startState, startPos))
            {
                return null;
            }

            while (stack.Count > 0)
            {
                if (--_budget < 0)
                {
                    BudgetExceeded = true;
                    return null;
                }

                var frame = stack.Peek();

                if (frame.Edges is null)
                {
                    // Node entry (once): prune, accept, memo-check, classify.
                    if (!_scan.IsAlive(frame.State, frame.Pos))
                    {
                        PopFrame(stack);
                        continue;
                    }

                    if (frame.State == _nfa.Accept && (_acceptAt == -1 || frame.Pos == _acceptAt))
                    {
                        return new PotentialMatchRecord(
                            _matchStart, frame.Pos, (int[])_groupStarts.Clone(), (int[])_groupEnds.Clone());
                    }

                    frame.EntryKey = $"{frame.State}:{frame.Pos}:{CapsKey()}";
                    if (_failed.Contains(frame.EntryKey))
                    {
                        PopFrame(stack);
                        continue;
                    }

                    var transitions = _nfa.States[frame.State].Transitions;

                    // A backreference loop node is deterministic: consume
                    // exactly the captured content of its group (a direct
                    // string comparison) or fail — no other path exists.
                    var backrefEdge = transitions.FirstOrDefault(t => t.BackrefGroup >= 0);
                    if (backrefEdge is not null)
                    {
                        frame.BackrefExit = transitions.First(t => t.Kind == TransitionKind.Epsilon).Target;
                        frame.Edges = new List<NfaTransition> { backrefEdge };
                    }
                    else
                    {
                        frame.Edges = transitions;
                    }
                }

                if (frame.Index >= frame.Edges.Count)
                {
                    _failed.Add(frame.EntryKey);
                    PopFrame(stack);
                    continue;
                }

                // Recursion semantics: a failed child leaves captures as
                // they were at this frame's entry, so restore before each try.
                RestoreCaptures((frame.EntryStarts, frame.EntryEnds));
                var t = frame.Edges[frame.Index++];

                if (frame.BackrefExit >= 0)
                {
                    var (ok, nextPos) = TryConsumeBackreference(t.BackrefGroup, frame.Pos);
                    if (ok)
                    {
                        TryPush(stack, frame.BackrefExit, nextPos);
                    }

                    continue;
                }

                switch (t.Kind)
                {
                    case TransitionKind.Epsilon when t.Marker is null:
                        TryPush(stack, t.Target, frame.Pos);
                        break;
                    case TransitionKind.Epsilon:
                        if (ApplyMarker(t.Marker, frame.Pos))
                        {
                            TryPush(stack, t.Target, frame.Pos);
                        }

                        break;
                    case TransitionKind.AnchorStart:
                        if (frame.Pos == _scan.WindowStart)
                        {
                            TryPush(stack, t.Target, frame.Pos);
                        }

                        break;
                    case TransitionKind.AnchorEnd:
                        if (frame.Pos == _scan.WindowEnd)
                        {
                            TryPush(stack, t.Target, frame.Pos);
                        }

                        break;
                    case TransitionKind.Char:
                        if (frame.Pos < _scan.WindowEnd && t.CharPredicate.Matches(_input[frame.Pos]))
                        {
                            TryPush(stack, t.Target, frame.Pos + 1);
                        }

                        break;
                }
            }

            return null;
        }

        /// <summary>
        /// Pushes a (state, position) node unless an identical node is already
        /// in progress further down the stack. Reaching the same pair through
        /// zero-width edges is a no-progress cycle (an empty-loop iteration
        /// such as (a*)*): it can never produce anything the shorter path has
        /// not, so the edge simply fails.
        /// </summary>
        private bool TryPush(Stack<Frame> stack, int state, int pos)
        {
            long key = ((long)state << 32) | (uint)pos;
            if (!_inProgress.Add(key))
            {
                return false;
            }

            stack.Push(new Frame(state, pos, SaveCaptures()));
            return true;
        }

        private void PopFrame(Stack<Frame> stack)
        {
            var frame = stack.Pop();
            _inProgress.Remove(((long)frame.State << 32) | (uint)frame.Pos);
        }

        /// <summary>One (state, position) node of the iterative search stack.</summary>
        private sealed class Frame
        {
            public Frame(int state, int pos, (int[] Starts, int[] Ends) captures)
            {
                State = state;
                Pos = pos;
                EntryStarts = captures.Starts;
                EntryEnds = captures.Ends;
            }

            public int State { get; }

            public int Pos { get; }

            /// <summary>Capture snapshot at frame entry, restored before each edge try.</summary>
            public int[] EntryStarts { get; }

            public int[] EntryEnds { get; }

            /// <summary>Failure-memo key computed at entry.</summary>
            public string EntryKey { get; set; } = string.Empty;

            /// <summary>The node's outgoing decisions, in preference order (null until entry processing).</summary>
            public IReadOnlyList<NfaTransition>? Edges { get; set; }

            /// <summary>Next decision to try.</summary>
            public int Index { get; set; }

            /// <summary>Exit target when this is a backreference loop node, else -1.</summary>
            public int BackrefExit { get; set; } = -1;
        }

        private (bool Ok, int NextPos) TryConsumeBackreference(int group, int pos)
        {
            // Group numbers are 1-based; the capture slots are 0-based.
            int slot = group - 1;
            if (slot >= _groupStarts.Length || _groupStarts[slot] < 0 || _groupEnds[slot] < 0)
            {
                return (false, pos); // Reference to an unset group never matches.
            }

            int len = _groupEnds[slot] - _groupStarts[slot];
            if (pos + len > _scan.WindowEnd)
            {
                return (false, pos);
            }

            bool equal = string.CompareOrdinal(_input, pos, _input, _groupStarts[slot], len) == 0;
            return equal ? (true, pos + len) : (false, pos);
        }

        private bool ApplyMarker(Marker marker, int pos)
        {
            switch (marker)
            {
                case Marker.GroupStart(int group):
                    _groupStarts[group - 1] = pos;
                    _groupEnds[group - 1] = -1;
                    return true;
                case Marker.GroupEnd(int group):
                    if (_groupStarts[group - 1] < 0)
                    {
                        return false;
                    }

                    _groupEnds[group - 1] = pos;
                    return true;
                case Marker.Assertion(Nfa subNfa, LookaroundKind kind):
                    return ValidateAssertion(subNfa, kind, pos);
                default:
                    return false;
            }
        }

        private bool ValidateAssertion(Nfa subNfa, LookaroundKind kind, int pos)
        {
            if (_lookaroundDepth >= MaxLookaroundDepth)
            {
                return false; // Bounded: nested assertions cannot recurse unboundedly.
            }

            return kind switch
            {
                LookaroundKind.PositiveAhead => MatchAhead(subNfa, pos, positive: true),
                LookaroundKind.NegativeAhead => MatchAhead(subNfa, pos, positive: false),
                LookaroundKind.PositiveBehind => MatchBehind(subNfa, pos, positive: true),
                LookaroundKind.NegativeBehind => MatchBehind(subNfa, pos, positive: false),
                _ => false,
            };
        }

        private bool MatchAhead(Nfa subNfa, int pos, bool positive)
        {
            // A lookahead is a bounded sub-match anchored at pos; the scan
            // window is the remainder of the subject.
            var scan = new PathfindingScan().Scan(subNfa, _input, pos, _scan.WindowEnd, new[] { pos });
            bool matched;
            if (!HasSemanticFeatures(subNfa))
            {
                matched = scan.AcceptAlive;
            }
            else
            {
                var record = scan.AcceptAlive
                    ? ResolveFirst(_engine, subNfa, _input, scan, pos, lookaroundDepth: _lookaroundDepth + 1)
                    : null;
                matched = record is not null;
            }

            return matched == positive;
        }

        private bool MatchBehind(Nfa subNfa, int pos, bool positive)
        {
            // A lookbehind is validated by scanning the prefix window for a
            // match that ends exactly at pos — any start position q ≤ pos.
            // Because validation is decoupled from the forward scan, the
            // lookbehind's length need not be bounded: variable-length
            // lookbehind works the same way as fixed-length.
            var allStarts = Enumerable.Range(0, pos + 1).ToArray();
            var structural = new PathfindingScan().Scan(
                subNfa, _input, 0, pos, allStarts, requireAcceptAt: pos);
            if (!HasSemanticFeatures(subNfa))
            {
                return structural.AcceptAlive == positive;
            }

            for (int q = pos; q >= 0; q--)
            {
                var scan = new PathfindingScan().Scan(subNfa, _input, 0, pos, new[] { q }, requireAcceptAt: pos);
                if (scan.AcceptAlive)
                {
                    var record = ResolveFirst(_engine, subNfa, _input, scan, q, acceptAt: pos, lookaroundDepth: _lookaroundDepth + 1);
                    if (record is not null)
                    {
                        return positive;
                    }
                }
            }

            return !positive;
        }

        private (int[] Starts, int[] Ends) SaveCaptures()
            => ((int[])_groupStarts.Clone(), (int[])_groupEnds.Clone());

        private void RestoreCaptures((int[] Starts, int[] Ends) saved)
        {
            Array.Copy(saved.Starts, _groupStarts, _groupStarts.Length);
            Array.Copy(saved.Ends, _groupEnds, _groupEnds.Length);
        }

        private string CapsKey()
        {
            var sb = new System.Text.StringBuilder();
            for (int g = 0; g < _groupStarts.Length; g++)
            {
                sb.Append(_groupStarts[g]).Append(':').Append(_groupEnds[g]).Append(';');
            }

            return sb.ToString();
        }
    }
}
