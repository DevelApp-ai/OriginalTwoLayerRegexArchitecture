# OriginalTwoLayerRegexArchitecture

A runnable showcase of the **two-layer regex architecture** — the way the
predecessor of the [ENFA StepLexer/StepParser](https://github.com/DevelApp-ai/ENFAStepLexer-StepParser)
line worked, recreated from the article
*"A Two-Phase Architecture for Secure and Expressive Regular Expression
Matching"*. No implementation of the original architecture survived, so this
repository is a **recreation**: a small, honest, fully tested C#/.NET 8 engine
that demonstrates the older way it was working.

## The idea in one paragraph

Classical regex engines interleave *structure* ("can these characters be
consumed at all?") and *semantics* ("which groups end where, does this
backreference hold, does that lookaround pass?") in a single backtracking
search — which is exactly where catastrophic backtracking (ReDoS) comes from.
The two-layer architecture splits them:

| Layer | Name | What it does | Cost |
| --- | --- | --- | --- |
| 1 | **Pathfinding scan** | Thompson-NFA-style simulation over all simultaneously-alive `(state, position)` pairs. Non-regular constructs (capture groups, lookarounds, backreferences) become **non-consuming markers** — recorded, never interpreted. Output: a potential-match DAG (shared prefixes collapsed) plus an annotation trace. | **O(m·n), guaranteed.** Each pair is visited at most once; it cannot loop, stall, or blow up. |
| 2 | **Grouping & content resolution** | Bounded post-processing over the Phase 1 DAG: finalizes capture groups from the recorded markers, validates lookarounds with bounded sub-scans of the same engine, verifies backreferences by direct string comparison. Memoized failures, hard step budget. | Bounded polynomial over the DAG; **never re-scans the input.** |

Phase 1 is the security boundary: an attack input that cannot match
structurally is rejected by the scan alone, in linear time, and Phase 2
never runs.

```
          input
            │
   ┌────────▼────────┐    potential-match DAG
   │ Phase 1: scan   │    (aliveness table, markers,
   │ O(m·n), linear   │    accept positions, trace)
   └────────┬────────┘
            │  (no structural match ⇒ done, linear)
   ┌────────▼────────┐
   │ Phase 2: resolve │   capture spans, lookarounds,
   │ bounded, budgeted │   backreference content checks
   └────────┬────────┘
            │
          match
```

## The payoff: ReDoS immunity

The classic catastrophic-backtracking patterns against 20,000-character
attack inputs (`dotnet run --project src/DevelApp.TwoLayerRegex.Demo`):

```
^(a+)+$         vs 'a×19999!'  →  no match in    8 ms
    Phase 1: alive nodes=339984 (states × positions ⇒ O(m·n)), edges=439975, char steps=59994, group markers=59997, assertions=0, backref steps=0, accept positions=[]
^(a+)+$         vs 'a×20000    →  match in   43 ms (20000 chars)
^(a|aa)+$       vs 'a×19999!'  →  no match in    4 ms
^([a-z]+)*$     vs 'a×19999!'  →  no match in   11 ms
```

The same non-matching inputs need *exponential* time on a classical
backtracking engine. Here the worst case stays linear because Phase 1 visits
each `(state, position)` pair at most once — `^(a+)+$` is structurally
identical to `^(a*)$` for the scanner, and the group marker is just an
annotation counted in the trace.

## The other payoff: variable-length lookbehind

Because a lookbehind is validated by a **prefix-window sub-scan that must end
exactly at the current position** — decoupled from forward matching — its
length need not be bounded:

```
Pattern: (?<=ab*)c
Input:   "c ac abc xabc"
Phase 1:  alive nodes=7, edges=6, char steps=1, assertions=1, accept positions=[4]
Phase 2:  match = 'c' at index 3
```

.NET's `System.Text.RegularExpressions` rejects `(?<=ab*)` outright
(unbounded lookbehind); here it works the same way as fixed-length lookbehind.
The same mechanism, applied to a lookahead, resolves it as a bounded sub-match
anchored at the current position.

## What Phase 2 actually resolves

From the demo (`(\w+)\s+\1` on `"say hello hello world"`):

```
Phase 1: alive nodes=92, edges=98, char steps=17, group markers=6, backref steps=11,
         accept positions=[10, 11, 12, ..., 21]   ← every structural candidate
Phase 2: match = 'hello hello' at index 4, length 11
         group 1 = 'hello' [4..9)                ← capture finalized from markers
```

The backreference is compiled as a structurally-permissive any-character run
(the `backref steps` in the trace), so the scan never re-reads the input for
it; Phase 2 consumes exactly the captured span's length and compares the two
spans directly. Greedy vs lazy is preserved by compiling preference edges in
backtracking order (`<a.+>` matches `<ab><cd>` greedily, `<a.+?>` stops at the
first exit: `<ab>`).

For patterns with **no** backreferences or lookarounds, structure and
semantics coincide, so `IsMatch` is answered by a single multi-start Phase 1
scan with no Phase 2 at all — the pure O(m·n) guarantee.

## Repository layout

```
src/DevelApp.TwoLayerRegex         the engine
  Pattern/       recursive-descent parser → AST (PatternParser, PatternAst)
  Compilation/   Thompson construction with marker edges (Nfa, NfaCompiler)
  Phase1/        the pathfinding scan + trace (PathfindingScan)
  Phase2/        the bounded grouping resolver (GroupingResolver)
  TwoLayerRegex  the facade: Match / MatchExact / IsMatch
src/DevelApp.TwoLayerRegex.Tests   41 xunit tests: features, semantics, ReDoS immunity
src/DevelApp.TwoLayerRegex.Demo    the showcase runner quoted above
```

## Honest limitations

This is a showcase, not a production engine:

- **No inline flags** (`(?i)` case-insensitivity etc.) and no `\p{...}`
  Unicode property classes.
- **Capture groups inside lookarounds are not propagated**: lookarounds are
  validated as bounded sub-scans, so `(?=(a+))\1` cannot see the inner
  capture. The assertion itself works.
- **Unanchored search is O(m·n²) worst case**: leftmost search runs one Phase 1
  scan per candidate start (first-character predicates prune most). Anchored
  patterns and `MatchExact` keep the full O(m·n) guarantee. The article's own
  evil patterns are anchored, which is where the linear security claim holds
  exactly.
- **Class escapes are ASCII-sampled** for negated classes in the parser
  (`\D`, `[^a-z]` unions), consistent with the showcase scope.
- **Step budget**: Phase 2 is capped at 2,000,000 steps per resolve; on budget
  exhaustion it reports failure (`LastPhase2BudgetExceeded`) instead of
  hanging. Lookaround recursion is capped at depth 8.
- Zero-progress cycles (e.g. `(a*)*` empty iterations) are pruned by an
  in-progress check on `(state, position)` — the standard linear-time
  treatment, which also keeps the resolver's frame stack on the heap, so long
  matches cannot overflow the call stack.

## Lineage

The architecture described here is the older two-layer way: a linear scan that
treats the non-regular parts of a pattern as markers, with bounded
post-scan resolution. The ENFA StepLexer/StepParser line carried this
philosophy forward — compiled rule tables, no unbounded re-scanning — and the
design document `docs/ENFA Parser vNext.pdf` (ENFA repo) continues parts of
it. This repository exists so the older way stays runnable, inspectable and
honest about its limits.

## Building and running

```bash
dotnet build OriginalTwoLayerRegexArchitecture.sln -c Debug
dotnet test  src/DevelApp.TwoLayerRegex.Tests
dotnet run   --project src/DevelApp.TwoLayerRegex.Demo
```

Release builds treat warnings as errors and generate XML documentation
(org-style gate). CI does the same on every push and pull request.

## License

MIT © 2026 Lars Shakya Buch-Jepsen — see [LICENSE](LICENSE).
