using System.Reflection;
using System.Text.RegularExpressions;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// The two rules that let this repository have feature flags without becoming
/// the codebase article 07 warns about (ADR 0023).
///
/// A flag that nobody reads is configuration promising a feature, and a flag read
/// in two places is two decisions that can disagree — the storefront hybrid and
/// the MCP tools lexical, with nothing to say which is right. A flag with no
/// removal condition is permanent the day it is added.
/// </summary>
public sealed partial class FeatureFlagRules
{
    [Fact]
    public void Every_flag_says_when_it_will_be_deleted()
    {
        var flags = typeof(FeatureId).GetFields(BindingFlags.Public | BindingFlags.Static);

        // Not vacuous: the set is closed and small, and it is not empty.
        Assert.NotEmpty(flags);

        var silent = flags
            .Where(flag => flag.GetCustomAttribute<RemovalConditionAttribute>() is not { Condition.Length: > 40 })
            .Select(flag => flag.Name)
            .ToArray();

        Assert.True(silent.Length == 0,
            $"These flags do not say, measurably, when they will be deleted: {string.Join(", ", silent)}.");
    }

    /// <summary>
    /// Read from the source rather than from IL, because what matters is the
    /// decision a person reads — a flag passed through a variable would hide a
    /// second reader from reflection and not from a search.
    /// </summary>
    [Fact]
    public void Every_flag_is_read_in_exactly_one_place_in_production_code()
    {
        var sources = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.EndsWith(Path.Combine("SharedKernel", "Features.cs"), StringComparison.Ordinal))
            .Select(path => (Path: Path.GetRelativePath(RepositoryRoot(), path), Text: File.ReadAllText(path)))
            .ToList();

        var wrong = Enum.GetNames<FeatureId>()
            .Select(flag => (Flag: flag, Readers: sources
                .Where(source => Regex.IsMatch(source.Text, $@"\bFeatureId\.{flag}\b"))
                .Select(source => $"{source.Path} ({Regex.Matches(source.Text, $@"\bFeatureId\.{flag}\b").Count})")
                .ToList()))
            .Where(flag => flag.Readers.Sum(reader => int.Parse(CountIn().Match(reader).Groups[1].Value)) != 1)
            .Select(flag => $"  {flag.Flag}: {(flag.Readers.Count == 0 ? "read nowhere" : string.Join(", ", flag.Readers))}")
            .ToArray();

        Assert.True(wrong.Length == 0,
            $"""
             Every flag has exactly one reader, and these do not:

             {string.Join(Environment.NewLine, wrong)}

             Read nowhere: delete the flag, or it guards nothing. Read twice: make
             it one decision, and pass its result to whatever else needs it.
             """);
    }

    [GeneratedRegex(@"\((\d+)\)$")]
    private static partial Regex CountIn();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find Tendero.slnx above the test's output directory.");
    }
}
