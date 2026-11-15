using DevelApp.TwoLayerRegex.Pattern;

namespace DevelApp.TwoLayerRegex.Compilation
{
    /// <summary>
    /// Thompson construction from a <see cref="PatternNode"/> tree to an
    /// <see cref="Nfa"/>. Every fragment is (start, accept); edges are
    /// appended in backtracking-preference order so that Phase 2's
    /// depth-first walk reproduces greedy/lazy and left-to-right semantics.
    /// </summary>
    public sealed class NfaCompiler
    {
        private const int MaxBoundedRepetition = 1000;

        private readonly List<NfaState> _states = new();

        private int _maxGroup;

        /// <summary>Compiles a pattern AST into an NFA.</summary>
        public static Nfa Compile(PatternNode ast)
        {
            var compiler = new NfaCompiler();
            var (start, accept) = compiler.CompileFragment(ast);
            // Guarantee a dedicated accepting state with no outgoing edges.
            var final = compiler.NewState();
            compiler.Connect(accept, final);
            return new Nfa(compiler._states.ToArray(), start, final, CountCaptureGroups(ast));
        }

        /// <summary>Counts capture groups anywhere in the AST, including inside lookarounds.</summary>
        public static int CountCaptureGroups(PatternNode node) => node switch
        {
            PatternNode.CaptureGroup g => 1 + CountCaptureGroups(g.Inner),
            PatternNode.Sequence s => s.Parts.Sum(CountCaptureGroups),
            PatternNode.Alternation a => a.Branches.Sum(CountCaptureGroups),
            PatternNode.Quantifier q => CountCaptureGroups(q.Inner),
            PatternNode.NonCaptureGroup n => CountCaptureGroups(n.Inner),
            PatternNode.Lookaround l => CountCaptureGroups(l.Inner),
            _ => 0,
        };

        private int NewState()
        {
            _states.Add(new NfaState());
            return _states.Count - 1;
        }

        private void Connect(int from, int to, Marker? marker = null)
            => _states[from].Transitions.Add(new NfaTransition(TransitionKind.Epsilon, to, marker: marker));

        private (int Start, int Accept) CompileFragment(PatternNode node) => node switch
        {
            PatternNode.CharMatcher m => CompileChar(m),
            PatternNode.Sequence s => CompileSequence(s),
            PatternNode.Alternation a => CompileAlternation(a),
            PatternNode.Quantifier q => CompileQuantifier(q),
            PatternNode.CaptureGroup g => CompileCaptureGroup(g),
            PatternNode.NonCaptureGroup n => CompileFragment(n.Inner),
            PatternNode.Lookaround l => CompileLookaround(l),
            PatternNode.Backreference b => CompileBackreference(b),
            PatternNode.StartAnchor => CompileAnchor(TransitionKind.AnchorStart),
            PatternNode.EndAnchor => CompileAnchor(TransitionKind.AnchorEnd),
            _ => throw new NotSupportedException($"Unsupported AST node {node.GetType().Name}."),
        };

        private (int, int) CompileChar(PatternNode.CharMatcher m)
        {
            int s = NewState();
            int a = NewState();
            _states[s].Transitions.Add(new NfaTransition(TransitionKind.Char, a, m.Predicate));
            return (s, a);
        }

        private (int, int) CompileSequence(PatternNode.Sequence seq)
        {
            int s = NewState();
            int current = s;
            foreach (var part in seq.Parts)
            {
                var (ps, pa) = CompileFragment(part);
                Connect(current, ps);
                current = pa;
            }

            int a = NewState();
            Connect(current, a);
            return (s, a);
        }

        private (int, int) CompileAlternation(PatternNode.Alternation alt)
        {
            int s = NewState();
            int a = NewState();
            foreach (var branch in alt.Branches)
            {
                var (bs, ba) = CompileFragment(branch);
                Connect(s, bs);
                Connect(ba, a);
            }

            return (s, a);
        }

        private (int, int) CompileQuantifier(PatternNode.Quantifier q)
        {
            if (q.Max != -1 && q.Max > MaxBoundedRepetition)
            {
                throw new PatternSyntaxException(
                    $"Bounded quantifier limit is {{{MaxBoundedRepetition}}}; got {{{q.Min},{q.Max}}}.", "", 0);
            }

            int s = NewState();
            int a = NewState();
            int current = s;

            // Mandatory copies first: {min, ...} => min chained fragments.
            for (int i = 0; i < q.Min; i++)
            {
                var (bs, ba) = CompileFragment(q.Inner);
                Connect(current, bs);
                current = ba;
            }

            if (q.Max == -1)
            {
                // One loop fragment; entering it is preferred over exiting in
                // greedy mode (edge order), and the reverse when lazy.
                var (ls, la) = CompileFragment(q.Inner);
                AddPreferenceEdge(current, ls, q.Greedy);
                AddPreferenceEdge(current, a, !q.Greedy);
                Connect(la, ls); // Loop back: another iteration.
                AddPreferenceEdge(la, a, !q.Greedy);
            }
            else
            {
                // Optional copies for {min, max}: a chain of slots, each of
                // which either enters one more copy or exits to the accept.
                for (int i = q.Min; i < q.Max; i++)
                {
                    var (os, oa) = CompileFragment(q.Inner);
                    AddPreferenceEdge(current, os, q.Greedy);
                    AddPreferenceEdge(current, a, !q.Greedy);
                    int next = NewState();
                    Connect(oa, next);
                    current = next;
                }

                Connect(current, a);
            }

            return (s, a);
        }

        private void AddPreferenceEdge(int from, int to, bool preferred)
        {
            // Preferred edges are inserted first in the target state's list so
            // Phase 2's depth-first walk tries them first.
            if (preferred)
            {
                _states[from].Transitions.Insert(0, new NfaTransition(TransitionKind.Epsilon, to));
            }
            else
            {
                _states[from].Transitions.Add(new NfaTransition(TransitionKind.Epsilon, to));
            }
        }

        private (int, int) CompileCaptureGroup(PatternNode.CaptureGroup g)
        {
            if (g.Number > _maxGroup)
            {
                _maxGroup = g.Number;
            }

            int s = NewState();
            int a = NewState();
            var (innerStart, innerAccept) = CompileFragment(g.Inner);
            Connect(s, innerStart, new Marker.GroupStart(g.Number));
            Connect(innerAccept, a, new Marker.GroupEnd(g.Number));
            return (s, a);
        }

        private (int, int) CompileLookaround(PatternNode.Lookaround l)
        {
            int s = NewState();
            int a = NewState();
            // The assertion's sub-pattern is compiled with a dedicated
            // compiler so its states stay a separate NFA; Phase 2 validates
            // it with a bounded sub-scan, never in the main scanning loop.
            var subCompiler = new NfaCompiler();
            var (ss, sa) = subCompiler.CompileFragment(l.Inner);
            var subFinal = subCompiler.NewState();
            subCompiler.Connect(sa, subFinal);
            var subNfa = new Nfa(subCompiler._states.ToArray(), ss, subFinal, subCompiler._maxGroup);

            Connect(s, a, new Marker.Assertion(subNfa, l.Kind));
            return (s, a);
        }

        private (int, int) CompileBackreference(PatternNode.Backreference b)
        {
            // Structurally permissive, semantically validated in Phase 2: the
            // reference may consume any run of characters (loop) or nothing
            // (epsilon exit). Phase 2 consumes exactly the captured content's
            // length and compares characters along the loop edges.
            int s = NewState();
            int a = NewState();
            int loop = NewState();
            Connect(s, loop);
            Connect(loop, a);
            _states[loop].Transitions.Add(
                new NfaTransition(TransitionKind.Char, loop, CharPredicate.Custom(_ => true, $"\\{b.Number}"), backrefGroup: b.Number));
            return (s, a);
        }

        private (int, int) CompileAnchor(TransitionKind kind)
        {
            int s = NewState();
            int a = NewState();
            _states[s].Transitions.Add(new NfaTransition(kind, a));
            return (s, a);
        }
    }
}
