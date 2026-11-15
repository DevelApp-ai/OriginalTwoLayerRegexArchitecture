// DevelApp.TwoLayerRegex.Demo — a runnable showcase of the two-layer regex
// architecture described in "A Two-Phase Architecture for Secure and
// Expressive Regular Expression Matching": the way the predecessor of the
// ENFA StepLexer/StepParser line worked.
//
// Run with: dotnet run --project src/DevelApp.TwoLayerRegex.Demo

using System.Diagnostics;
using DevelApp.TwoLayerRegex;

Console.WriteLine("=================================================================");
Console.WriteLine(" Two-Layer Regex Architecture — runnable showcase");
Console.WriteLine(" Phase 1: pathfinding scan (linear, O(m·n), the security boundary)");
Console.WriteLine(" Phase 2: grouping & content resolution (bounded post-processing)");
Console.WriteLine("=================================================================");
Console.WriteLine();

// ---------------------------------------------------------------------------
Console.WriteLine("--- 1. Capture groups: order-42 ---------------------------------");
Console.WriteLine();

RunSample(
    @"(\w+)-(\d+)",
    "invoice order-42 shipped",
    showGroups: true);

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("--- 2. Backreference: repeated word ------------------------------");
Console.WriteLine();
Console.WriteLine(@"   Pattern: (\w+)\s+\1  —  a backreference is a structural any-run in");
Console.WriteLine("   Phase 1 (never re-scans the input) and a direct string comparison");
Console.WriteLine("   in Phase 2.");

RunSample(
    @"(\w+)\s+\1",
    "say hello hello world",
    showGroups: true);

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("--- 3. Variable-length lookbehind ---------------------------------");
Console.WriteLine();
Console.WriteLine(@"   Pattern: (?<=ab*)c  —  the lookbehind is validated by a bounded");
Console.WriteLine("   prefix-window sub-scan that must end exactly at the current");
Console.WriteLine("   position. Because validation is decoupled from matching, the");
Console.WriteLine("   lookbehind's length need not be fixed — variable-length lookbehind");
Console.WriteLine("   works like fixed-length. (Classical engines such as .NET's");
Console.WriteLine("   System.Text.RegularExpressions reject it outright.)");

RunSample(
    @"(?<=ab*)c",
    "c ac abc xabc",
    showGroups: false);

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("--- 4. Lookahead with a capture ----------------------------------");
Console.WriteLine();

RunSample(
    @"\w+(?=\s*\()",
    "sqrt(2) + max(3, 4)",
    showGroups: false);

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("--- 5. Greedy vs lazy --------------------------------------------");
Console.WriteLine();

Console.WriteLine("   Greedy  <a.+>    on <ab><cd>:");
var greedy = new TwoLayerRegex(@"<a.+>");
var g = greedy.Match("<ab><cd>");
Console.WriteLine($"     match = '{g.Value}' at {g.Index}  (loop edge preferred: longest)");

Console.WriteLine("   Lazy    <a.+?>   on <ab><cd>:");
var lazy = new TwoLayerRegex(@"<a.+?>");
var l = lazy.Match("<ab><cd>");
Console.WriteLine($"     match = '{l.Value}' at {l.Index}  (exit edge preferred: shortest)");

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("--- 6. ReDoS immunity: the evil patterns --------------------------");
Console.WriteLine();
Console.WriteLine("   The classic catastrophic-backtracking patterns, run against");
Console.WriteLine("   attack inputs of length 20,000. Phase 1 is a single left-to-right");
Console.WriteLine("   pass that visits each (state, position) pair at most once, so a");
Console.WriteLine("   non-matching attack input cannot blow up the search: worst case");
Console.WriteLine("   stays linear in the input. A classical backtracking engine needs");
Console.WriteLine("   exponential time on the same inputs.");
Console.WriteLine();

const int evilLength = 20_000;
var attack = new string('a', evilLength - 1) + "!";
var valid = new string('a', evilLength);

string[] evilPatterns =
{
    @"^(a+)+$",
    @"^(a|aa)+$",
    @"^([a-z]+)*$",
};

foreach (var pattern in evilPatterns)
{
    var engine = new TwoLayerRegex(pattern);

    var sw = Stopwatch.StartNew();
    var miss = engine.Match(attack);          // leading ^ ⇒ single Phase 1 scan
    sw.Stop();
    Console.WriteLine($"   {pattern,-15} vs '{'a'}×{evilLength - 1}!'  →  no match in {sw.ElapsedMilliseconds,4} ms");
    Console.WriteLine($"       Phase 1: {engine.LastPhase1Trace}");

    sw.Restart();
    var hit = engine.Match(valid);
    sw.Stop();
    Console.WriteLine($"   {pattern,-15} vs '{'a'}×{evilLength}    →  match in {sw.ElapsedMilliseconds,4} ms ({hit.Value.Length} chars)");
    Console.WriteLine($"       Phase 1: {engine.LastPhase1Trace}");
    Console.WriteLine();
}

// ---------------------------------------------------------------------------
Console.WriteLine("--- 7. Structural fast path: IsMatch on pure-regular patterns -----");
Console.WriteLine();
Console.WriteLine("   Without backreferences or lookarounds, structure and semantics");
Console.WriteLine("   coincide: one multi-start Phase 1 scan answers IsMatch outright,");
Console.WriteLine("   with no Phase 2 at all. The evil pattern's non-matching input is");
Console.WriteLine("   rejected by the scan alone.");
Console.WriteLine();

var fast = new TwoLayerRegex(@"^(a+)+$");
var swFast = Stopwatch.StartNew();
bool isMatch = fast.IsMatch(attack);
swFast.Stop();
Console.WriteLine($"   ^(a+)+$ IsMatch('a×{evilLength - 1}!') = {isMatch} in {swFast.ElapsedMilliseconds} ms");
Console.WriteLine($"   Phase 1: {fast.LastPhase1Trace}");
Console.WriteLine();

Console.WriteLine("=================================================================");
Console.WriteLine(" End of showcase.");
Console.WriteLine("=================================================================");

static void RunSample(string pattern, string input, bool showGroups)
{
    Console.WriteLine($"   Pattern: {pattern}");
    Console.WriteLine($"   Input:   \"{input}\"");

    var engine = new TwoLayerRegex(pattern);
    var match = engine.Match(input);

    Console.WriteLine($"   Phase 1 (pathfinding scan): {engine.LastPhase1Trace}");

    if (match.Success)
    {
        Console.WriteLine($"   Phase 2 (grouping resolver): match = '{match.Value}' at index {match.Index}, length {match.Length}");
        if (showGroups)
        {
            foreach (var group in match.Groups)
            {
                Console.WriteLine(group.Success
                    ? $"       group {group.Number} = '{group.Value}' [{group.Index}..{group.Index + group.Length})"
                    : $"       group {group.Number} = <unset>");
            }
        }
    }
    else
    {
        Console.WriteLine("   Phase 2 (grouping resolver): no match");
    }

    Console.WriteLine();
}
