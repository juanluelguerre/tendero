using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Elasticsearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// Relevancia léxica: corre las consultas anotadas contra un Elasticsearch real,
/// por los MISMOS puertos que usa la aplicación, y compara NDCG@10 y recall@50
/// con los umbrales commiteados.
///
/// Dos cosas de aquí se aprendieron con números equivocados y no son
/// negociables. Se BORRAN los índices antes de nada, porque los ProductId son
/// GUID v7 nuevos en cada ejecución y los documentos viejos competirían en el
/// ranking (dos ejecuciones del mismo código dieron 0.674 y 0.360). Y se
/// REFRESCA explícitamente después de indexar, porque Elasticsearch refresca una
/// vez por segundo y si no la puntuación depende de una carrera (0.860 y 0.769).
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

        using var host = builder.Build();

        // Arranca el hosted service que crea products_es y products_en si no existen.
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

        // Sin --ci el informe se imprime igual pero no rompe la sesión de nadie.
        return options.FailUnderThresholds ? 1 : 0;
    }
}
