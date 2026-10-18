using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.SearchEval.Voice;

/// <summary>One line the author recorded: its id (the file's name), its language, and what was said.</summary>
public sealed record VoiceLine(string Id, string Culture, string Said);

/// <summary>The lines of docs/analysis/voice-recordings.md: the one list the recordings and the measurement share.</summary>
public static partial class VoiceLines
{
    public static IReadOnlyList<VoiceLine> Parse(string markdown) =>
    [
        .. Row().Matches(markdown).Select(match =>
            new VoiceLine(match.Groups["id"].Value, match.Groups["culture"].Value, match.Groups["said"].Value.Trim())),
    ];

    [GeneratedRegex(@"^\s*\| (?<id>(?<culture>es|en)-\d\d) \| (?<said>[^|]+)\|\s*$", RegexOptions.Multiline)]
    private static partial Regex Row();
}
