using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tendero.Catalog;
using Tendero.Search.Contracts;
using Tendero.Search.Elasticsearch;
using Tendero.SearchEval;

// Puerta de calidad de la búsqueda. Corre las consultas anotadas contra un
// Elasticsearch real, por los MISMOS puertos que usa la aplicación, y compara
// NDCG@10 y recall@50 con los umbrales comprometidos.

var IndexNames = SearchCultures.Analyzers.Keys.Select(ProductSearchDocument.IndexNameFor).ToArray();

EvaluationOptions options;
try
{
    options = EvaluationOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(EvaluationOptions.Usage);
    return 2;
}

// Corpus limpio antes de nada: la evaluación tiene que dar el mismo número dos
// veces seguidas o no sirve como puerta.
await IndexAdmin.DropAsync(
    options.Elasticsearch,
    IndexNames,
    CancellationToken.None);

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
await host.StartAsync();

var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var services = host.Services;

var corpus = await new SeedCorpus(services).IndexAsync("seed", cancellation.Token);

// Refresco explícito: sin esto la puntuación depende de una carrera con el
// refresco automático de Elasticsearch, y una puerta que da números distintos
// en dos ejecuciones seguidas no es una puerta.
await IndexAdmin.RefreshAsync(options.Elasticsearch, IndexNames, cancellation.Token);

Console.WriteLine($"Indexed {corpus.Count} seed products into {options.Elasticsearch}");

var thresholds = await EvaluationThresholds.LoadAsync(options.ThresholdsPath, cancellation.Token);
var runner = new EvaluationRunner(services);
var scores = new List<CultureScore>();

foreach (var path in Directory.EnumerateFiles(options.GoldenDirectory, "*.json").Order())
{
    var golden = await GoldenSet.LoadAsync(path, cancellation.Token);
    scores.Add(await runner.RunAsync(golden, corpus, cancellation.Token));
}

var report = EvaluationReport.Render(scores, thresholds);
Console.WriteLine();
Console.WriteLine(report);

if (options.ReportPath is { } reportPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
    await File.WriteAllTextAsync(reportPath, report, cancellation.Token);
}

await host.StopAsync();

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
