using System.Text;
using ElGuerre.Tendero.Search.Evaluation;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// Which judgments a score was measured against. If a pull request changes the
/// code and the golden set together, the two runs are not scored against the
/// same expectations, and a moved score cannot tell which of the two moved it.
/// So the committed thresholds name the golden set they were set against, by a
/// fingerprint of what the gate scores: each culture's queries and their graded
/// judgments. A reason (<c>why</c>), a query's kind or split, or the file's
/// layout move no score, and move no fingerprint either.
/// (A reader's suggestion on LinkedIn, 2026-10-24.)
/// </summary>
public sealed class GoldenSetFingerprintTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GoldenSet Spanish(params GoldenQuery[] queries) => new("es", "seed", queries);

    private static GoldenQuery Query(string text, params (string Id, int Relevance)[] judgments) =>
        new(text, [.. judgments.Select(judgment => new Judgment(judgment.Id, judgment.Relevance, "por qué"))], Kind: "keyword", Split: "tune");

    private static readonly GoldenSet[] Committed =
        [Spanish(Query("mochila", ("B1", 3), ("B2", 1)), Query("taza", ("B3", 2)))];

    private static string Of(params GoldenSet[] sets) => GoldenSet.Fingerprint(sets);

    [Fact]
    public void A_fingerprint_is_twelve_lowercase_hex_characters_and_the_same_every_time()
    {
        var fingerprint = Of(Committed);

        Assert.Matches("^[0-9a-f]{12}$", fingerprint);
        Assert.Equal(fingerprint, Of(Committed));
    }

    /// <summary>What does not move a score does not move the fingerprint.</summary>
    [Fact]
    public void Reasons_kinds_splits_and_the_order_of_queries_and_judgments_do_not_count()
    {
        var reworded = Spanish(
            new GoldenQuery("taza", [new Judgment("B3", 2, "otra razón, mejor escrita")], Kind: "product-type", Split: "holdout", Pair: "E9"),
            new GoldenQuery("mochila", [new Judgment("B2", 1, "cambiada"), new Judgment("B1", 3, "cambiada")]));

        Assert.Equal(Of(Committed), Of(reworded));
    }

    /// <summary>Every change to what is scored moves it.</summary>
    [Theory]
    [MemberData(nameof(ScoredChanges))]
    public void Any_change_to_what_is_scored_changes_it(string change, GoldenSet changed)
    {
        Assert.True(Of(Committed) != Of(changed), $"{change} kept the fingerprint");
    }

    public static TheoryData<string, GoldenSet> ScoredChanges() => new()
    {
        { "a grade", Spanish(Query("mochila", ("B1", 2), ("B2", 1)), Query("taza", ("B3", 2))) },
        { "a judgment added", Spanish(Query("mochila", ("B1", 3), ("B2", 1), ("B4", 1)), Query("taza", ("B3", 2))) },
        { "a judgment removed", Spanish(Query("mochila", ("B1", 3)), Query("taza", ("B3", 2))) },
        { "a product judged instead of another", Spanish(Query("mochila", ("B1", 3), ("B5", 1)), Query("taza", ("B3", 2))) },
        { "a query's words", Spanish(Query("mochilas", ("B1", 3), ("B2", 1)), Query("taza", ("B3", 2))) },
        { "a query removed", Spanish(Query("mochila", ("B1", 3), ("B2", 1))) },
        { "a query added", Spanish(Query("mochila", ("B1", 3), ("B2", 1)), Query("taza", ("B3", 2)), Query("vaso")) },
        { "the culture", new GoldenSet("en", "seed", Committed[0].Queries) },
        { "the source", new GoldenSet("es", "shopify", Committed[0].Queries) },
    };

    /// <summary>A grade moved from one query to the other is a different golden set, not the same numbers rearranged.</summary>
    [Fact]
    public void A_judgment_belongs_to_its_query()
    {
        var moved = Spanish(Query("mochila", ("B1", 3)), Query("taza", ("B3", 2), ("B2", 1)));

        Assert.NotEqual(Of(Committed), Of(moved));
    }

    /// <summary>The cultures are one golden set: the fingerprint is the same whichever order the files are read in.</summary>
    [Fact]
    public void The_cultures_are_read_in_any_order()
    {
        var english = new GoldenSet("en", "seed", [Query("backpack", ("B1", 3))]);

        Assert.Equal(Of(Committed[0], english), Of(english, Committed[0]));
    }

    [Fact]
    public async Task The_thresholds_file_names_the_golden_set_it_was_set_against()
    {
        var json = """{ "goldenSet": "0123456789ab", "lexical": { "es": { "ndcgAt10": 0.69, "recallAt50": 0.71 } } }""";

        var thresholds = await EvaluationThresholds.LoadAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), "test", Ct);

        Assert.Equal("0123456789ab", thresholds.GoldenSet);
        Assert.Equal(0.69, thresholds.For("lexical", "es").NdcgAt10);
    }

    /// <summary>
    /// The guard that runs on every pull request, with no Elasticsearch: the
    /// committed thresholds were set against the committed golden set. A pull
    /// request that changes the judgments re-derives the thresholds and writes the
    /// new fingerprint beside them, in the same diff, where a reviewer sees it.
    /// </summary>
    [Fact]
    public async Task The_committed_thresholds_were_set_against_the_committed_golden_set()
    {
        var thresholds = await EmbeddedEvaluation.ThresholdsAsync(Ct);
        var current = GoldenSet.Fingerprint(await EmbeddedEvaluation.GoldenSetsAsync(Ct));

        Assert.True(
            thresholds.GoldenSet == current,
            $"tools/SearchEval/eval.thresholds.json says its thresholds were set against golden set '{thresholds.GoldenSet}', " +
            $"but the golden set is now '{current}'. Re-run the gate, re-derive the thresholds if the scores moved, " +
            $"set \"goldenSet\": \"{current}\" and say why in the pull request (docs/search-evaluation.md).");
    }
}
