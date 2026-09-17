using ElGuerre.Tendero.Search.Evaluation;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The queries the scope classifier is measured on, held to the structure the
/// protocol needs: both cultures ask the same pairs, each split holds both answers,
/// and none of them is a golden query the classifier could have been shaped around.
/// </summary>
public sealed class ScopeSetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_cultures_ask_the_same_pairs_in_the_same_split_and_scope()
    {
        var es = await ScopeSet.LoadAsync(ScopePath("es"), Ct);
        var en = await ScopeSet.LoadAsync(ScopePath("en"), Ct);

        var byPair = en.Queries.ToDictionary(query => query.Pair, StringComparer.Ordinal);
        Assert.Equal(es.Queries.Count, en.Queries.Count);
        Assert.All(es.Queries, query =>
        {
            Assert.True(byPair.TryGetValue(query.Pair, out var twin), query.Pair);
            Assert.Equal(query.Split, twin!.Split);
            Assert.Equal(query.Scope, twin.Scope);
            Assert.Equal(query.Kind, twin.Kind);
        });
    }

    [Fact]
    public async Task The_split_follows_the_pair_id_and_not_a_choice()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            var set = await ScopeSet.LoadAsync(ScopePath(culture), Ct);

            Assert.Equal(culture, set.Culture);
            Assert.Equal(set.Queries.Count, set.Queries.Select(query => query.Pair).Distinct(StringComparer.Ordinal).Count());
            Assert.All(set.Queries, query =>
            {
                Assert.Matches("^S[0-9]{2}$", query.Pair);
                var expected = (query.Pair[^1] - '0') % 2 == 1 ? "scope-dev" : "scope-test";
                Assert.Equal(expected, query.Split);
                Assert.True(query.Scope is "in" or "out", $"'{query.Query}' has scope '{query.Scope}'");
            });
        }
    }

    [Fact]
    public async Task Each_split_holds_both_scopes_and_every_query_says_why()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            var set = await ScopeSet.LoadAsync(ScopePath(culture), Ct);

            foreach (var split in new[] { "scope-dev", "scope-test" })
            {
                Assert.Contains(set.Queries, query => query.Split == split && query.Scope == "in");
                Assert.Contains(set.Queries, query => query.Split == split && query.Scope == "out");
            }

            Assert.All(set.Queries, query => Assert.False(string.IsNullOrWhiteSpace(query.Why)));
        }
    }

    [Fact]
    public async Task Every_kind_the_protocol_needs_is_asked_often_enough_in_each_culture()
    {
        var minimums = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["gift"] = 5,
            ["symptom"] = 4,
            ["use-case"] = 4,
            ["negation"] = 3,
            ["typo"] = 4,
            ["edge"] = 6
        };

        foreach (var culture in new[] { "es", "en" })
        {
            var set = await ScopeSet.LoadAsync(ScopePath(culture), Ct);

            Assert.All(minimums, minimum =>
            {
                var asked = set.Queries.Count(query => query.Kind == minimum.Key);
                Assert.True(asked >= minimum.Value, $"{culture}: {asked} '{minimum.Key}' queries, at least {minimum.Value} needed");
            });
        }
    }

    [Fact]
    public async Task Every_kind_belongs_to_the_closed_set_and_agrees_with_its_scope()
    {
        string[] inKinds = ["gift", "symptom", "use-case", "negation", "typo"];
        string[] outKinds = ["edge", "not-sold"];

        foreach (var culture in new[] { "es", "en" })
        {
            var set = await ScopeSet.LoadAsync(ScopePath(culture), Ct);

            Assert.All(set.Queries, query => Assert.Contains(query.Kind, query.Scope == "in" ? inKinds : outKinds));
        }
    }

    [Fact]
    public async Task No_scope_query_repeats_a_golden_query()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            var scope = await ScopeSet.LoadAsync(ScopePath(culture), Ct);
            var golden = await GoldenSet.LoadAsync(
                Path.Combine(RepositoryRoot(), "tools", "SearchEval", "golden", $"{culture}.json"), Ct);

            var asked = golden.Queries.Select(query => query.Query.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.NotEmpty(asked);
            Assert.All(scope.Queries, query => Assert.DoesNotContain(query.Query.Trim(), asked));
        }
    }

    private static string ScopePath(string culture) =>
        Path.Combine(RepositoryRoot(), "tools", "SearchEval", "scope", $"{culture}.json");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find Tendero.slnx above the test's output directory.");
    }
}
