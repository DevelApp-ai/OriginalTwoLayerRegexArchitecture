using DevelApp.TwoLayerRegex.Pattern;

namespace DevelApp.TwoLayerRegex.Compilation
{
    /// <summary>
    /// A compiled pattern: a Thompson-style NFA. Constructs that are not
    /// part of a regular language — capture groups, lookarounds,
    /// backreferences, anchors — are compiled as non-consuming
    /// <em>markers</em> on transitions, never as matching logic. Phase 1
    /// records them during the scan; Phase 2 gives them their semantics.
    /// </summary>
    public sealed class Nfa
    {
        /// <summary>Creates an NFA from compiled states.</summary>
        public Nfa(NfaState[] states, int start, int accept, int groupCount)
        {
            States = states;
            Start = start;
            Accept = accept;
            GroupCount = groupCount;
        }

        /// <summary>All states, indexed by state number.</summary>
        public NfaState[] States { get; }

        /// <summary>The start state.</summary>
        public int Start { get; }

        /// <summary>The accepting state; it has no outgoing transitions.</summary>
        public int Accept { get; }

        /// <summary>The number of capturing groups in the pattern.</summary>
        public int GroupCount { get; }
    }

    /// <summary>One NFA state; holds its outgoing transitions in preference order.</summary>
    public sealed class NfaState
    {
        /// <summary>The outgoing transitions, ordered by backtracking-preference.</summary>
        public List<NfaTransition> Transitions { get; } = new();
    }

    /// <summary>What a zero-width marker edge annotates.</summary>
    public abstract record Marker
    {
        /// <summary>Entering capture group <see cref="Group"/> at the current position.</summary>
        public sealed record GroupStart(int Group) : Marker;

        /// <summary>Leaving capture group <see cref="Group"/> at the current position.</summary>
        public sealed record GroupEnd(int Group) : Marker;

        /// <summary>A lookaround assertion whose sub-NFA must be validated in Phase 2.</summary>
        public sealed record Assertion(Nfa SubNfa, LookaroundKind Kind) : Marker;
    }

    /// <summary>The kind of an NFA transition.</summary>
    public enum TransitionKind
    {
        /// <summary>Zero-width epsilon edge, optionally carrying a marker.</summary>
        Epsilon,

        /// <summary>Consumes one character that satisfies the predicate; may carry a backreference annotation.</summary>
        Char,

        /// <summary>Zero-width edge valid only at window start (^).</summary>
        AnchorStart,

        /// <summary>Zero-width edge valid only at window end ($).</summary>
        AnchorEnd,
    }

    /// <summary>One edge of the NFA.</summary>
    public sealed class NfaTransition
    {
        /// <summary>Creates a transition.</summary>
        public NfaTransition(
            TransitionKind kind,
            int target,
            CharPredicate charPredicate = default,
            int backrefGroup = -1,
            Marker? marker = null)
        {
            Kind = kind;
            Target = target;
            CharPredicate = charPredicate;
            BackrefGroup = backrefGroup;
            Marker = marker;
        }

        /// <summary>The edge kind.</summary>
        public TransitionKind Kind { get; }

        /// <summary>The target state.</summary>
        public int Target { get; }

        /// <summary>The character predicate for a consuming edge (default when not a Char edge).</summary>
        public CharPredicate CharPredicate { get; }

        /// <summary>The 1-based group number when this edge belongs to a backreference loop, else -1.</summary>
        public int BackrefGroup { get; }

        /// <summary>The marker for an annotated epsilon edge, else null.</summary>
        public Marker? Marker { get; }
    }
}
