namespace DevelApp.TwoLayerRegex
{
    /// <summary>The result of a two-layer match attempt.</summary>
    public sealed class MatchResult
    {
        /// <summary>Creates a result.</summary>
        public MatchResult(bool success, int index, int length, string value, IReadOnlyList<GroupResult> groups)
        {
            Success = success;
            Index = index;
            Length = length;
            Value = value;
            Groups = groups;
        }

        /// <summary>Whether a match was found.</summary>
        public bool Success { get; }

        /// <summary>The input position where the match starts.</summary>
        public int Index { get; }

        /// <summary>The length of the match.</summary>
        public int Length { get; }

        /// <summary>The matched text.</summary>
        public string Value { get; }

        /// <summary>The capture groups, 1-based (index 0 is the whole match convention is not used; group i is Groups[i-1]).</summary>
        public IReadOnlyList<GroupResult> Groups { get; }

        /// <summary>The failed match singleton value.</summary>
        public static MatchResult Failed { get; } = new(false, -1, 0, string.Empty, Array.Empty<GroupResult>());
    }

    /// <summary>One capture group's resolved span.</summary>
    public sealed class GroupResult
    {
        /// <summary>Creates a group result.</summary>
        public GroupResult(int number, int index, int length, string value)
        {
            Number = number;
            Index = index;
            Length = length;
            Value = value;
        }

        /// <summary>The 1-based group number.</summary>
        public int Number { get; }

        /// <summary>The input position where the group starts, or -1 when the group did not participate.</summary>
        public int Index { get; }

        /// <summary>The length of the group's text.</summary>
        public int Length { get; }

        /// <summary>The group's captured text, or an empty string when the group did not participate.</summary>
        public string Value { get; }

        /// <summary>Whether the group participated in the match.</summary>
        public bool Success => Index >= 0;
    }
}
