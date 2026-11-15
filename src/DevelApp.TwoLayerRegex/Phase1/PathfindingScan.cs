using DevelApp.TwoLayerRegex.Compilation;

namespace DevelApp.TwoLayerRegex.Phase1
{
    /// <summary>
    /// The result of a Phase 1 run: the set of alive (state, position) pairs
    /// — the shared-prefix DAG of all potential structural match paths —
    /// together with the accept positions and an annotation trace. This is
    /// the data structure Phase 2 walks; Phase 2 never re-scans the input.
    /// </summary>
    public sealed class ScanResult
    {
        /// <summary>Creates a scan result.</summary>
        public ScanResult(bool[] alive, int windowStart, int windowEnd, bool acceptAlive, List<int> acceptPositions, ScanTrace trace)
        {
            Alive = alive;
            WindowStart = windowStart;
            WindowEnd = windowEnd;
            AcceptAlive = acceptAlive;
            AcceptPositions = acceptPositions;
            Trace = trace;
        }

        /// <summary>Aliveness indexed as state × windowLength + (pos − windowStart).</summary>
        public bool[] Alive { get; }

        /// <summary>The first position of the scan window.</summary>
        public int WindowStart { get; }

        /// <summary>The end of the scan window (exclusive).</summary>
        public int WindowEnd { get; }

        /// <summary>Whether the accepting state was reached under the scan's constraints.</summary>
        public bool AcceptAlive { get; }

        /// <summary>The positions at which the accepting state was reached.</summary>
        public List<int> AcceptPositions { get; }

        /// <summary>The annotation trace for display.</summary>
        public ScanTrace Trace { get; }

        /// <summary>Tests whether a (state, position) pair was alive in the scan.</summary>
        public bool IsAlive(int state, int pos)
            => pos >= WindowStart && pos <= WindowEnd && Alive[state * (WindowEnd - WindowStart + 1) + (pos - WindowStart)];
    }

    /// <summary>
    /// Statistics and annotation trace from one Phase 1 run — the
    /// "potential match records" materialization, summarized for display.
    /// </summary>
    public sealed class ScanTrace
    {
        /// <summary>Number of distinct (state, position) DAG nodes visited.</summary>
        public int AliveNodes { get; set; }

        /// <summary>Number of DAG edges traversed.</summary>
        public int Edges { get; set; }

        /// <summary>Number of consuming character steps taken.</summary>
        public int CharSteps { get; set; }

        /// <summary>Number of group marker annotations recorded.</summary>
        public int GroupMarkers { get; set; }

        /// <summary>Number of lookaround assertion positions recorded.</summary>
        public int Assertions { get; set; }

        /// <summary>Number of backreference loop steps recorded.</summary>
        public int BackreferenceSteps { get; set; }

        /// <summary>The positions at which the accepting state was reached.</summary>
        public List<int> AcceptPositions { get; } = new();

        /// <summary>Human-readable one-line summary.</summary>
        public override string ToString()
            => $"alive nodes={AliveNodes} (states × positions ⇒ O(m·n)), edges={Edges}, char steps={CharSteps}, "
                + $"group markers={GroupMarkers}, assertions={Assertions}, backref steps={BackreferenceSteps}, "
                + $"accept positions=[{string.Join(", ", AcceptPositions)}]";
    }

    /// <summary>
    /// Phase 1: the Pathfinding Scan. A modified Thompson NFA simulation
    /// that processes the input strictly left-to-right, once per character,
    /// tracking all simultaneously-alive (state, position) pairs. Capture
    /// groups, lookarounds and backreferences are non-consuming markers and
    /// are recorded, never interpreted. The output is a reachability
    /// structure over (state, position) — a DAG that shares common prefixes
    /// among all potential structural match paths — plus an annotation
    /// trace. Each pair is processed at most once, so the scan is O(m · n)
    /// and cannot loop, stall or blow up: this is the security guarantee
    /// against catastrophic backtracking.
    /// </summary>
    public sealed class PathfindingScan
    {
        /// <summary>
        /// Runs the scan over a window of the input, seeded at one or more
        /// start positions. Zero-width edges (including markers and
        /// anchors) are followed within the same position; consuming edges
        /// advance one position.
        /// </summary>
        /// <param name="nfa">The compiled pattern.</param>
        /// <param name="input">The subject text.</param>
        /// <param name="windowStart">First position of the scan window (inclusive); ^ holds here.</param>
        /// <param name="windowEnd">End of the scan window (exclusive); $ holds here.</param>
        /// <param name="startPositions">Where potential matches may begin (seeded in order).</param>
        /// <param name="requireAcceptAt">If not null, only an accept at exactly this position counts.</param>
        /// <returns>The scan result, including the aliveness table Phase 2 prunes against.</returns>
        public ScanResult Scan(
            Nfa nfa,
            string input,
            int windowStart,
            int windowEnd,
            IReadOnlyList<int> startPositions,
            int? requireAcceptAt = null)
        {
            int width = windowEnd - windowStart + 1;
            var alive = new bool[nfa.States.Length * width];
            var queued = new bool[nfa.States.Length * width];
            var queue = new Queue<(int State, int Pos)>();
            foreach (var s in startPositions)
            {
                Enqueue(nfa, queue, queued, alive, windowStart, windowEnd, nfa.Start, s);
            }

            // Pre-mark seeds alive so shared-prefix nodes collapse even when
            // multiple seeds would reach the same pair.
            var acceptPositions = new List<int>();
            bool acceptAlive = false;
            var trace = new ScanTrace();

            while (queue.Count > 0)
            {
                var (state, pos) = queue.Dequeue();
                int key = state * width + (pos - windowStart);
                if (alive[key])
                {
                    continue;
                }

                alive[key] = true;
                trace.AliveNodes++;

                if (state == nfa.Accept && (requireAcceptAt is null || pos == requireAcceptAt.Value))
                {
                    acceptAlive = true;
                    trace.AcceptPositions.Add(pos);
                    acceptPositions.Add(pos);
                }

                foreach (var t in nfa.States[state].Transitions)
                {
                    trace.Edges++;
                    switch (t.Kind)
                    {
                        case TransitionKind.Epsilon:
                            if (t.Marker != null)
                            {
                                RecordMarker(trace, t.Marker);
                            }

                            Enqueue(nfa, queue, queued, alive, windowStart, windowEnd, t.Target, pos);
                            break;
                        case TransitionKind.AnchorStart:
                            if (pos == windowStart)
                            {
                                Enqueue(nfa, queue, queued, alive, windowStart, windowEnd, t.Target, pos);
                            }

                            break;
                        case TransitionKind.AnchorEnd:
                            if (pos == windowEnd)
                            {
                                Enqueue(nfa, queue, queued, alive, windowStart, windowEnd, t.Target, pos);
                            }

                            break;
                        case TransitionKind.Char:
                            if (pos < windowEnd && t.CharPredicate.Matches(input[pos]))
                            {
                                trace.CharSteps++;
                                if (t.BackrefGroup >= 0)
                                {
                                    trace.BackreferenceSteps++;
                                }

                                Enqueue(nfa, queue, queued, alive, windowStart, windowEnd, t.Target, pos + 1);
                            }

                            break;
                    }
                }
            }

            return new ScanResult(alive, windowStart, windowEnd, acceptAlive, acceptPositions, trace);
        }

        private static void Enqueue(
            Nfa nfa,
            Queue<(int State, int Pos)> queue,
            bool[] queued,
            bool[] alive,
            int windowStart,
            int windowEnd,
            int state,
            int pos)
        {
            if (pos < windowStart || pos > windowEnd)
            {
                return;
            }

            int key = state * (windowEnd - windowStart + 1) + (pos - windowStart);
            if (!queued[key] && !alive[key])
            {
                queued[key] = true;
                queue.Enqueue((state, pos));
            }
        }

        private static void RecordMarker(ScanTrace trace, Marker marker)
        {
            switch (marker)
            {
                case Marker.GroupStart or Marker.GroupEnd:
                    trace.GroupMarkers++;
                    break;
                case Marker.Assertion:
                    trace.Assertions++;
                    break;
            }
        }
    }
}
