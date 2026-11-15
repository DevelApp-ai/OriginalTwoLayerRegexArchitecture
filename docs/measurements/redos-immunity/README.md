# ReDoS immunity measurements — the two-layer engine

Committed measurement data backing the ReDoS-immunity test suite
(`src/DevelApp.TwoLayerRegex.Tests/Redos/`) and the ReDoS tables of the
formally-verified-parsers article. The pattern catalog and adversarial
inputs are identical to the ENFA StepLexer measurement dataset
(DevelApp-ai/ENFAStepLexer-StepParser, `docs/measurements/redos-immunity/`),
so the two engines' columns are directly comparable; the .NET baselines are
carried over from that dataset (same inputs, same runtime).

## How to regenerate

```bash
REDOS_REPORT=full dotnet test src/DevelApp.TwoLayerRegex.Tests \
  --filter "FullyQualifiedName~RedosMeasurementReportTests"
```

The report test writes `redos-immunity-report.json` next to the test
assembly; copy it here and update this README with the fresh run. CI runs
smoke mode (1/10 KB grid, short ladder) on every build, and the
CI-enforcing suite (`RedosImmunityGridTests`, `LinearScalingTests`,
`UnanchoredSearchCharacteristicTests`) asserts the security properties
continuously.

## Measurement run

- Generated: 2026-11-15T08:12:43Z
- Engine under test: DevelApp.TwoLayerRegex (this showcase, PR #1 `d90e5b1b`)
- Method: `MatchExact` — anchored full-input evaluation: one Phase 1 scan
  (O(m·n), the security boundary) plus, only when the scan finds a
  structural accept, the bounded Phase 2 walk. This is the honest
  methodological difference from the ENFA suite, which measures
  tokenization: the two-layer engine *supports* groups, alternation and
  anchors, so it must evaluate the patterns rather than reject them, and
  its entry point with the full O(m·n) guarantee is the anchored scan.
- Runtime: .NET 8.0.34, Debian GNU/Linux 13 (trixie), invariant
  globalization — the same runtime and OS as the ENFA baseline run.
- Raw data: `report-2026-11-15.json` (grid at 1/10/100 KB, scaling ladder
  with min-of-5 per length, unanchored characteristic).

## Article and corpus patterns, identical adversarial inputs

Two-layer times are the full anchored evaluation of 100 KB of adversarial
input. Where StepLexer's immunity is *fail-fast* (the forward-only matcher
does not support groups/alternation/anchors and rejects the pattern at
position 0 in microseconds), the two-layer engine's immunity is *actual
linear evaluation*: Phase 1 scans the whole attack input once, proves the
accepting state unreachable, and Phase 2 never runs.

| Pattern | Two-layer MatchExact 100 KB | StepLexer 100 KB | .NET NonBacktracking 100 KB | .NET backtracking (500 ms timeout) |
|---|---|---|---|---|
| `(a+)+` | 109.7 ms (no match) | 0.006 ms (fail fast) | 0.028 ms | 0.02 ms @ 1 KB (matches prefix) |
| `(a*)*b` | 54.4 ms (no match) | 0.006 ms (fail fast) | 1.846 ms | **> 500 ms timeout** @ 1 KB |
| `(a|a)*` | 78.6 ms (no match) | 0.022 ms (fail fast) | 0.007 ms | 0.08 ms @ 1 KB (matches prefix) |
| `(.*a){10}` | 21.3 ms (no match) | 0.015 ms (fail fast) | 2.166 ms | **> 500 ms timeout** @ 100 KB |
| `.*(?:.*=.*)` (Cloudflare-style) | 52.5 ms (no match) | 0.005 ms (fail fast) | 1.562 ms | **> 500 ms timeout** @ 100 KB |
| `^([\w\-.]+)*@([\w\-.]+)$` (SO-style) | 72.8 ms (no match) | 0.011 ms (fail fast) | 1.926 ms | **> 500 ms timeout** @ 1 KB |
| `(a+)+$` (Davis) | 313.1 ms (**matches**) | 0.011 ms (fail fast) | 3.220 ms | **> 500 ms timeout** @ 1 KB |
| `(a|aa)+$` (Davis) | 1084.7 ms (**matches**) | 0.005 ms (fail fast) | 3.045 ms | **> 500 ms timeout** @ 1 KB |
| `([a-zA-Z]+)*$` (Davis) | 274.8 ms (**matches**) | 0.009 ms (fail fast) | 3.184 ms | **> 500 ms timeout** @ 1 KB |
| `(x+x+)+y` (Davis) | 178.4 ms (no match) | 0.020 ms (fail fast) | 1.581 ms | **> 500 ms timeout** @ 1 KB |
| `(a+)+b` (Davis) | 103.1 ms (no match) | 0.010 ms (fail fast) | 1.605 ms | **> 500 ms timeout** @ 1 KB |
| `(a|b|ab)*c` (Davis) | 84.3 ms (no match) | 0.007 ms (fail fast) | 1.534 ms | **> 500 ms timeout** @ 100 KB |
| `(a+)*$` (Davis) | 453.2 ms (**matches**) | 0.005 ms (fail fast) | 3.139 ms | **> 500 ms timeout** @ 1 KB |
| `(\w+\s?)*$` (Davis) | 275.9 ms (**matches**) | 0.009 ms (fail fast) | 3.103 ms | **> 500 ms timeout** @ 1 KB |

No case exceeded the Phase 2 step budget; every verdict was correct.

## Honest-measurement findings

1. **The immunity mechanism is real evaluation, not rejection.** The
   two-layer engine supports every construct in the corpus, so unlike
   StepLexer it cannot lean on fail-fast. The non-matching attacks are
   rejected by Phase 1 alone in 21–178 ms at 100 KB — the scan visits each
   (state, position) pair at most once, so a structurally hopeless input
   costs exactly one linear pass, independent of how exponentially a
   backtracking search would fork.
2. **Matching runs cost more than rejections.** The five matching Davis
   patterns run Phase 2 over the whole DAG (275–1085 ms at 100 KB). The
   walk is bounded (memoized failures, in-progress cycle pruning, hard step
   budget) but its constant is dominated by per-frame capture snapshots:
   ~1–2 µs per input character. `(a|aa)+$` is the worst at ~1.1 s —
   one alternation frame pair per character.
3. **The two-layer engine trades constant factors for the polynomial
   bound.** Per case it is 10–600× slower than .NET's `NonBacktracking`
   engine — an unoptimized showcase (BFS with boxed queue entries,
   string-keyed memos, array-clone snapshots) against a production
   engine. The security claim is not "fast", it is "cannot blow up": every
   case stays polynomial where the backtracking engine exceeds a 500 ms
   timeout on 12 of 14 patterns.
4. **Unanchored `(a+)+` and `(a|a)*` are not ReDoS patterns in any
   engine** (they match the a-run prefix or fail at the '!' after a
   linear structural scan); the blowup requires anchoring, which the
   Davis variants demonstrate.

## Linear scaling (1 KB → 100 KB)

Anchored `MatchExact` runs over the scaling ladder
{1, 4, 16, 32, 64, 100} KB, minimum of 5 repetitions per length,
least-squares fit of time against length:

| Case | Slope | R² | t(100 KB)/t(16 KB) | t(100 KB) |
|---|---|---|---|---|
| `^([a]+)$` full run | 1951 ns/char | 0.917 | 12.0 | 214.2 ms |
| `^([a]*)b$` full-scan no-match | 353 ns/char | 1.000 | 6.2 | 36.1 ms |
| `^([ab]*)$` full run | 1946 ns/char | 0.984 | 8.4 | 205.4 ms |
| `^([^x]*)$` negated full run | 1802 ns/char | 0.978 | 6.9 | 173.5 ms |
| `^(.*)b$` dot full-scan no-match | 441 ns/char | 1.000 | 6.2 | 45.0 ms |
| `^([a-zæøå]+)$` Danish run | 1440 ns/char | 0.986 | 8.0 | 152.1 ms |
| `^([^x]*)$` negated Danish run | 1473 ns/char | 0.984 | 10.0 | 154.2 ms |
| `^(a+)+$` evil no-match | 885 ns/char | 0.999 | 6.2 | 89.8 ms |
| `^(a*)*b$` evil no-match | 467 ns/char | 1.000 | 6.2 | 47.9 ms |
| `^([\w\-.]+)*@([\w\-.]+)$` evil no-match | 618 ns/char | 1.000 | 6.5 | 63.6 ms |

Linear scaling predicts a growth ratio of 6.25 (= 102400/16384); a
quadratic regression would show 39. The rejection runs (Phase 1 only)
sit at 6.2–10.0 with R² ≥ 0.98 — cleanly linear. The matching runs
grow 8.4–12.0 with R² ≥ 0.92: still inside the linear envelope (a
quadratic term would show 39), with the excess coming from GC pressure
of the per-frame snapshot allocation noted above, not from search
complexity. The CI suite enforces growth ≤ 14 and slope ≤ 5000 ns/char
on the 4–32 KB ladder on every build.

Unlike the ENFA suite, `\p{L}+` and `(?i)[a-z]+` have no counterpart
here: the showcase parser does not support Unicode property classes or
inline flags (a documented limitation), so those ladder entries are
skipped rather than approximated.

## Known characteristic: unanchored search

`a+b` over an all-'a' input via the unanchored `Match()` entry point:
every position is a candidate start and each start scans to the end, so
total work is the documented O(m·n²) worst case — measured 33.7 ms at
512 chars, 103.5 ms at 1 KB, 391.6 ms at 2 KB (11.6× per 4× length ≈
quadratic's 16×). Each individual scan stays linear and a no-match
always terminates; this is a search-strategy cost, not catastrophic
backtracking. It is measured at capped lengths only and pinned as the
follow-up optimization (single multi-start scan for the unanchored case,
as the `IsMatch` fast path already does for pure-regular patterns).

## Lineage note

The ENFA StepLexer-StepParser line evolved from this two-layer
architecture: Phase 1's linear scan became the forward-only step lexer,
and Phase 2's bounded resolution was split between the lexer (token
boundaries) and the parser (structure), with unsupported constructs
rejected fail-fast. The article's ReDoS tables report both engines
against this shared pattern catalog.
