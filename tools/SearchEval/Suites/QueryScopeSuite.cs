using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Answers;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using ElGuerre.Tendero.Search.Evaluation;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// Layer B's protocol (ADR 0032), exactly as the notebook declared it on
/// 2026-09-17 before anything was measured: a margin chosen on <c>scope-dev</c>
/// from a fixed grid, one margin for both cultures, then ONE acceptance run over
/// <c>scope-test</c> and every scored golden query — with zero in-scope queries
/// told the shop sells nothing for them, and at least half the out-of-scope ones
/// recognised per culture.
///
/// It needs the embeddings model and the category seed, and nothing else: no
/// Elasticsearch, no Qdrant. A classifier that answers <see cref="ScopeOutcome.Unknown"/>
/// for any query did not run, so the suite exits 2 rather than turn a model
/// server that was down into a verdict.
/// </summary>
internal sealed class QueryScopeSuite : IEvaluationSuite
{
    public string Name => "scope";

    public string Description => "layer B: out-of-scope recognition by anchors, by the protocol declared on 2026-09-17";

    public async Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        var scopeDirectory = options.ScopeDirectory ?? Path.Combine("tools", "SearchEval", "scope");
        var categoriesPath = options.CategoriesPath ?? Path.Combine("seed", "categories.sample.json");

        if (!File.Exists(categoriesPath))
        {
            Console.Error.WriteLine($"The category seed '{categoriesPath}' does not exist; layer B's inside anchors are the shop's categories.");
            return 2;
        }

        var served = await ServedModelAsync(options.Embeddings, cancellationToken);
        var pinned = EmbeddingModelDescriptor.BgeM3;
        if (served is null || served.Value.Sha != pinned.Revision)
        {
            Console.Error.WriteLine(
                $"The model server at {options.Embeddings} must serve {pinned.ModelId}@{pinned.Revision}; it answered " +
                (served is null ? "nothing." : $"{served.Value.Id}@{served.Value.Sha}."));
            return 2;
        }

        var scopeSets = new List<ScopeSet>();
        foreach (var path in Directory.EnumerateFiles(scopeDirectory, "*.json").Order())
            scopeSets.Add(await ScopeSet.LoadAsync(path, cancellationToken));

        var goldenSets = new List<GoldenSet>();
        foreach (var path in Directory.EnumerateFiles(options.GoldenDirectory, "*.json").Order())
            goldenSets.Add(await GoldenSet.LoadAsync(path, cancellationToken));

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Catalog:Categories:FilePath"] = categoriesPath
        });
        builder.Services.AddCatalogReaders(builder.Configuration);
        builder.Services.AddProductEmbeddings(new Uri(options.Embeddings));

        using var host = builder.Build();

        var timing = new TimingGenerator(host.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());
        var categories = host.Services.GetRequiredService<ICategoryReader>();
        var loggers = host.Services.GetRequiredService<ILoggerFactory>();

        var run = new ScopeRun(timing, categories, loggers);

        try
        {
            return await run.ExecuteAsync(options, scopeSets, goldenSets, cancellationToken);
        }
        catch (ScopeUnavailableException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static async Task<(string Id, string Sha)?> ServedModelAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(new Uri(endpoint), "/"), Timeout = TimeSpan.FromSeconds(5) };
            var info = await http.GetFromJsonAsync<JsonElement>("info", ct);
            return (info.TryGetProperty("model_id", out var id) ? id.GetString() ?? "" : "",
                    info.TryGetProperty("model_sha", out var sha) ? sha.GetString() ?? "" : "");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private sealed class ScopeRun(TimingGenerator timing, ICategoryReader categories, ILoggerFactory loggers)
    {
        public async Task<int> ExecuteAsync(
            EvaluationOptions options, IReadOnlyList<ScopeSet> scopeSets, IReadOnlyList<GoldenSet> goldenSets, CancellationToken ct)
        {
            var report = new StringBuilder();
            report.AppendLine("# Layer B — the query-scope protocol run");
            report.AppendLine();
            report.AppendLine($"Run at {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC against {options.Embeddings}, serving {EmbeddingModelDescriptor.BgeM3.ModelId}@{EmbeddingModelDescriptor.BgeM3.Revision}.");
            report.AppendLine($"Scope sets: {string.Join(", ", scopeSets.Select(set => $"{set.Culture} ({set.Queries.Count})"))}. Golden sets: {string.Join(", ", goldenSets.Select(set => $"{set.Culture} ({set.Queries.Count})"))}.");
            report.AppendLine();

            // 1 · The grid on scope-dev, one classifier per margin.
            var devPerCulture = new List<MarginResult>();
            var devRows = new List<(string Culture, MarginResult Result)>();
            var devOutcomes = new Dictionary<double, List<QueryOutcome>>();
            var warmUps = new List<(double Margin, IReadOnlyList<GenerationCall> Calls)>();

            foreach (var margin in QueryScopeEvaluation.Grid)
            {
                var (classifier, warm) = await CreateAsync(margin, ct);
                warmUps.Add((margin, warm));
                devOutcomes[margin] = [];

                foreach (var set in scopeSets)
                {
                    var outcomes = await ClassifyAsync(classifier, set.Culture,
                        set.Queries.Where(query => query.Split == "scope-dev").Select(query => (query.Query, query.Scope, query.Kind)), ct);
                    var result = Count(margin, outcomes);

                    devPerCulture.Add(result);
                    devRows.Add((set.Culture, result));
                    devOutcomes[margin].AddRange(outcomes);
                }
            }

            var combined = QueryScopeEvaluation.Combine(devPerCulture);
            var chosen = QueryScopeEvaluation.Choose(combined);

            report.AppendLine("## scope-dev — the declared grid");
            report.AppendLine();
            report.AppendLine("| margin | culture | out recognised | in marked out |");
            report.AppendLine("|---:|---|---:|---:|");
            foreach (var (culture, result) in devRows)
                report.AppendLine($"| {F(result.Margin)} | {culture} | {result.OutRecognised} / {result.OutTotal} | {result.InMarkedOut} |");
            foreach (var result in combined)
                report.AppendLine($"| {F(result.Margin)} | **both** | **{result.OutRecognised} / {result.OutTotal}** | **{result.InMarkedOut}** |");
            report.AppendLine();
            report.AppendLine("One margin for the shop: both cultures are summed per margin before the rule chooses — the most `out` recognised among margins marking no `in` query out, ties to the larger.");
            report.AppendLine();
            report.AppendLine(chosen is { } picked
                ? $"**Chosen margin: {F(picked)}.**"
                : "**No margin chosen: every margin on the grid told an in-scope dev query the shop sells nothing for it.**");
            report.AppendLine();

            foreach (var margin in QueryScopeEvaluation.Grid)
                AppendOutcomes(report, $"scope-dev at margin {F(margin)}", devOutcomes[margin], onlyWrong: margin != chosen);

            var accepted = false;
            if (chosen is { } margin2)
            {
                var (classifier, warm) = await CreateAsync(margin2, ct);
                warmUps.Add((margin2, warm));

                // 2 · scope-test, once.
                var testPerCulture = new List<(string Culture, MarginResult Result)>();
                var testOutcomes = new List<QueryOutcome>();
                foreach (var set in scopeSets)
                {
                    var outcomes = await ClassifyAsync(classifier, set.Culture,
                        set.Queries.Where(query => query.Split == "scope-test").Select(query => (query.Query, query.Scope, query.Kind)), ct);
                    testPerCulture.Add((set.Culture, Count(margin2, outcomes)));
                    testOutcomes.AddRange(outcomes);
                }

                // 3 · every scored golden query is in scope; the no-answer ones are reported apart.
                var goldenPerCulture = new List<(string Culture, int InMarkedOut, int Total)>();
                var goldenOutcomes = new List<QueryOutcome>();
                var noAnswerOutcomes = new List<QueryOutcome>();
                foreach (var set in goldenSets)
                {
                    var scored = await ClassifyAsync(classifier, set.Culture,
                        set.Queries.Where(query => !QueryKinds.IsNoAnswer(query.Kind)).Select(query => (query.Query, "in", query.Kind ?? "")), ct);
                    goldenPerCulture.Add((set.Culture, scored.Count(outcome => outcome.Outcome == ScopeOutcome.OutOfScope), scored.Count));
                    goldenOutcomes.AddRange(scored);

                    noAnswerOutcomes.AddRange(await ClassifyAsync(classifier, set.Culture,
                        set.Queries.Where(query => QueryKinds.IsNoAnswer(query.Kind)).Select(query => (query.Query, "out", query.Kind ?? "")), ct));
                }

                accepted = QueryScopeEvaluation.Accepts(
                    [.. testPerCulture.Select(row => row.Result)],
                    [.. goldenPerCulture.Select(row => row.InMarkedOut)]);

                report.AppendLine($"## scope-test at margin {F(margin2)} — run once");
                report.AppendLine();
                report.AppendLine("| culture | out recognised | at least half | in marked out |");
                report.AppendLine("|---|---:|---|---:|");
                foreach (var (culture, result) in testPerCulture)
                    report.AppendLine($"| {culture} | {result.OutRecognised} / {result.OutTotal} | {(2 * result.OutRecognised >= result.OutTotal ? "yes" : "**no**")} | {result.InMarkedOut} |");
                report.AppendLine();
                AppendOutcomes(report, "scope-test", testOutcomes, onlyWrong: false);

                report.AppendLine("## The scored golden queries — every one in scope");
                report.AppendLine();
                report.AppendLine("| culture | queries | marked out of scope |");
                report.AppendLine("|---|---:|---:|");
                foreach (var (culture, inMarkedOut, total) in goldenPerCulture)
                    report.AppendLine($"| {culture} | {total} | {inMarkedOut} |");
                report.AppendLine();
                AppendOutcomes(report, "golden (scored)", goldenOutcomes, onlyWrong: true);

                report.AppendLine("## The eight no-answer golden queries per culture — secondary evidence, not gated");
                report.AppendLine();
                AppendOutcomes(report, "golden (no answer)", noAnswerOutcomes, onlyWrong: false);
            }

            AppendLatency(report, warmUps, timing.Calls, await QueryScopeEvaluation.AnchorTextCountsAsync(categories, ct));

            report.AppendLine("## Verdict");
            report.AppendLine();
            report.AppendLine(accepted ? "**ACCEPTED**" : "**REFUTED**");
            report.AppendLine();

            var text = report.ToString();
            Console.WriteLine();
            Console.WriteLine(text);

            if (options.ReportPath is { } reportPath)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
                await File.WriteAllTextAsync(reportPath, text, ct);
            }

            if (accepted && options.WriteCalibration && chosen is { } calibrated)
            {
                var record = await QueryScopeEvaluation.FingerprintAsync(categories, calibrated, ct);
                var path = options.CalibrationPath ?? Path.Combine("src", "Search", "Answers", "query-scope.calibration.json");

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                await File.WriteAllTextAsync(path,
                    JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine, ct);
                Console.WriteLine($"Wrote the calibration to {path}.");
            }

            return accepted ? 0 : options.FailUnderThresholds ? 1 : 0;
        }

        private async Task<(ScopeClassifier Classifier, IReadOnlyList<GenerationCall> WarmUp)> CreateAsync(double margin, CancellationToken ct)
        {
            var before = timing.Calls.Count;
            var classifier = await QueryScopeEvaluation.CreateAsync(timing, categories, margin, loggers, ct);
            return (classifier, [.. timing.Calls.Skip(before)]);
        }

        private static async Task<List<QueryOutcome>> ClassifyAsync(
            ScopeClassifier classifier, string culture, IEnumerable<(string Query, string Scope, string Kind)> queries, CancellationToken ct)
        {
            var outcomes = new List<QueryOutcome>();

            foreach (var (query, scope, kind) in queries)
            {
                var (outcome, label) = await classifier.ClassifyAsync(query, culture, ct);
                if (outcome == ScopeOutcome.Unknown)
                    throw new ScopeUnavailableException(
                        $"Layer B gave no verdict for '{query}' ({culture}): the anchors were not built or the model server failed. No verdict is recorded.");

                outcomes.Add(new QueryOutcome(culture, query, scope, kind, outcome, label));
            }

            return outcomes;
        }
    }

    internal static MarginResult Count(double margin, IReadOnlyCollection<QueryOutcome> outcomes) =>
        new(margin,
            outcomes.Count(outcome => outcome.Scope == "out" && outcome.Outcome == ScopeOutcome.OutOfScope),
            outcomes.Count(outcome => outcome.Scope == "out"),
            outcomes.Count(outcome => outcome.Scope == "in" && outcome.Outcome == ScopeOutcome.OutOfScope));

    private static void AppendOutcomes(StringBuilder report, string title, IReadOnlyList<QueryOutcome> outcomes, bool onlyWrong)
    {
        var rows = onlyWrong ? outcomes.Where(outcome => outcome.IsWrong).ToList() : outcomes.ToList();

        report.AppendLine($"### {title}{(onlyWrong ? " — only the queries the rule counts against" : "")}");
        report.AppendLine();
        if (rows.Count == 0)
        {
            report.AppendLine("None.");
            report.AppendLine();
            return;
        }

        report.AppendLine("| culture | query | label | kind | outcome | named |");
        report.AppendLine("|---|---|---|---|---|---|");
        foreach (var row in rows)
            report.AppendLine($"| {row.Culture} | {row.Query} | {row.Scope} | {row.Kind} | {(row.IsWrong ? $"**{row.Outcome}**" : row.Outcome.ToString())} | {row.Label} |");
        report.AppendLine();
    }

    private static void AppendLatency(StringBuilder report, IReadOnlyList<(double Margin, IReadOnlyList<GenerationCall> Calls)> warmUps, IReadOnlyList<GenerationCall> all, IReadOnlyList<(string Culture, int Texts)> anchorTexts)
    {
        report.AppendLine("## Latency");
        report.AppendLine();
        report.AppendLine($"Anchor warm-up per culture: its inside and outside anchor texts, embedded in batches of at most {AnchorBatchSize} (the model server's `max_client_batch_size`), cultures in the order the build takes them.");
        report.AppendLine();
        report.AppendLine("| classifier built for margin | culture | anchor texts | requests | ms |");
        report.AppendLine("|---:|---|---:|---:|---:|");
        foreach (var (margin, calls) in warmUps)
        {
            var remaining = calls.ToList();
            foreach (var (culture, texts) in anchorTexts)
            {
                var taken = new List<GenerationCall>();
                while (taken.Sum(call => call.Texts) < texts && remaining.Count > 0)
                {
                    taken.Add(remaining[0]);
                    remaining.RemoveAt(0);
                }

                report.AppendLine($"| {F(margin)} | {culture} | {taken.Sum(call => call.Texts)} | {taken.Count} | {taken.Sum(call => call.Milliseconds):0.0} |");
            }
        }
        report.AppendLine();

        var queries = all.Where(call => call.Texts == 1).Select(call => call.Milliseconds).Order().ToList();
        report.AppendLine("Query embedding — one text per call, every classification after its classifier's anchors were built:");
        report.AppendLine();
        report.AppendLine("| calls | p50 ms | p95 ms | max ms |");
        report.AppendLine("|---:|---:|---:|---:|");
        report.AppendLine(queries.Count == 0
            ? "| 0 | — | — | — |"
            : $"| {queries.Count} | {Percentile(queries, 0.50):0.0} | {Percentile(queries, 0.95):0.0} | {queries[^1]:0.0} |");
        report.AppendLine();
    }

    /// <summary>Nearest-rank percentile over an ascending list.</summary>
    internal static double Percentile(IReadOnlyList<double> ascending, double p) =>
        ascending[Math.Clamp((int)Math.Ceiling(p * ascending.Count) - 1, 0, ascending.Count - 1)];

    internal static string F(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    internal sealed record QueryOutcome(string Culture, string Query, string Scope, string Kind, ScopeOutcome Outcome, string? Label)
    {
        /// <summary>An in-scope query told the shop sells nothing, or an out-of-scope query not recognised.</summary>
        public bool IsWrong => Scope == "in" ? Outcome == ScopeOutcome.OutOfScope : Outcome != ScopeOutcome.OutOfScope;
    }

    /// <summary>What the anchor build sends per request; mirrors the classifier's own limit, for the report.</summary>
    private const int AnchorBatchSize = 32;

    internal sealed record GenerationCall(int Texts, double Milliseconds);

    private sealed class ScopeUnavailableException(string message) : Exception(message);

    /// <summary>
    /// Times every call to the model server, so the report can say what layer B
    /// costs: the anchors once per culture, and one query embedding per search —
    /// the recorded deviation from the design, which assumed the vector side's
    /// query vector would be reused.
    /// </summary>
    internal sealed class TimingGenerator(IEmbeddingGenerator<string, Embedding<float>> inner)
        : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
    {
        private readonly List<GenerationCall> calls = [];

        public IReadOnlyList<GenerationCall> Calls
        {
            get { lock (this.calls) return [.. this.calls]; }
        }

        public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var texts = values as IReadOnlyCollection<string> ?? [.. values];
            var watch = Stopwatch.StartNew();
            var result = await base.GenerateAsync(texts, options, cancellationToken);
            watch.Stop();

            lock (this.calls)
                this.calls.Add(new GenerationCall(texts.Count, watch.Elapsed.TotalMilliseconds));

            return result;
        }
    }
}
