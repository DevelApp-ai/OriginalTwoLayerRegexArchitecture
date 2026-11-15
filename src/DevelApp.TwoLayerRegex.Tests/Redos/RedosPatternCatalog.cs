namespace DevelApp.TwoLayerRegex.Tests.Redos
{
    /// <summary>The adversarial input shapes used by the ReDoS measurement suite.</summary>
    public enum AttackInputKind
    {
        /// <summary>'a' repeated n times.</summary>
        AllAs,

        /// <summary>'x' repeated n times.</summary>
        AllXs,

        /// <summary>'a' repeated n-1 times followed by '!' — almost matches, never does.</summary>
        AsTrailingBang,

        /// <summary>'b' repeated n-9 times followed by nine 'a's.</summary>
        BsPlusNineAs,

        /// <summary>'h' repeated n times — a header with no '=' anywhere.</summary>
        HeaderNoEquals,

        /// <summary>The Danish letters "æøå" cycled to n characters.</summary>
        DanishLetters,
    }

    /// <summary>One pattern/input pair of the ReDoS measurement catalog.</summary>
    public sealed record RedosCase(string Name, string Pattern, AttackInputKind Input, bool ExpectMatch);

    /// <summary>
    /// The ReDoS pattern catalog of the two-phase articles and of the Davis
    /// catastrophic-backtracking corpus, paired with their adversarial
    /// inputs. The same catalog is used by the ENFA StepLexer measurement
    /// suite (DevelApp-ai/ENFAStepLexer-StepParser, docs/measurements/),
    /// so the two engines are measured against identical patterns and
    /// inputs.
    /// </summary>
    public static class RedosPatternCatalog
    {
        /// <summary>
        /// Builds an adversarial input of the given kind and length. These
        /// builders are byte-for-byte identical to the ENFA suite's, so the
        /// baseline measurements carry over.
        /// </summary>
        public static string BuildInput(AttackInputKind kind, int length)
        {
            switch (kind)
            {
                case AttackInputKind.AllAs:
                    return new string('a', length);
                case AttackInputKind.AllXs:
                    return new string('x', length);
                case AttackInputKind.AsTrailingBang:
                    return new string('a', length - 1) + "!";
                case AttackInputKind.BsPlusNineAs:
                    return new string('b', length - 9) + new string('a', 9);
                case AttackInputKind.HeaderNoEquals:
                    return new string('h', length);
                case AttackInputKind.DanishLetters:
                    var letters = "æøå";
                    var sb = new System.Text.StringBuilder(length);
                    for (int i = 0; i < length; i++)
                    {
                        sb.Append(letters[i % letters.Length]);
                    }

                    return sb.ToString();
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>
        /// The 14 article/corpus patterns with their adversarial inputs.
        /// ExpectMatch records what the two-layer engine must answer: the
        /// anchored full-input evaluation either rejects the attack or
        /// confirms the pure run — in bounded time either way.
        /// </summary>
        public static IReadOnlyList<RedosCase> ArticleAndCorpusCases { get; } = new[]
        {
            // Article patterns.
            new RedosCase("article-(a+)+", @"(a+)+", AttackInputKind.AsTrailingBang, ExpectMatch: false),
            new RedosCase("article-(a*)*b", @"(a*)*b", AttackInputKind.AllAs, ExpectMatch: false),
            new RedosCase("article-(a|a)*", @"(a|a)*", AttackInputKind.AsTrailingBang, ExpectMatch: false),
            new RedosCase("article-(.*a){10}", @"(.*a){10}", AttackInputKind.BsPlusNineAs, ExpectMatch: false),
            new RedosCase("article-cloudflare", @".*(?:.*=.*)", AttackInputKind.HeaderNoEquals, ExpectMatch: false),
            new RedosCase("article-stackoverflow", @"^([\w\-.]+)*@([\w\-.]+)$", AttackInputKind.AsTrailingBang, ExpectMatch: false),

            // Davis catastrophic-backtracking corpus.
            new RedosCase("davis-(a+)+$", @"(a+)+$", AttackInputKind.AllAs, ExpectMatch: true),
            new RedosCase("davis-(a|aa)+$", @"(a|aa)+$", AttackInputKind.AllAs, ExpectMatch: true),
            new RedosCase("davis-([a-zA-Z]+)*$", @"([a-zA-Z]+)*$", AttackInputKind.AllAs, ExpectMatch: true),
            new RedosCase("davis-(x+x+)+y", @"(x+x+)+y", AttackInputKind.AllXs, ExpectMatch: false),
            new RedosCase("davis-(a+)+b", @"(a+)+b", AttackInputKind.AllAs, ExpectMatch: false),
            new RedosCase("davis-(a|b|ab)*c", @"(a|b|ab)*c", AttackInputKind.AllAs, ExpectMatch: false),
            new RedosCase("davis-(a+)*$", @"(a+)*$", AttackInputKind.AllAs, ExpectMatch: true),
            new RedosCase("davis-(\\w+\\s?)*$", @"(\w+\s?)*$", AttackInputKind.AllAs, ExpectMatch: true),
        };

        /// <summary>
        /// Scaling patterns: supported-subset analogues of the pathological
        /// families (mirroring the ENFA suite's ladder) plus the anchored
        /// article patterns that carry the security claim. The two-layer
        /// engine does not support \p{...} or (?i), so those ENFA ladder
        /// entries have no counterpart here — an honest subset, noted in
        /// the measurement README.
        /// </summary>
        public static IReadOnlyList<RedosCase> ScalingCases { get; } = new[]
        {
            new RedosCase("scale-[a]+", @"^([a]+)$", AttackInputKind.AllAs, ExpectMatch: true),
            new RedosCase("scale-[a]*b", @"^([a]*)b$", AttackInputKind.AllAs, ExpectMatch: false),
            new RedosCase("scale-[ab]*", @"^([ab]*)$", AttackInputKind.AllAs, ExpectMatch: true),
            new RedosCase("scale-negated[^x]*", @"^([^x]*)$", AttackInputKind.AllAs, ExpectMatch: true),
            new RedosCase("scale-dot.*b", @"^(.*)b$", AttackInputKind.AllAs, ExpectMatch: false),
            new RedosCase("scale-danish[a-zæøå]+", @"^([a-zæøå]+)$", AttackInputKind.DanishLetters, ExpectMatch: true),
            new RedosCase("scale-danish-negated[^x]*", @"^([^x]*)$", AttackInputKind.DanishLetters, ExpectMatch: true),
            new RedosCase("scale-evil-(a+)+", @"^(a+)+$", AttackInputKind.AsTrailingBang, ExpectMatch: false),
            new RedosCase("scale-evil-(a*)*b", @"^(a*)*b$", AttackInputKind.AllAs, ExpectMatch: false),
            new RedosCase("scale-evil-so-email", @"^([\w\-.]+)*@([\w\-.]+)$", AttackInputKind.AsTrailingBang, ExpectMatch: false),
        };

        /// <summary>The scaling ladder lengths (bytes), matching the ENFA suite.</summary>
        public static IReadOnlyList<int> ScalingLadder { get; } = new[] { 1024, 4096, 16384, 32768, 65536, 102400 };
    }
}
