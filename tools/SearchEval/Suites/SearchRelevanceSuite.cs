using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Elasticsearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// Lexical relevance: it runs the annotated queries against a real
/// Elasticsearch, through the SAME ports the application uses, and compares
/// NDCG@10 and recall@50 with the committed thresholds.
///
/// Two things here were learned from wrong numbers and are not negotiable. The
/// indexes are DROPPED before anything else, because the ProductIds are fresh
/// GUID v7 on every run and the old documents would compete in the ranking (two
/// runs of the same code gave 0.674 and 0.360). And a REFRESH is issued
/// explicitly after indexing, because Elasticsearch refreshes once a second and
/// otherwise the score depends on a race (0.860 and 0.769).
/// </summary>
internal sealed class SearchRelevanceSuite : IEvaluationSuite
{
    public string Name => "search";
    public string Description => "Lexical relevance: NDCG@10 and recall@50 per culture.";

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

        using var host = builder.Build();

        // Starts the hosted service that creates products_es and products_en if they are missing.
        await host.StartAsync(cancellationToken);

        var corpus = await new SeedCorpus(host.Services).IndexAsync("seed", cancellationToken);
        await IndexAdmin.RefreshAsync(options.Elasticsearch, indexNames, cancellationToken);

        Console.WriteLine($"Indexed {corpus.Count} seed products into {options.Elasticsearch}");

        var thresholds = await EvaluationThresholds.LoadAsync(options.ThresholdsPath, cancellationToken);
        var runner = new EvaluationRunner(host.Services);
        var scores = new List<CultureScore>();

        foreach (var path in Directory.EnumerateFiles(options.GoldenDirectory, "*.json").Order())
        {
            var golden = await GoldenSet.LoadAsync(path, cancellationToken);
            scores.Add(await runner.RunAsync(golden, corpus, cancellationToken));
        }

        var report = EvaluationReport.Render(scores, thresholds);
        Console.WriteLine();
        Console.WriteLine(report);

        if (options.ReportPath is { } reportPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await File.WriteAllTextAsync(reportPath, report, cancellationToken);
        }

        await host.StopAsync(cancellationToken);

        var failures = scores
            .Where(score => !EvaluationReport.Passes(score, thresholds.For(score.Culture)))
            .Select(score => score.Culture)
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
