using ElGuerre.Tendero.Search.Evaluation;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// What the gate is told, and what it holds each strategy to.
/// </summary>
public sealed class EvaluationSettingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Without_a_strategy_the_gate_is_the_lexical_one_it_always_was()
    {
        var options = EvaluationOptions.Parse(["--ci", "--elasticsearch", "http://localhost:59210"]);

        Assert.Equal("lexical", options.Strategy);
        Assert.False(options.IsHybrid);
        Assert.True(options.FailUnderThresholds);
        Assert.Null(options.Reranker);
    }

    [Fact]
    public void Hybrid_names_the_three_services_it_needs()
    {
        var options = EvaluationOptions.Parse(
            ["--strategy", "hybrid", "--qdrant", "http://localhost:59334", "--qdrant-key", "k", "--embeddings", "http://localhost:58091", "--reranker", "http://localhost:58092"]);

        Assert.True(options.IsHybrid);
        Assert.Equal(("http://localhost:59334", "k", "http://localhost:58091", "http://localhost:58092"),
            (options.Qdrant, options.QdrantKey, options.Embeddings, options.Reranker));
    }

    [Fact]
    public void A_tuning_run_can_score_one_split_and_the_gate_cannot()
    {
        Assert.Equal("holdout", EvaluationOptions.Parse(["--split", "holdout"]).Split);
        Assert.Null(EvaluationOptions.Parse([]).Split);

        var gate = Assert.Throws<ArgumentException>(() => EvaluationOptions.Parse(["--ci", "--split", "tune"]));
        Assert.Contains("--ci", gate.Message);

        var unknown = Assert.Throws<ArgumentException>(() => EvaluationOptions.Parse(["--split", "test"]));
        Assert.Contains("test", unknown.Message);
    }

    [Fact]
    public async Task A_query_carries_its_kind_split_and_pair_and_an_old_file_without_them_still_loads()
    {
        const string json = """
            { "culture": "es", "source": "seed", "queries": [
              { "query": "bambas para correr", "kind": "synonym", "split": "tune", "pair": "N11", "judgments": [] },
              { "query": "mochila", "judgments": [ { "externalId": "B1", "relevance": 3, "why": "mochila" } ] }
            ] }
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var golden = await GoldenSet.LoadAsync(stream, "inline", Ct);

        Assert.Equal(("synonym", "tune", "N11"), (golden.Queries[0].Kind, golden.Queries[0].Split, golden.Queries[0].Pair));
        Assert.Equal(((string?)null, (string?)null), (golden.Queries[1].Kind, golden.Queries[1].Split));
    }

    [Fact]
    public void A_candidate_reranker_is_named_with_its_revision_and_only_beside_an_endpoint()
    {
        const string candidate = "Alibaba-NLP/gte-multilingual-reranker-base@8215cf04918ba6f7b6a62bb44238ce2953d8831c";

        Assert.Equal(candidate, EvaluationOptions.Parse(["--reranker", "http://localhost:58093", "--reranker-model", candidate]).RerankerModel);
        Assert.Null(EvaluationOptions.Parse([]).RerankerModel);

        var alone = Assert.Throws<ArgumentException>(() => EvaluationOptions.Parse(["--reranker-model", candidate]));
        Assert.Contains("--reranker", alone.Message);

        var unpinned = Assert.Throws<ArgumentException>(() => EvaluationOptions.Parse(["--reranker", "http://localhost:58093", "--reranker-model", "main"]));
        Assert.Contains("main", unpinned.Message);

        // The endpoint is only asked at the first rerank, so registering refuses a
        // tag before anything is measured with it.
        var tagged = Assert.Throws<ArgumentException>(() =>
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(
                ElGuerre.Tendero.Search.Reranking.RerankerServiceCollectionExtensions.AddCandidateReranker(
                    new Microsoft.Extensions.DependencyInjection.ServiceCollection(), new Uri("http://localhost:58093"), "Alibaba-NLP/gte-multilingual-reranker-base", "main")));
        Assert.Contains("commit SHA", tagged.Message);

        // A model served from a local folder has no commit: its file hash pins it instead.
        Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(
            ElGuerre.Tendero.Search.Reranking.RerankerServiceCollectionExtensions.AddCandidateReranker(
                new Microsoft.Extensions.DependencyInjection.ServiceCollection(), new Uri("http://localhost:58094"), "/models/gte-int8",
                "sha256:ccf51dba7f8aa9205753761cfaa68c55f741792501463a3bf25d7e5bcdac7c35"));

        var shortHash = Assert.Throws<ArgumentException>(() =>
            ElGuerre.Tendero.Search.Reranking.RerankerServiceCollectionExtensions.AddCandidateReranker(
                new Microsoft.Extensions.DependencyInjection.ServiceCollection(), new Uri("http://localhost:58094"), "/models/gte-int8", "sha256:ccf51dba"));
        Assert.Contains("sha256", shortHash.Message);
    }

    [Fact]
    public void The_pool_is_only_written_when_somebody_asks_where()
    {
        Assert.Null(EvaluationOptions.Parse([]).PoolPath);
        Assert.Equal("pool.json", EvaluationOptions.Parse(["--pool", "pool.json"]).PoolPath);
    }

    [Fact]
    public void A_strategy_nobody_implements_is_refused_by_name()
    {
        var refusal = Assert.Throws<ArgumentException>(() => EvaluationOptions.Parse(["--strategy", "semantic"]));

        Assert.Contains("semantic", refusal.Message);
    }

    /// <summary>
    /// Both strategies, both cultures, in the file the gate reads and the panel
    /// embeds — a missing entry would fail a run halfway, after minutes of embedding.
    /// </summary>
    [Theory]
    [InlineData("lexical", "es")]
    [InlineData("lexical", "en")]
    [InlineData("hybrid", "es")]
    [InlineData("hybrid", "en")]
    public async Task The_committed_thresholds_hold_every_strategy_in_every_culture(string strategy, string culture)
    {
        var thresholds = await EmbeddedEvaluation.ThresholdsAsync(Ct);

        var limit = thresholds.For(strategy, culture);

        Assert.InRange(limit.NdcgAt10, 0.5, 1.0);
        Assert.InRange(limit.RecallAt50, 0.5, 1.0);
    }

    [Fact]
    public async Task The_lexical_bar_did_not_move_when_hybrid_arrived()
    {
        var thresholds = await EmbeddedEvaluation.ThresholdsAsync(Ct);

        // Hybrid gets its own bar; lowering the fallback's to make room for it would
        // be the regression P8-10 exists to refuse. These are the 2026-09-15
        // thresholds, and they moved for the one reason that is allowed: the RULER
        // changed, not the engine. The golden set went from 450 to 732 judgments,
        // pooled from every strategy's first ten instead of lexical's alone, and
        // relevant products lexical never retrieved started to count against it
        // (docs/search-evaluation.md, "The pool, judged").
        Assert.Equal(new CultureThresholds(0.67, 0.72), thresholds.For("lexical", "es"));
        Assert.Equal(new CultureThresholds(0.73, 0.78), thresholds.For("lexical", "en"));
    }

    [Fact]
    public async Task An_unknown_strategy_is_named_when_its_thresholds_are_asked_for()
    {
        var thresholds = await EmbeddedEvaluation.ThresholdsAsync(Ct);

        var refusal = Assert.Throws<InvalidDataException>(() => thresholds.For("semantic", "es"));

        Assert.Contains("semantic", refusal.Message);
    }

    [Fact]
    public async Task The_embedded_golden_sets_are_the_committed_files()
    {
        var embedded = await EmbeddedEvaluation.GoldenSetsAsync(Ct);

        Assert.Equal(["en", "es"], embedded.Select(set => set.Culture));
        Assert.All(embedded, set => Assert.Equal(60, set.Queries.Count));
    }

    [Fact]
    public async Task Every_golden_query_has_a_kind_a_split_and_a_counterpart_in_the_other_culture()
    {
        var kinds = typeof(QueryKinds)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        var embedded = await EmbeddedEvaluation.GoldenSetsAsync(Ct);

        foreach (var set in embedded)
        {
            Assert.All(set.Queries, query =>
            {
                Assert.True(query.Kind is not null && kinds.Contains(query.Kind), $"'{query.Query}' has kind '{query.Kind}'");
                Assert.True(query.Split is "tune" or "holdout", $"'{query.Query}' has split '{query.Split}'");
                Assert.False(string.IsNullOrWhiteSpace(query.Pair), $"'{query.Query}' has no pair");
            });

            Assert.Equal(set.Queries.Count, set.Queries.Select(query => query.Pair).Distinct(StringComparer.Ordinal).Count());

            // A query nothing can answer has nothing relevant to judge.
            Assert.All(set.Queries.Where(query => QueryKinds.IsNoAnswer(query.Kind)),
                query => Assert.DoesNotContain(query.Judgments, judgment => judgment.Relevance > 0));
        }

        // A pair lands in one split whole, so tuning never sees half of it.
        var es = embedded.Single(set => set.Culture == "es").Queries.ToDictionary(query => query.Pair!, StringComparer.Ordinal);
        var en = embedded.Single(set => set.Culture == "en").Queries.ToDictionary(query => query.Pair!, StringComparer.Ordinal);

        Assert.Equal(es.Keys.Order(StringComparer.Ordinal), en.Keys.Order(StringComparer.Ordinal));
        Assert.All(es, entry => Assert.Equal(entry.Value.Split, en[entry.Key].Split));
    }
}
