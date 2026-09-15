using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Embeddings;
using ElGuerre.Tendero.Search.Evaluation;
using ElGuerre.Tendero.Search.Qdrant;
using ElGuerre.Tendero.Search.Reranking;
using ElGuerre.Tendero.Search.Strategies;
using ElGuerre.Tendero.SearchEval.Diagnostics;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// Relevance: it runs the annotated queries against a real Elasticsearch —
/// and, for hybrid, a real Qdrant and a real model server — through the SAME
/// ports and strategies the application uses, and compares NDCG@10 and recall@50
/// with the committed thresholds.
///
/// Two things here were learned from wrong numbers and are not negotiable. The
/// indexes are DROPPED before anything else, because the ProductIds are fresh
/// GUID v7 on every run and the old documents would compete in the ranking (two
/// runs of the same code gave 0.674 and 0.360). And a REFRESH is issued
/// explicitly after indexing, because Elasticsearch refreshes once a second and
/// otherwise the score depends on a race (0.860 and 0.769). The vector collection
/// is recreated for the first reason too: a previous run's points carry ids no
/// product of this run has, and hydration would drop them, but a nearest-neighbour
/// slot they occupy is a slot this run's products do not get.
///
/// <c>--strategy hybrid</c> scores BOTH strategies over the one corpus it indexed,
/// and fails if either falls under its own thresholds (P8-10): a hybrid that won
/// by making the fallback worse would have made worse the mode every AI failure
/// lands in. It also scores meaning alone, as a diagnostic that gates nothing, so a
/// weak vector retrieval and a fusion that drowns a good one can be told apart.
/// </summary>
internal sealed class SearchRelevanceSuite : IEvaluationSuite
{
    public string Name => "search";

    public string Description => "Relevance per strategy: NDCG@10 and recall@50 per culture.";

    public async Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        var indexNames = SearchCultures.IndexNames.ToArray();

        try
        {
            await IndexAdmin.EnsureReachableAsync(options.Elasticsearch, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        await IndexAdmin.DropAsync(options.Elasticsearch, indexNames, cancellationToken);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Catalog:Connectors:Seed:FilePath"] = options.SeedPath
        });

        builder.Services.AddCatalog(builder.Configuration);
        builder.Services.AddLexicalSearch(options.Elasticsearch);
        builder.Services.AddSearchIndexInitializer();

        // The indexer needs to know what can be bought, and this corpus has no
        // warehouses: it measures RELEVANCE, and nothing in the golden set or in
        // the query mentions stock. So availability answers "unknown" for every
        // SKU, deliberately and by a named type rather than by a null.
        //
        // The day a filter puts `inStock` in the query, this line is what has to
        // change — and a fake called `NoInventory` is the thing that will make
        // that obvious, which "no registration" would not.
        builder.Services.AddSingleton<IAvailabilityReader, NoInventory>();

        // The strategies the application composes, with the knobs it reads —
        // `Search__Hybrid__VectorDepth=10` in the environment tunes a run the way
        // it tunes the API.
        builder.Services.AddOptions<HybridSearchOptions>().Bind(builder.Configuration.GetSection(HybridSearchOptions.Section));
        builder.Services.AddSearchStrategies();
        builder.Services.AddSingleton<IFeatureGate>(new EvaluationFeatures(rerank: options.Reranker is not null));

        if (options.IsHybrid)
        {
            builder.Services.AddProductEmbeddings(new Uri(options.Embeddings));
            builder.Services.AddQdrantVectors(new Uri(options.Qdrant), options.QdrantKey);

            if (options.Reranker is { } reranker)
            {
                if (options.RerankerModel?.Split('@') is [var modelId, var revision])
                    builder.Services.AddCandidateReranker(new Uri(reranker), modelId, revision);
                else
                    builder.Services.AddReranker(new Uri(reranker));
            }

            builder.Services.AddKeyedScoped<ISearchStrategy, VectorOnlyStrategy>(VectorOnlyStrategy.Name);
        }

        using var host = builder.Build();

        // Starts the hosted service that creates products_es and products_en if they are missing.
        await host.StartAsync(cancellationToken);

        if (options.IsHybrid)
        {
            try
            {
                await host.Services.GetRequiredService<IProductVectorIndexer>().RecreateAsync(cancellationToken);
            }
            catch (SearchUnavailableException exception)
            {
                Console.Error.WriteLine(
                    $"The vector layer cannot be evaluated: {exception.Message}{Environment.NewLine}" +
                    $"Qdrant at {options.Qdrant} and bge-m3 at {options.Embeddings} are both needed for --strategy hybrid.");
                return 2;
            }
        }

        var corpus = await new SeedCorpus(host.Services).IndexAsync("seed", options.IsHybrid, cancellationToken);
        await IndexAdmin.RefreshAsync(options.Elasticsearch, indexNames, cancellationToken);

        Console.WriteLine($"Indexed {corpus.Count} seed products into {options.Elasticsearch}" +
                          (options.IsHybrid ? $" and {options.Qdrant}" : ""));

        var thresholds = await EvaluationThresholds.LoadAsync(options.ThresholdsPath, cancellationToken);
        var goldenSets = new List<GoldenSet>();

        foreach (var path in Directory.EnumerateFiles(options.GoldenDirectory, "*.json").Order())
            goldenSets.Add(await GoldenSet.LoadAsync(path, cancellationToken));

        // A tuning run looks at one half; the other half is what a change has to
        // survive. Options refuse a split with --ci, so the gate always sees all.
        if (options.Split is { } split)
        {
            goldenSets = goldenSets
                .Select(golden => golden with { Queries = [.. golden.Queries.Where(query => query.Split == split)] })
                .ToList();

            Console.WriteLine($"Scoring the {split} split only: {string.Join(", ", goldenSets.Select(golden => $"{golden.Queries.Count} {golden.Culture}"))} queries");
        }

        (string Name, bool Gated)[] strategies = options.IsHybrid
            ? [(SearchStrategies.Lexical, true), (SearchStrategies.Hybrid, true), (VectorOnlyStrategy.Name, false)]
            : [(SearchStrategies.Lexical, true)];

        var results = new List<StrategyScores>();

        foreach (var strategy in strategies)
        {
            var evaluator = new RelevanceEvaluator(host.Services.GetRequiredKeyedService<ISearchStrategy>(strategy.Name));
            var cultures = new List<CultureScore>();

            foreach (var golden in goldenSets)
                cultures.Add(await evaluator.RunAsync(golden, corpus, cancellationToken));

            results.Add(new StrategyScores(strategy.Name, cultures, strategy.Gated));
        }

        var report = EvaluationReport.Render(results, thresholds, rerank: options.IsHybrid && options.Reranker is not null);
        Console.WriteLine();
        Console.WriteLine(report);

        if (options.ReportPath is { } reportPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await File.WriteAllTextAsync(reportPath, report, cancellationToken);
        }

        if (options.PoolPath is { } poolPath)
        {
            var pool = JudgmentPool.Build(results);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(poolPath))!);
            await File.WriteAllTextAsync(poolPath, JudgmentPool.ToJson(pool), cancellationToken);

            Console.WriteLine(
                $"Wrote {pool.Sum(culture => culture.Queries.Sum(query => query.Candidates.Count))} unjudged candidates " +
                $"across {pool.Sum(culture => culture.Queries.Count)} queries to {poolPath}");
        }

        await host.StopAsync(cancellationToken);

        // A diagnostic strategy is reported and never gates: it has no thresholds.
        var failures = results
            .Where(result => result.Gated)
            .SelectMany(result => result.Cultures
                .Where(score => !thresholds.For(result.Strategy, score.Culture).AreMetBy(score))
                .Select(score => $"{result.Strategy} {score.Culture}"))
            .ToList();

        if (failures.Count == 0)
            return 0;

        Console.Error.WriteLine($"Below the committed thresholds: {string.Join(", ", failures)}.");
        Console.Error.WriteLine(
            "Either the change hurt relevance, or the golden set needs updating — and that needs justifying in the PR.");

        // Without --ci the report still prints but breaks nobody's session.
        return options.FailUnderThresholds ? 1 : 0;
    }
}

/// <summary>
/// The flags of an evaluation run are its arguments, not configuration: reranking
/// is on exactly when a reranker was given. The vector flag is never read here —
/// the suite asks each strategy by name.
/// </summary>
internal sealed class EvaluationFeatures(bool rerank) : IFeatureGate
{
    public bool IsEnabled(FeatureId feature) => feature == FeatureId.SearchRerank && rerank;
}
