using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// Parsed by hand: there are a handful of options and they do not justify a
/// package (CLAUDE.md's dependency policy).
/// </summary>
public sealed record EvaluationOptions(
    string Suite,
    string Elasticsearch,
    string GoldenDirectory,
    string ThresholdsPath,
    string SeedPath,
    string? ReportPath,
    bool FailUnderThresholds,
    string Strategy,
    string Qdrant,
    string? QdrantKey,
    string Embeddings,
    string? Reranker,
    string? PoolPath = null,
    string? Split = null,
    string? RerankerModel = null)
{
    public bool IsHybrid => Strategy == SearchStrategies.Hybrid;

    public static string Usage(IReadOnlyList<Suites.IEvaluationSuite> suites) =>
        $"""
        Usage: dotnet run --project tools/SearchEval -- [options]

          --suite <name>          Which gate to run (default search)
        {string.Join(Environment.NewLine, suites.Select(s => $"                            {s.Name}: {s.Description}"))}
          --strategy <name>       lexical (default), or hybrid to score lexical AND hybrid side by side
          --elasticsearch <url>   Elasticsearch endpoint (default http://localhost:9200)
          --qdrant <url>          Qdrant's gRPC endpoint, for hybrid (default http://localhost:6334)
          --qdrant-key <key>      Qdrant's API key, when it has one
          --embeddings <url>      Text Embeddings Inference serving bge-m3, for hybrid (default http://localhost:8080)
          --reranker <url>        Text Embeddings Inference serving bge-reranker-v2-m3; reranks hybrid when given
          --reranker-model <m@sha> A candidate reranker the --reranker endpoint serves instead, pinned by full commit SHA
          --golden <dir>          Golden set directory  (default tools/SearchEval/golden)
          --thresholds <file>     Committed thresholds  (default tools/SearchEval/eval.thresholds.json)
          --seed <file>           Seed catalogue        (default seed/products.sample.json)
          --report <file>         Write the Markdown report to a file as well
          --pool <file>           Write every strategy's unjudged first-ten results as JSON, to be judged
          --split <name>          tune or holdout: score only that half of the golden set (not with --ci)
          --ci                    Exit non-zero when a strategy falls under its thresholds in any culture
        """;

    public static EvaluationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ci = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (argument == "--ci")
            {
                ci = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{argument}'.");

            if (index + 1 >= args.Length)
                throw new ArgumentException($"Option '{argument}' needs a value.");

            values[argument[2..]] = args[++index];
        }

        var strategy = values.GetValueOrDefault("strategy", SearchStrategies.Lexical);
        if (strategy is not (SearchStrategies.Lexical or SearchStrategies.Hybrid))
            throw new ArgumentException($"Unknown strategy '{strategy}'. Available: lexical, hybrid.");

        // A split is for tuning runs. The gate measures every query, and a gate that
        // could be pointed at half of them would pass on whichever half it liked.
        var split = values.GetValueOrDefault("split");
        if (split is not (null or "tune" or "holdout"))
            throw new ArgumentException($"Unknown split '{split}'. Available: tune, holdout.");
        if (split is not null && ci)
            throw new ArgumentException("--split cannot be combined with --ci: the gate measures every query.");

        // A candidate is measured, never gated: it names what the endpoint serves,
        // so it means nothing without one.
        var rerankerModel = values.GetValueOrDefault("reranker-model");
        if (rerankerModel is not null && !values.ContainsKey("reranker"))
            throw new ArgumentException("--reranker-model names the model a --reranker endpoint serves, and no --reranker was given.");
        if (rerankerModel is not null && rerankerModel.Split('@') is not [{ Length: > 0 }, { Length: > 0 }])
            throw new ArgumentException($"--reranker-model is <model id>@<full commit SHA>, and '{rerankerModel}' is not.");

        return new EvaluationOptions(
            values.GetValueOrDefault("suite", "search"),
            values.GetValueOrDefault("elasticsearch", "http://localhost:9200"),
            values.GetValueOrDefault("golden", Path.Combine("tools", "SearchEval", "golden")),
            values.GetValueOrDefault("thresholds", Path.Combine("tools", "SearchEval", "eval.thresholds.json")),
            values.GetValueOrDefault("seed", Path.Combine("seed", "products.sample.json")),
            values.GetValueOrDefault("report"),
            ci,
            strategy,
            values.GetValueOrDefault("qdrant", "http://localhost:6334"),
            values.GetValueOrDefault("qdrant-key"),
            values.GetValueOrDefault("embeddings", "http://localhost:8080"),
            values.GetValueOrDefault("reranker"),
            values.GetValueOrDefault("pool"),
            split,
            rerankerModel);
    }
}
